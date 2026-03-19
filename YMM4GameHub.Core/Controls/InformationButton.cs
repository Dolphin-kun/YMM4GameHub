using System.Windows;
using System.Windows.Controls;

namespace YMM4GameHub.Core.Controls
{
    public class InformationButton : Button
    {
        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register("Description", typeof(string), typeof(InformationButton), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty IsDescriptionOpenProperty =
            DependencyProperty.Register("IsDescriptionOpen", typeof(bool), typeof(InformationButton), new PropertyMetadata(false));

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public bool IsDescriptionOpen
        {
            get => (bool)GetValue(IsDescriptionOpenProperty);
            set => SetValue(IsDescriptionOpenProperty, value);
        }

        protected override void OnClick()
        {
            base.OnClick();
            IsDescriptionOpen = !IsDescriptionOpen;
        }

        static InformationButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(InformationButton), new FrameworkPropertyMetadata(typeof(InformationButton)));
        }
    }
}
