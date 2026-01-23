using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace QuestPatcher.Views
{
    public partial class VersionSelectWindow : Window
    {
        public VersionSelectWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
