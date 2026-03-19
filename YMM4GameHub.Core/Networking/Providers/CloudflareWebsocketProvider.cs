using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using YMM4GameHub.Core.Commons;

namespace YMM4GameHub.Core.Networking.Providers
{
    /// <summary>
    /// Cloudflare Workersを使用したWebSocket通信プロバイダー
    /// </summary>
    public class CloudflareWebsocketProvider : INetworkProvider, IDisposable
    {
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;
        string? _roomId;
        readonly string _playerId;

        public event Action<string, object>? MessageReceived;
        public event Action? Disconnected;

        public string LocalPlayerId => _playerId;
        public string? RoomId => _roomId;
        public long Latency { get; private set; }

        private DateTime _pingStartTime;

        public CloudflareWebsocketProvider()
        {
            // 簡易的なプレイヤーIDの生成
            _playerId = Guid.NewGuid().ToString("N")[..8];
        }

        public async Task ConnectAsync(string url)
        {
            if (_webSocket != null)
            {
                await DisconnectAsync();
            }

            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            _roomId = query["roomId"];

            try
            {
                await _webSocket.ConnectAsync(uri, CancellationToken.None);
                
                // 受信ループの開始
                _ = ReceiveLoopAsync();
                _ = PingLoopAsync();
            }
            catch
            {
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_webSocket != null)
            {
                _cts?.Cancel();
                if (_webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                    catch { }
                }
                _webSocket.Dispose();
                _webSocket = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public async Task SendAsync(string? targetId, object message)
        {
            if (_webSocket?.State != WebSocketState.Open)
            {
                return;
            }

            // 通信用のエンベロープ（送信者情報などを含む）を作成
            var payload = new
            {
                senderId = _playerId,
                targetId,
                data = message
            };

            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[1024 * 4];
            var cts = _cts;
            if (_webSocket == null || cts == null) return;

            // Dispose後のTokenアクセスを防ぐため、最初にとTokenを取得しておく
            CancellationToken token;
            try
            {
                token = cts.Token;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                while (_webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await DisconnectAsync();
                            Disconnected?.Invoke();
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    
                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        
                        string senderId = root.GetProperty("senderId").GetString() ?? "unknown";
                        var data = root.GetProperty("data");
                        
                        // サーバーまたは他プレイヤーからの特殊メッセージを確認
                        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("type", out var typeProp))
                        {
                            var type = typeProp.GetString();
                            if (senderId == "server" && type == "player_disconnected")
                            {
                                Disconnected?.Invoke();
                                return;
                            }
                            if (type == "ping")
                            {
                                _ = SendAsync(senderId, new { type = "pong" });
                                continue;
                            }
                            if (type == "pong")
                            {
                                Latency = (long)(DateTime.Now - _pingStartTime).TotalMilliseconds;
                                continue;
                            }
                        }

                        // メッセージ受信イベントの発行
                        MessageReceived?.Invoke(senderId, data);
                    }
                    catch (Exception)
                    {
                        MessageReceived?.Invoke("server", json);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { } // 追加のガード
            catch (Exception)
            {
                if (_webSocket != null)
                {
                    await DisconnectAsync();
                }
                Disconnected?.Invoke();
                return;
            }

            // ループが正常終了したが意図しない切断（サーバー側からの切断など）の場合
            try
            {
                if (!token.IsCancellationRequested)
                {
                    Disconnected?.Invoke();
                }
            }
            catch (ObjectDisposedException) { }
        }

        private async Task PingLoopAsync()
        {
            var cts = _cts;
            if (cts == null) return;

            while (_webSocket?.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
            {
                _pingStartTime = DateTime.Now;
                await SendAsync("server", new { type = "ping" });
                await Task.Delay(10000, cts.Token); // 10秒ごとにPing
            }
        }

        public void Dispose()
        {
            DisconnectAsync().Wait();
            GC.SuppressFinalize(this);
        }
    }
}
