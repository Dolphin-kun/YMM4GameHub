using System.Collections.ObjectModel;
using System.Windows.Input;
using System.IO;
using YMM4GameHub.Core.Networking;
using YMM4GameHub.Core.Networking.Providers;
using YukkuriMovieMaker.Commons;
using YMM4GameHub.Core.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace YMM4GameHub.ViewModels
{
    internal class YMM4GameHubViewModel : Bindable, ITimelineToolViewModel
    {
        public ObservableCollection<GameDisplayItem> AvailableGames { get; } = [];

        private GameDisplayItem? _selectedGame;
        public GameDisplayItem? SelectedGame
        {
            get => _selectedGame;
            set
            {
                var previous = _selectedGame;
                if (Set(ref _selectedGame, value))
                {
                    (StartGameCommand as ActionCommand)?.RaiseCanExecuteChanged();
                    (CreateRoomCommand as ActionCommand)?.RaiseCanExecuteChanged();
                    (JoinRoomCommand as ActionCommand)?.RaiseCanExecuteChanged();
                    (DownloadCommand as ActionCommand)?.RaiseCanExecuteChanged();
                    (RandomMatchCommand as ActionCommand)?.RaiseCanExecuteChanged();
                    
                    if (previous != null) _ = LeaveMatchmakingAsync(previous.Id);
                    UpdateMatchStatusFromLobby();
                }
            }
        }

        private readonly MatchmakingLobby _lobby;
        private Dictionary<string, int> _waitingCounts = [];

        private string _matchStatus = "ステータスを確認中...";
        public string MatchStatus
        {
            get => _matchStatus;
            set => Set(ref _matchStatus, value);
        }

        private int _totalMatchingUsers;
        public int TotalMatchingUsers
        {
            get => _totalMatchingUsers;
            set => Set(ref _totalMatchingUsers, value);
        }

        private int _totalOnlineUsers;
        public int TotalOnlineUsers
        {
            get => _totalOnlineUsers;
            set => Set(ref _totalOnlineUsers, value);
        }

        public UserSettings UserSettings => YMM4GameHubSettings.Default.UserSettings;

        private bool _isLaunchMenuVisible;
        public bool IsLaunchMenuVisible
        {
            get => _isLaunchMenuVisible;
            set => Set(ref _isLaunchMenuVisible, value);
        }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set => Set(ref _isProcessing, value);
        }

        public ICommand StartGameCommand { get; }
        public ICommand RequestLaunchCommand { get; }
        public ICommand SelectLaunchModeCommand { get; }
        public ICommand CancelLaunchCommand { get; }
        public ICommand StartMatchmakingCommand { get; }
        public ICommand CreateRoomCommand { get; }
        public ICommand JoinRoomCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand RandomMatchCommand { get; }
        public ICommand CancelMatchmakingCommand { get; }
        public ICommand ToggleLevelDetailCommand { get; }

        private string _roomId = "";
        public string RoomId
        {
            get => _roomId;
            set
            {
                if (Set(ref _roomId, value))
                {
                    (JoinRoomCommand as ActionCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        // ----  Level Props (UI 表示用)  ----
        private int _level;
        public int Level { get => _level; private set => Set(ref _level, value); }

        private double _levelPoints;
        public double LevelPoints { get => _levelPoints; private set => Set(ref _levelPoints, value); }

        /// <summary>現在レベル内のプログレス（0～100）。</summary>
        private double _levelProgressPercent;
        public double LevelProgressPercent { get => _levelProgressPercent; private set => Set(ref _levelProgressPercent, value); }

        private double _nextLevelThreshold;
        public double NextLevelThreshold { get => _nextLevelThreshold; private set => Set(ref _nextLevelThreshold, value); }

        private double _remainingPoints;
        public double RemainingPoints { get => _remainingPoints; private set => Set(ref _remainingPoints, value); }

        private bool _isLevelDetailOpen;
        public bool IsLevelDetailOpen { get => _isLevelDetailOpen; set => Set(ref _isLevelDetailOpen, value); }

        private bool _isGainingPoints;
        public bool IsGainingPoints { get => _isGainingPoints; set => Set(ref _isGainingPoints, value); }

        private bool _isRecentlyPenalized;
        public bool IsRecentlyPenalized { get => _isRecentlyPenalized; set => Set(ref _isRecentlyPenalized, value); }

        private System.Windows.Threading.DispatcherTimer? _penaltyTimer;

        private void RefreshLevelFromManager()
        {
            var data = LevelManager.GetData();
            UpdateLevelProps(data);
        }

        private void UpdateLevelProps(LevelData data)
        {
            Level = data.Level;
            LevelPoints = data.Points;
            
            double threshold = LevelManager.GetThreshold();
            NextLevelThreshold = threshold;
            RemainingPoints = Math.Max(0, threshold - data.Points);
            
            LevelProgressPercent = threshold > 0
                ? Math.Clamp(data.Points / threshold * 100.0, 0, 100)
                : 0;

            IsGainingPoints = LevelManager.IsGainingPoints;
        }

        // ---- ITimelineToolViewModel ----
        public void SetTimelineToolInfo(TimelineToolInfo info)
        {
            LevelManager.SetUndoRedoManager(info.UndoRedoManager);
        }

        public YMM4GameHubViewModel()
        {
            StartGameCommand = new ActionCommand(_ => SelectedGame?.IsInstalled == true, _ => RequestLaunch());
            RequestLaunchCommand = new ActionCommand(_ => SelectedGame?.IsInstalled == true, _ => RequestLaunch());
            SelectLaunchModeCommand = new ActionCommand(_ => SelectedGame?.IsInstalled == true, async mode => await SelectLaunchModeAsync((GameLaunchMode)mode!));
            CancelLaunchCommand = new ActionCommand(_ => true, _ => SelectedGame = null);
            
            CreateRoomCommand = new ActionCommand(_ => SelectedGame?.IsInstalled == true, async _ => await CreateRoomAsync());
            JoinRoomCommand = new ActionCommand(_ => SelectedGame?.IsInstalled == true && !string.IsNullOrEmpty(RoomId), async _ => await JoinRoomAsync());
            DownloadCommand = new ActionCommand(_ => SelectedGame?.IsInstalled == false, async _ => await DownloadGameAsync());
            RandomMatchCommand = new ActionCommand(_ => SelectedGame?.IsInstalled == true, async _ => await StartRandomMatchAsync());
            CancelMatchmakingCommand = new ActionCommand(_ => IsProcessing, async _ => await CancelRandomMatchAsync());
            StartMatchmakingCommand = RandomMatchCommand;
            ToggleLevelDetailCommand = new ActionCommand(_ => true, _ => IsLevelDetailOpen = !IsLevelDetailOpen);

            _lobby = new MatchmakingLobby("wss://ymm4-game-hub.dolphin-discord-js.workers.dev/matchmaking", UserSettings.UserId);
            _lobby.CountsUpdated += (s, e) =>
            {
                _waitingCounts = e.Counts;
                
                // 公開されている（RemoteInfoがある）ゲームのみの合計を計算する
                var publicGameIds = AvailableGames.Where(g => g.RemoteInfo != null).Select(g => g.Id).ToHashSet();
                TotalMatchingUsers = e.Counts
                    .Where(kvp => publicGameIds.Contains(kvp.Key))
                    .Sum(kvp => kvp.Value);

                TotalOnlineUsers = e.TotalOnline;
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(UpdateMatchStatusFromLobby));
            };
            _lobby.MatchFound += (s, e) =>
            {
                if (SelectedGame?.Id == e.GameId && IsProcessing)
                {
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(async () =>
                    {
                        var settings = new GameLaunchSettings
                        {
                            Mode = GameLaunchMode.RandomMatch,
                            RoomId = e.RoomId,
                            IsHost = true
                        };
                        await HandleMatchSuccessAsync(e.RoomId, settings);
                    }));
                }
            };
            _ = _lobby.StartAsync();

            // レベルデータを初期化
            RefreshLevelFromManager();
            LevelManager.LevelChanged += (s, data) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    UpdateLevelProps(data));
            };

            LevelManager.PenaltyApplied += () =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsRecentlyPenalized = true;
                    _penaltyTimer?.Stop();
                    _penaltyTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    _penaltyTimer.Tick += (s, e) =>
                    {
                        IsRecentlyPenalized = false;
                        _penaltyTimer.Stop();
                    };
                    _penaltyTimer.Start();
                });
            };

            _ = DiscoverGamesAsync();
        }

        private async Task HandleMatchSuccessAsync(string roomId, GameLaunchSettings settings)
        {
            var window = await StartGameWithNetworkAsync(roomId, settings);
            if (window != null)
            {
                MatchStatus = "対戦中";
                window.Closed += (s, e) =>
                {
                    IsProcessing = false;
                    UpdateMatchStatusFromLobby();
                };
            }
            else
            {
                IsProcessing = false;
                UpdateMatchStatusFromLobby();
            }
        }

        private void UpdateMatchStatusFromLobby()
        {
            foreach (var item in AvailableGames)
            {
                item.WaitingCount = _waitingCounts.TryGetValue(item.Id, out var c) ? c : 0;
            }

            if (SelectedGame == null || IsProcessing) return;

            if (_waitingCounts.TryGetValue(SelectedGame.Id, out int count) && count > 0)
            {
                MatchStatus = $"オンライン対戦 ({count}人待機中)";
            }
            else
            {
                MatchStatus = "オンライン対戦";
            }
        }

        private void RequestLaunch()
        {
            if (SelectedGame?.IsInstalled == true)
            {
                IsLaunchMenuVisible = true;
            }
        }

        private async Task SelectLaunchModeAsync(GameLaunchMode mode)
        {
            IsLaunchMenuVisible = false;
            
            if (mode == GameLaunchMode.RandomMatch)
            {
                await StartRandomMatchAsync();
            }
            else if (mode == GameLaunchMode.Offline)
            {
                var settings = new GameLaunchSettings { Mode = GameLaunchMode.Offline, IsHost = true };
                await StartGameWithNetworkAsync("offline", settings);
            }
        }

        private async Task LeaveMatchmakingAsync(string? gameId = null)
        {
            var targetId = gameId ?? SelectedGame?.Id;
            if (targetId != null)
            {
                await MatchmakingClient.LeaveMatchmakingAsync(targetId, UserSettings.UserId);
            }
        }

        private async Task StartRandomMatchAsync()
        {
            if (SelectedGame == null || IsProcessing) return;
            
            MatchStatus = "マッチング中...";
            IsProcessing = true;
            (CancelMatchmakingCommand as ActionCommand)?.RaiseCanExecuteChanged();

            try
            {
                var result = await MatchmakingClient.StartMatchmakingAsync(SelectedGame.Id, UserSettings.UserId);
                if (result == null)
                {
                    System.Windows.MessageBox.Show("マッチングに失敗しました。サーバーが混み合っているか、オフラインの可能性があります。");
                    IsProcessing = false;
                    UpdateMatchStatusFromLobby();
                    return;
                }

                if (result.IsHost && SelectedGame.MinPlayers >= 2)
                {
                    // 2人以上のゲーム且つホストの場合は、対戦相手が来るまで待機（ウィンドウを開かない）
                    MatchStatus = "対戦相手を探しています...";
                    return; 
                }

                MatchStatus = result.IsHost ? "対戦待機中..." : "参加中...";

                var settings = new GameLaunchSettings
                {
                    Mode = GameLaunchMode.RandomMatch,
                    RoomId = result.RoomId,
                    IsHost = result.IsHost
                };
                
                // ホストが接続するのを少し待つ（マッチング成功直後に参加者が入ると、ホストがまだ準備中なことがあるため）
                if (!result.IsHost)
                {
                    MatchStatus = "接続中...";
                    await Task.Delay(800);
                }
                
                await HandleMatchSuccessAsync(result.RoomId, settings);
            }
            catch (Exception)
            {
                IsProcessing = false;
                (CancelMatchmakingCommand as ActionCommand)?.RaiseCanExecuteChanged();
                UpdateMatchStatusFromLobby();
            }
        }

        private async Task CancelRandomMatchAsync()
        {
            if (!IsProcessing) return;
            
            await LeaveMatchmakingAsync();
            IsProcessing = false;
            (CancelMatchmakingCommand as ActionCommand)?.RaiseCanExecuteChanged();
            UpdateMatchStatusFromLobby();
        }

        private async Task DiscoverGamesAsync()
        {
            AvailableGames.Clear();

            var gameTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IGame).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            var installedIds = new HashSet<string>();
            foreach (var type in gameTypes)
            {
                if (Activator.CreateInstance(type) is IGame game)
                {
                    AvailableGames.Add(new GameDisplayItem(game));
                    installedIds.Add(game.AssemblyName);
                }
            }

            var remoteGames = await GameRegistry.FetchGamesAsync();
            var localItemsMap = AvailableGames.Where(i => i.IsInstalled).ToDictionary(i => i.LocalGame!.AssemblyName);

            foreach (var remote in remoteGames)
            {
                if (localItemsMap.TryGetValue(remote.Id, out var localItem))
                {
                    localItem.MergeRemoteInfo(remote);
                }
                else
                {
                    AvailableGames.Add(new GameDisplayItem(remote));
                }
            }
        }

        private async Task DownloadGameAsync()
        {
            if (SelectedGame?.RemoteInfo == null) return;
            
            var gamesDir = Path.Combine(AppDirectories.PluginDirectory, "YMM4GameHub", "Games");
            var success = await GameRegistry.DownloadGameAsync(SelectedGame.RemoteInfo, gamesDir);
            
            if (success)
            {
                System.Windows.MessageBox.Show("ダウンロードが完了しました。YMM4を再起動すると反映されます。");
            }
            else
            {
                System.Windows.MessageBox.Show("ダウンロードに失敗しました。");
            }
        }

        private async Task CreateRoomAsync()
        {
            if (SelectedGame?.LocalGame == null) return;
            var roomId = Guid.NewGuid().ToString("N")[..6];
            RoomId = roomId;
            var settings = new GameLaunchSettings { Mode = GameLaunchMode.Room, RoomId = roomId, IsHost = true };
            await StartGameWithNetworkAsync(roomId, settings);
        }

        private async Task JoinRoomAsync()
        {
            if (SelectedGame?.LocalGame == null || string.IsNullOrEmpty(RoomId)) return;
            var settings = new GameLaunchSettings { Mode = GameLaunchMode.Room, RoomId = RoomId, IsHost = false };
            await StartGameWithNetworkAsync(RoomId, settings);
        }

        private static Views.GameWindow ShowGameWindow(IGame game, string roomInfo, GameLaunchMode mode)
        {
            var title = (mode == GameLaunchMode.RandomMatch) ? game.Name : $"{game.Name} - {roomInfo}";
            var window = new Views.GameWindow
            {
                Title = title
            };

            if (game is IGameViewProvider viewProvider)
            {
                var view = viewProvider.CreateView(game);
                window.SetContent(view);
            }

            window.Closed += async (s, e) =>
            {
                await game.StopAsync();
                LevelManager.SetGameActive(false);
                LevelManager.ApplyGamePenalty();
            };

            LevelManager.SetGameActive(true);
            window.Show();
            return window;
        }

        private async Task<Views.GameWindow?> StartGameWithNetworkAsync(string roomId, GameLaunchSettings settings)
        {
            if (SelectedGame?.LocalGame == null) return null;
            var game = SelectedGame.LocalGame;

            var provider = new CloudflareWebsocketProvider();
            try
            {
                if (settings.Mode != GameLaunchMode.Offline)
                {
                    // MatchmakingLobby.BaseUrl (wss://...) からゲーム用URLを構築
                    var wsBase = _lobby.BaseUrl.TrimEnd('/').Replace("/matchmaking", "");
                    var wsUrl = $"{wsBase}/ws?roomId={roomId}&playerId={provider.LocalPlayerId}";
                    
                    await provider.ConnectAsync(wsUrl);
                }
                
                game.LocalProfile.Name = UserSettings.UserName;
                await game.StartAsync(provider, settings);
                var window = ShowGameWindow(game, $"Room: {roomId}", settings.Mode);

                provider.Disconnected += () =>
                {
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        System.Windows.MessageBox.Show("通信エラーが発生しました。ゲームを終了します。");
                        window.Close();
                    }));
                };
                return window;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"ゲームの開始に失敗しました: {ex.Message}");
                return null;
            }
        }
    }

    public class GameDisplayItem : Bindable
    {
        public bool IsInstalled { get; }
        public IGame? LocalGame { get; }
        public YMM4GameHub.Core.Networking.RemoteGameInfo? RemoteInfo { get; internal set; }

        public string Id => IsInstalled ? LocalGame!.AssemblyName : RemoteInfo!.Id;
        public string Name => IsInstalled ? LocalGame!.Name : RemoteInfo!.Name;
        public string Category => IsInstalled ? LocalGame!.Category.ToString() : RemoteInfo!.Category;
        public string Version => IsInstalled ? LocalGame!.Version ?? "Unknown" : RemoteInfo!.Version;
        public string Author => IsInstalled ? LocalGame!.Author ?? "Unknown" : RemoteInfo!.Author;
        public string? ThumbnailUrl => string.IsNullOrEmpty(RemoteInfo?.ThumbnailUrl) ? null : RemoteInfo!.ThumbnailUrl;
        public string Description => RemoteInfo?.Description ?? "";

        public int MinPlayers => IsInstalled ? LocalGame!.SupportedPlayerCounts.Min : (RemoteInfo?.MinPlayers ?? 0);
        public int MaxPlayers => IsInstalled ? LocalGame!.SupportedPlayerCounts.Max : (RemoteInfo?.MaxPlayers ?? 0);

        public bool IsSinglePlaySupported => MinPlayers <= 1 && MaxPlayers >= 1;
        public bool IsMultiplaySupported => MaxPlayers >= 2;

        public string PlayerCountText
        {
            get
            {
                if (MinPlayers == 0 || MaxPlayers == 0) return "不明";
                return MinPlayers == MaxPlayers ? $"{MinPlayers}人" : $"{MinPlayers}-{MaxPlayers}人";
            }
        }

        public bool IsUpdateAvailable => IsInstalled && RemoteInfo != null && Version != RemoteInfo.Version;

        private int _waitingCount;
        public int WaitingCount
        {
            get => _waitingCount;
            set => Set(ref _waitingCount, value);
        }

        public GameDisplayItem(IGame local)
        {
            IsInstalled = true;
            LocalGame = local;
        }

        public GameDisplayItem(YMM4GameHub.Core.Networking.RemoteGameInfo remote)
        {
            IsInstalled = false;
            RemoteInfo = remote;
        }

        public void MergeRemoteInfo(YMM4GameHub.Core.Networking.RemoteGameInfo remote)
        {
            RemoteInfo = remote;
            OnPropertyChanged(nameof(ThumbnailUrl));
            OnPropertyChanged(nameof(IsUpdateAvailable));
        }
    }
}
