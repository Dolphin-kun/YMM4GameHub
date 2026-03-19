using System.Text.Json;
using YukkuriMovieMaker.Commons;

namespace YMM4GameHub.Core.Commons
{
    /// <summary>
    /// ゲーム開発を容易にするためのベースクラス。
    /// 状態の同期やメッセージのディスパッチをハンドルします。
    /// </summary>
    /// <typeparam name="TState">ゲームの状態を表すクラス。</typeparam>
    public abstract class GameBase<TState> : Bindable, IGame where TState : class, new()
    {
        public INetworkProvider? Network { get; private set; }
        public TState State { get; protected set; } = new();
        protected bool IsHost { get; private set; }
        public long Latency => Network?.Latency ?? 0;

        public class PlayerInfo : Bindable
        {
            public string Id { get; set; } = "";
            private PlayerProfile profile = new();
            public PlayerProfile Profile { get => profile; set => Set(ref profile, value); }
            private long latency;
            public long Latency { get => latency; set => Set(ref latency, value); }
        }

        public System.Collections.Concurrent.ConcurrentDictionary<string, PlayerInfo> PlayerInfos { get; } = new();
        public PlayerProfile LocalProfile { get; set; } = new();
        public IEnumerable<PlayerInfo> AllPlayers => new[] { new PlayerInfo { Id = Network?.LocalPlayerId ?? "", Profile = LocalProfile, Latency = Latency } }
            .Concat(PlayerInfos.Values);

        public abstract string Name { get; }
        public virtual string AssemblyName => GetType().Assembly.GetName().Name ?? "";
        public abstract string GameKey { get; }
        public abstract string? Version { get; }
        public abstract string? Author { get; }
        public abstract GameCategory Category { get; }
        public virtual PlayerCountRange SupportedPlayerCounts => new(2, 2);

        public virtual async Task StartAsync(INetworkProvider network, GameLaunchSettings settings)
        {
            IsHost = settings.IsHost;
            // 開始時に状態をリセット
            State = new TState();
            PlayerInfos.Clear();
            OnPropertyChanged(nameof(State));

            Network = network;
            Network.MessageReceived += OnMessageReceivedInternal;

            // 自身のプロフィールを共有
            await Network.SendAsync(null, new ProfileSyncMessage { Profile = LocalProfile });

            // レイテンシ更新通知ループ
            _ = Task.Run(async () =>
            {
                while (Network != null)
                {
                    var net = Network;
                    if (net == null) break;

                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        if (Network == null) return;
                        OnPropertyChanged(nameof(Latency));
                        OnPropertyChanged(nameof(AllPlayers));
                    }));
                    
                    try
                    {
                        // プロフィールとレイテンシを共有
                        await net.SendAsync(null, new ProfileSyncMessage { Profile = LocalProfile, Latency = Latency });
                    }
                    catch { break; }
                    
                    await Task.Delay(1000);
                }
            });
        }

        public virtual async Task StopAsync()
        {
            if (Network != null)
            {
                Network.MessageReceived -= OnMessageReceivedInternal;
                await Network.DisconnectAsync();
                Network = null;
            }
        }

        /// <summary>
        /// 他のプレイヤーにアクション（コマンド）を送信し、自身の状態も更新します。
        /// </summary>
        protected async Task SendActionAsync(object action)
        {
            if (Network == null) return;

            // 自身のアクションを適用
            ApplyAction(Network.LocalPlayerId, action);

            // 他のプレイヤーに送信
            await Network.SendAsync(null, action);
        }

        private void OnMessageReceivedInternal(string senderId, object message)
        {
            var json = message.ToString() ?? "";
            
            // 特殊メッセージの判定
            if (message is JsonElement element)
            {
                if (element.TryGetProperty("Profile", out _))
                {
                    var sync = element.Deserialize<ProfileSyncMessage>();
                    if (sync != null)
                    {
                        var info = PlayerInfos.GetOrAdd(senderId, id => new PlayerInfo { Id = id });
                        info.Profile = sync.Profile;
                        info.Latency = sync.Latency;
                        
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            OnPropertyChanged(nameof(PlayerInfos));
                            OnPropertyChanged(nameof(AllPlayers));
                        }));
                    }
                    return;
                }

                if (element.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "system:joined")
                {
                    // 他のプレイヤーが入室したら、即座に自分のプロフィールを送信して同期を早める
                    _ = Network?.SendAsync(null, new ProfileSyncMessage { Profile = LocalProfile, Latency = Latency });
                    return;
                }
            }

            // 受信したアクションを状態に適用
            ApplyAction(senderId, message);
        }

        /// <summary>
        /// アクションをゲーム状態に適用するロジックを実装します。
        /// </summary>
        protected abstract void ApplyAction(string senderId, object action);

        /// <summary>
        /// 現在の状態を全プレイヤーに強制同期します。
        /// </summary>
        protected async Task BroadcastStateAsync()
        {
            if (Network == null) return;
            await Network.SendAsync(null, new StateSyncMessage { StateJson = JsonSerializer.Serialize(State) });
        }

        public class StateSyncMessage
        {
            public string StateJson { get; set; } = "";
        }

        public class ProfileSyncMessage
        {
            public PlayerProfile Profile { get; set; } = new();
            public long Latency { get; set; }
        }
    }
}
