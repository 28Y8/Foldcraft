using System.Globalization;

namespace Foldcraft.App.Services;

/// <summary>
/// Records problems in the data directory. Logging is what runs when something has already
/// gone wrong, so it never throws and it never grows without limit.
/// </summary>
public sealed class FileLog
{
    private const long DefaultMaxBytes = 1024 * 1024;

    private readonly long _maxBytes;
    private readonly Lock _gate = new();

    public FileLog(string path, long maxBytes = DefaultMaxBytes)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        _maxBytes = maxBytes;
    }

    public string Path { get; }

    public void Write(string message, Exception? exception = null)
    {
        try
        {
            var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            var text = exception is null ? $"{stamp} {message}\n" : $"{stamp} {message}\n{exception}\n";

            lock (_gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                RollOverIfFull();
                File.AppendAllText(Path, text);
            }
        }
        catch (Exception)
        {
            // There is nowhere left to report this. Losing a log line must not cost the app.
        }
    }

    private void RollOverIfFull()
    {
        var info = new FileInfo(Path);

        if (info.Exists && info.Length >= _maxBytes)
        {
            File.Move(Path, Path + ".old", overwrite: true);
        }
    }
}
