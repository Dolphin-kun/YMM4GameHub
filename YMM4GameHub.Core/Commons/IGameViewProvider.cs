namespace YMM4GameHub.Core.Commons
{
    /// <summary>
    /// 各ゲームが自身のUI（View）を提供するためのインターフェース
    /// </summary>
    public interface IGameViewProvider
    {
        /// <summary>
        /// 指定されたViewModelに対応するView（WPFのUserControl等）を作成します
        /// </summary>
        /// <param name="viewModel">ゲームのViewModel</param>
        /// <returns>作成されたViewオブジェクト</returns>
        object CreateView(object viewModel);
    }
}
