using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuestPatcher.Core.Models
{
    /// <summary>
    /// Specifies which permissions will be added to the APK during patching
    /// </summary>
    public class PatchingOptions : INotifyPropertyChanged
    {
        public bool ExternalFiles { get; set; } = true; // Not changeable in UI, since 90% of mods need this to work

        public bool Debuggable { get; set; } // Allows debugging with GDB or LLDB

        /// <summary>
        /// Used to support loading legacy configs
        /// </summary>
        public bool HandTracking
        {
            set
            {
                HandTrackingType = value ? HandTrackingVersion.V1 : HandTrackingVersion.None;
            }
        }

        public bool MrcWorkaround { get; set; }

        public bool Microphone { get; set; }

        public bool OpenXR { get; set; }

        public bool FlatScreenSupport { get; set; }

        public HandTrackingVersion HandTrackingType { get; set; }

        private ModLoader _modLoader = ModLoader.Scotland2;

        public ModLoader ModLoader
        {
            get => _modLoader;
            set
            {
                _modLoader = value;
                NotifyPropertyChanged();
            }
        }

        public bool Passthrough { get; set; }

        public bool BodyTracking { get; set; }

        /// <summary>
        /// Path to a PNG file containing a custom splash screen.
        /// </summary>
        public string? CustomSplashPath { get; set; } = null;

        private bool _cleanUpMods = true;

        public bool CleanUpMods
        {
            get => _cleanUpMods;
            set
            {
                _cleanUpMods = value;
                NotifyPropertyChanged();
            }
        }

        private bool _installCoreMods = true;

        public bool InstallCoreMods
        {
            get => _installCoreMods;
            set
            {
                _installCoreMods = value;
                NotifyPropertyChanged();
            }
        }

        private bool _allowDowngrade = true;

        public bool AllowDowngrade
        {
            get => _allowDowngrade;
            set
            {
                _allowDowngrade = value;
                NotifyPropertyChanged();
            }
        }

        private bool _autoDowngrade;

        public bool AutoDowngrade
        {
            get => _autoDowngrade;
            set
            {
                _autoDowngrade = value;
                NotifyPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
