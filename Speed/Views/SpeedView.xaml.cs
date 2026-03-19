using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using YMM4GameHub.Core.Controls;
using YMM4GameHub.Core.Commons;
using YMM4GameHub.Core.Animations.GameAnimations;
using Speed.Models;

namespace Speed.Views
{
    public partial class SpeedView : UserControl
    {
        private bool _isProcessingClick;
        private SpeedState? _previousState;
        private readonly HashSet<SpeedGame.CardViewModel> _activeAnimationVMs = [];

        public SpeedView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is INotifyPropertyChanged oldVm)
                    oldVm.PropertyChanged -= ViewModel_PropertyChanged;
                if (e.NewValue is INotifyPropertyChanged newVm)
                    newVm.PropertyChanged += ViewModel_PropertyChanged;
                
                HandleStateChange();
            };
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "State")
            {
                Dispatcher.Invoke(HandleStateChange);
            }
        }

        private async void CardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessingClick) return;
            _isProcessingClick = true;

            try
            {
                var btn = (Button)sender;
                if (btn.DataContext is SpeedGame.CardViewModel vm && DataContext is SpeedGame game && game.State.WinnerPlayerId == null)
                {
                    if (vm.Card == null || vm.IsAnimating || _activeAnimationVMs.Contains(vm)) return;
                    
                    // 1. 手札から出すアニメーションを開始
                    var cardToPlay = vm.Card;
                    var startPos = btn.TransformToVisual(AnimationLayer).Transform(new Point(0, 0));

                    // 場札のどこに出せるか判定
                    int targetPileIndex = -1;
                    if (SpeedGame.IsConnectable(cardToPlay.Number, game.State.Piles[0]?.Number ?? -1)) targetPileIndex = 0;
                    else if (SpeedGame.IsConnectable(cardToPlay.Number, game.State.Piles[1]?.Number ?? -1)) targetPileIndex = 1;

                    if (targetPileIndex != -1)
                    {
                        var targetPile = targetPileIndex == 0 ? Pile0 : Pile1;
                        var endPos = targetPile.TransformToVisual(AnimationLayer).Transform(new Point(0, 0));
                        
                        _activeAnimationVMs.Add(vm);
                        btn.Opacity = 0;

                        // 移動アニメーション
                        _ = AnimateCardMove(cardToPlay, startPos, endPos, isFaceUp: true, onComplete: () =>
                        {
                            _activeAnimationVMs.Remove(vm);
                            vm.IsAnimating = false; // ここでフラグを落とす（CardはまだPendingでnullのはず）
                        });

                        await game.PlayCardAsync(cardToPlay);
                    }
                }
            }
            catch (Exception ex)
            {
                if (sender is Button b && b.DataContext is SpeedGame.CardViewModel vm)
                {
                    _activeAnimationVMs.Remove(vm);
                    vm.IsAnimating = false;
                    b.Opacity = 1;
                }
            }
            finally
            {
                _isProcessingClick = false;
            }
        }

        private void HandleStateChange()
        {
            if (DataContext is not SpeedGame game) return;

            // 新たに IsAnimating = true になったスロットを検知して演出を開始する
            foreach (var vm in game.MyHandViewModels) { DetectAndStartAnimation(vm, true); }
            foreach (var vm in game.OpponentHandViewModels) { DetectAndStartAnimation(vm, false); }

            _previousState = Utils.DeepClone(game.State);
        }

        private void DetectAndStartAnimation(SpeedGame.CardViewModel vm, bool isLocal)
        {
            if (vm.IsAnimating && !_activeAnimationVMs.Contains(vm))
            {
                // まだ演出が始まっていないのに IsAnimating が true になっている = 補充 または 相手のプレイ
                var game = DataContext as SpeedGame;
                if (game == null) return;

                // 手札スロットのインデックスを特定
                var hand = isLocal ? game.MyHandViewModels : game.OpponentHandViewModels;
                int handIndex = hand.IndexOf(vm);
                if (handIndex == -1) return;

                // 以前の状態と比較して「何が起きたか」を判断
                PlayingCard? oldCard = null;
                if (_previousState != null)
                {
                    var playerId = isLocal ? game.LocalPlayerId : game.State.Players.Keys.FirstOrDefault(id => id != game.LocalPlayerId);
                    if (playerId != null && _previousState.Players.TryGetValue(playerId, out var oldP))
                    {
                        oldCard = handIndex < oldP.Hand.Count ? oldP.Hand[handIndex] : null;
                    }
                }

                if (oldCard == null && vm.Card != null)
                {
                    // 補充アニメーション (山札 -> 手札)
                    _ = AnimateRefill(vm, isLocal, handIndex);
                }
                else if (oldCard != null && vm.Card == null && !isLocal)
                {
                    // 相手のプレイアニメーション (手札 -> 場札)
                    DetectAndAnimateOpponentPlay(oldCard, handIndex);
                }
                else
                {
                    // それ以外（またはアニメーションの必要がない整合性合わせ）
                    vm.IsAnimating = false;
                }
            }
        }

        private void DetectAndAnimateOpponentPlay(PlayingCard playedCard, int handIndex)
        {
            if (DataContext is not SpeedGame game) return;

            // どの山に出されたか特定
            for (int i = 0; i < 2; i++)
            {
                var oldP = _previousState?.Piles[i];
                var newP = game.State.Piles[i];
                if (newP != null && (oldP == null || newP.Suit != oldP.Suit || newP.Number != oldP.Number))
                {
                    if (newP.Suit == playedCard.Suit && newP.Number == playedCard.Number)
                    {
                        _ = AnimateOpponentPlay(newP, i, handIndex);
                        return;
                    }
                }
            }
            // 特定できなかった場合はフラグ解除
            var vm = handIndex < game.OpponentHandViewModels.Count ? game.OpponentHandViewModels[handIndex] : null;
            if (vm != null) vm.IsAnimating = false;
        }

        private async Task AnimateRefill(SpeedGame.CardViewModel vm, bool isLocal, int handIndex)
        {
            _activeAnimationVMs.Add(vm);
            
            // UI要素の取得
            var container = isLocal ? MyHandItems : OpponentHandItems;
            if (container.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                await Task.Delay(50);
            
            if (container.ItemContainerGenerator.ContainerFromIndex(handIndex) is not FrameworkElement targetElement)
            {
                _activeAnimationVMs.Remove(vm);
                vm.IsAnimating = false;
                return;
            }

            var deckElement = isLocal ? MyDeck : OpponentDeck;
            var startPos = deckElement.TransformToVisual(AnimationLayer).Transform(new Point(0, 0));
            var endPos = targetElement.TransformToVisual(AnimationLayer).Transform(new Point(0, 0));

            targetElement.Opacity = 0;
            await AnimateCardMove(vm.Card!, startPos, endPos, isFaceUp: isLocal, onComplete: () =>
            {
                targetElement.Opacity = 1;
                vm.IsAnimating = false;
                _activeAnimationVMs.Remove(vm);
            });
        }

        private async Task AnimateOpponentPlay(PlayingCard card, int pileIndex, int handIndex)
        {
            if (DataContext is not SpeedGame game) return;
            var vm = handIndex < game.OpponentHandViewModels.Count ? game.OpponentHandViewModels[handIndex] : null;
            if (vm == null) return;

            _activeAnimationVMs.Add(vm);

            var container = OpponentHandItems;
            if (container.ItemContainerGenerator.ContainerFromIndex(handIndex) is not FrameworkElement element)
            {
                _activeAnimationVMs.Remove(vm);
                vm.IsAnimating = false;
                return;
            }

            var startPos = element.TransformToVisual(AnimationLayer).Transform(new Point(0, 0));
            var targetPile = pileIndex == 0 ? Pile0 : Pile1;
            var endPos = targetPile.TransformToVisual(AnimationLayer).Transform(new Point(0, 0));

            element.Opacity = 0;
            await AnimateCardMove(card, startPos, endPos, isFaceUp: true, onComplete: () =>
            {
                element.Opacity = 1;
                vm.IsAnimating = false;
                _activeAnimationVMs.Remove(vm);
            });
        }

        private async Task AnimateCardMove(PlayingCard card, Point from, Point to, bool isFaceUp, Action? onComplete = null)
        {
            var ghost = new CardControl
            {
                Suit = card.Suit,
                Number = card.Number,
                IsFaceUp = isFaceUp,
                Width = 60,
                Height = 90
            };

            AnimationLayer.Children.Add(ghost);
            AnimationLayer.UpdateLayout(); 
            
            await MoveTo.MoveToAsync(ghost, from, to, TimeSpan.FromMilliseconds(250));
            
            onComplete?.Invoke();
            AnimationLayer.Children.Remove(ghost);
            await Task.Yield();
        }
    }

    public class ResetVisibleConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            // カウントダウンが1以上のときだけ表示する（0のときは表示しない）
            return value is int countdown && countdown > 0;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }
}
