using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using QuestPatcher.Core;
using QuestPatcher.Core.ModBrowser;
using QuestPatcher.Core.ModBrowser.Models;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Models;
using QuestPatcher.Models;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.ViewModels.ModBrowser
{
    public class BrowseModViewModel : ViewModelBase
    {
        public enum ModListState
        {
            Loading,
            Empty,
            ModsLoaded,
            LoadError
        }
        
        private readonly Window _window;
        private readonly Config _config;
        private readonly InstallManager _installManager;
        private readonly ModManager _modManager;
        private readonly ExternalModManager _externalModManager;

        // Is concurrent dict necessary here?
        private readonly IDictionary<string, ExternalModViewModel> _modDictionary = new ConcurrentDictionary<string, ExternalModViewModel>();
        public IReadOnlyList<ExternalModViewModel> Mods => _modDictionary.Values.OrderBy(mod => mod.Name).ToList();

        public OperationLocker Locker { get; }
        public ProgressViewModel ProgressView { get; }

        private ModListState _state = ModListState.Empty;
        // do this so that the UI can bind to the state without using a long converter as below
        // Converter={x:Static ObjectConverters.Equal}, ConverterParameter={x:Static modBrowserVMs:ModListState.ModsLoaded}
        public bool IsModLoading => State == ModListState.Loading;
        public bool IsModListEmpty => State == ModListState.Empty;
        public bool IsModListLoaded => State == ModListState.ModsLoaded;
        public bool IsModLoadError => State == ModListState.LoadError;
        
        private HashSet<string> _selectedMods = new HashSet<string>();
        
        public bool IsAnyModSelected => _selectedMods.Count > 0;
        
        private string? _currentGameVersion = null;

        public ModListState State
        {
            get => _state;
            private set
            {
                _state = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(IsModLoading));
                this.RaisePropertyChanged(nameof(IsModListEmpty));
                this.RaisePropertyChanged(nameof(IsModListLoaded));
                this.RaisePropertyChanged(nameof(IsModLoadError));
            }
        }
        public string EmptyMessage { get; private set; } = "";
        
        public Exception? LoadError { get; private set; } = null;
        
        private bool _showBatchInstall = false;
        public bool ShowBatchInstall
        {
            get => _showBatchInstall;
            set
            {
                _showBatchInstall = value;
                this.RaisePropertyChanged();
                if (!value)
                {
                    // uncheck all the mods when disable batch selection
                    foreach (var mod in Mods)
                    {
                        mod.IsChecked = false;
                    }
                }
            }
        }
        
        public string SelectedModsCountText => _selectedMods.Count == 0 ? "" : $"已选择 {_selectedMods.Count} 个Mod";
        
        public bool IsAllModsSelected
        {
            get => _selectedMods.Count == _modDictionary.Count;
            set
            {
                foreach (var mod in Mods)
                {
                    mod.IsChecked = value;
                }
            }
        }

        public BrowseModViewModel(Window window, Config config, OperationLocker locker, ProgressViewModel progressView, InstallManager installManager, ModManager modManager, ExternalModManager externalModManager)
        {
            _window = window;
            _config = config;
            Locker = locker;
            ProgressView = progressView;
            _installManager = installManager;
            _modManager = modManager;
            _externalModManager = externalModManager;

            _installManager.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(InstallManager.InstalledApp))
                {
                    string? newVersion = _installManager.InstalledApp?.Version;
                    // no need to load again if the version is the same, it may because the app has just been patched
                    if (newVersion != _currentGameVersion)  
                    {
                        _currentGameVersion = newVersion;
                        Task.Run(LoadMods);
                    }
                }
            };
        }

        public async Task LoadMods()
        {
            string? version = _currentGameVersion;
            if (string.IsNullOrWhiteSpace(version)) return;
            if (State == ModListState.Loading) return;
            State = ModListState.Loading;
            UnsubscribeFromModInstallEvents();
            await Task.Delay(100); // Delay to prevent flickering, also make sure no event handler is running (hopefully)
            _modDictionary.Clear();
            try
            {
                var mods = await _externalModManager.GetAvailableMods(version);
                if (mods == null)
                {
                    // network error
                    SetListState(ModListState.LoadError, "出错了！\n无法加载Mod列表，请检查网络连接并重试");
                }
                else if (mods.Count == 0)
                {
                    SetListState(ModListState.Empty, $"当前游戏版本 {version} 暂无可用Mod");
                }
                else
                {
                    foreach (var mod in mods)
                    {
                        _modDictionary[mod.Id] = new ExternalModViewModel(mod, Locker, this);
                    }

                    SubscribeToModInstallEventsAndLoadInstallStatus();
                    SetListState(ModListState.ModsLoaded, "");
                }
            }
            catch (Exception e)
            {
                // unexpected error
                Log.Error("Unexpected error when loading mods: {Message}", e.Message);
                SetListState(ModListState.LoadError, "加载Mod列表时发生了意外错误", e);
            }
            finally
            {
                this.RaisePropertyChanged(nameof(Mods));
            }

            Debug.Assert(State != ModListState.Loading);
        }

        private void SetListState(ModListState state, string message, Exception? exception = null)
        {
            Log.Debug("Setting state to {State} ", state);
            State = state;
            EmptyMessage = message;
            LoadError = exception;
            this.RaisePropertyChanged(nameof(EmptyMessage));
            this.RaisePropertyChanged(nameof(LoadError));
        }
        
        private void SubscribeToModInstallEventsAndLoadInstallStatus()
        {
            _modManager.Mods.CollectionChanged -= OnModListChanged;
            _modManager.Mods.CollectionChanged += OnModListChanged;
            _modManager.Libraries.CollectionChanged -= OnModListChanged;
            _modManager.Libraries.CollectionChanged += OnModListChanged;
            
            // clone the collection to avoid threading issues
            // ModManager will be loading current mods in another thread
            AddInstallStatus(_modManager.Mods.ToArray());
            AddInstallStatus(_modManager.Libraries.ToArray());
        }
        
        private void UnsubscribeFromModInstallEvents()
        {
            _modManager.Mods.CollectionChanged -= OnModListChanged;
            _modManager.Libraries.CollectionChanged -= OnModListChanged;
        }
        
        private void OnModListChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                RemoveInstallStatus(_modDictionary.Keys);
                return;
            }
            
            if (args.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace)
            {
                AddInstallStatus(args.NewItems?.Cast<IMod>().ToArray());
            }
            
            if (args.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace)
            {
                if (args.OldItems != null)
                {
                    RemoveInstallStatus(args.OldItems.Cast<IMod>().Select(mod => mod.Id).ToArray());
                }
            }
        }
        
        private void AddInstallStatus(ICollection<IMod>? mods)
        {
            if (mods == null) return;
            foreach (var mod in mods)
            {
                if (_modDictionary.TryGetValue(mod.Id, out var modVm))
                {
                    modVm.UpdateInstallStatus(mod);
                }
            }
        }

        private void RemoveInstallStatus(ICollection<string> mods)
        {
            foreach (string modId in mods)
            {
                if (_modDictionary.TryGetValue(modId, out var modVm))
                {
                    modVm.UpdateInstallStatus(null);
                }
            }
        }
        
        // Should only be called by ExternalModViewModel to sync the select state
        internal void SetModSelection(string id, bool selected)
        {
            bool wasAllModsSelected = IsAllModsSelected;
            if (selected)
            {
                _selectedMods.Add(id);
                if (IsAllModsSelected)
                {
                    this.RaisePropertyChanged(nameof(IsAllModsSelected));
                }
            }
            else
            {
                _selectedMods.Remove(id);
                if (wasAllModsSelected)
                {
                    // it is definitely not all selected now
                    this.RaisePropertyChanged(nameof(IsAllModsSelected));
                }
            }
            
            this.RaisePropertyChanged(nameof(IsAnyModSelected));
            this.RaisePropertyChanged(nameof(SelectedModsCountText));
        }

        public async Task OnBatchInstallClicked()
        {
            if (_selectedMods.Count == 0) return;
            var selectedMods = Mods.Where(mod => mod is {IsChecked:true, IsLatestInstalled:false}).Select(mod => mod.Mod).ToList();
            if (await InstallSelectedMods(selectedMods))
            {
                ShowBatchInstall = false; //hide the batch install things after we successfully finished the batch install
            }
        }

        /// <summary>
        /// Install mods
        /// </summary>
        /// <param name="mods">Collection of mods to install</param>
        /// <returns>Whether all mods are successfully installed</returns>
        public async Task<bool> InstallSelectedMods(ICollection<ExternalMod> mods)
        {
            try
            {
                Locker.StartOperation();
                if (mods.Count == 1)
                {
                    return await InstallMod(mods.First(), true);
                }
                else
                {
                    foreach (var mod in mods)
                    {
                        // Install mod
                        if (_modDictionary.TryGetValue(mod.Id, out var modVm) && !modVm.IsLatestInstalled)
                        {
                            if (!await InstallMod(mod, false)) return false;
                        }
                    }
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to install selected mods: {Message}", e.Message);
                var dialog = new DialogBuilder { Title = "出错了！", Text = "安装Mod时发生了意料之外错误", HideCancelButton = true };
                dialog.WithException(e);
                await dialog.OpenDialogue(_window);
                return false;
            }
            finally
            {
                Locker.FinishOperation();
            }
        }
        
        private async Task<bool> InstallMod(ExternalMod mod, bool isSingle)
        {
            Log.Debug("Installing {Mod}", mod.Name);
            try
            {
                if (await _externalModManager.InstallMod(mod))
                {
                    return true;
                }

                Log.Warning("Failed to install mod {Mod}", mod.Name);
                DialogBuilder dialog;
                if (isSingle)
                {
                    dialog = new DialogBuilder { Title = "安装失败", Text = $"无法安装Mod {mod.Name}，检查日志以获取更多信息。" };
                }
                else
                {
                    dialog = new DialogBuilder
                    {
                        Title = "安装失败", Text = $"无法安装 {mod.Name}，检查日志以获取更多信息。\n要继续安装其他Mod吗？"
                    };
                    dialog.OkButton.Text = "继续";
                }

                return await dialog.OpenDialogue(_window);
            }
            catch (InstallationException e) when (e.Message.Contains("Mod loader mis-match"))
            {
                var dialog = new DialogBuilder
                {
                    Title = "不匹配的Mod注入器",
                    Text = "正在尝试安装的Mod需要的Mod注入器和当前使用的Mod注入器不匹配\n可以在工具页面点击 “重新打补丁” 来更换Mod注入器",
                    HideCancelButton = true
                };
                await dialog.OpenDialogue(_window);
                return false;
            }
            catch (FileDownloadFailedException e)
            {
                Log.Error(e, "Failed to download files when installing mod {Mod}: {Message}", mod.Name, e.Message);
                var dialog = new DialogBuilder
                {
                    Title = "无法下载文件",
                    Text = $"QuestPatcher 无法下载安装 {mod.Name} 所需的文件。请检查您的互联网连接，然后重试。",
                    HideCancelButton = true
                };
                await dialog.OpenDialogue(_window);
                return false;
            }
        }
    }
}
