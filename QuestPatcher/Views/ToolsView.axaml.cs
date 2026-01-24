using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using QuestPatcher.ViewModels;

namespace QuestPatcher.Views
{
    public class ToolsView : UserControl
    {
        public ToolsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void ExpertModeToggle_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton toggle || DataContext is not ToolsViewModel vm)
            {
                return;
            }

            switch (toggle.IsChecked, vm.ExpertModeEnabled)
            {
                case (false, true):
                    vm.ExpertModeEnabled = false;
                    break;
                case (true, false):
                    // revert UI back to false
                    toggle.IsChecked = false;
                    // confirm enable
                    vm.ExpertModeEnabled = await vm.ConfirmEnableExpertMode();
                    break;
            }
        }
    }
}
