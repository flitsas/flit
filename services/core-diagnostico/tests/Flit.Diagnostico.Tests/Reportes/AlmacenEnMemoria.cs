using System.Collections.Concurrent;
using System.Security.Cryptography;
using Flit.Diagnostico.Api.Infraestructura.Archivos;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// Almacén de archivos en memoria para las pruebas del registro de evidencias (HU #13846): hace las veces del file-manager
/// (las variables <c>FILE_MANAGER_*</c> son dependencia de infra). Cuenta los guardados y permite «perder» un archivo.
/// </summary>
/// <example>
/// <code>
/// var almacen = new AlmacenEnMemoria();
/// var guardado = await almacen.GuardarAsync(empresa, "PDF", "a.pdf", "application/pdf", bytes, ct);
/// (await almacen.LeerAsync(guardado.Referencia, ct)).Should().Equal(bytes);
/// </code>
/// </example>
internal sealed class AlmacenEnMemoria : IAlmacenArchivos
{
    private readonly ConcurrentDictionary<string, byte[]> _archivos = new();
    private int _guardados;

    public int Guardados => _guardados;

    public Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct)
    {
        Interlocked.Increment(ref _guardados);
        var referencia = $"mem:{empresaId:N}/{Guid.NewGuid():N}";
        _archivos[referencia] = [.. contenido];
        return Task.FromResult(new ArchivoGuardado(referencia, Convert.ToHexStringLower(SHA256.HashData(contenido)), contenido.LongLength));
    }

    public Task<byte[]?> LeerAsync(string referencia, CancellationToken ct) =>
        Task.FromResult(_archivos.TryGetValue(referencia, out var bytes) ? [.. bytes] : (byte[]?)null);

    /// <summary>Simula que el file-manager ya no tiene el archivo.</summary>
    public void Perder(string referencia) => _archivos.TryRemove(referencia, out _);
}
