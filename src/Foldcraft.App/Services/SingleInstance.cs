using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Foldcraft.App.Services;

/// <summary>
/// Lets a second start hand its folder to the Foldcraft already running, so right-clicking
/// several folders fills one window. Each data folder has its own channel, and the pipe only
/// accepts this user. What arrives is treated like any other untrusted path by the receiver.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>A Windows path can't be longer than this, whatever is on the other end claims.</summary>
    private const int MaxMessageBytes = 66 * 1024;

    /// <summary>Long enough to reach a copy that's still starting up, which listens from early on.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly Mutex? _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();

    public SingleInstance(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        _pipeName = ChannelName(dataDirectory);

        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName(dataDirectory), out var createdNew);
            IsFirst = createdNew;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // A copy running as administrator holds a name this one can't open. It's still another
            // copy, so this start tries to hand off, and if that fails it runs by itself.
            IsFirst = false;
        }
    }

    /// <summary>True when no other Foldcraft is using this data folder.</summary>
    public bool IsFirst { get; }

    /// <summary>The name of the marker a running copy holds for a data folder.</summary>
    public static string MutexName(string dataDirectory) => $@"Local\{ChannelName(dataDirectory)}";

    /// <summary>
    /// Hands a folder to the running app, which comes to the front. An empty message only
    /// brings it forward. False means it couldn't be reached, and this start should carry on.
    /// </summary>
    public bool TrySend(string? path)
    {
        try
        {
            AllowSetForegroundWindow(AnyProcess);

            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);

            pipe.Connect(ConnectTimeout);
            pipe.Write(Encoding.UTF8.GetBytes(path ?? string.Empty));
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts taking messages. The callback runs on a background thread. Only the first copy
    /// listens: a later one that couldn't hand off just runs by itself. The channel is open by the
    /// time this returns, because a start that's busy with other work could otherwise leave
    /// the next copy waiting for it until that copy gave up.
    /// </summary>
    public void Listen(Action<string> onMessage)
    {
        ArgumentNullException.ThrowIfNull(onMessage);

        if (IsFirst)
        {
            var stop = _stop.Token;
            var channel = TryOpenChannel();

            _ = Task.Run(() => ListenLoopAsync(onMessage, channel, stop));
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();

        try
        {
            if (IsFirst)
            {
                _mutex?.ReleaseMutex();
            }
        }
        catch (ApplicationException)
        {
            // Only the owning thread can release it, and the process is ending anyway.
        }

        _mutex?.Dispose();
    }

    private static string ChannelName(string dataDirectory)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory).ToUpperInvariant())));

        return $"Foldcraft-{Environment.UserName}-{hash[..24]}";
    }

    private NamedPipeServerStream OpenChannel() => new(
        _pipeName,
        PipeDirection.In,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    /// <summary>Null when it can't be opened right now. The loop keeps trying.</summary>
    private NamedPipeServerStream? TryOpenChannel()
    {
        try
        {
            return OpenChannel();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task ListenLoopAsync(Action<string> onMessage, NamedPipeServerStream? channel, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = channel ?? OpenChannel();

                channel = null;

                await pipe.WaitForConnectionAsync(stop);

                if (await ReadMessageAsync(pipe, stop) is { } message)
                {
                    onMessage(message);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // One bad connection mustn't end the listening, and a pipe that can't be
                // created fails instantly, so a pause keeps this from spinning.
                try
                {
                    await Task.Delay(RetryDelay, stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        // Stopped before the loop got as far as taking this one.
        channel?.Dispose();
    }

    /// <summary>Null when the message is too long to be a path, or isn't text.</summary>
    private static async Task<string?> ReadMessageAsync(Stream pipe, CancellationToken stop)
    {
        var buffer = new byte[MaxMessageBytes + 1];
        var length = 0;

        while (length < buffer.Length)
        {
            var read = await pipe.ReadAsync(buffer.AsMemory(length), stop);

            if (read == 0)
            {
                break;
            }

            length += read;
        }

        if (length > MaxMessageBytes)
        {
            return null;
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(buffer, 0, length);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private const int AnyProcess = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
