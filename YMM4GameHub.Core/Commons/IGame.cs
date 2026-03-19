namespace YMM4GameHub.Core.Commons
{
    /// <summary>
    /// サポートされるプレイヤー人数。
    /// </summary>
    public record struct PlayerCountRange(int Min, int Max);

    /// <summary>
    /// ゲームの基本インターフェース。
    /// </summary>
    public interface IGame
    {
        /// <summary>
        /// ゲームの表示名。
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// アセンブリ名（DLLのファイル名に対応する識別子）。
        /// </summary>
        string AssemblyName { get; }

        /// <summary>
        /// ゲームの一意な名前。
        /// </summary>
        string GameKey { get; }

        /// <summary>
        /// ゲームのバージョン。
        /// </summary>
        string? Version { get; }

        /// <summary>
        /// 作成者。
        /// </summary>
        string? Author { get; }

        /// <summary>
        /// ゲームのカテゴリ。
        /// </summary>
        GameCategory Category { get; }

        /// <summary>
        /// 自身のプロフィール情報。
        /// </summary>
        PlayerProfile LocalProfile { get; }

        /// <summary>
        /// サポートされているプレイヤー人数の範囲。
        /// </summary>
        PlayerCountRange SupportedPlayerCounts { get; }

        /// <summary>
        /// ゲームの開始
        /// </summary>
        Task StartAsync(INetworkProvider network, GameLaunchSettings settings);

        /// <summary>
        /// ゲームの終了
        /// </summary>
        Task StopAsync();
    }
}
