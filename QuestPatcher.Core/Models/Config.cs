using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace QuestPatcher.Core.Models
{
    public class Config : INotifyPropertyChanged
    {
        private string _appId = "";
        public string AppId
        {
            get => _appId;
            set
            {
                if (value != _appId)
                {
                    _appId = value;
                    NotifyPropertyChanged();
                }
            }
        }

        [DefaultValue(Language.Default)]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Language Language { get; set; } = Language.Default;

        private bool _displayLogs;

        [DefaultValue(false)]
        public bool DisplayLogs
        {
            get => _displayLogs;
            set
            {
                if (value != _displayLogs)
                {
                    _displayLogs = value;
                    NotifyPropertyChanged();
                }
            }
        }

        [DefaultValue(null)]
        [JsonPropertyName("patchingPermissions")]
        public PatchingOptions PatchingOptions
        {
            get => _patchingPermissions;
            set
            {
                // Used to get round default JSON values not being able to be objects. We instead set it to null by default then have the default backing field set to the default value
                if (value != _patchingPermissions && value != null)
                {
                    _patchingPermissions = value;
                    NotifyPropertyChanged();
                }

            }
        }
        private PatchingOptions _patchingPermissions = new();

        [DefaultValue(false)]
        public bool ShowPatchingOptions
        {
            get => _showPatchingOptions;
            set
            {
                if (value != _showPatchingOptions)
                {
                    _showPatchingOptions = value;
                    NotifyPropertyChanged();
                }
            }
        }
        private bool _showPatchingOptions;

        [DefaultValue(true)]
        public bool UseMirrorDownload { get; set; } = true;

        private ExternalModSource _externalModSource = ExternalModSource.BSQModsCN;

        [DefaultValue(ExternalModSource.BSQModsCN)]
        public ExternalModSource ExternalModSource
        {
            get => _externalModSource;
            set
            {
                if (value != _externalModSource)
                {
                    _externalModSource = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private bool _expertMode = false;

        [DefaultValue(false)]
        public bool ExpertMode
        {
            get => _expertMode;
            set
            {
                if (value != _expertMode)
                {
                    _expertMode = value;
                    NotifyPropertyChanged();
                }

                if (!value)
                {
                    PatchingOptions.CleanUpMods = true;
                    PatchingOptions.AllowDowngrade = true;
                    PatchingOptions.InstallCoreMods = true;
                }
            }
        }

        public string SelectedThemeName { get; set; } = "Dark";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
