using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using DynamicData;
using QuestPatcher.Core;
using QuestPatcher.Core.Downgrading;
using QuestPatcher.Core.Downgrading.Models;
using QuestPatcher.Core.Models;
using QuestPatcher.Models;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.ViewModels
{
    public class DowngradeViewModel: ViewModelBase
    {
        private readonly Window _window;
        private readonly Window _mainWindow;
        private readonly Config _config;
        private readonly DowngradeManger _downgradeManger;
        private readonly InstallManager _installManager;
        private readonly OperationLocker _locker;

        private bool _isLoading = true;

        public ObservableCollection<AppDiff> AvailablePaths { get; } = new();

        public AppDiff? SelectedPath { get; set; }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                this.RaisePropertyChanged();
            }
        }

        public DowngradeViewModel(Window window, Window mainWindow, Config config, InstallManager installManager,
            DowngradeManger downgradeManger, OperationLocker locker)
        {
            _window = window;
            _mainWindow = mainWindow;
            _config = config;
            _installManager = installManager;
            _downgradeManger = downgradeManger;
            _locker = locker;

            window.Opened += async (sender, args) => await LoadVersions(false);
            window.Closing += (sender, args) =>
            {
                if (IsLoading)
                {
                    args.Cancel = true;
                }
            };
        }

        public void Refresh()
        {
            if (!IsLoading)
            {
                _ = LoadVersions(true);
            }
        }

        private async Task LoadVersions(bool refresh)
        {
            IsLoading = true;
            Log.Debug("Loading available versions...");

            IReadOnlyList<AppDiff> paths;
            try
            {
                paths = await _downgradeManger.GetAvailablePathAsync(_installManager.InstalledApp?.Version, refresh);
                Log.Debug("Available paths: {Paths}", paths);
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to load available versions");
                IsLoading = false;
                var dialog = new DialogBuilder
                {
                    Title = "出错了",
                    Text = "无法加载可用的降级版本",
                    HideCancelButton = true
                };
                dialog.WithException(e);
                _ = dialog.OpenDialogue(_mainWindow);
                _window.Close();
                return;
            }
            
            if (paths.Count == 0)
            {
                IsLoading = false;
                var dialog = new DialogBuilder
                {
                    Title = "无法降级",
                    Text = $"{_installManager.InstalledApp?.Version ?? "null"} 暂无可用的降级版本\n刚刚更新的最新版游戏可能需要一些时间才会有可用降级",
                    HideCancelButton = true
                };
                _ = dialog.OpenDialogue(_mainWindow);
                _window.Close();
                return;
            }

            AvailablePaths.Clear();
            AvailablePaths.AddRange(paths);

            SelectedPath = paths[0];
            this.RaisePropertyChanged(nameof(SelectedPath));
            
            IsLoading = false;
        }
        
        public async Task Downgrade()
        {
            Log.Debug("Selected version: {SelectedVersion}", SelectedPath);
            if (SelectedPath is null)
            {
                return;
            }

            if (_installManager.InstalledApp == null)
            {
                Log.Warning("Trying to downgrade without game being installed");
                return;
            }

            IsLoading = true;
            DialogBuilder? dialog = null;
            try
            {
                _locker.StartOperation();
                bool result = await _downgradeManger.DowngradeApp(SelectedPath);
                if (result)
                {
                    dialog = new DialogBuilder { Title = "降级成功", Text = "现在可以给游戏打补丁装Mod了！", HideCancelButton = true };
                }
            }
            catch (FileDownloadFailedException e)
            {
                Log.Error("Downgrade failed due to files could not be downloaded: {Message}", e.Message);

                dialog = new DialogBuilder
                {
                    Title = "无法下载文件",
                    Text = "QuestPatcher 无法下载降级所需的文件。请检查您的互联网连接，然后重试。",
                    HideCancelButton = true
                };
            }
            catch (Exception e)
            {
                Log.Error(e, "Downgrade failed with exception: {Exception}", e.Message);
                dialog = new DialogBuilder
                {
                    Title = "降级失败",
                    Text = "检查日志以获取更多信息",
                    HideCancelButton = true
                };
                
                dialog.WithException(e);
            }
            finally
            {
                _locker.FinishOperation();
                IsLoading = false;
                if (dialog != null)
                {
                    _ = dialog.OpenDialogue(_mainWindow);
                    _window.Close();
                }
            }
        }
    }
}
