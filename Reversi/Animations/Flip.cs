using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using YMM4GameHub.Core.Animations.GameAnimations;

namespace Reversi.Animations
{
    public static class Flip
    {
        public static async Task FlipAsync(UIElement target, Action onHalfway, TimeSpan duration, IEasingFunction? easing = null)
        {
            // リバーシ専用のフリップ。デフォルトでイージングを適用してきれいに見せる
            easing ??= new CubicEase { EasingMode = EasingMode.EaseInOut };
            await YMM4GameHub.Core.Animations.GameAnimations.Flip.FlipAsync(target, onHalfway, duration, easing);
        }
    }
}
