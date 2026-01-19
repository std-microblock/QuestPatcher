using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QuestPatcher.Core;
using QuestPatcher.Core.CoreMod;
using QuestPatcher.Core.Downgrading;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using QuestPatcher.Core.Utils;
using QuestPatcher.Models;
using QuestPatcher.Resources;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.ViewModels
{
    public class PatchingViewModel : ViewModelBase
    {
        public bool IsPatchingInProgress { get => _isPatchingInProgress; set { if (_isPatchingInProgress != value) { this.RaiseAndSetIfChanged(ref _isPatchingInProgress, value); } } }
        private bool _isPatchingInProgress;

        public string PatchingStageText { get; private set; } = "";

        public string? CustomSplashPath => Config.PatchingOptions.CustomSplashPath;

        public ModLoader? PreferredModLoader
        {
            get
            {
                var version = _installManager.InstalledApp?.SemVersion;
                if (version == null)
                {
                    return null;
                }

                return version > SharedConstants.BeatSaberLastQuestLoaderVersion
                    ? ModLoader.Scotland2
                    : ModLoader.QuestLoader;
            }
        }

        public Config Config { get; }

        public OperationLocker Locker { get; }

        public ProgressViewModel ProgressBarView { get; }

        public ExternalFilesDownloader FilesDownloader { get; }

        private readonly PatchingManager _patchingManager;
        private readonly InstallManager _installManager;
        private readonly CoreModsManager _coreModsManager;
        private readonly Window _mainWindow;

        public PatchingViewModel(Config config, OperationLocker locker, PatchingManager patchingManager,
            InstallManager installManager, CoreModsManager coreModsManager, Window mainWindow,
            ProgressViewModel progressBarView, ExternalFilesDownloader filesDownloader)
        {
            Config = config;
            Locker = locker;
            ProgressBarView = progressBarView;
            FilesDownloader = filesDownloader;

            _patchingManager = patchingManager;
            _installManager = installManager;
            _coreModsManager = coreModsManager;
            _mainWindow = mainWindow;

            _patchingManager.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(_patchingManager.PatchingStage))
                {
                    OnPatchingStageChange(_patchingManager.PatchingStage);
                }
            };
        }

        public async void StartPatching()
        {
            var apk = _installManager.InstalledApp;
            if (apk == null)
            {
                Log.Warning("Trying to patch game without an installed app");
                return;
            }

            if (!Config.PatchingOptions.CleanUpMods)
            {
                var builder = new DialogBuilder
                {
                    Title = "清理Mod", 
                    Text = "补丁选项 “清理Mod” 未启用\n这会跳过清理残留的旧版Mod导致游戏无法正常运行。"
                };
                
                builder.OkButton.Text = Strings.Generic_ContinueAnyway;
                if (!await builder.OpenDialogue(_mainWindow))
                {
                    return;
                }
            }

            var apkSemVer = apk.SemVersion;
            if (apkSemVer != null)
            {
                // check version and selected mod loader
                var modLoader = Config.PatchingOptions.ModLoader;
                string? text = null;
                if (modLoader == ModLoader.QuestLoader && apkSemVer > SharedConstants.BeatSaberLastQuestLoaderVersion)
                {
                    text = $"当前游戏版本 {apkSemVer.BaseVersion()} 的Mod需要 Scotland2 Mod注入器，而您选择了 QuestLoader。\n这会导致Mod无法加载并且需要重新打补丁。";
                }
                else if (modLoader == ModLoader.Scotland2 && apkSemVer <= SharedConstants.BeatSaberLastQuestLoaderVersion)
                {
                    text = $"当前游戏版本 {apkSemVer.BaseVersion()} 的Mod需要 QuestLoader Mod注入器，而您选择了 Scotland2。\n这会导致Mod无法加载并且需要重新打补丁。";
                }

                if (text != null)
                {
                    var builder = new DialogBuilder
                    {
                        Title = "不匹配的Mod注入器",
                        Text = text
                    };
                
                    builder.OkButton.Text = Strings.Generic_ContinueAnyway;
                    if (!await builder.OpenDialogue(_mainWindow))
                    {
                        return;
                    }
                }
            }
            
            if (Config.PatchingOptions.FlatScreenSupport)
            {
                // Disable VR requirement apparently causes infinite load
                var builder = new DialogBuilder
                {
                    Title = "禁用VR要求已启用",
                    Text = "您在补丁选项中禁用了VR要求，这可能会导致出现错误，例如启动游戏时无限加载"
                };
                
                builder.OkButton.Text = Strings.Generic_ContinueAnyway;
                if (!await builder.OpenDialogue(_mainWindow))
                {
                    return;
                }
            }

            IsPatchingInProgress = true;
            Locker.StartOperation();
            try
            {
                if (await IsCoreModsAvailable(apk))
                {
                    await _patchingManager.PatchApp();
                }
            }
            catch (FileDownloadFailedException ex)
            {
                Log.Error("Patching failed as essential files could not be downloaded: {Message}", ex.Message);

                var builder = new DialogBuilder
                {
                    Title = Strings.Patching_FileDownloadFailed_Title,
                    Text = Strings.Patching_FileDownloadFailed_Text,
                    HideCancelButton = true
                };

                await builder.OpenDialogue(_mainWindow);
            }
            catch (Exception ex)
            {
                // Print troubleshooting information for debugging
                Log.Error(ex, "Patching failed!");
                var builder = new DialogBuilder
                {
                    Title = Strings.Patching_PatchingFailed_Title,
                    Text = Strings.Patching_PatchingFailed_Text,
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
            }
            finally
            {
                IsPatchingInProgress = false;
                Locker.FinishOperation();
            }

            if (_installManager.InstalledApp?.IsModded ?? false)
            {
                // Display a dialogue to give the user some info about what to expect next, and to avoid them pressing restore app by mistake
                Log.Debug("Patching completed successfully, displaying info dialogue");
                var builder = new DialogBuilder
                {
                    Title = Strings.Patching_PatchingSuccess_Title,
                    Text = Strings.Patching_PatchingSuccess_Text,
                    HideCancelButton = true
                };
                await builder.OpenDialogue(_mainWindow);
            }
        }

        private async Task<bool> IsCoreModsAvailable(ApkInfo apk)
        {
            try
            {
                var coreMods = await _coreModsManager.GetCoreModsAsync(apk.Version);
                if (coreMods.Count == 0)
                {
                    Log.Warning("Trying to patch game without available core mods!");
                    var builder = new DialogBuilder
                    {
                        Title = "没有核心MOD", Text = $"当前游戏版本 {apk.Version} 暂时还没有可用的核心MOD\n确定要继续打补丁吗？"
                    };

                    if (DowngradeManger.DowngradeFeatureAvailable(_installManager.InstalledApp, Config.AppId))
                    {
                        builder.Text += "\n\n您可以通过工具页面的“一键降级”按钮来自动降级游戏, 无需APK文件！";
                    }

                    builder.OkButton.Text = Strings.Generic_ContinueAnyway;
                    if (!await builder.OpenDialogue(_mainWindow))
                    {
                        Log.Debug("Patching not started due to no core mods");
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to load core mods, continuing patching");
            }

            return true;
        }

        public async void SelectSplashPath()
        {
            try
            {
                var files = await _mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    FileTypeFilter = new[]
                    {
                        FilePickerFileTypes.ImagePng
                    }
                });
                Config.PatchingOptions.CustomSplashPath = files.FirstOrDefault()?.Path.LocalPath;
                this.RaisePropertyChanged(nameof(CustomSplashPath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to select splash screen path");
            }
        }

        /// <summary>
        /// Updates the patching stage text in the view
        /// </summary>
        /// <param name="stage">The new patching stage</param>
        private void OnPatchingStageChange(PatchingStage stage)
        {
            PatchingStageText = stage switch
            {
                PatchingStage.NotStarted => Strings.PatchingStage_NotStarted,
                PatchingStage.FetchingFiles => Strings.PatchingStage_FetchFiles,
                PatchingStage.MovingToTemp => Strings.PatchingStage_MoveToTemp,
                PatchingStage.Patching => Strings.PatchingStage_Patching,
                PatchingStage.Signing => Strings.PatchingStage_Signing,
                PatchingStage.UninstallingOriginal => Strings.PatchingStage_UninstallOriginal,
                PatchingStage.InstallingModded => Strings.PatchingStage_InstallModded,
                PatchingStage.CleanUpMods => Strings.PatchingStage_CleanUpMods,
                _ => throw new NotImplementedException()
            };
            this.RaisePropertyChanged(nameof(PatchingStageText));
        }
    }
}
