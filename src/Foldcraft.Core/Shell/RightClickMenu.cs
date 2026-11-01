namespace Foldcraft.Core.Shell;

/// <summary>
/// The few things the right-click entry needs from the current user's registry. It's an
/// interface so that everything except <see cref="CurrentUserRegistry"/> can run without
/// touching a real registry, which is how the tests run.
/// </summary>
public interface IUserRegistry
{
    /// <summary>A string value, or null if the key or the value isn't there. A null name means the default value.</summary>
    string? GetString(string subKey, string? name);

    /// <summary>Sets a string value, creating the key if needed.</summary>
    void SetString(string subKey, string? name, string value);

    /// <summary>Removes one value. Removing one that isn't there does nothing.</summary>
    void DeleteValue(string subKey, string? name);

    /// <summary>True when the key exists.</summary>
    bool KeyExists(string subKey);

    /// <summary>The names of a key's values, with the default value as an empty string, and of its subkeys.</summary>
    (IReadOnlyList<string> Values, IReadOnlyList<string> SubKeys) Contents(string subKey);

    /// <summary>Removes a key that has nothing left in it. This can't remove anything else.</summary>
    void DeleteEmptyKey(string subKey);
}

public enum RightClickOutcome
{
    /// <summary>The entry was added, or was already exactly right.</summary>
    Added,

    /// <summary>The entry was taken off, along with everything Foldcraft added.</summary>
    Removed,

    /// <summary>There was nothing to take off.</summary>
    NothingThere,

    /// <summary>Something that isn't Foldcraft's is using that name, or changed the entry, so it was left alone.</summary>
    LeftAlone,

    /// <summary>The app's location can't be written safely into a command.</summary>
    CannotQuote,

    /// <summary>The registry refused. Nothing more was changed.</summary>
    Failed,
}

/// <summary>What a change to the entry did.</summary>
/// <param name="Outcome">How it went.</param>
/// <param name="Command">The command written, when there is one.</param>
/// <param name="Message">A technical reason, for the log.</param>
public sealed record RightClickResult(RightClickOutcome Outcome, string? Command = null, string? Message = null)
{
    /// <summary>Keys above the entry that had to be made for it. Turning it off needs these back.</summary>
    public IReadOnlyList<string> AddedParents { get; init; } = [];
}

/// <summary>
/// The optional "Customize with Foldcraft" entry on a folder's right-click menu: one key under
/// HKCU\Software\Classes\Directory\shell, for this user only. It's off until someone turns it
/// on, and turning it off takes away exactly the values added here, only if they're still what
/// was written and only removes a key once it's empty. Windows makes any missing key above the
/// entry along with it, and those count as added here too.
/// </summary>
public sealed class RightClickMenu(IUserRegistry registry)
{
    public const string VerbKey = @"Software\Classes\Directory\shell\Foldcraft.Customize";
    public const string MenuText = "Customize with Foldcraft";

    private const string CommandKey = VerbKey + @"\command";
    private const string IconValue = "Icon";

    /// <summary>The keys that may have to be made for the entry, innermost first, which is the order they can go in.</summary>
    private static readonly string[] ParentKeys = [@"Software\Classes\Directory\shell", @"Software\Classes\Directory"];

    private readonly IUserRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <summary>
    /// The command Explorer will run. The folder arrives as %1, quoted, so a space or an
    /// ampersand in its path stays part of the path. Null when a path can't be quoted safely.
    /// </summary>
    /// <param name="exePath">The full path of Foldcraft.exe.</param>
    /// <param name="dataDirectory">A custom data folder to carry over, or null for the default.</param>
    public static string? BuildCommand(string exePath, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(exePath);

        if (!IsQuotable(exePath) || (dataDirectory is not null && !IsQuotable(dataDirectory)))
        {
            return null;
        }

        var data = dataDirectory is null ? string.Empty : $" --data-dir \"{dataDirectory.TrimEnd('\\')}\"";

        return $"\"{exePath}\" --customize \"%1\"{data}";
    }

    /// <summary>Adds the entry. Anything already under that name that isn't Foldcraft's is left alone.</summary>
    public RightClickResult Enable(string exePath, string? dataDirectory = null)
    {
        var command = BuildCommand(exePath, dataDirectory);

        if (command is null)
        {
            return new RightClickResult(RightClickOutcome.CannotQuote);
        }

        var existed = false;
        string[] missing = [];

        try
        {
            existed = _registry.KeyExists(VerbKey);

            if (existed && !IsOurs(command, exePath))
            {
                return new RightClickResult(RightClickOutcome.LeftAlone);
            }

            missing = ParentKeys.Where(key => !_registry.KeyExists(key)).ToArray();

            _registry.SetString(CommandKey, null, command);
            _registry.SetString(VerbKey, null, MenuText);
            _registry.SetString(VerbKey, IconValue, $"\"{exePath}\",0");

            return new RightClickResult(RightClickOutcome.Added, command) { AddedParents = missing };
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            if (!existed)
            {
                TryTakeBack(missing);
            }

            return new RightClickResult(RightClickOutcome.Failed, Message: ex.Message);
        }
    }

    /// <summary>A failed start can't leave half an entry behind, because nothing would remember it.</summary>
    private void TryTakeBack(IReadOnlyCollection<string> parents)
    {
        try
        {
            RemoveEntry(parents);
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            // The first failure is the one to report.
        }
    }

    /// <summary>Only the known keys can go, whatever the settings file says. Even those go only once they're empty.</summary>
    private void RemoveEntry(IReadOnlyCollection<string>? parents)
    {
        _registry.DeleteValue(CommandKey, null);
        _registry.DeleteValue(VerbKey, null);
        _registry.DeleteValue(VerbKey, IconValue);

        DeleteIfEmpty(CommandKey);
        DeleteIfEmpty(VerbKey);

        foreach (var key in ParentKeys.Where(key => parents?.Contains(key) == true))
        {
            DeleteIfEmpty(key);
        }
    }

    /// <summary>
    /// Takes the entry off, if it's still what was added. Any value or key someone else put
    /// there stays, and so does the key holding it.
    /// </summary>
    /// <param name="command">The command Foldcraft wrote, from the settings file.</param>
    /// <param name="exePath">The path the entry's icon was written with.</param>
    /// <param name="addedParents">The keys above the entry that were made for it, from the settings file.</param>
    public RightClickResult Disable(string command, string exePath, IReadOnlyCollection<string>? addedParents = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(exePath);

        try
        {
            if (!_registry.KeyExists(VerbKey))
            {
                return new RightClickResult(RightClickOutcome.NothingThere);
            }

            if (!IsOurs(command, exePath))
            {
                return new RightClickResult(RightClickOutcome.LeftAlone);
            }

            RemoveEntry(addedParents);

            return new RightClickResult(RightClickOutcome.Removed);
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            return new RightClickResult(RightClickOutcome.Failed, Message: ex.Message);
        }
    }

    /// <summary>
    /// Takes the entry off if it starts this copy of Foldcraft, whichever data folder it was
    /// turned on with. This is for when there may be no settings file to say what was written.
    /// </summary>
    /// <param name="exePath">The copy of Foldcraft the entry has to start.</param>
    /// <param name="addedParents">The keys above the entry that were made for it, if they're known.</param>
    public RightClickResult DisableFor(string exePath, IReadOnlyCollection<string>? addedParents = null)
    {
        ArgumentNullException.ThrowIfNull(exePath);

        try
        {
            if (!_registry.KeyExists(VerbKey))
            {
                return new RightClickResult(RightClickOutcome.NothingThere);
            }

            var command = _registry.GetString(CommandKey, null);

            return command is not null && StartsThisCopy(command, exePath)
                ? Disable(command, exePath, addedParents)
                : new RightClickResult(RightClickOutcome.LeftAlone);
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            return new RightClickResult(RightClickOutcome.Failed, Message: ex.Message);
        }
    }

    /// <summary>True when the entry is exactly as written. A registry that can't be read says yes, so nothing acts on a guess.</summary>
    public bool IsInPlace(string command, string exePath)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(exePath);

        try
        {
            return IsOurs(command, exePath);
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            return true;
        }
    }

    private static bool StartsThisCopy(string command, string exePath)
    {
        var plain = BuildCommand(exePath);

        return plain is not null &&
            (command == plain || (command.StartsWith($"{plain} --data-dir \"", StringComparison.Ordinal) && command.EndsWith('"')));
    }

    private bool IsOurs(string command, string exePath) =>
        _registry.GetString(CommandKey, null) == command &&
        _registry.GetString(VerbKey, null) == MenuText &&
        _registry.GetString(VerbKey, IconValue) == $"\"{exePath}\",0";

    private void DeleteIfEmpty(string key)
    {
        if (!_registry.KeyExists(key))
        {
            return;
        }

        var (values, subKeys) = _registry.Contents(key);

        if (values.Count == 0 && subKeys.Count == 0)
        {
            _registry.DeleteEmptyKey(key);
        }
    }

    /// <summary>
    /// A path can't hold a quote, but a percent sign would be read as a variable and a
    /// relative path or a control character has no place in a stored command at all.
    /// </summary>
    private static bool IsQuotable(string path) =>
        Path.IsPathFullyQualified(path) &&
        !path.Contains('"') &&
        !path.Contains('%') &&
        !path.Any(char.IsControl);

    private static bool IsRegistryFailure(Exception ex) =>
        ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException;
}
