using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using QuestPatcher.Core.ModBrowser.Models;
using QuestPatcher.Core.Modding;
using Serilog;

namespace QuestPatcher.Core.ModBrowser
{
    public class ExternalModManager
    {
        private const string VersionModUrlBase = "https://mods.bsquest.xyz/";
        
        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly InstallManager _installManager;
        private readonly ModManager _modManager;
        
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly Dictionary<string, List<ExternalMod>> _modCache = new Dictionary<string, List<ExternalMod>>();

        public ExternalModManager(ExternalFilesDownloader filesDownloader, InstallManager installManager,
            ModManager modManager)
        {
            _filesDownloader = filesDownloader;
            _installManager = installManager;
            _modManager = modManager;
        }

        /// <summary>
        /// Gets the available mods for the specified game version
        /// </summary>
        /// <param name="gameVersion">The specific game version</param>
        /// <returns>The mods available for the game version or null if http request failed</returns>
        /// <exception cref="Exception">Unexpected exception (not http) when loading mods</exception>
        public async Task<IReadOnlyList<ExternalMod>?> GetAvailableMods(string gameVersion)
        {
            Log.Debug("Fetching mods for {GameVersion}", gameVersion);
            if (_modCache.TryGetValue(gameVersion, out var mods))
            {
                return mods;
            }

            mods = null;

            try
            {
                // id -> version -> mod
                var modsRaw =
                    await _httpClient.GetFromJsonAsync<Dictionary<string, Dictionary<string, ExternalMod>>>($"{VersionModUrlBase}{gameVersion}.json");
                mods = new List<ExternalMod>(modsRaw!.Count);
                var latestMods = modsRaw.Values.Select(modGroup => modGroup.Values.MaxBy(mod => mod.Version)!);
                mods.AddRange(latestMods);
                Log.Debug("Loaded {ModCount} available mods for {GameVersion}", mods.Count, gameVersion);
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == HttpStatusCode.NotFound)
                {
                    Log.Information("No mods found for {GameVersion}", gameVersion);
                    // There is no mods for this version
                    mods = new List<ExternalMod>();
                }
                // something went wrong with the request if it is not a 404
                if (e.StatusCode != null)
                {
                    Log.Warning("Fail to fetch mods, status code: {StatusCode}", e.StatusCode);
                }
                else
                {
                    Log.Warning(e, "Failed to fetch mods: {Message}", e.Message);
                }
            }
            catch (Exception e)
            {
                // Something unexpected happened
                Log.Error(e, "Failed to fetch mods for {GameVersion}: {Message}", gameVersion, e.Message);
                throw new Exception($"Failed to fetch mods for {gameVersion}", e);
            }

            if (mods != null)
            {
                _modCache[gameVersion] = mods;
            }

            return mods;
        }
        
        /// <summary>
        /// Install the specified mod
        /// </summary>
        /// <param name="eMod">The mod to install</param>
        /// <exception cref="FileDownloadFailedException">Failed to download the mod file</exception>
        /// <returns>Whether install was successful</returns>
        public async Task<bool> InstallMod(ExternalMod eMod)
        {
            Log.Debug("Installing mod {Mod}", eMod.ToString());
            using var tempFile = new TempFile();
            await _filesDownloader.DownloadUri(eMod.DownloadUrl, tempFile.Path, eMod.Name);
            // assume the file is qmod since there isn't any other supported mod file type
            var qMod = await _modManager.TryParseMod(tempFile.Path, ".qmod");
            if (qMod is null)
            {
                return false;
            }

            if (qMod.ModLoader != _installManager.InstalledApp?.ModLoader)
            {
                Log.Warning("Trying to install an external mod with a different mod loader!");
                throw new InstallationException("Mod loader mis-match");
            }

            await qMod.Install();
            await _modManager.SaveMods();
            return true;
        }
    }
}
