using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace Foldcraft.App.Services;

/// <summary>Whether Foldcraft has administrator rights, and how to ask for them.</summary>
public interface IElevation
{
    bool IsElevated { get; }

    /// <summary>
    /// Starts a copy with administrator rights and closes this one. False means no copy started,
    /// as when the person says no to Windows, and this one carries on. Anything else that stops it,
    /// such as a policy, throws <see cref="Win32Exception"/>.
    /// </summary>
    /// <param name="dataDirectory">The data folder given on the command line, if any.</param>
    bool TryRestartElevated(string? dataDirectory);
}

/// <summary>Used unless the real app passes the real thing, so no test can ever show a Windows prompt.</summary>
public sealed class NoElevation : IElevation
{
    public bool IsElevated => false;

    public bool TryRestartElevated(string? dataDirectory) => false;
}

public sealed class WindowsElevation : IElevation
{
    /// <summary>What Windows reports when someone says no, which is a choice and not a problem.</summary>
    private const int PersonSaidNo = 1223;

    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();

            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public bool TryRestartElevated(string? dataDirectory)
    {
        if (Environment.ProcessPath is not { } exePath)
        {
            return false;
        }

        var start = new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas" };

        start.ArgumentList.Add(AppOptions.ReplacesOption);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (dataDirectory is not null)
        {
            start.ArgumentList.Add(AppOptions.DataDirectoryOption);
            start.ArgumentList.Add(dataDirectory);
        }

        try
        {
            Process.Start(start)?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == PersonSaidNo)
        {
            return false;
        }

        Application.Current.Shutdown();
        return true;
    }
}
