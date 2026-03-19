using System.Windows;
using System.Windows.Media.Animation;

namespace YMM4GameHub.Core.Animations.GameAnimations.CardAnimations
{
    public static class Flip
    {
        public static async Task FlipAsync(UIElement target, Action onHalfway, TimeSpan duration, IEasingFunction? easing = null)
        {
            // トランプ専用のフリップ。デフォルトでイージングを適用してきれいに見せる
            easing ??= new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            await GameAnimations.Flip.FlipAsync(target, onHalfway, duration, easing);
        }
    }
}
