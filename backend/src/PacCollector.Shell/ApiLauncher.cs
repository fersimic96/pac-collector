using System.Diagnostics;

namespace PacCollector.Shell;

// si el Api no esta corriendo como service, lo arrancamos como child process
// del Shell. busca el binario al lado del Shell (publish output).
//
// CRITICAL: en single-file publish AppContext.BaseDirectory apunta al folder
// TEMPORAL de extraccion de DLLs nativos, NO donde esta el .exe. Usamos
// Environment.ProcessPath para resolver el folder real del Shell.exe; ahi
// es donde el publish dejo PacCollector.Api.exe como sibling.
internal static class ApiLauncher
{
    public static void SpawnSibling()
    {
        var shellDir = Path.GetDirectoryName(Environment.ProcessPath ?? typeof(ApiLauncher).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var apiName = OperatingSystem.IsWindows() ? "PacCollector.Api.exe" : "PacCollector.Api";
        var apiPath = Path.Combine(shellDir, apiName);
        if (!File.Exists(apiPath))
        {
            Console.Error.WriteLine($"[shell] PacCollector.Api.exe NOT FOUND at {apiPath} - UI will be blank");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = apiPath,
                WorkingDirectory = shellDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            Process.Start(psi);
            Console.WriteLine($"[shell] spawned sibling Api: {apiPath}");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[shell] failed to spawn Api: {e.Message}");
        }
    }
}
