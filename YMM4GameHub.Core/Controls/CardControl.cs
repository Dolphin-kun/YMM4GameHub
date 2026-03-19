using System.Windows;
using System.Windows.Controls;

namespace YMM4GameHub.Core.Controls
{
    public enum CardSuit
    {
        Spades,
        Hearts,
        Diamonds,
        Clubs,
        Joker
    }

    public class CardControl : Control
    {
        public static readonly DependencyProperty SuitProperty =
            DependencyProperty.Register("Suit", typeof(CardSuit), typeof(CardControl), new PropertyMetadata(CardSuit.Spades));

        public static readonly DependencyProperty NumberProperty =
            DependencyProperty.Register("Number", typeof(int), typeof(CardControl), new PropertyMetadata(1));

        public static readonly DependencyProperty IsFaceUpProperty =
            DependencyProperty.Register("IsFaceUp", typeof(bool), typeof(CardControl), new PropertyMetadata(true));

        public static readonly DependencyProperty IsPlayableProperty =
            DependencyProperty.Register("IsPlayable", typeof(bool), typeof(CardControl), new PropertyMetadata(false));

        public CardSuit Suit
        {
            get => (CardSuit)GetValue(SuitProperty);
            set => SetValue(SuitProperty, value);
        }

        public int Number
        {
            get => (int)GetValue(NumberProperty);
            set => SetValue(NumberProperty, value);
        }

        public bool IsFaceUp
        {
            get => (bool)GetValue(IsFaceUpProperty);
            set => SetValue(IsFaceUpProperty, value);
        }

        public bool IsPlayable
        {
            get => (bool)GetValue(IsPlayableProperty);
            set => SetValue(IsPlayableProperty, value);
        }

        static CardControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CardControl), new FrameworkPropertyMetadata(typeof(CardControl)));
        }
    }
}
