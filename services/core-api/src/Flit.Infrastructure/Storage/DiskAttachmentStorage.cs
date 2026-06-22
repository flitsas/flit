using System.Security.Cryptography;
using System.Text;
using Flit.Tramites.Application.Storage;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Almacenamiento de adjuntos en disco local. Raíz configurable
/// (<see cref="AttachmentStorageOptions.Root"/>); cada instancia tiene su carpeta
/// <c>{root}/{instanceId}/</c>. Nombre final saneado <c>{tipo}_{timestamp}_{safeName}</c>.
/// Protección contra path traversal: saneo de componentes + verificación de que la ruta
/// resuelta queda contenida en la raíz (tanto al guardar como al borrar).
/// <para>Deuda prod: no es multi-instancia; migrar a object storage (S3/Blob).</para>
/// </summary>
internal sealed class DiskAttachmentStorage : IAttachmentStorage
{
    private readonly string _root;

    public DiskAttachmentStorage(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public DiskAttachmentStorage(IOptions<AttachmentStorageOptions> options)
        : this(options.Value.Root)
    {
    }

    public async Task<StoredFile> SaveAsync(
        Guid procedureInstanceId,
        string tipo,
        string originalFilename,
        Stream content,
        CancellationToken ct = default)
    {
        var instanceDir = ResolveInstanceDir(procedureInstanceId);
        Directory.CreateDirectory(instanceDir);

        var safeTipo = Sanitize(tipo, fallback: "otro");
        var safeName = Sanitize(Path.GetFileName(originalFilename ?? string.Empty), fallback: "file");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var finalName = $"{safeTipo}_{timestamp}_{safeName}";

        var fullPath = Path.GetFullPath(Path.Combine(instanceDir, finalName));
        EnsureWithinRoot(fullPath);

        long size;
        string sha256;
        using (var sha = SHA256.Create())
        await using (var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            int read;
            size = 0;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                size += read;
            }

            sha.TransformFinalBlock([], 0, 0);
            sha256 = Convert.ToHexStringLower(sha.Hash!);
        }

        // Ruta relativa a la raíz: portable, segura de almacenar en BD.
        var relativePath = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
        return new StoredFile(relativePath, sha256, size);
    }

    public void Delete(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return;

        var fullPath = Path.GetFullPath(Path.Combine(_root, storagePath));
        EnsureWithinRoot(fullPath);

        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    public Stream? OpenRead(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return null;

        var fullPath = Path.GetFullPath(Path.Combine(_root, storagePath));
        EnsureWithinRoot(fullPath);

        return File.Exists(fullPath)
            ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
    }

    private string ResolveInstanceDir(Guid instanceId)
    {
        // El Guid en formato "D" no contiene separadores de ruta; aun así forzamos saneo.
        var safe = Sanitize(instanceId.ToString("D"), fallback: instanceId.ToString("N"));
        var dir = Path.GetFullPath(Path.Combine(_root, safe));
        EnsureWithinRoot(dir);
        return dir;
    }

    private void EnsureWithinRoot(string fullPath)
    {
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSep, StringComparison.Ordinal)
            && !string.Equals(fullPath, _root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ruta de almacenamiento fuera de la raíz permitida.");
        }
    }

    /// <summary>
    /// Reduce un componente de ruta a [A-Za-z0-9._-]; descarta separadores, "..",
    /// y caracteres inválidos. Trunca a 200 chars. Vacío ⇒ <paramref name="fallback"/>.
    /// </summary>
    private static string Sanitize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
                sb.Append(c);
            else
                sb.Append('_');
        }

        var cleaned = sb.ToString().Trim('.', '_', '-');
        if (cleaned.Length > 200)
            cleaned = cleaned[..200];

        return string.IsNullOrEmpty(cleaned) ? fallback : cleaned;
    }
}
