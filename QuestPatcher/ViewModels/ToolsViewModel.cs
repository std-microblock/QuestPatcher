using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using QuestPatcher.Core;
using QuestPatcher.Core.Downgrading;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Models;
using QuestPatcher.Models;
using QuestPatcher.Resources;
using QuestPatcher.Services;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.ViewModels
{
    public class ToolsViewModel : ViewModelBase
    {
        public Config Config { get; }

        public ProgressViewModel ProgressView { get; }

        public OperationLocker Locker { get; }

        public ThemeManager ThemeManager { get; }
        
        public Language SelectedLanguage
        {
            get => Config.Language;
            set
            {
                if (value != Config.Language)
                {
                    // Use ToCultureInfo to check if the new language is actually a different culture
                    // E.g. This is to account for the fact that a change from English to the system default 
                    // isn't actually a change in language if the system default IS English
                    string newCultureName = value.ToCultureInfo().Name;
                    string oldCultureName = Config.Language.ToCultureInfo().Name;

                    // If the culture name is different AND at least one of the cultures isn't English, make the change
                    // QuestPatcher doesn't differentiate between US and UK (real) English so we don't want to prompt
                    // an app reload if both are English.
                    if (newCultureName != oldCultureName && !(newCultureName.StartsWith("en-") && oldCultureName.StartsWith("en")))
                    {
                        ShowLanguageChangeDialog();
                    }

                    Config.Language = value;
                }
            }
        }

        public bool PatchDowngradeAvailable =>
            DowngradeManger.DowngradeFeatureAvailable(_installManager.InstalledApp, Config.AppId);

        public bool RepatchAvailable => _installManager.InstalledApp?.ModLoader != null;

        public bool ExpertModeEnabled
        {
            get => Config.ExpertMode;
            set
            {
                Config.ExpertMode = value;
                this.RaisePropertyChanged();
            }
        }

        public string AdbButtonText => _isAdbLogging ? Strings.Tools_Tool_ToggleADB_Stop : Strings.Tools_Tool_ToggleADB_Start;

        private bool _isAdbLogging;

        private readonly Window _mainWindow;
        private readonly SpecialFolders _specialFolders;
        private readonly InstallManager _installManager;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly QuestPatcherUiService _uiService;
        private readonly InfoDumper _dumper;
        private readonly BrowseImportManager _browseManager;
        private readonly ModManager _modManager;
        private readonly Action _quit;

        public ToolsViewModel(Config config, ProgressViewModel progressView, OperationLocker locker, 
            Window mainWindow, SpecialFolders specialFolders, InstallManager installManager, 
            AndroidDebugBridge debugBridge, QuestPatcherUiService uiService, InfoDumper dumper, ThemeManager themeManager, 
            BrowseImportManager browseManager, ModManager modManager, Action quit)
        {
            Config = config;
            ProgressView = progressView;
            Locker = locker;
            ThemeManager = themeManager;
            _browseManager = browseManager;
            _modManager = modManager;

            _mainWindow = mainWindow;
            _specialFolders = specialFolders;
            _installManager = installManager;
            _debugBridge = debugBridge;
            _uiService = uiService;
            _dumper = dumper;
            _quit = quit;

            _debugBridge.StoppedLogging += (_, _) =>
            {
                Log.Information("ADB log exited");
                _isAdbLogging = false;
                this.RaisePropertyChanged(nameof(AdbButtonText));
            };
            
            _installManager.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(_installManager.InstalledApp))
                {
                    this.RaisePropertyChanged(nameof(PatchDowngradeAvailable));
                    this.RaisePropertyChanged(nameof(RepatchAvailable));
                    SubscribeToApkEvents();
                }
            };
            
            SubscribeToApkEvents();
        }
        
        private void SubscribeToApkEvents()
        {
            var apk = _installManager.InstalledApp;
            if (apk == null) return;
            
            apk.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ApkInfo.IsModded))
                {
                    this.RaisePropertyChanged(nameof(PatchDowngradeAvailable));
                    this.RaisePropertyChanged(nameof(RepatchAvailable));
                }
            };
        }

        public void DowngradeApp()
        {
            _uiService.OpenDowngradeMenu();
        }
        
        public async void UninstallAndInstall()
        {
            DialogBuilder builder1 = new()
            {
                Title = "更换游戏版本",
                Text = "换版本会删除所有的Mod，但不会影响您的歌曲、模型资源。降级完成后您可以把对应版本的Mod重新装回去，即可继续使用这些资源。\n\n点击继续并选择目标版本的游戏APK和可选OBB即可完成更换版本",
            };
            builder1.OkButton.Text = "继续";
            
            if (!await builder1.OpenDialogue(_mainWindow)) return;
            
            _ = _uiService.OpenGameInstallerMenu(true);
        }

        public async void InstallServerSwitcher()
        {
            await _browseManager.InstallApkFromUrl("https://ganbei-hot-update-1258625969.file.myqcloud.com/questpatcher_mirror/Icey-latest.apk?_=" + DateTime.Now.ToFileTime(), "Icey-latest.apk");
        }

        public async void UninstallApp()
        {
            try
            {
                var builder = new DialogBuilder
                {
                    Title = Strings.Tools_Tool_UninstallApp_Title,
                    Text = Strings.Tools_Tool_UninstallApp_Text,
                };
                builder.OkButton.Text = Strings.Tools_Tool_UninstallApp_Confirm;
                builder.CancelButton.Text = "算了，我再想想";
                if (await builder.OpenDialogue(_mainWindow))
                {
                    Locker.StartOperation();
                    try
                    {
                        Log.Information("正在卸载 . . .");
                        await _installManager.UninstallApp();
                    }
                    finally
                    {
                        Locker.FinishOperation();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "卸载失败！");
            }
        }
        
        public async void DeleteAllMods()
        {
            DialogBuilder builder = new()
            {
                Title = "你确定要删除所有Mod吗？此操作不可恢复!",
                Text = "删除后，你可以通过Mod管理页面的“检查核心Mod”按钮来重新安装核心Mod，\n 歌曲、模型等资源不会受到任何影响，在装好版本匹配的Mod之后即可继续使用。"
            };
            builder.OkButton.Text = "好的，删掉";
            builder.CancelButton.Text = "算了，我再想想";
            if (!await builder.OpenDialogue(_mainWindow)) return;
            
            Locker.StartOperation();
            try
            {
                Log.Information("开始删除所有MOD！");
                //TODO Sky: Check result and prompt retry if failed
                await _modManager.DeleteAllMods();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "删除所有Mod竟然失败了！");
                await _modManager.SaveMods();
            }
            finally
            {
                Locker.FinishOperation();
            }
        }

        public void OpenLogsFolder()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _specialFolders.LogsFolder,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        public async void QuickFix()
        {
            Locker.StartOperation(true); // ADB is not available during a quick fix, as we redownload platform-tools
            try
            {
                await _uiService.QuickFix();
                Log.Information("Done!");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to clear cache");
                var builder = new DialogBuilder
                {
                    Title = Strings.Tools_Tool_QuickFix_Failed_Title,
                    Text = Strings.Tools_Tool_QuickFix_Failed_Text,
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
            }
            finally
            {
                Locker.FinishOperation();
            }
        }

        public async void ToggleAdbLog()
        {
            if (_isAdbLogging)
            {
                _debugBridge.StopLogging();
            }
            else
            {
                await _debugBridge.RunCommand("logcat -c");
                Log.Information("Starting ADB log");
                await _debugBridge.StartLogging(Path.Combine(_specialFolders.LogsFolder, "adb.log"));

                _isAdbLogging = true;
                this.RaisePropertyChanged(nameof(AdbButtonText));
            }
        }

        public async void CreateDump()
        {
            Locker.StartOperation();
            try
            {
                // Create the dump in the default location (the data directory)
                string dumpLocation = await _dumper.CreateInfoDump();

                string? dumpFolder = Path.GetDirectoryName(dumpLocation);
                if (dumpFolder != null)
                {
                    // Open the dump's directory for convenience
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dumpFolder,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            catch (Exception ex)
            {
                // Show a dialog with any errors
                Log.Error(ex, "Failed to create dump");
                var builder = new DialogBuilder
                {
                    Title = Strings.Tools_Tool_CreateDump_Failed_Title,
                    Text = Strings.Tools_Tool_CreateDump_Failed_Text,
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
            }
            finally
            {
                Locker.FinishOperation();
            }
        }

        public void RepatchApp()
        {
            _uiService.OpenRepatchMenu();
        }

        public async void ChangeApp()
        {
            await _uiService.OpenChangeAppMenu(false);
        }

        public async void RestartApp()
        {
            try
            {
                Log.Information("Restarting app");
                Locker.StartOperation();
                await _debugBridge.ForceStop(Config.AppId);

                // Run the app once, wait, and run again.
                // This bypasses the restore app prompt
                await _debugBridge.RunUnityPlayerActivity(Config.AppId);
                await Task.Delay(1000);
                await _debugBridge.RunUnityPlayerActivity(Config.AppId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to restart app");
                var builder = new DialogBuilder
                {
                    Title = Strings.Tools_Tool_RestartApp_Failed_Title,
                    Text = Strings.Tools_Tool_RestartApp_Failed_Text,
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
            }
            finally
            {
                Locker.FinishOperation();
            }
        }

        public void OpenThemesFolder()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ThemeManager.ThemesDirectory,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        
        private async void ShowLanguageChangeDialog()
        {
            Strings.Culture = Config.Language.ToCultureInfo(); // Update the resource language so the dialog is in the correct language 
            
            var builder = new DialogBuilder
            {
                Title = Strings.Tools_Option_Language_Title,
                Text = Strings.Tools_Option_Language_Text,
            };
            builder.OkButton.Text = Strings.Generic_OK;
            builder.CancelButton.Text = Strings.Generic_NotNow;
            builder.OkButton.CloseDialogue = false;
            builder.OkButton.OnClick = () =>
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if(exePath != null)
                {
                    Process.Start(exePath);
                }

                _quit();
            };
            

            await builder.OpenDialogue(_mainWindow);
        }

        public async Task<bool> ConfirmEnableExpertMode()
        {
            var builder = new DialogBuilder { Title = "启用专家模式", Text = "专家模式允许更改一些高级设置\n更改这些设置可能会导致游戏黑屏崩溃或Mod无法正常加载" };
            builder.OkButton.Text = "启用(不推荐)";
            return await builder.OpenDialogue(_mainWindow);
        }
    }
}
