using System.Text;

namespace PacCollector.Infrastructure.Filesystem;

// escribe a tmp con guid, flush a disco, rename atomico
internal static class AtomicWriter
{
    private const int BufferSize = 4096;
    private static readonly UTF8Encoding Utf8NoBom = new(false, throwOnInvalidBytes: false);

    public static async Task WriteAllTextAsync(string path, string content, CancellationToken ct = default)
    {
        var bytes = Utf8NoBom.GetBytes(content);
        await WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
    }

    public static async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> content, CancellationToken ct = default)
    {
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var fs = new FileStream(
                tmp,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await fs.WriteAsync(content, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort cleanup */ }
            throw;
        }
    }
}
