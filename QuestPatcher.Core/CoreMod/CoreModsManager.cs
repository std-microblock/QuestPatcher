using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuestPatcher.Core.CoreMod.Models;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Utils;
using Serilog;
using Version = SemanticVersioning.Version;

namespace QuestPatcher.Core.CoreMod
{
    public class CoreModsManager : SharableLoading<ReadOnlyDictionary<string, CoreMods>>
    {
        private const string BeatSaberCoreModsUrl =
            "https://raw.githubusercontent.com/QuestPackageManager/bs-coremods/main/core_mods.json";

        private readonly ModManager _modManager;
        private readonly InstallManager _installManager;
        private readonly HttpClient _client = new();

        public CoreModsManager(ModManager modManager, InstallManager installManager)
        {
            _modManager = modManager;
            _installManager = installManager;
        }

        protected override async Task<ReadOnlyDictionary<string, CoreMods>> LoadAsync(CancellationToken cToken)
        {
            Log.Information("Loading Core Mods");
            string res = await _client.GetStringAsync(BeatSaberCoreModsUrl, cToken);
            var coreMods = JsonSerializer.Deserialize<Dictionary<string, CoreMods>>(res);

            if (coreMods == null)
            {
                throw new Exception("Failed to deserialize core mods, invalid data structure");
            }

            Log.Debug("Loaded core mods for {CoreMods} versions", coreMods.Count);
            return new ReadOnlyDictionary<string, CoreMods>(coreMods);
        }

        public async Task<IReadOnlyList<CoreModData>> GetCoreModsAsync(string version, bool refresh = false,
            CancellationToken cToken = default)
        {
            var data = await GetOrLoadAsync(refresh, cToken);
            if (data.TryGetValue(version, out var coreMods))
            {
                return coreMods.Mods.AsReadOnly();
            }

            return Array.Empty<CoreModData>();
        }

        public async Task<bool> IsCoreModsAvailableAsync(string version, bool refresh = false)
        {
            var coreMods = await GetCoreModsAsync(version, refresh);
            return coreMods.Count > 0;
        }

        public async Task<IReadOnlySet<string>> GetAllAvailableVersionsAsync(bool refresh = false)
        {
            var data = await GetOrLoadAsync(refresh);
            return data.Keys.ToHashSet();
        }

        /// <summary>
        ///     Verify the core mods install status. Will enable disabled core mods.
        /// </summary>
        /// <returns>The missing core mods. Null if there are no core mods at all</returns>
        public async Task<IReadOnlyList<CoreModData>?> VerifyCoreModsAsync(bool refresh)
        {
            Log.Information("Verifying core mods");
            string? packageVersion = _installManager.InstalledApp?.Version;
            if (packageVersion == null)
            {
                Log.Warning("Trying to check core mods without game being installed");
                return null;
            }

            var coreMods = await GetCoreModsAsync(packageVersion, refresh);
            if (coreMods.Count == 0)
            {
                return null;
            }

            var missingCoreMods = new List<CoreModData>();
            foreach (var coreMod in coreMods)
            {
                var existingCoreMod = _modManager.AllMods.Find(mod => mod.Id == coreMod.Id);
                if (existingCoreMod == null)
                {
                    // not installed at all, or not for the right version of the game
                    missingCoreMods.Add(coreMod);
                }
                else if (Version.TryParse(coreMod.Version, true, out var version) && version > existingCoreMod.Version)
                {
                    // this coreMod is newer than the installed one
                    // don't allow core mod downgrade when checking against core mod json

                    missingCoreMods.Add(coreMod); // install the new one
                }
                else
                {
                    // the existing one is the "latest", enable it if not already

                    // we can't reliably check existingCoreMod's target game version
                    // existingCoreMod.PackageVersion can be null which we will assume it will work

                    // existingCoreMod.PackageVersion can be not matching the game installed while still
                    // list as the core mod for the installed game version

                    // game downgrade or upgrade from qp will delete all mods

                    if (!existingCoreMod.IsInstalled)
                    {
                        await existingCoreMod.Install();
                    }
                }
            }

            return missingCoreMods;
        }
    }
}
