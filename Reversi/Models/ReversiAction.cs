namespace Reversi.Models
{
    public record PutStoneAction(int X, int Y);
    public record JoinAction(string PlayerId);
}
