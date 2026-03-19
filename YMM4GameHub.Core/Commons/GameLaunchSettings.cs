namespace YMM4GameHub.Core.Commons
{
    /// <summary>
    /// ゲームの起動モード。
    /// </summary>
    public enum GameLaunchMode
    {
        /// <summary>
        /// オフライン（ローカル対戦）。
        /// </summary>
        Offline,

        /// <summary>
        /// オンライン（ランダムマッチ。自動的にホストまたはクライアントになる）。
        /// </summary>
        RandomMatch,

        /// <summary>
        /// カスタムルーム作成（ホスト）。
        /// </summary>
        CreateRoom,

        /// <summary>
        /// カスタムルーム参加（クライアント）。
        /// </summary>
        JoinRoom,

        /// <summary>
        /// ルーム形式（ホストまたはクライアント）。
        /// </summary>
        Room
    }

    /// <summary>
    /// ゲーム起動時の設定。
    /// </summary>
    public class GameLaunchSettings
    {
        public GameLaunchMode Mode { get; set; }
        public string? RoomId { get; set; }
        public bool IsHost { get; set; }
    }
}
