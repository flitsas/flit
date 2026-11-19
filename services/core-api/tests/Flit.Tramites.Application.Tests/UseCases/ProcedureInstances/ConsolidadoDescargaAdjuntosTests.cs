using System.Text;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #14631 — los consolidados bajan sus adjuntos en paralelo (antes, uno por uno dentro de la misma
/// petición: 60-150 s en PDN con el almacenamiento lento). Se fija el contrato que el bucle secuencial ya
/// cumplía: mismo orden, <c>null</c> para el faltante y la primera excepción en orden.
/// </summary>
public sealed class ConsolidadoDescargaAdjuntosTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProcedureInstanceAttachment Adjunto(string path) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureInstanceId = Guid.NewGuid(),
        Tipo = path,
        Filename = $"{path}.pdf",
        Mimetype = "application/pdf",
        StoragePath = path,
        UploadedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Almacenamiento falso: cada ruta tarda lo indicado y cuenta la concurrencia máxima.</summary>
    private sealed class StorageLento(IReadOnlyDictionary<string, int> demoraMs, ISet<string>? faltantes = null, string? falla = null)
        : IAttachmentStorage
    {
        private int _enCurso;
        public int MaxEnCurso;

        public async Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
        {
            var ahora = Interlocked.Increment(ref _enCurso);
            InterlockedMax(ref MaxEnCurso, ahora);
            try
            {
                await Task.Delay(demoraMs.TryGetValue(storagePath, out var d) ? d : 20, ct);
                if (storagePath == falla)
                    throw new IOException($"almacenamiento caído en {storagePath}");
                return faltantes?.Contains(storagePath) == true
                    ? null
                    : new MemoryStream(Encoding.UTF8.GetBytes(storagePath));
            }
            finally
            {
                Interlocked.Decrement(ref _enCurso);
            }
        }

        private static void InterlockedMax(ref int destino, int valor)
        {
            int actual;
            while (valor > (actual = Volatile.Read(ref destino))
                   && Interlocked.CompareExchange(ref destino, valor, actual) != actual)
            {
            }
        }

        public Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => throw new NotSupportedException();

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task ConservaElOrdenAunqueTerminenDesordenados()
    {
        // El primero es el más lento: si el resultado siguiera el orden de llegada, el PDF saldría revuelto.
        var storage = new StorageLento(new Dictionary<string, int> { ["a"] = 150, ["b"] = 10, ["c"] = 60 });
        var adjuntos = new[] { Adjunto("a"), Adjunto("b"), Adjunto("c") };

        var bytes = await ConsolidadoDescargaAdjuntos.DescargarAsync(storage, adjuntos, Ct);

        bytes.Select(b => Encoding.UTF8.GetString(b!)).Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task DescargaEnParaleloSinPasarDelTope()
    {
        var adjuntos = Enumerable.Range(0, 12).Select(i => Adjunto($"doc{i}")).ToList();
        var storage = new StorageLento(adjuntos.ToDictionary(a => a.StoragePath, _ => 80));

        await ConsolidadoDescargaAdjuntos.DescargarAsync(storage, adjuntos, Ct, maxParalelo: 4);

        storage.MaxEnCurso.Should().BeGreaterThan(1, "el bucle secuencial era el problema");
        storage.MaxEnCurso.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public async Task UnFaltanteVuelveNullEnSuPosicion()
    {
        var storage = new StorageLento(new Dictionary<string, int>(), faltantes: new HashSet<string> { "b" });

        var bytes = await ConsolidadoDescargaAdjuntos.DescargarAsync(
            storage, new[] { Adjunto("a"), Adjunto("b"), Adjunto("c") }, Ct);

        bytes[0].Should().NotBeNull();
        bytes[1].Should().BeNull();
        bytes[2].Should().NotBeNull();
    }

    [Fact]
    public async Task UnFalloDelAlmacenamientoSePropaga()
    {
        var storage = new StorageLento(new Dictionary<string, int>(), falla: "b");

        var acto = () => ConsolidadoDescargaAdjuntos.DescargarAsync(
            storage, new[] { Adjunto("a"), Adjunto("b"), Adjunto("c") }, Ct);

        await acto.Should().ThrowAsync<IOException>().WithMessage("*b*");
    }

    [Fact]
    public async Task SinAdjuntos_DevuelveVacio()
    {
        var bytes = await ConsolidadoDescargaAdjuntos.DescargarAsync(
            new StorageLento(new Dictionary<string, int>()), Array.Empty<ProcedureInstanceAttachment>(), Ct);

        bytes.Should().BeEmpty();
    }
}
