using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Mejora sin HU — el portal de persona natural puede subir el MISMO archivo para dos documentos distintos
/// (<c>tarjeta_propiedad</c> y <c>soporte_cambio_color</c>). Con <c>dedupPorTipo: true</c> la clave de idempotencia de
/// <see cref="RegisterIntegrationAttachmentHandler.HandleBatchAsync(Guid, Guid, IReadOnlyList{RegisterAttachmentInput}, string, Guid?, bool, CancellationToken)"/>
/// es (tipo + sha256); apagado (ICT y demás) conserva la dedup histórica por sha256.
/// </summary>
public sealed class RegisterIntegrationAttachmentDedupPorTipoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly Guid _id = Guid.NewGuid();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly string _sha = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RegisterIntegrationAttachmentDedupPorTipoTests()
    {
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Traspaso,
            Id = _id,
            TenantId = _tenant,
            ProcedureTypeId = ProcedureTypeFixture.Traspaso.Id,
            ReferenceNumber = "TRM-2026-PN-DEDUP",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithAttachmentsAsync(_id, _tenant, Arg.Any<CancellationToken>()).Returns(instance);
        Instance = instance;
    }

    private ProcedureInstance Instance { get; }

    private RegisterAttachmentInput Entrada(string tipo, string? sha = null) =>
        new(tipo, $"{tipo}.pdf", "application/pdf", 1024, sha ?? _sha, $"portal/{tipo}");

    private Task<(IReadOnlyList<AttachmentDto> Registered, IReadOnlyList<string> Warnings)> Lote(bool porTipo, params RegisterAttachmentInput[] e) =>
        new RegisterIntegrationAttachmentHandler(_repo).HandleBatchAsync(_id, _tenant, e, "portal", null, porTipo, Ct);

    [Fact]
    public async Task ConFlag_MismoArchivoEnDosTipos_RegistraDosAdjuntos()
    {
        var (registrados, avisos) = await Lote(true, Entrada("tarjeta_propiedad"), Entrada("soporte_cambio_color"));

        avisos.Should().BeEmpty();
        registrados.Should().HaveCount(2);
        Instance.Attachments.Select(a => a.Tipo).Should().BeEquivalentTo("tarjeta_propiedad", "soporte_cambio_color");
    }

    [Fact]
    public async Task ConFlag_ReintentoDelLote_NoDuplica()
    {
        await Lote(true, Entrada("tarjeta_propiedad"), Entrada("soporte_cambio_color"));

        var (registrados, _) = await Lote(true, Entrada("tarjeta_propiedad"), Entrada("soporte_cambio_color"), Entrada("tarjeta_propiedad"));

        registrados.Should().BeEmpty();
        Instance.Attachments.Should().HaveCount(2);
    }

    [Fact]
    public async Task SinFlag_MismoArchivoEnDosTipos_ConservaSoloElPrimero()
    {
        var (registrados, _) = await Lote(false, Entrada("tarjeta_propiedad"), Entrada("soporte_cambio_color"));

        registrados.Should().ContainSingle().Which.Tipo.Should().Be("tarjeta_propiedad");
        Instance.Attachments.Should().ContainSingle();
    }
}
