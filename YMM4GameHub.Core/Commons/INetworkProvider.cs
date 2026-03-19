namespace YMM4GameHub.Core.Commons
{
    /// <summary>
    /// すべての通信プロバイダーの基本インターフェース。
    /// P2P, WebSocket, Relay等をこのインターフェースを通じて切り替え可能にします。
    /// </summary>
    public interface INetworkProvider
    {
        /// <summary>
        /// 指定されたURL（サーバー）に接続します。
        /// </summary>
        Task ConnectAsync(string url);

        /// <summary>
        /// 接続を終了します。
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// メッセージを送信します。
        /// </summary>
        /// <param name="targetId">送信先のID。nullの場合は全員（部屋全体）。</param>
        /// <param name="message">送信するオブジェクト。Json等にシリアライズされます。</param>
        Task SendAsync(string? targetId, object message);

        /// <summary>
        /// メッセージを受信した際のイベント。
        /// </summary>
        /// <remarks>送信者ID, メッセージオブジェクト</remarks>
        event Action<string, object> MessageReceived;

        /// <summary>
        /// 接続が切断された際のイベント。
        /// </summary>
        event Action Disconnected;

        /// <summary>
        /// 自身のプレイヤーID。
        /// </summary>
        string LocalPlayerId { get; }

        /// <summary>
        /// 現在のルームID。
        /// </summary>
        string? RoomId { get; }

        /// <summary>
        /// 現在の通信レイテンシ（ミリ秒）。
        /// </summary>
        long Latency { get; }
    }
}
