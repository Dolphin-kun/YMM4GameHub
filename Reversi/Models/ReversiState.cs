namespace Reversi.Models
{
    public enum CellState
    {
        Empty = 0,
        Black = 1,
        White = 2
    }

    public class ReversiState
    {
        public CellState[] Board { get; set; } = new CellState[64];
        public CellState CurrentTurn { get; set; } = CellState.Black;
        public Dictionary<string, CellState> Players { get; set; } = [];

        public ReversiState()
        {
            // 初期配置 (3,3)=27, (4,4)=36, (3,4)=28, (4,3)=35
            Board[3 + 3 * 8] = CellState.White;
            Board[4 + 4 * 8] = CellState.White;
            Board[3 + 4 * 8] = CellState.Black;
            Board[4 + 3 * 8] = CellState.Black;
        }
    }
}
