using System.Reflection;
using Reversi.Models;
using YMM4GameHub.Core.Commons;

namespace Reversi
{
    public class ReversiGame : GameBase<ReversiState>, IGameViewProvider
    {
        public override string Name => "リバーシ";
        public override string GameKey => "Reversi";
        public override string? Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        public override string? Author => "いるかぁぁ";
        public override GameCategory Category => GameCategory.BoardGame;
        public override PlayerCountRange SupportedPlayerCounts => new(2, 2);

        private string _status = "対戦相手を待っています...";
        public string Status
        {
            get => _status;
            set => Set(ref _status, value);
        }

        public CellState MyColor 
        {
            get
            {
                if (Network != null && State.Players.TryGetValue(Network.LocalPlayerId, out var color))
                {
                    return color;
                }
                return CellState.Empty;
            }
        }

        public override async Task StartAsync(INetworkProvider network, GameLaunchSettings settings)
        {
            await base.StartAsync(network, settings);
            
            // ホストの場合は初期状態をセット
            if (IsHost)
            {
                State.Players[Network!.LocalPlayerId] = CellState.Black;
                // 初期配置もホストが行う
                State.Board[27] = CellState.White;
                State.Board[28] = CellState.Black;
                State.Board[35] = CellState.Black;
                State.Board[36] = CellState.White;
                UpdateStatus();
                await BroadcastStateAsync();
            }

            await SendActionAsync(new JoinAction(Network!.LocalPlayerId));
            UpdateStatus();
        }

        public object CreateView(object viewModel)
        {
            return new Views.ReversiView { DataContext = viewModel };
        }

        protected override async void ApplyAction(string senderId, object action)
        {
            bool isStateSync = false;

            if (action is System.Text.Json.JsonElement element)
            {
                if (element.TryGetProperty("PlayerId", out _))
                {
                    action = System.Text.Json.JsonSerializer.Deserialize<JoinAction>(element.GetRawText())!;
                }
                else if (element.TryGetProperty("X", out _))
                {
                    action = System.Text.Json.JsonSerializer.Deserialize<PutStoneAction>(element.GetRawText())!;
                }
                else if (element.TryGetProperty("StateJson", out _))
                {
                    var stateJson = element.GetProperty("StateJson").GetString();
                    if (!string.IsNullOrEmpty(stateJson))
                    {
                        var newState = System.Text.Json.JsonSerializer.Deserialize<ReversiState>(stateJson);
                        if (newState != null)
                        {
                            State = newState;
                            isStateSync = true;
                        }
                    }
                }
                else if (element.TryGetProperty("Type", out var typeProp) && typeProp.GetString() == "player-left")
                {
                    string leftPlayerId = element.GetProperty("PlayerId").GetString() ?? "";
                    if (State.Players.ContainsKey(leftPlayerId))
                    {
                        Status = "相手が切断されました。";
                        return; // ここで終了
                    }
                }
            }

            if (!isStateSync)
            {
                if (action is JoinAction join)
                {
                    if (!State.Players.ContainsKey(join.PlayerId))
                    {
                        if (IsHost)
                        {
                            State.Players[join.PlayerId] = State.Players.Count == 1 ? CellState.White : CellState.Empty;
                            await BroadcastStateAsync();
                        }
                        else
                        {
                            State.Players[join.PlayerId] = CellState.Empty;
                        }
                    }
                }
                else if (action is PutStoneAction put)
                {
                    HandlePutStone(senderId, put.X, put.Y);
                }
            }
            
            OnPropertyChanged(nameof(State));
            UpdateStatus();
        }

        private void HandlePutStone(string senderId, int x, int y)
        {
            if (!State.Players.TryGetValue(senderId, out var color)) return;
            if (State.CurrentTurn != color) return;

            if (IsValidMove(x, y, color))
            {
                PlaceStone(x, y, color);
                
                // 次のプレイヤーが置けるかチェック
                CellState nextTurn = color == CellState.Black ? CellState.White : CellState.Black;
                if (HasAnyValidMove(nextTurn))
                {
                    State.CurrentTurn = nextTurn;
                }
                else if (HasAnyValidMove(color))
                {
                    // パス
                    State.CurrentTurn = color;
                }
                else
                {
                    // ゲーム終了
                    State.CurrentTurn = CellState.Empty;
                }
            }
        }

        public bool IsValidMove(int x, int y, CellState color)
        {
            if (x < 0 || x >= 8 || y < 0 || y >= 8) return false;
            if (State.Board[x + y * 8] != CellState.Empty) return false;
            if (color == CellState.Empty) return false;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (CountFlips(x, y, dx, dy, color) > 0) return true;
                }
            }
            return false;
        }

        private int CountFlips(int x, int y, int dx, int dy, CellState color)
        {
            int count = 0;
            CellState opponent = color == CellState.Black ? CellState.White : CellState.Black;

            int nx = x + dx;
            int ny = y + dy;

            bool foundOpponent = false;
            while (nx >= 0 && nx < 8 && ny >= 0 && ny < 8)
            {
                var current = State.Board[nx + ny * 8];
                if (current == opponent)
                {
                    count++;
                    foundOpponent = true;
                }
                else if (current == color)
                {
                    return foundOpponent ? count : 0;
                }
                else
                {
                    break;
                }
                nx += dx;
                ny += dy;
            }
            return 0;
        }

        private void PlaceStone(int x, int y, CellState color)
        {
            State.Board[x + y * 8] = color;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (CountFlips(x, y, dx, dy, color) > 0)
                    {
                        FlipLine(x, y, dx, dy, color);
                    }
                }
            }
        }

        private void FlipLine(int x, int y, int dx, int dy, CellState color)
        {
            int nx = x + dx;
            int ny = y + dy;
            while (State.Board[nx + ny * 8] != color)
            {
                State.Board[nx + ny * 8] = color;
                nx += dx;
                ny += dy;
            }
        }

        private bool HasAnyValidMove(CellState color)
        {
            if (color == CellState.Empty) return false;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (IsValidMove(x, y, color)) return true;
            return false;
        }

        private void UpdateStatus()
        {
            if (Status == "相手が切断されました。") return;

            if (State.Players.Count < 2)
            {
                Status = "対戦相手を待っています...";
                return;
            }

            if (State.CurrentTurn == CellState.Empty)
            {
                int black = 0, white = 0;
                foreach (var cell in State.Board)
                {
                    if (cell == CellState.Black) black++;
                    if (cell == CellState.White) white++;
                }
                Status = $"ゲーム終了！ 黒:{black} 白:{white} - {(black > white ? "黒の勝ち" : black < white ? "白の勝ち" : "引き分け")}";
                return;
            }

            if (MyColor == CellState.Empty)
            {
                Status = "観戦中";
                return;
            }

            if (State.CurrentTurn == MyColor)
            {
                Status = $"あなたの番です ({(MyColor == CellState.Black ? "黒" : "白")})";
            }
            else
            {
                var opponentColor = MyColor == CellState.Black ? "白" : "黒";
                Status = $"相手の番です ({opponentColor})";
            }
        }

        public async Task PutStoneAsync(int x, int y)
        {
            if (Network == null) return;
            if (State.Players.Count < 2) return;
            if (MyColor == CellState.Empty) return;
            if (State.CurrentTurn != MyColor) return;
            if (!IsValidMove(x, y, MyColor)) return;

            await SendActionAsync(new PutStoneAction(x, y));
        }

        public override async Task StopAsync()
        {
            await base.StopAsync();
        }
    }
}
