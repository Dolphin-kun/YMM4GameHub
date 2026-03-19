using System.Windows;

namespace YMM4GameHub.Views
{
    /// <summary>
    /// ゲームのViewを表示するための汎用ウィンドウ
    /// </summary>
    public partial class GameWindow : Window
    {
        public GameWindow()
        {
            InitializeComponent();
        }

        public void SetContent(object view)
        {
            if (view is UIElement element)
            {
                MainGrid.Children.Clear();
                MainGrid.Children.Add(element);
            }
        }
    }
}
