namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// User scenario types for determining cooling period durations
/// </summary>
public enum UserScenario
{
    /// <summary>
    /// General use - 30 minutes cooling period
    /// </summary>
    GeneralUse,

    /// <summary>
    /// Office work - 15 minutes cooling period
    /// </summary>
    OfficeWork,

    /// <summary>
    /// Development session - 60 minutes cooling period
    /// </summary>
    DevelopmentSession,

    /// <summary>
    /// Gaming session - 90 minutes cooling period
    /// </summary>
    GamingSession,

    /// <summary>
    /// Video watching - 120 minutes cooling period
    /// </summary>
    VideoWatching
}
