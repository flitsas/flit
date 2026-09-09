using Flit.Admin.Application.GeneracionDocumental.Download;
using Flit.Admin.Domain.GeneracionDocumental;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12204 — redescarga presignada y auditoría (CF-03 / CF-19 / CF-20).
///
/// Uso de ejemplo:
/// <code>
/// var result = await new GetStandaloneDocumentDownloadHandler(repo, storage, reloj)
///     .HandleAsync(tenantId, documentoId, ct);
/// // result.Outcome == StandaloneDocumentDownloadOutcome.Ok; result.Link!.Url
/// </code>
/// </summary>
public sealed class GetStandaloneDocumentDownloadHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtroTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Autor = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DocumentoId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private readonly FakeStandaloneDocumentRepository _repo = new();
    private readonly FakeStandaloneDocumentStorage _storage = new();
    private readonly FakeStandaloneRuesCompanyLookup _lookup = new();
    private readonly FakeStandaloneRuesCertificateRenderer _renderer = new();
    private readonly RelojFijo _reloj = new(new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero));

    private GetStandaloneDocumentDownloadHandler Descargar => new(_repo, _storage, _reloj);

    // ── CF-19: dos descargas dejan el contador en 2 ─────────────────────────────────

    [Fact]
    public async Task DosDescargas_DejanElContadorEnDosYLaFechaDeLaUltima()
    {
        Sembrar(StandaloneDocumentStatus.Generated);

        var primera = await Descargar.HandleAsync(Tenant, DocumentoId, TestContext.Current.CancellationToken);
        _reloj.Advance(TimeSpan.FromMinutes(30));
        var segunda = await Descargar.HandleAsync(Tenant, DocumentoId, TestContext.Current.CancellationToken);

        primera.Outcome.Should().Be(StandaloneDocumentDownloadOutcome.Ok);
        segunda.Outcome.Should().Be(StandaloneDocumentDownloadOutcome.Ok);
        primera.Link!.Url.Should().NotBeNullOrWhiteSpace();
        primera.Link.ExpiresAt.Should().BeAfter(default);
        segunda.Link!.ExpiresAt.Should().BeAfter(default);

        var fila = _repo.Rows.Single(r => r.Id == DocumentoId);
        fila.DownloadCount.Should().Be(2, "cada descarga suma una, no solo la primera");
        fila.DownloadedAt.Should().Be(_reloj.GetUtcNow(), "downloaded_at es la fecha de la ÚLTIMA descarga");
    }

    /// <summary>
    /// La atomicidad la impone el repositorio con un único UPDATE; lo que se verifica aquí es que el
    /// handler NO participa: nunca lee el contador para escribirlo después. Si lo hiciera, habría
    /// que ver más de una lectura por descarga o una escritura con el valor calculado en memoria.
    /// </summary>
    [Fact]
    public async Task ElHandlerNoLeeElContadorParaIncrementarlo()
    {
        Sembrar(StandaloneDocumentStatus.Generated);

        await Descargar.HandleAsync(Tenant, DocumentoId, TestContext.Current.CancellationToken);

        _repo.Downloads.Should().ContainSingle()
            .Which.At.Should().Be(_reloj.GetUtcNow());
    }

    // ── CF-03: redescarga sin regeneración ──────────────────────────────────────────

    [Fact]
    public async Task Redescarga_NoInvocaGeneradorNiProveedorYNoCambiaElHash()
    {
        Sembrar(StandaloneDocumentStatus.Generated);
        var hashAntes = _repo.Rows.Single(r => r.Id == DocumentoId).StorageSha256;

        await Descargar.HandleAsync(Tenant, DocumentoId, TestContext.Current.CancellationToken);
        await Descargar.HandleAsync(Tenant, DocumentoId, TestContext.Current.CancellationToken);

        _lookup.Calls.Should().Be(0, "redescargar no vuelve a consultar al proveedor externo");
        _renderer.Calls.Should().Be(0, "redescargar no vuelve a renderizar el PDF");
        _storage.Saved.Should().BeEmpty("no se escribe ningún archivo nuevo en storage");
        _repo.Rows.Single(r => r.Id == DocumentoId).StorageSha256.Should().Be(hashAntes);
    }

    // ── CF-20 / R3: aislamiento por autorización, no por RLS ────────────────────────

    /// <summary>
    /// El documento existe, pero es de otra compañía. El handler consulta por (tenant, id) y no lo
    /// encuentra: 404. No se apoya en ninguna política RLS —que en este repo no aísla nada—, sino en
    /// el filtro del repositorio.
    /// </summary>
    [Fact]
    public async Task DocumentoDeOtroTenant_Responde404SinUrlNiAuditoria()
    {
        Sembrar(StandaloneDocumentStatus.Generated, tenant: OtroTenant);

        var result = await Descargar.HandleAsync(Tenant, DocumentoId, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(StandaloneDocumentDownloadOutcome.NotFound);
        result.Link.Should().BeNull("no se entrega presigned URL de un documento ajeno");
        result.Status.Should().BeNull("ni siquiera se revela el estado: eso confirmaría que existe");
        _storage.Presigned.Should().BeEmpty();
        _repo.Downloads.Should().BeEmpty("una descarga que no ocurrió no se audita");
        _repo.Rows.Single(r => r.Id == DocumentoId).DownloadCount.Should().Be(0);
    }

    [Fact]
    public async Task DocumentoInexistente_Responde404()
    {
        var result = await Descargar.HandleAsync(
            Tenant, Guid.Parse("aaaaaaaa-0000-0000-0000-00000000ffff"), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(StandaloneDocumentDownloadOutcome.NotFound);
        _storage.Presigned.Should().BeEmpty();
    }

    // ── Documento no generado: 409 ──────────────────────────────────────────────────

    [Theory]
    [InlineData("error")]
    [InlineData("pending")]
    [InlineData("processing")]
    public async Task DocumentoNoGenerado_Responde409SinPresignedUrl(string status)
    {
        Sembrar(status);

        var result = await Descargar.HandleAsync(Tenant, DocumentoId, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(StandaloneDocumentDownloadOutcome.NotGenerated);
        result.Link.Should().BeNull();
        result.Status.Should().Be(status);
        _storage.Presigned.Should().BeEmpty("no se firma la descarga de un documento sin binario");
        _repo.Downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task FilaGeneradaSinBinarioRecuperable_Responde404YNoAudita()
    {
        Sembrar(StandaloneDocumentStatus.Generated);
        _storage.BinarioDisponible = false;

        var result = await Descargar.HandleAsync(Tenant, DocumentoId, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(StandaloneDocumentDownloadOutcome.NotFound);
        _repo.Downloads.Should().BeEmpty();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private void Sembrar(string status, Guid? tenant = null)
    {
        var generado = status == StandaloneDocumentStatus.Generated;
        _repo.Rows.Add(new StandaloneDocument
        {
            Id = DocumentoId,
            TenantId = tenant ?? Tenant,
            CreatedByUserId = Autor,
            DocumentType = StandaloneDocumentType.CertificadoRues,
            Status = status,
            ErrorCode = status == StandaloneDocumentStatus.Error ? "rues_not_found" : null,
            StoragePath = generado ? "fm://tenant/certificado.pdf" : null,
            StorageSha256 = generado ? new string('a', 64) : null,
            SizeBytes = generado ? 1024 : null,
            Filename = generado ? "certificado.pdf" : null,
            CreatedAt = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero),
        });
    }
}

/// <summary>
/// Reloj controlable: la fecha de descarga debe poder avanzar entre dos llamadas para comprobar que
/// <c>downloaded_at</c> guarda la ÚLTIMA, no la primera.
/// </summary>
internal sealed class RelojFijo : TimeProvider
{
    private DateTimeOffset _now;

    public RelojFijo(DateTimeOffset inicio) => _now = inicio;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
