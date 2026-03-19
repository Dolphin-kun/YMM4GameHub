using Speed.Models;
using System.Reflection;
using YukkuriMovieMaker.Commons;
using Speed.Views;
using YMM4GameHub.Core.Commons;
using YMM4GameHub.Core.Controls;

namespace Speed
{
    public class SpeedGame : GameBase<SpeedState>, IGameViewProvider
    {
        public override string Name => "スピード";
        public override string GameKey => "Speed";
        public override string? Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        public override string? Author => "いるかぁぁ";
        public override GameCategory Category => GameCategory.CardGame;
        public override PlayerCountRange SupportedPlayerCounts => new(2, 2);

        private string _status = "対戦相手を待っています...";
        public string Status
        {
            get => _status;
            set => Set(ref _status, value);
        }

        public string LocalPlayerId => Network?.LocalPlayerId ?? "";
        public List<PlayingCard> MyHand => State.Players.TryGetValue(LocalPlayerId, out var p) ? p.Hand : [];
        public int MyDeckCount => State.Players.TryGetValue(LocalPlayerId, out var p) ? p.Deck.Count : 0;
        public int OpponentHandCount => State.Players.Where(p => p.Key != LocalPlayerId).Select(p => p.Value.Hand.Count(c => c != null)).FirstOrDefault();
        public List<PlayingCard> OpponentHandSlots => State.Players.Where(p => p.Key != LocalPlayerId).Select(p => p.Value.Hand).FirstOrDefault() ?? [];
        public int OpponentDeckCount => State.Players.Where(p => p.Key != LocalPlayerId).Select(p => p.Value.Deck.Count).FirstOrDefault();
        public bool IsGameStarted => State.IsGameStarted;

        public bool IsSpectator => Network != null && !State.Players.ContainsKey(LocalPlayerId);

        private bool _isResetting;
        private readonly HashSet<PlayingCard> _pendingPlayedCards = [];
        private readonly System.Timers.Timer _pendingCleanupTimer = new(2000) { AutoReset = false };

        public SpeedGame()
        {
            _pendingCleanupTimer.Elapsed += (s, e) =>
            {
                lock (_pendingPlayedCards) _pendingPlayedCards.Clear();
                System.Windows.Application.Current?.Dispatcher?.Invoke(NotifyUI);
            };
        }

        public async Task PlayCardAsync(PlayingCard card)
        {
            if (card == null || Network == null || !State.IsGameStarted || State.WinnerPlayerId != null || IsSpectator) return;

            var index = MyHand.FindIndex(c => c != null && c.Suit == card.Suit && c.Number == card.Number);
            if (index >= 0)
            {
                // 送信したカードを一時的に非表示リストに追加
                lock (_pendingPlayedCards)
                {
                    _pendingPlayedCards.Add(card);
                    // 以前のタイマーがあればリセット
                    _pendingCleanupTimer.Stop();
                    _pendingCleanupTimer.Start();
                }
                NotifyUI(); // 即座にUIに反映して手札を消す

                await SendActionAsync(new PlayCardAction(index, -1, Network!.LocalPlayerId));
            }
        }

        private bool _isFirstPlayer;
        public override async Task StartAsync(INetworkProvider network, GameLaunchSettings settings)
        {
            await base.StartAsync(network, settings);
            _isFirstPlayer = settings.IsHost;

            if (settings.IsHost)
            {
                InitializeGame();
                // プレイヤーデータの初期化（ホスト分）
                var hostId = Network!.LocalPlayerId;
                State.Players[hostId] = new PlayerData();
                SetupPlayerState(hostId, true);
                await BroadcastStateAsync();
            }
            // 参加アクションを送信（ゲスト・ホスト共通）
            await SendActionAsync(new JoinAction(Network!.LocalPlayerId));
            NotifyUI();
        }

        private List<PlayingCard> _blackCards = []; // Host: ♠♣
        private List<PlayingCard> _redCards = [];   // Guest: ♥♦

        private void InitializeGame()
        {
            var black = new List<PlayingCard>();
            var red = new List<PlayingCard>();

            for (int i = 1; i <= 13; i++)
            {
                black.Add(new PlayingCard(CardSuit.Spades, i));
                black.Add(new PlayingCard(CardSuit.Clubs, i));
                red.Add(new PlayingCard(CardSuit.Hearts, i));
                red.Add(new PlayingCard(CardSuit.Diamonds, i));
            }

            var rnd = new Random();
            _blackCards = [.. black.OrderBy(x => rnd.Next())];
            _redCards = [.. red.OrderBy(x => rnd.Next())];

            // 台札の初期化（一旦nullにしておき、StartGameSequenceでめくる）
            State.Piles[0] = null!;
            State.Piles[1] = null!;
        }

        /// <summary>プレイヤーの初期手札とデッキをセットアップします</summary>
        private void SetupPlayerState(string playerId, bool isHost)
        {
            var pData = State.Players[playerId];
            var cards = isHost ? _blackCards : _redCards;

            // 26枚のうち: 4枚手札, 22枚山札 (うち1枚をStartで台札に使う)
            pData.Hand = [.. cards.Take(4)];
            pData.Deck = [.. cards.Skip(4)];

            while (pData.Hand.Count < 4) pData.Hand.Add(null!);
        }

        protected override async void ApplyAction(string senderId, object action)
        {
            if (action is System.Text.Json.JsonElement element)
            {
                var json = element.GetRawText();
                if (element.TryGetProperty("HandIndex", out _))
                    action = System.Text.Json.JsonSerializer.Deserialize<PlayCardAction>(json)!;
                else if (element.TryGetProperty("PlayerId", out _) && !element.TryGetProperty("data", out _))
                    action = System.Text.Json.JsonSerializer.Deserialize<JoinAction>(json)!;
                else if (element.TryGetProperty("StateJson", out _))
                {
                    var stateJson = element.GetProperty("StateJson").GetString();
                    if (!string.IsNullOrEmpty(stateJson))
                    {
                        var newState = System.Text.Json.JsonSerializer.Deserialize<SpeedState>(stateJson);
                        if (newState != null)
                        {
                            State = newState;
                            NotifyUI();
                            return;
                        }
                    }
                }
                else if (element.TryGetProperty("Type", out var typeProp) && typeProp.GetString() == "player-left")
                {
                    Status = "対戦相手が切断されました。";
                    return;
                }
            }

            if (action is JoinAction join)
            {
                if (IsHost && !State.Players.ContainsKey(join.PlayerId))
                {
                    if (State.Players.Count < 2)
                    {
                        State.Players[join.PlayerId] = new PlayerData();
                        SetupPlayerState(join.PlayerId, State.Players.Count == 1);
                        if (State.Players.Count == 2) _ = StartGameSequence();
                    }
                    await BroadcastStateAsync();
                }
            }
            else if (action is PlayCardAction play)
            {
                if (IsHost)
                {
                    HandlePlayCard(play);
                    // 確実に同期させるために即座に配信
                    await BroadcastStateAsync();
                }
            }

            NotifyUI();
        }

        private void NotifyUI()
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(MyHandViewModels));
            OnPropertyChanged(nameof(OpponentHandViewModels));
            OnPropertyChanged(nameof(MyDeckCount));
            OnPropertyChanged(nameof(OpponentHandCount));
            OnPropertyChanged(nameof(OpponentHandSlots));
            OnPropertyChanged(nameof(OpponentDeckCount));
            OnPropertyChanged(nameof(IsGameStarted));
            UpdateStatus();
        }

        private void HandlePlayCard(PlayCardAction play)
        {
            if (!State.Players.TryGetValue(play.PlayerId, out var pData)) return;
            if (play.HandIndex >= pData.Hand.Count) return;

            var card = pData.Hand[play.HandIndex];
            if (card == null) return;

            bool played = false;

            // 両方の山をチェック
            for (int i = 0; i < 2; i++)
            {
                if (IsConnectable(card.Number, State.Piles[i].Number))
                {
                    State.Piles[i] = card;
                    played = true;
                    break;
                }
            }

            if (played)
            {
                pData.Hand[play.HandIndex] = null!;
                RefillHand(play.PlayerId);
                CheckWinner();
            }

            if (IsHost) CheckStuckState();
        }

        private void CheckWinner()
        {
            if (State.WinnerPlayerId != null) return;
            foreach (var kv in State.Players)
            {
                if (kv.Value.Hand.All(c => c == null) && kv.Value.Deck.Count == 0)
                {
                    State.WinnerPlayerId = kv.Key;
                    break;
                }
            }
        }

        private void RefillHand(string playerId)
        {
            if (!State.Players.TryGetValue(playerId, out var pData)) return;

            bool refilled = false;
            // 空きスロットがあれば山札から補充
            for (int i = 0; i < pData.Hand.Count; i++)
            {
                if (pData.Hand[i] == null && pData.Deck.Count > 0)
                {
                    pData.Hand[i] = pData.Deck[0];
                    pData.Deck.RemoveAt(0);
                    refilled = true;
                }
            }

            if (refilled && IsHost) CheckStuckState();
        }

        private void CheckStuckState()
        {
            if (State.WinnerPlayerId != null || _isResetting) return;
            if (State.ResetCountdown >= 0) return; // すでにカウントダウン中

            bool canAnyPlay = false;
            foreach (var p in State.Players.Values)
            {
                foreach (var card in p.Hand)
                {
                    if (card == null) continue;
                    if ((State.Piles[0] != null && IsConnectable(card.Number, State.Piles[0].Number)) ||
                        (State.Piles[1] != null && IsConnectable(card.Number, State.Piles[1].Number)))
                    {
                        canAnyPlay = true;
                        break;
                    }
                }
                if (canAnyPlay) break;
            }

            if (!canAnyPlay)
            {
                // すぐにリセットせず、1秒待機してプレイヤーの反応を待つ
                _ = Task.Run(async () => {
                    await Task.Delay(1000);
                    // 1秒後にもう一度チェック（その間に誰かが出したかもしれない）
                    CheckStuckStateFinal();
                });
            }
        }

        private void CheckStuckStateFinal()
        {
            if (State.WinnerPlayerId != null || _isResetting || State.ResetCountdown >= 0) return;

            bool canAnyPlay = false;
            foreach (var p in State.Players.Values)
            {
                foreach (var card in p.Hand)
                {
                    if (card == null) continue;
                    if ((State.Piles[0] != null && IsConnectable(card.Number, State.Piles[0].Number)) ||
                        (State.Piles[1] != null && IsConnectable(card.Number, State.Piles[1].Number)))
                    {
                        canAnyPlay = true;
                        break;
                    }
                }
                if (canAnyPlay) break;
            }

            if (!canAnyPlay)
            {
                _ = StartResetCountdownAsync();
            }
        }

        private async Task StartGameSequence()
        {
            if (State.ResetCountdown >= 0 || _isResetting) return;
            _isResetting = true;
            try
            {
                // 開始時に各プレイヤーの山札から1枚選んで移動中状態にする
                var playerIds = State.Players.Keys.ToList();
                for (int i = 0; i < playerIds.Count && i < 2; i++)
                {
                    var pData = State.Players[playerIds[i]];
                    if (pData.Deck.Count > 0)
                    {
                        State.ResetPiles[i] = pData.Deck[0];
                        pData.Deck.RemoveAt(0);
                    }
                }

                for (int i = 3; i >= 0; i--)
                {
                    State.ResetCountdown = i;
                    
                    // カウントダウンが0になった瞬間に台札を更新
                    // これによりViewが「スピード！」の表示と同時に新しいカードのアニメーションを開始できる
                    if (i == 0)
                    {
                        for (int j = 0; j < 2; j++)
                        {
                            if (State.ResetPiles[j] != null)
                            {
                                State.Piles[j] = State.ResetPiles[j]!;
                            }
                        }
                    }

                    if (IsHost) NotifyUI();
                    await BroadcastStateAsync();
                    await Task.Delay(1000);
                }

                // リセット完了処理（データ上の不整合を防ぐ）
                for (int i = 0; i < 2; i++)
                {
                    State.ResetPiles[i] = null;
                }

                State.IsGameStarted = true;
                State.ResetCountdown = -1;

                // 初期手札の空きがあれば補充（通常は埋まっているはずだが念のため）
                foreach (var pid in State.Players.Keys) RefillHand(pid);
                CheckWinner();

                if (IsHost)
                {
                    NotifyUI();
                }
                await BroadcastStateAsync();
            }
            finally
            {
                _isResetting = false;
                if (IsHost) CheckStuckState();
            }
        }

        private async Task StartResetCountdownAsync()
        {
            if (State.ResetCountdown >= 0 || _isResetting) return;
            _isResetting = true;
            try
            {
                // 手詰め解消時に各プレイヤーの山札（なければ手札）から1枚選んで移動中状態にする
                var playerIds = State.Players.Keys.ToList();
                for (int i = 0; i < playerIds.Count && i < 2; i++)
                {
                    var pData = State.Players[playerIds[i]];
                    if (pData.Deck.Count > 0)
                    {
                        State.ResetPiles[i] = pData.Deck[0];
                        pData.Deck.RemoveAt(0);
                    }
                    else
                    {
                        var handIndex = pData.Hand.FindIndex(c => c != null);
                        if (handIndex >= 0)
                        {
                            State.ResetPiles[i] = pData.Hand[handIndex];
                            pData.Hand[handIndex] = null!;
                        }
                    }
                }

                for (int i = 3; i >= 0; i--)
                {
                    State.ResetCountdown = i;

                    if (i == 0)
                    {
                        for (int j = 0; j < 2; j++)
                        {
                            if (State.ResetPiles[j] != null)
                            {
                                State.Piles[j] = State.ResetPiles[j]!;
                            }
                        }
                    }

                    if (IsHost) NotifyUI();
                    await BroadcastStateAsync();
                    await Task.Delay(1000);
                }

                // リセット実行完了
                for (int i = 0; i < 2; i++)
                {
                    State.ResetPiles[i] = null;
                }

                State.ResetCountdown = -1;

                // リセット後に空いた手札を山札から補充
                foreach (var pid in State.Players.Keys) RefillHand(pid);
                CheckWinner();

                if (IsHost)
                {
                    NotifyUI();
                    await BroadcastStateAsync();
                }
            }
            finally
            {
                _isResetting = false;
                if (IsHost) CheckStuckState();
            }
        }

        public static bool IsConnectable(int n1, int n2)
        {
            int diff = Math.Abs(n1 - n2);
            return diff == 1 || diff == 12;
        }

        private void UpdateStatus()
        {
            if (State.WinnerPlayerId != null)
            {
                Status = State.WinnerPlayerId == LocalPlayerId ? "あなたの勝利！" : "あなたの敗北...";
                return;
            }

            if (State.ResetCountdown >= 0)
            {
                if (State.IsGameStarted)
                {
                    Status = State.ResetCountdown > 0 ? $"出せません！... {State.ResetCountdown}" : "スピード！";
                }
                else
                {
                    Status = State.ResetCountdown > 0 ? $"{State.ResetCountdown}" : "スピード！";
                }
                return;
            }

            if (State.Players.Count < 2)
            {
                Status = "対戦相手を待っています...";
            }
            else if (IsSpectator)
            {
                Status = "観戦中";
            }
            else
            {
                Status = "スピード！";
            }
        }

        public class CardViewModel(PlayingCard? card) : Bindable
        {
            private PlayingCard? _card = card;
            public PlayingCard? Card { get => _card; set => Set(ref _card, value); }
            private bool _isPlayable;
            public bool IsPlayable { get => _isPlayable; set => Set(ref _isPlayable, value); }
            private bool _isAnimating;
            public bool IsAnimating { get => _isAnimating; set => Set(ref _isAnimating, value); }
        }

        private List<CardViewModel> _myHandViewModels = [];
        public List<CardViewModel> MyHandViewModels
        {
            get
            {
                var currentHand = MyHand;
                // 最初期または不整合時に初期化
                if (_myHandViewModels.Count != 4)
                {
                    _myHandViewModels = [.. Enumerable.Range(0, 4).Select(_ => new CardViewModel(null))];
                }

                for (int i = 0; i < 4; i++)
                {
                    var card = i < currentHand.Count ? currentHand[i] : null;

                    // 送信中（Pending）のカードはUI上はnullとして扱う（フリッカー防止）
                    lock (_pendingPlayedCards)
                    {
                        if (card != null && _pendingPlayedCards.Any(p => p.Suit == card.Suit && p.Number == card.Number))
                        {
                            // まだ手元にあると判定されているが、送信済みなので非表示にする
                            card = null;
                        }
                    }

                    if (_myHandViewModels[i].Card != card)
                    {
                        // カードが実際に「消えた」ことが確定したら、Pendingリストから削除
                        if (card == null && _myHandViewModels[i].Card != null)
                        {
                            lock (_pendingPlayedCards)
                            {
                                var oldC = _myHandViewModels[i].Card;
                                if (oldC != null)
                                {
                                    var match = _pendingPlayedCards.FirstOrDefault(p => p.Suit == oldC.Suit && p.Number == oldC.Number);
                                    if (match != null) _pendingPlayedCards.Remove(match);
                                }
                            }
                        }

                        _myHandViewModels[i].IsAnimating = true;
                        _myHandViewModels[i].Card = card;
                    }

                    // プレイ可能判定
                    _myHandViewModels[i].IsPlayable = Vm_canPlay(_myHandViewModels[i].Card);
                }
                return [.. _myHandViewModels];
            }
        }

        private List<CardViewModel> _opponentHandViewModels = [];
        public List<CardViewModel> OpponentHandViewModels
        {
            get
            {
                var opponentHand = OpponentHandSlots;
                if (_opponentHandViewModels.Count != 4)
                {
                    _opponentHandViewModels = [.. Enumerable.Range(0, 4).Select(_ => new CardViewModel(null))];
                }

                for (int i = 0; i < 4; i++)
                {
                    var card = i < opponentHand.Count ? opponentHand[i] : null;
                    if (_opponentHandViewModels[i].Card != card)
                    {
                        _opponentHandViewModels[i].IsAnimating = true;
                        _opponentHandViewModels[i].Card = card;
                    }
                    _opponentHandViewModels[i].IsPlayable = false; // 相手側なので常にfalse
                }
                return [.. _opponentHandViewModels];
            }
        }

        private bool Vm_canPlay(PlayingCard? card)
        {
            if (card == null || !State.IsGameStarted) return false;
            return (State.Piles[0] != null && IsConnectable(card.Number, State.Piles[0].Number)) ||
                   (State.Piles[1] != null && IsConnectable(card.Number, State.Piles[1].Number));
        }

        public object CreateView(object viewModel)
        {
            return new SpeedView { DataContext = viewModel };
        }
    }
}
