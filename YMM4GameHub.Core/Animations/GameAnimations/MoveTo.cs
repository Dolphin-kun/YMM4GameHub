using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace YMM4GameHub.Core.Animations.GameAnimations
{
    public static class MoveTo
    {
        public static async Task MoveToAsync(UIElement target, Point from, Point to, TimeSpan duration, IEasingFunction? easing = null)
        {
            var group = AnimationHelper.EnsureTransformGroup(target);
            var translate = AnimationHelper.GetOrCreateTransform<TranslateTransform>(group);

            try
            {
                var animX = new DoubleAnimation(from.X, to.X, new Duration(duration)) 
                { 
                    FillBehavior = FillBehavior.HoldEnd,
                    EasingFunction = easing
                };
                var animY = new DoubleAnimation(from.Y, to.Y, new Duration(duration)) 
                { 
                    FillBehavior = FillBehavior.HoldEnd,
                    EasingFunction = easing
                };

                var tcs = new TaskCompletionSource<bool>();
                animX.Completed += (s, e) => tcs.TrySetResult(true);
                
                translate.BeginAnimation(TranslateTransform.XProperty, animX);
                translate.BeginAnimation(TranslateTransform.YProperty, animY);

                await tcs.Task;
            }
            finally
            {
                // アニメーションのクリーンアップ
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                
                // 最終位置を確定させる（アニメーション解除後の値をセット）
                translate.X = to.X;
                translate.Y = to.Y;
            }
        }
    }
}
