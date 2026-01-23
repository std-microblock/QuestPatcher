using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using QuestPatcher.Core.Downgrading.Models;
using QuestPatcher.Views;

namespace QuestPatcher.ViewModels
{
    public class VersionSelectViewModel : ViewModelBase
    {
        public IReadOnlyList<AppDiff?> Versions { get; }

        public AppDiff? SelectedVersion { get; set; }

        public string Message { get; }

        private readonly Window _dialog;

        private VersionSelectViewModel(Window dialog, IReadOnlyList<AppDiff?> versions,
            string message)
        {
            _dialog = dialog;
            Versions = versions;
            Message = message;
            SelectedVersion = versions[0];
        }

        public void OnCancel()
        {
            SelectedVersion = null;
            _dialog.Close(new PatchingViewModel.SelectionData { Proceed = false, Downgrade = null });
        }

        public void OnSelect()
        {
            _dialog.Close(new PatchingViewModel.SelectionData { Proceed = true, Downgrade = SelectedVersion });
        }

        public static async Task<PatchingViewModel.SelectionData> ShowDialog(Window owner,
            IReadOnlyList<AppDiff?> versions, string message)
        {
            var dialog = new VersionSelectWindow();
            var vm = new VersionSelectViewModel(dialog, versions, message);
            dialog.DataContext = vm;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var result = await dialog.ShowDialog<PatchingViewModel.SelectionData?>(owner);
            return result ?? new PatchingViewModel.SelectionData { Proceed = false, Downgrade = null };
        }
    }
}
