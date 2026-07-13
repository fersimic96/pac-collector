using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PacCollector.Api.Services;

public sealed class SystemService
{
    public string OsPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macos";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        return "unknown";
    }

    public bool OpenNetworkSettings()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start("ms-settings:network");
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", "x-apple.systempreferences:com.apple.preference.network");
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", "settings://network");
            else
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
