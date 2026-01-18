using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace QuestPatcher.Core.Utils;

public class DownloadMirrorManager : SharableLoading<Dictionary<string, string>>
{
    private const string MirrorUrl = "https://bs.wgzeyu.com/localization/mods.json";

    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
    private readonly HttpClient _client = new();

    // Add static ones if needed
    private readonly Dictionary<string, string> _staticMirrors = new()
    {
    };

    private DateTime _lastRefreshTime;

    protected override async Task<Dictionary<string, string>> LoadAsync(CancellationToken cToken)
    {
        Log.Information("Loading download mirror urls");
        string res = await _client.GetStringAsync(MirrorUrl, cToken);
        var jObject = JsonNode.Parse(res)?.AsObject();
        if (jObject is null)
        {
            throw new Exception("Failed to deserialize download mirrors, invalid data structure");
        }

        var existing = Data;
        // we don't want to lose existing data
        var mirrorUrls = existing is null ? new Dictionary<string, string>() : new Dictionary<string, string>(existing);
        int count = 0;
        foreach (var pair in jObject)
        {
            string? mirror = pair.Value?["mirrorUrl"]?.ToString();
            if (mirror != null)
            {
                mirrorUrls[pair.Key] = mirror;
                count++;
            }
        }

        if (count == 0)
        {
            Log.Warning("No mirror urls found!");
        }
        else
        {
            Log.Debug("Loaded {Count} mirror urls", count);
        }

        _lastRefreshTime = DateTime.UtcNow;
        return mirrorUrls;
    }

    public async Task<string> GetMirrorUrl(string original)
    {
        bool refresh = false;
        if (DateTime.UtcNow - _lastRefreshTime > _refreshInterval)
        {
            Log.Information("Mirror Url cache too old! Refreshing");
            refresh = true;
        }

        Dictionary<string, string>? mirrors;
        try
        {
            mirrors = await GetOrLoadAsync(refresh);
        }
        catch (Exception e)
        {
            mirrors = Data;
            if (mirrors is not null)
            {
                Log.Warning(e, "Failed to load download mirror, using existing mirrors");
            }
            else
            {
                Log.Warning(e, "Failed to load download mirror");
            }
        }

        if (mirrors?.TryGetValue(original, out string? mirror) == true ||
            _staticMirrors.TryGetValue(original, out mirror))
        {
            Log.Debug("Mirror Url found: {Mirror}", mirror);
            return mirror;
        }

        Log.Warning("Mirror Url not found for {Original}", original);
        return original;
    }
}
