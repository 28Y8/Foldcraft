using System.Text.Json;
using System.Text.Json.Serialization;
using Foldcraft.Core.Infrastructure;

namespace Foldcraft.Core.Settings;

/// <summary>Which colors the app itself uses.</summary>
public enum AppThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>What was added to the registry, kept so turning it off removes that and nothing else.</summary>
/// <param name="Command">The command written for the entry.</param>
/// <param name="ExePath">The copy of Foldcraft the entry starts.</param>
/// <param name="AddedParents">Keys above the entry that weren't there until it was added, if any.</param>
public sealed record RightClickRecord(string Command, string ExePath, IReadOnlyList<string>? AddedParents = null);

/// <summary>The app's choices. Nothing here affects any folder.</summary>
public sealed record AppSettings
{
    public AppThemeMode ThemeMode { get; init; } = AppThemeMode.System;

    /// <summary>Set while the right-click entry is on. It's the only reason the registry gets read at all.</summary>
    public RightClickRecord? RightClick { get; init; }

    /// <summary>Whether to ask GitHub for a new version at startup, at most once a day. On unless turned off.</summary>
    public bool CheckForUpdatesAutomatically { get; init; } = true;

    /// <summary>When a check last started, whether or not it got an answer. This is what limits checks to one a day.</summary>
    public DateTimeOffset? LastUpdateAttempt { get; init; }

    /// <summary>When a check last got an answer. This is the time Settings shows.</summary>
    public DateTimeOffset? LastUpdateCheck { get; init; }
}

/// <summary>What reading the settings file found.</summary>
/// <param name="Settings">The saved settings, or the defaults.</param>
/// <param name="Unreadable">
/// True when there is a file but it couldn't be used. The defaults then say nothing about what the
/// person chose, and the file may still hold those choices, so it must not simply be overwritten.
/// </param>
public sealed record SettingsRead(AppSettings Settings, bool Unreadable);

/// <summary>Keeps <see cref="AppSettings"/> in the data folder. A file that can't be read means the defaults.</summary>
public sealed class SettingsStore
{
    private const int MaxFileBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter<AppThemeMode>() },
    };

    private readonly FoldcraftPaths _paths;
    private readonly FileOperations _ops;

    public SettingsStore(FoldcraftPaths paths, FileOperations? operations = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _ops = operations ?? new FileOperations();
    }

    public AppSettings Load() => Read().Settings;

    public SettingsRead Read()
    {
        try
        {
            var info = new FileInfo(_paths.SettingsFile);

            if (!info.Exists)
            {
                return new SettingsRead(new AppSettings(), Unreadable: false);
            }

            if (info.Length <= MaxFileBytes &&
                JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(info.FullName), JsonOptions) is { } settings &&
                Enum.IsDefined(settings.ThemeMode))
            {
                return new SettingsRead(settings, Unreadable: false);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Settings are only preferences, so losing them is no reason not to start.
        }

        return new SettingsRead(new AppSettings(), Unreadable: true);
    }

    /// <summary>Moves a file that couldn't be read to where a save won't touch it. Only the newest one is kept.</summary>
    public void SetAsideUnreadable()
    {
        if (File.Exists(_paths.SettingsFile))
        {
            File.Move(_paths.SettingsFile, _paths.UnreadableSettingsFile, overwrite: true);
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(_paths.DataDirectory);
        AtomicFile.Write(_paths.SettingsFile, JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions), FileAttributes.Normal, _ops);
    }
}
