using System.Net.Http;
using System.Net.Sockets;
using Velopack.Exceptions;

namespace Foldcraft.App.Services.Updates;

/// <summary>Why a check or a download failed, in terms the app can explain.</summary>
public enum UpdateFailure
{
    /// <summary>GitHub couldn't be reached, as when this PC is offline.</summary>
    CouldNotConnect,

    /// <summary>GitHub answered, but not with what was asked for.</summary>
    ServiceRefused,

    /// <summary>A download didn't match its recorded hash, so it was thrown away.</summary>
    DamagedDownload,

    /// <summary>The app's own folder couldn't be written.</summary>
    CouldNotSave,

    /// <summary>Anything else.</summary>
    Unknown,
}

/// <summary>Sorts failures. Exception messages can hold paths under the user's profile, so none of this uses one.</summary>
public static class UpdateFailures
{
    /// <summary>
    /// Where a connection that ends in the middle of an encrypted record fails. .NET throws a plain
    /// IOException for that, with nothing inside it and only this to tell it from a full disk.
    /// </summary>
    private const string TlsLibrary = "System.Net.Security";

    public static UpdateFailure Classify(Exception exception) => exception switch
    {
        ChecksumFailedException => UpdateFailure.DamagedDownload,
        HttpRequestException { StatusCode: not null } => UpdateFailure.ServiceRefused,
        HttpRequestException or SocketException or TimeoutException or TaskCanceledException => UpdateFailure.CouldNotConnect,
        HttpIOException or IOException { InnerException: SocketException } or IOException { Source: TlsLibrary } => UpdateFailure.CouldNotConnect,
        IOException or UnauthorizedAccessException => UpdateFailure.CouldNotSave,
        _ => UpdateFailure.Unknown,
    };

    /// <summary>A line for the log: what kind of failure it was and no more.</summary>
    public static string ForLog(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var status = exception is HttpRequestException { StatusCode: { } code } ? $", status {(int)code}" : string.Empty;

        return $"{exception.GetType().Name} (0x{exception.HResult:X8}{status})";
    }
}
