using System.Runtime.InteropServices;
using Foldcraft.Core.Infrastructure;

namespace Foldcraft.Core.Safety;

/// <summary>
/// The system locations Foldcraft must leave alone. <see cref="Detect"/> asks Windows for
/// them; nothing here is a hardcoded drive letter. Tests build their own instance, because
/// the system temp directory sits inside the real AppData and would otherwise be refused.
/// </summary>
public sealed record ProtectedFolders
{
    public IReadOnlyList<string> Windows { get; init; } = [];
    public IReadOnlyList<string> ProgramFiles { get; init; } = [];
    public IReadOnlyList<string> ProgramData { get; init; } = [];

    /// <summary>Only the profile root is refused: Documents, Pictures and so on are fair game.</summary>
    public IReadOnlyList<string> UserProfiles { get; init; } = [];

    public IReadOnlyList<string> AppData { get; init; } = [];

    /// <summary>
    /// Every other account's profile. Windows keeps a normal copy out of these by itself, but not
    /// one running as administrator, so that's when they're refused.
    /// </summary>
    public IReadOnlyList<string> OtherProfiles { get; init; } = [];

    public static ProtectedFolders Detect()
    {
        var profile = Known(Environment.SpecialFolder.UserProfile);

        return new ProtectedFolders
        {
            Windows = Present(Known(Environment.SpecialFolder.Windows)),
            ProgramFiles = Present(
                Known(Environment.SpecialFolder.ProgramFiles),
                Known(Environment.SpecialFolder.ProgramFilesX86),
                KnownFolder(NativeMethods.FOLDERID_ProgramFilesX64)),
            ProgramData = Present(Known(Environment.SpecialFolder.CommonApplicationData)),
            UserProfiles = Present(profile),
            AppData = Present(
                Known(Environment.SpecialFolder.ApplicationData),
                Known(Environment.SpecialFolder.LocalApplicationData),
                string.IsNullOrEmpty(profile) ? null : Path.Combine(profile, "AppData")),
            OtherProfiles = ProfilesBeside(profile),
        };
    }

    /// <summary>The folders next to a profile, which are the other accounts'. Public is shared, so it isn't one.</summary>
    private static string[] ProfilesBeside(string profile)
    {
        if (string.IsNullOrEmpty(profile) || Path.GetDirectoryName(profile) is not { } parent)
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(parent)
                .Where(path => !path.Equals(profile, StringComparison.OrdinalIgnoreCase))
                .Where(path => !Path.GetFileName(path).Equals("Public", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Known(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>SHGetKnownFolderPath, which a 32-bit process needs for the 64-bit Program Files.</summary>
    private static string? KnownFolder(Guid id)
    {
        if (NativeMethods.SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var pointer) != 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static string[] Present(params string?[] paths) =>
        paths.Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
