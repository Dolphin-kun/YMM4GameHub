namespace YMM4GameHub.Core.Networking
{
    /// <summary>
    /// リモートレジストリから取得するゲーム情報のモデル
    /// </summary>
    public class RemoteGameInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "";
        public string Author { get; set; } = "";
        public string Category { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
        public string DllFileName { get; set; } = "";
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
    }

    public class GameRegistryResponse
    {
        public List<RemoteGameInfo> Games { get; set; } = new();
    }

    public static class GameRegistry
    {
        private const string RegistryUrl = "https://ymm4-game-hub.dolphin-discord-js.workers.dev/games";
        private static readonly System.Net.Http.HttpClient HttpClient = new();

        /// <summary>
        /// リモートから利用可能なゲーム一覧を取得します（起動時1回想定）
        /// </summary>
        public static async Task<List<RemoteGameInfo>> FetchGamesAsync()
        {
            try
            {
                var response = await HttpClient.GetAsync(RegistryUrl);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var result = System.Text.Json.JsonSerializer.Deserialize<GameRegistryResponse>(json);
                return result?.Games ?? new List<RemoteGameInfo>();
            }
            catch
            {
                return new List<RemoteGameInfo>();
            }
        }

        /// <summary>
        /// 指定されたURLからDLLをダウンロードし、ローカルに保存します
        /// </summary>
        public static async Task<bool> DownloadGameAsync(RemoteGameInfo game, string destinationFolder)
        {
            try
            {
                if (!System.IO.Directory.Exists(destinationFolder))
                    System.IO.Directory.CreateDirectory(destinationFolder);

                var destinationPath = System.IO.Path.Combine(destinationFolder, game.DllFileName);
                
                var response = await HttpClient.GetAsync(game.DownloadUrl);
                response.EnsureSuccessStatusCode();

                using var fs = new System.IO.FileStream(destinationPath, System.IO.FileMode.Create);
                await response.Content.CopyToAsync(fs);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
