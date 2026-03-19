namespace YMM4GameHub.Core.Commons
{
    /// <summary>
    /// プレイヤーのプロフィール情報。
    /// </summary>
    public class PlayerProfile
    {
        /// <summary>
        /// プレイヤー名。
        /// </summary>
        public string Name { get; set; } = "Player";

        /// <summary>
        /// レベル（将来用）。
        /// </summary>
        public int Level { get; set; } = 1;

        /// <summary>
        /// アイコンURL（将来用）。
        /// </summary>
        public string IconUrl { get; set; } = "";
    }
}
