using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using DynamicData;
using QuestPatcher.Core;
using QuestPatcher.Core.Downgrading;
using QuestPatcher.Core.Models;
using QuestPatcher.Models;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.ViewModels
{
    public class DowngradeViewModel: ViewModelBase
    {
        private readonly Window _window;

        private readonly DowngradeManger _downgradeManger;
        
        private readonly InstallManager _installManager;
        
        private readonly OperationLocker _locker;

        private bool _isLoading = true;
        
        private readonly Config _config;

        public ObservableCollection<string> AvailableToVersions { get; set; } = new ObservableCollection<string>();

        public string SelectedToVersion { get; set; } = "";

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                this.RaisePropertyChanged();
            }
        }

        public DowngradeViewModel(Window window, Config config, InstallManager installManager, DowngradeManger downgradeManger, OperationLocker locker)
        {
            _window = window;
            _config = config;
            _installManager = installManager;
            _downgradeManger = downgradeManger;
            _locker = locker;
            
            window.Opened += async (sender, args) => await LoadVersions();
            window.Closing += (sender, args) =>
            {
                if (IsLoading)
                {
                    args.Cancel = true;
                }
            };
        }

        private async Task LoadVersions()
        {
            IsLoading = true;
            Log.Debug("Loading available versions...");

            IList<string> paths;
            try
            {
                paths = await _downgradeManger.GetAvailableVersionsAsync(_installManager.InstalledApp?.Version);
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
                await dialog.OpenDialogue(_window);
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
                await dialog.OpenDialogue(_window);
                _window.Close();
                return;
            }
            
            AvailableToVersions.Clear();
            AvailableToVersions.AddRange(paths);
            
            if (!AvailableToVersions.Contains(SelectedToVersion))
            {
                SelectedToVersion = paths[0];
                this.RaisePropertyChanged(nameof(SelectedToVersion));
            }
            
            IsLoading = false;
        }
        
        public async Task Downgrade()
        {
            Log.Debug("Selected version: {SelectedVersion}", SelectedToVersion);
            if (string.IsNullOrWhiteSpace(SelectedToVersion)) return;
            IsLoading = true;
            DialogBuilder? dialog = null;
            try
            {
                _locker.StartOperation();
                await _downgradeManger.DowngradeApp(SelectedToVersion);
                dialog = new DialogBuilder
                {
                    Title = "降级成功",
                    Text = "现在可以给游戏打补丁装Mod了！",
                    HideCancelButton = true
                };
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
                    Text = "检查日志以获取详细信息",
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
                    await dialog.OpenDialogue(_window);
                }
                _window.Close();
            }
        }
    }
}
