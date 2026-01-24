namespace QuestPatcher.Core.Patching
{
    /// <summary>
    /// Represents a part of the patching process
    /// </summary>
    public enum PatchingStage
    {
        NotStarted,
        Downgrading,
        FetchingFiles,
        MovingToTemp,
        Patching,
        Signing,
        UninstallingOriginal,
        InstallingModded,
        CleanUpMods,
        InstallCoreMods
    }
}
