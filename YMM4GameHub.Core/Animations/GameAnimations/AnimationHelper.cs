using System.Windows;
using System.Windows.Media;

namespace YMM4GameHub.Core.Animations.GameAnimations
{
    internal static class AnimationHelper
    {
        public static TransformGroup EnsureTransformGroup(UIElement target)
        {
            if (target.RenderTransform is not TransformGroup group)
            {
                group = new TransformGroup();
                if (target.RenderTransform != null)
                {
                    group.Children.Add(target.RenderTransform);
                }
                target.RenderTransform = group;
            }
            return group;
        }

        public static T GetOrCreateTransform<T>(TransformGroup group) where T : Transform, new()
        {
            var transform = group.Children.OfType<T>().FirstOrDefault();
            bool isNew = transform == null;
            if (isNew) transform = new T();

            // 変換の順序を強制する: Scale (0) -> Rotate (1) -> Translate (End)
            // これにより、フリップ（Scale）が移動（Translate）を拡大縮小してしまうのを防ぐ
            if (typeof(T) == typeof(ScaleTransform))
            {
                if (group.Children.IndexOf(transform) != 0)
                {
                    if (!isNew) group.Children.Remove(transform);
                    group.Children.Insert(0, transform);
                }
            }
            else if (typeof(T) == typeof(TranslateTransform))
            {
                if (group.Children.IndexOf(transform) != group.Children.Count - 1)
                {
                    if (!isNew) group.Children.Remove(transform);
                    group.Children.Add(transform);
                }
            }
            else if (isNew)
            {
                group.Children.Add(transform);
            }

            return transform;
        }
    }
}
