using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PacCollector.Shell;

// wrapper minimo del SCM de Windows para chequear si el service esta instalado
// y arrancarlo si lo esta. Si la app corre fuera de Windows o el service no
// existe, el Shell hace fallback a spawnear el .Api colateral.
internal static partial class ServiceController
{
    public static bool IsServiceInstalled(string serviceName)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return CheckOrStart(serviceName, startIfStopped: false);
    }

    public static bool TryStart(string serviceName)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return CheckOrStart(serviceName, startIfStopped: true);
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckOrStart(string serviceName, bool startIfStopped)
    {
        var scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var access = startIfStopped ? SERVICE_START | SERVICE_QUERY_STATUS : SERVICE_QUERY_STATUS;
            var svc = OpenServiceW(scm, serviceName, access);
            if (svc == IntPtr.Zero) return false;
            try
            {
                if (!startIfStopped) return true;
                return StartServiceW(svc, 0, IntPtr.Zero);
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_QUERY_STATUS = 0x0004;

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr OpenSCManagerW(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr OpenServiceW(IntPtr scm, string name, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartServiceW(IntPtr svc, uint argc, IntPtr argv);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(IntPtr h);
}
