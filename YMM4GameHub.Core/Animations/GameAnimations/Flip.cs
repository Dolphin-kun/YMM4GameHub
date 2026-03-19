using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace YMM4GameHub.Core.Animations.GameAnimations
{
    public static class Flip
    {
        public static async Task FlipAsync(UIElement target, Action onHalfway, TimeSpan duration, IEasingFunction? easing = null)
        {
            var halfDuration = new Duration(TimeSpan.FromTicks(duration.Ticks / 2));
            
            var group = AnimationHelper.EnsureTransformGroup(target);
            var scale = AnimationHelper.GetOrCreateTransform<ScaleTransform>(group);

            if (target is FrameworkElement fe)
            {
                // Measureしていない場合は強制的に計算
                if (fe.ActualWidth <= 0)
                {
                    fe.UpdateLayout();
                    if (fe.ActualWidth <= 0) await Task.Delay(10);
                }
                
                scale.CenterX = fe.ActualWidth / 2;
                scale.CenterY = fe.ActualHeight / 2;
                fe.RenderTransformOrigin = new Point(0, 0);
            }

            try
            {
                // 縮小: 1 -> 0
                var shrink = new DoubleAnimation(1, 0, halfDuration) 
                { 
                    FillBehavior = FillBehavior.HoldEnd,
                    EasingFunction = easing 
                };
                var tcs1 = new TaskCompletionSource<bool>();
                shrink.Completed += (s, e) => tcs1.TrySetResult(true);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
                await tcs1.Task;

                onHalfway?.Invoke();

                // 拡大: 0 -> 1
                var expand = new DoubleAnimation(0, 1, halfDuration) 
                { 
                    FillBehavior = FillBehavior.HoldEnd,
                    EasingFunction = easing
                };
                var tcs2 = new TaskCompletionSource<bool>();
                expand.Completed += (s, e) => tcs2.TrySetResult(true);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, expand);
                await tcs2.Task;
            }
            finally
            {
                // アニメーションのクリーンアップ（プロパティのロックを解除）
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            }
        }
    }
}
