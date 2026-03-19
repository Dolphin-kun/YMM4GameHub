using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace YMM4GameHub.Views
{
    /// <summary>
    /// YMM4GameHubView.xaml の相互作用ロジック
    /// </summary>
    public partial class YMM4GameHubView : UserControl
    {
        public YMM4GameHubView()
        {
            InitializeComponent();
            DataContext = new ViewModels.YMM4GameHubViewModel();

            IsVisibleChanged += (s, e) =>
                LevelManager.SetHubVisible((bool)e.NewValue);

            Unloaded += (s, e) =>
                LevelManager.FlushToSettings();
        }
    }

    /// <summary>LevelProgressPercent (0~100) をプログレスバーの幅 (px) に変換する。</summary>
    public class ProgressToWidthConverter : IValueConverter
    {
        private const double TrackWidth = 106.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double pct = value is double d ? d : 0;
            return Math.Clamp(pct, 0, 100) / 100.0 * TrackWidth;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => DependencyProperty.UnsetValue;
    }

    /// <summary>レベルの進捗状態に応じた色を返す。</summary>
    public class ProgressToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // ポイントが非負になったため、基本は常に青紫。
            return new SolidColorBrush(Color.FromRgb(0x4A, 0x6C, 0xF7));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => DependencyProperty.UnsetValue;
    }
}
