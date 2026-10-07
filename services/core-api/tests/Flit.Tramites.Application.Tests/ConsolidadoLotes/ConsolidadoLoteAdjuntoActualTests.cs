using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13378 AC4 — <see cref="ConsolidadoLoteAdjuntoActual"/>: el adjunto ACTUAL del tipo del lote con la precedencia
/// del entregador (maestro radicado primero), con la compañía del ítem y sin generar nada.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await new ConsolidadoLoteAdjuntoActual(repo, radicado).StoragePathActualAsync(id, tenant, "consolidado", ct);</c>.</remarks>
public sealed class ConsolidadoLoteAdjuntoActualTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IMaestroRadicadoLookup _radicado = Substitute.For<IMaestroRadicadoLookup>();

    private ProcedureInstance Instancia()
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013378",
            Status = TramiteEstado.Aprobado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private static ProcedureInstanceAttachment Adjunto(ProcedureInstance instance, string tipo, string ruta)
    {
        var a = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = tipo + ".pdf",
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = "sha",
            StoragePath = ruta,
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow,
        };
        instance.Attachments.Add(a);
        return a;
    }

    [Fact]
    public async Task AC4_Consolidado_DevuelveElAdjuntoActualDelTipo()
    {
        var instance = Instancia();
        Adjunto(instance, LoteTipoDocumento.ConsolidadoMaestro, "fm/maestro");
        Adjunto(instance, LoteTipoDocumento.Consolidado, "fm/consolidado-actual");

        var ruta = await new ConsolidadoLoteAdjuntoActual(_repo, _radicado)
            .StoragePathActualAsync(instance.Id, instance.TenantId, LoteTipoDocumento.Consolidado, Ct);

        ruta.Should().Be("fm/consolidado-actual");
    }

    [Fact]
    public async Task AC4_Maestro_ElRadicadoPrimero()
    {
        var instance = Instancia();
        var radicado = Adjunto(instance, LoteTipoDocumento.ConsolidadoMaestro, "fm/maestro-radicado");
        Adjunto(instance, LoteTipoDocumento.ConsolidadoMaestro, "fm/maestro-otro");
        _radicado.AttachmentRadicadoAsync(instance.TenantId, instance.Id, Arg.Any<CancellationToken>()).Returns(radicado.Id);

        var ruta = await new ConsolidadoLoteAdjuntoActual(_repo, _radicado)
            .StoragePathActualAsync(instance.Id, instance.TenantId, LoteTipoDocumento.ConsolidadoMaestro, Ct);

        ruta.Should().Be("fm/maestro-radicado");
    }

    [Fact]
    public async Task AC4_Negativo_SinAdjuntoDelTipo_OTramiteBorrado_Null()
    {
        var sinAdjunto = Instancia();
        var borrado = Instancia();
        Adjunto(borrado, LoteTipoDocumento.Consolidado, "fm/x");
        borrado.DeletedAt = DateTimeOffset.UtcNow;
        var sut = new ConsolidadoLoteAdjuntoActual(_repo, _radicado);

        (await sut.StoragePathActualAsync(sinAdjunto.Id, sinAdjunto.TenantId, LoteTipoDocumento.Consolidado, Ct)).Should().BeNull();
        (await sut.StoragePathActualAsync(borrado.Id, borrado.TenantId, LoteTipoDocumento.Consolidado, Ct)).Should().BeNull();
        (await sut.StoragePathActualAsync(Guid.NewGuid(), Guid.NewGuid(), LoteTipoDocumento.Consolidado, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Contrato_TipoFueraDelLote_Lanza() =>
        await new ConsolidadoLoteAdjuntoActual(_repo).Invoking(s =>
                s.StoragePathActualAsync(Guid.NewGuid(), Guid.NewGuid(), "fur", Ct))
            .Should().ThrowAsync<ArgumentException>();
}
