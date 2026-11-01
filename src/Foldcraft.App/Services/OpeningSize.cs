namespace Foldcraft.App.Services;

/// <summary>How big the window opens. Windows doesn't shrink a new window to fit, so a small laptop would lose the bottom of it.</summary>
public static class OpeningSize
{
    /// <summary>The share of the screen the window opens at, at most.</summary>
    private const double LargestShare = 0.92;

    public static double Fit(double wanted, double screen, double minimum) =>
        Math.Max(minimum, Math.Min(wanted, screen * LargestShare));
}
