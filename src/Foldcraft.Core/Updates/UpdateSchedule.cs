using Foldcraft.Core.Settings;

namespace Foldcraft.Core.Updates;

/// <summary>Decides when Foldcraft may ask for a new version by itself.</summary>
public static class UpdateSchedule
{
    /// <summary>The shortest time between two checks nobody asked for.</summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromDays(1);

    /// <summary>
    /// True when a check may run at this start. It's the attempt that counts, so being offline
    /// doesn't turn into a new try at every start. A time in the future means the clock moved,
    /// and it doesn't get to stop checks for good.
    /// </summary>
    public static bool IsDue(AppSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.CheckForUpdatesAutomatically)
        {
            return false;
        }

        return settings.LastUpdateAttempt is not { } last || last > now || now - last >= MinimumGap;
    }
}
