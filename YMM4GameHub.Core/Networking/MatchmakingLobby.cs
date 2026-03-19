using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace YMM4GameHub.Core.Networking
{
    public class LobbyCountsEventArgs(Dictionary<string, int> counts, int totalMatching, int totalOnline) : EventArgs
    {
        public Dictionary<string, int> Counts { get; } = counts;
        public int TotalMatching { get; } = totalMatching;
        public int TotalOnline { get; } = totalOnline;
    }

    public class MatchFoundEventArgs(string gameId, string roomId) : EventArgs
    {
        public string GameId { get; } = gameId;
        public string RoomId { get; } = roomId;
    }

    public class MatchmakingLobby : IDisposable
    {
        public string BaseUrl { get; }
        private readonly string _playerId;
        private readonly Uri _uri;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;

        public event EventHandler<LobbyCountsEventArgs>? CountsUpdated;
        public event EventHandler<MatchFoundEventArgs>? MatchFound;

        public MatchmakingLobby(string baseUrl, string playerId)
        {
            BaseUrl = baseUrl;
            _playerId = playerId;
            var uri = new Uri(baseUrl);
            var wsScheme = uri.Scheme == "https" ? "wss" : uri.Scheme == "http" ? "ws" : uri.Scheme;
            var builder = new UriBuilder(uri) { Scheme = wsScheme };
            if (!builder.Path.EndsWith("/")) builder.Path += "/";
            builder.Path += "ws";
            builder.Query = $"playerId={playerId}";
            _uri = builder.Uri;
        }

        public async Task StartAsync()
        {
            await StopAsync();

            _cts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();

            try
            {
                await _webSocket.ConnectAsync(_uri, _cts.Token);
                _ = ReceiveLoopAsync(_cts.Token);
            }
            catch (Exception)
            {
                // 接続失敗してもリトライは上位で判断
            }
        }

        public async Task StopAsync()
        {
            _cts?.Cancel();
            if (_webSocket != null)
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                _webSocket.Dispose();
                _webSocket = null;
            }
            _cts?.Dispose();
            _cts = null;
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[1024 * 4];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_webSocket == null || _webSocket.State != WebSocketState.Open)
                    {
                        await Task.Delay(5000, token);
                        await StartAsync();
                        continue;
                    }

                    using var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    ms.Seek(0, System.IO.SeekOrigin.Begin);
                    using var reader = new System.IO.StreamReader(ms, Encoding.UTF8);
                    var json = await reader.ReadToEndAsync(token);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("type", out var typeProp))
                    {
                        var type = typeProp.GetString();
                        if (type == "lobby_counts")
                        {
                            var counts = new Dictionary<string, int>();
                            foreach (var prop in root.GetProperty("counts").EnumerateObject())
                            {
                                counts[prop.Name] = prop.Value.GetInt32();
                            }
                            int totalMatching = root.TryGetProperty("totalMatching", out var tm) ? tm.GetInt32() : 0;
                            int totalOnline = root.TryGetProperty("totalOnline", out var to) ? to.GetInt32() : 0;

                            CountsUpdated?.Invoke(this, new LobbyCountsEventArgs(counts, totalMatching, totalOnline));
                        }
                        else if (type == "match_found")
                        {
                            var gId = root.GetProperty("gameId").GetString() ?? "";
                            var rId = root.GetProperty("roomId").GetString() ?? "";
                            MatchFound?.Invoke(this, new MatchFoundEventArgs(gId, rId));
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!token.IsCancellationRequested)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(5000);
                        await StartAsync();
                    }, token);
                }
            }
        }

        public void Dispose()
        {
            StopAsync().Wait();
            GC.SuppressFinalize(this);
        }
    }
}
