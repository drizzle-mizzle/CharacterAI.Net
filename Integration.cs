using CharacterAI.Models;
using CharacterAI.Services;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;

namespace CharacterAI
{
    public class Integration : CommonService
    {
        public Character CurrentCharacter { get => _currentCharacter; }
        public List<string> Chats { get => _chatsList; }

        private Character _currentCharacter = new();
        private readonly List<string> _chatsList = new();
        private readonly HttpClient _httpClient;
        private readonly string? _userToken;

        public Integration(string userToken)
        {
            _userToken = userToken;
            _httpClient = CreateHttpClient();
        }

        // Use it to quickly setup integration with a character and get-last/create-new chat with it.
        public async Task<SetupResult> SetupAsync(string? characterId = null, bool startWithNewChat = false)
        {
            Log($"\nStarting character setup...\n  (Character ID: {characterId ?? _currentCharacter.Id})\n");

            Log("Fetching character info... ");
            var character = await GetInfoAsync(characterId);
            if (character.IsEmpty)
                return new SetupResult(false, "Failed to get character info.");

            Success($"OK\n  (Character name: {character.Name})");
            _currentCharacter = character;
            _chatsList.Clear();

            Log("Fetching dialog history... ");
            var historyId = startWithNewChat ? await CreateNewChatAsync() : await GetLastChatAsync();
            if (historyId is null)
                return new SetupResult(false, "Failed to get chat history.");

            Success($"OK\n  (History ID: {historyId})");
            _chatsList.Add(historyId);

            Log("CharacterAI - ");
            Success("Ready\n");

            return new SetupResult(true);
        }

        // Forget all chats with a character and create new one
        public async Task<SetupResult> Reset()
        {
            _chatsList.Clear();
            var historyId = await CreateNewChatAsync();
            if (historyId is null)
                return new SetupResult(false, "Failed to create new chat.");

            _chatsList.Add(historyId);

            return new SetupResult(true);
        }

        // Send message and get reply
        public async Task<CharacterResponse> CallCharacterAsync(string message = "", string? imagePath = null, string? historyId = null, ulong? primaryMsgId = null, ulong? parentMsgId = null)
        {
            var contentDynamic = BasicCallContent(_currentCharacter, message, imagePath, historyId ?? _chatsList.First());

            // Fetch new answer ("perform swipe").
            if (parentMsgId is not null)
            {   // When parent_msg_id is present, character will generate new response for a last message.
                contentDynamic.parent_msg_id = parentMsgId;
            }
            // Or set new (swiped) answer as one to reply on.
            else if (primaryMsgId is not null)
            {   // Provide primary_msg_id to point out which character's response you've chosen.
                contentDynamic.primary_msg_id = primaryMsgId;
                // (seen_msg_ids[] is also required, either it just won't work, but I didn't bother to collect
                //  every single swiped message, just fill it with chosen one)
                contentDynamic.seen_msg_ids = new ulong[] { (ulong)primaryMsgId };
            }

            // Prepare request content data
            var requestContent = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(contentDynamic)));
            requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            // Create request
            HttpRequestMessage request = new(HttpMethod.Post, "https://beta.character.ai/chat/streaming/");
            request.Content = requestContent;
            request.Headers.Add("accept-encoding", "gzip");

            // Send request
            var response = await _httpClient.SendAsync(request);

            return new CharacterResponse(response);
        }

        // Get info about character
        public async Task<Character> GetInfoAsync(string? characterId = null)
        {
            string url = "https://beta.character.ai/chat/character/info/";

            HttpRequestMessage request = new(HttpMethod.Post, url);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                { "external_id", characterId ?? _currentCharacter.Id! }
            });

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            dynamic? character = null;

            if (response.IsSuccessStatusCode)
                character = JsonConvert.DeserializeObject<dynamic>(content)?.character;
            else
                Failure(response: response);

            return new Character(character);
        }

        // Test
        //public async Task<Character> GetInfoNew(string? characterId = null)
        //{
        //    string url = $"https://beta.character.ai/chat/character/info-cached/{characterId ?? _currentCharacter.Id!}";
        //    HttpRequestMessage request = new(HttpMethod.Get, url);
        //    request = SetHeadersForRequest(request);

        //    var response = await _httpClient.SendAsync(request);
        //    var content = await response.Content.ReadAsStringAsync();
        //    Success(content);

        //    dynamic? character = null;

        //    if (response.IsSuccessStatusCode)
        //        character = JsonConvert.DeserializeObject<dynamic>(content)?.character;
        //    else
        //        Failure(response: response);

        //    return new Character(character);
        //}

        // Fetch last chat histoty or create one
        // returns chat history id if successful
        // returns null if fails
        public async Task<string?> GetLastChatAsync(string? characterId = null)
        {
            HttpRequestMessage request = new(HttpMethod.Post, "https://beta.character.ai/chat/history/continue/");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                { "character_external_id", characterId ?? _currentCharacter.Id! }
            });

            using var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<dynamic>(content)?.external_id;
            }

            return await CreateNewChatAsync(characterId);
        }

        // Create new chat with a character
        // returns chat history id if successful
        // returns null if fails
        public async Task<string?> CreateNewChatAsync(string? characterId = null)
        {
            HttpRequestMessage request = new(HttpMethod.Post, "https://beta.character.ai/chat/history/create/");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                { "character_external_id", characterId ?? _currentCharacter.Id! },
                { "override_history_set", null! }
            });

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Failure(response: response);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var externalId = JsonConvert.DeserializeObject<dynamic>(content)?.external_id;
            if (externalId is null)
                Failure("Something went wrong...", response: response);

            return externalId;
        }

        // not working
        public async Task<HistoriesResponse> GetHistoriesAsync(string? characterId = null)
        {
            HttpRequestMessage request = new(HttpMethod.Post, "https://beta.character.ai/chat/character/histories/");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                { "external_id", characterId ?? _currentCharacter.Id! },
                { "number", "50" } // Idk what it is. Probably an amount of chats to show. Default value is 50, so I'll just leave it like this.
            });
            request.Headers.Add("accept-encoding", "gzip");


            var response = await _httpClient.SendAsync(request);

            return new HistoriesResponse(response);
        }

        // Search for a character
        public async Task<SearchResponse> SearchAsync(string query)
        {
            string url = $"https://beta.character.ai/chat/characters/search/?query={query}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            using var response = await _httpClient.SendAsync(request);

            return new SearchResponse(response);
        }

        // Upload image on a server. Use it to attach image to your reply.
        // returns image path if successful
        // returns null if fails
        public async Task<string?> UploadImageAsync(byte[] img)
        {
            var image = new ByteArrayContent(img);
            image.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://beta.character.ai/chat/upload-image/");
            request.Content = new MultipartFormDataContent { { image, "\"image\"", $"\"image.jpg\"" } };

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Failure(response: response);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<dynamic>(content)?.value;
        }

        private HttpClient CreateHttpClient()
        {
            var httpClient = new HttpClient();
            var headers = new string[]
            {
                "Accept", "text/html,application/xhtml+xml,application/json,application/xml,text/plain,*/*",
                "Authorization", $"Token {_userToken}",
                "accept-Language", "en-US;q=0.8,en;q=0.7",
                "Accept-Charset", "ISO-8859-1",
                "accept-encoding", "deflate, br",
                "ContentType", "application/json",
                "dnt", "1",
                "Origin", "https://beta.character.ai",
                "Referer", $"https://beta.character.ai/" + (_currentCharacter?.Id is null ? "search?" : $"chat?char={_currentCharacter.Id}"),
                "sec-ch-ua", "\"Chromium\";v=\"109\", \"Not_A Brand\";v=\"99\"",
                "sec-ch-ua-mobile", "?0",
                "sec-fetch-dest", "empty",
                "sec-fetch-mode", "cors",
                "sec-fetch-site", "same-origin",
                "User-Agent", "Chrome/108.0.0.0"
            };

            for (int i = 0; i < headers.Length - 1; i += 2)
                httpClient.DefaultRequestHeaders.Add(headers[i], headers[i + 1]);

            return httpClient;
        }
    }
}