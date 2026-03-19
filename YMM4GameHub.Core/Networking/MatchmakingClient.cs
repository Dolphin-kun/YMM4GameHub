using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace YMM4GameHub.Core.Networking
{
    public class MatchmakingResult
    {
        public string RoomId { get; set; } = "";
        public bool IsHost { get; set; }
    }

    public class MatchmakingStatus
    {
        public int WaitingCount { get; set; }
    }

    public static class MatchmakingClient
    {
        private const string BaseUrl = "https://ymm4-game-hub.dolphin-discord-js.workers.dev/matchmaking";
        private static readonly HttpClient HttpClient = new();
        private static readonly JsonSerializerOptions DefaultOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// マッチングを開始し、RoomId を取得します。
        /// </summary>
        public static async Task<MatchmakingResult?> StartMatchmakingAsync(string gameId, string playerId)
        {
            try
            {
                var response = await HttpClient.GetAsync($"{BaseUrl}?gameId={gameId}&playerId={playerId}");
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MatchmakingResult>(json, DefaultOptions);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 現在の待機人数を取得します。
        /// </summary>
        public static async Task<int> GetWaitingCountAsync(string gameId, string playerId)
        {
            try
            {
                var response = await HttpClient.GetAsync($"{BaseUrl}/status?gameId={gameId}&playerId={playerId}");
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var status = JsonSerializer.Deserialize<MatchmakingStatus>(json, DefaultOptions);
                return status?.WaitingCount ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 待機状態を解除します。
        /// </summary>
        public static async Task<bool> LeaveMatchmakingAsync(string gameId, string playerId)
        {
            try
            {
                var response = await HttpClient.GetAsync($"{BaseUrl}/leave?gameId={gameId}&playerId={playerId}");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
