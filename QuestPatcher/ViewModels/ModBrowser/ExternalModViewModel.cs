using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using QuestPatcher.Core.ModBrowser.Models;
using QuestPatcher.Core.Modding;
using QuestPatcher.Models;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.ViewModels.ModBrowser
{
    public class ExternalModViewModel : ViewModelBase
    {
        private readonly OperationLocker _locker;

        public BrowseModViewModel Parent { get; }

        public ExternalMod Mod { get; }

        public string Id => Mod.Id;
        public string Name => Mod.Name;
        public string Description => Mod.Description;
        public string Author => $"(作者: {Mod.Author})";
        public string Version => Mod.VersionString;

        private bool _isChecked;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                this.RaisePropertyChanged();
                Parent.SetModSelection(Id, value);
            }
        }

        private string _installButtonText = "安装";

        public string InstallButtonText
        {
            get => _installButtonText;
            set
            {
                _installButtonText = value;
                this.RaisePropertyChanged();
            }
        }

        public bool IsLatestInstalled => _installedMod != null && Mod.Version <= _installedMod.Version;
        
        public bool ShouldEnableButton => _locker.IsFree && !IsLatestInstalled;
        
        public string InstalledVersionText => _installedMod == null ? "(未安装)" : $"(已安装 {_installedMod.Version})";

        private readonly object _installedModLock = new();
        private IMod? _installedMod;

        public ExternalModViewModel(ExternalMod mod, OperationLocker locker, BrowseModViewModel parent)
        {
            Mod = mod;
            _locker = locker;
            Parent = parent;

            _locker.PropertyChanged -= OnLockerPropertyChanged;
            _locker.PropertyChanged += OnLockerPropertyChanged;
        }

        ~ExternalModViewModel()
        {
            _locker.PropertyChanged -= OnLockerPropertyChanged;
            UpdateInstallStatus(null);
        }

        private void OnLockerPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            this.RaisePropertyChanged(nameof(ShouldEnableButton));
        }

        public void UpdateInstallStatus(IMod? installedMod)
        {
            lock (_installedModLock)
            {
                Log.Debug("Updating install status for external mod {Mod}: {Status}", Mod.Name, installedMod?.Version.ToString() ?? "Not installed");
                var current = _installedMod;
                if (!ReferenceEquals(current, installedMod))
                {
                    if (current != null)
                    {
                        current.PropertyChanged -= OnModPropertyChanged;
                    }

                    _installedMod = installedMod;
                    if (_installedMod != null)
                    {
                        _installedMod.PropertyChanged -= OnModPropertyChanged;
                        _installedMod.PropertyChanged += OnModPropertyChanged;
                    }
                }

                current = _installedMod;

                InstallButtonText = current == null ? "安装" : (Mod.Version > current.Version ? "更新" : "已安装");
                this.RaisePropertyChanged(nameof(ShouldEnableButton));
                this.RaisePropertyChanged(nameof(IsLatestInstalled));
                this.RaisePropertyChanged(nameof(InstalledVersionText));
            }
        }

        private void OnModPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if ((sender as IMod) != null && args.PropertyName == nameof(IMod.Version))
            {
                UpdateInstallStatus((IMod) sender);
            }
        }

        public async void InstallClicked()
        {
            await Parent.InstallSelectedMods(new List<ExternalMod> { Mod });
        }
        
        public void ViewClicked()
        {
            if (ShouldEnableButton && Parent.ShowBatchInstall)
            {
                IsChecked = !IsChecked;
            }
        }
    }
}
