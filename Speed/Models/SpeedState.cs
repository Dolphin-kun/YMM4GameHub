using YMM4GameHub.Core.Controls;

namespace Speed.Models
{
    public class PlayingCard
    {
        public CardSuit Suit { get; set; }
        public int Number { get; set; }

        public PlayingCard() { }
        public PlayingCard(CardSuit suit, int number)
        {
            Suit = suit;
            Number = number;
        }

        public override string ToString() => $"{Suit} {Number}";
    }

    public class SpeedState
    {
        // プレイヤーごとのデータ
        public Dictionary<string, PlayerData> Players { get; set; } = [];
        public string? WinnerPlayerId { get; set; }

        // 台札（2枚）
        public PlayingCard[] Piles { get; set; } = new PlayingCard[2];
        public PlayingCard?[] ResetPiles { get; set; } = new PlayingCard[2];

        // カウントダウン (3, 2, 1, 0)
        public int ResetCountdown { get; set; } = -1;

        public bool IsGameStarted { get; set; }

        public SpeedState() { }
    }

    public class PlayerData
    {
        public List<PlayingCard> Hand { get; set; } = [];
        public List<PlayingCard> Deck { get; set; } = [];
        public int DeckCount => Deck.Count;
    }

    public class PlayCardAction
    {
        public int HandIndex { get; set; }
        public int PileIndex { get; set; }
        public string PlayerId { get; set; } = "";

        public PlayCardAction() { }
        public PlayCardAction(int handIndex, int pileIndex, string playerId)
        {
            HandIndex = handIndex;
            PileIndex = pileIndex;
            PlayerId = playerId;
        }
    }

    public class JoinAction
    {
        public string PlayerId { get; set; } = "";
        public JoinAction() { }
        public JoinAction(string playerId) => PlayerId = playerId;
    }
}
