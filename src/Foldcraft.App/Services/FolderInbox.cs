namespace Foldcraft.App.Services;

/// <summary>
/// Holds folders handed over by other starts until the window can take them, so listening can
/// begin long before the window is ready. Everything here runs on the UI thread.
/// </summary>
public sealed class FolderInbox
{
    private readonly List<string> _waiting = [];
    private Func<string, Task>? _open;

    /// <summary>Opens the folder now if the window is ready, and otherwise holds on to it.</summary>
    public void Receive(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_open is null)
        {
            _waiting.Add(path);
        }
        else
        {
            _ = _open(path);
        }
    }

    /// <summary>The window is ready. Whatever has been waiting goes first, in the order it came.</summary>
    /// <param name="open">Opens one folder. It handles its own failures.</param>
    public void Open(Func<string, Task> open)
    {
        _open = open ?? throw new ArgumentNullException(nameof(open));

        foreach (var path in _waiting)
        {
            _ = open(path);
        }

        _waiting.Clear();
    }
}
