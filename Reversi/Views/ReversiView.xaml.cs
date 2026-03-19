using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using YMM4GameHub.Core;
using Reversi.Animations;
using Reversi.Models;

namespace Reversi.Views
{
    public partial class ReversiView : UserControl
    {
        private readonly Button[,] _cells = new Button[8, 8];
        private CellState[]? _previousBoard;

        public ReversiView()
        {
            InitializeComponent();
            InitializeBoard();

            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is INotifyPropertyChanged oldVm)
                    oldVm.PropertyChanged -= ViewModel_PropertyChanged;
                if (e.NewValue is INotifyPropertyChanged newVm)
                    newVm.PropertyChanged += ViewModel_PropertyChanged;
                
                UpdateBoard();
            };
        }

        #region Initialization

        private void InitializeBoard()
        {
            BoardGrid.Children.Clear();
            var style = TryFindResource("BoardCellButtonStyle") as Style;

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var btn = new Button
                    {
                        Style = style,
                        Tag = new Point(x, y)
                    };
                    btn.Click += Cell_Click;
                    
                    var grid = new Grid();
                    grid.Children.Add(new Ellipse
                    {
                        Width = 36,
                        Height = 36,
                        Margin = new Thickness(2),
                        Visibility = Visibility.Collapsed
                    });
                    btn.Content = grid;

                    _cells[x, y] = btn;
                    BoardGrid.Children.Add(btn);
                }
            }
        }

        #endregion

        #region Event Handlers

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "State" || e.PropertyName == "Status")
            {
                Dispatcher.Invoke(UpdateBoard);
            }
        }

        private async void Cell_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var point = (Point)btn.Tag;
            if (DataContext is ReversiGame game)
            {
                await game.PutStoneAsync((int)point.X, (int)point.Y);
            }
        }

        private void CloseResultButton_Click(object sender, RoutedEventArgs e)
        {
            ResultOverlay.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region UI Updates

        private void UpdateBoard()
        {
            if (DataContext is not ReversiGame game) return;

            bool isMyTurn = game.State.CurrentTurn != CellState.Empty && game.State.CurrentTurn == game.MyColor;

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var cellState = game.State.Board[x + y * 8];
                    var btn = _cells[x, y];
                    var grid = (Grid)btn.Content;
                    var stone = (Ellipse)grid.Children[0];

                    // 石の表示
                    if (cellState == CellState.Empty)
                    {
                        stone.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        stone.Visibility = Visibility.Visible;
                        
                        // 以前の状態と比較してアニメーション
                        bool stateChanged = _previousBoard != null && _previousBoard[x + y * 8] != CellState.Empty && _previousBoard[x + y * 8] != cellState;
                        
                        if (stateChanged)
                        {
                            // 反転前に対象のレイアウトを確定させ、描画の準備を整える
                            stone.UpdateLayout();

                            // 反転アニメーション
                            _ = Animations.Flip.FlipAsync(stone, () => {
                                UpdateStoneVisual(stone, cellState);
                            }, TimeSpan.FromMilliseconds(500), new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut });
                        }
                        else
                        {
                            UpdateStoneVisual(stone, cellState);
                        }
                    }

                    // 合法手判定とUIフィードバック
                    if (cellState == CellState.Empty)
                    {
                        bool isValid = game.IsValidMove(x, y, game.MyColor);
                        
                        if (!isMyTurn)
                        {
                            // 自分の番でない：置けない模様（赤）
                            btn.Cursor = Cursors.No;
                            SetButtonHoverColor(btn, Brushes.Red);
                        }
                        else if (isValid)
                        {
                            // 自分の番で合法手：置ける（青/シアン）
                            btn.Cursor = Cursors.Hand;
                            SetButtonHoverColor(btn, Brushes.Cyan);
                        }
                        else
                        {
                            // 自分の番だが非合法手：置けない（赤）
                            btn.Cursor = Cursors.No;
                            SetButtonHoverColor(btn, Brushes.Red);
                        }
                    }
                    else
                    {
                        btn.Cursor = Cursors.Arrow;
                        SetButtonHoverColor(btn, Brushes.Transparent);
                    }
                }
            }

            // ゲーム終了時のリザルト表示
            if (game.State.CurrentTurn == CellState.Empty && game.State.Players.Count >= 2)
            {
                ResultText.Text = game.Status;
                ResultOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                ResultOverlay.Visibility = Visibility.Collapsed;
            }

            _previousBoard = (CellState[])game.State.Board.Clone();
        }

        private static void UpdateStoneVisual(Ellipse stone, CellState cellState)
        {
            stone.Fill = cellState == CellState.Black ? Brushes.Black : Brushes.White;
            stone.Stroke = cellState == CellState.Black ? Brushes.White : Brushes.Gray;
            stone.StrokeThickness = 1;
        }

        private static void SetButtonHoverColor(Button btn, Brush color)
        {
            var template = btn.Template;
            if (template != null)
            {
                if (template.FindName("Overlay", btn) is Border overlay)
                {
                    overlay.Background = color;
                }
            }
        }

        #endregion
    }
}
