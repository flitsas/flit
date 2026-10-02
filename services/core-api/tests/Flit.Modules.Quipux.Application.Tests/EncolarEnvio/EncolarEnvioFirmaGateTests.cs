using Flit.Modules.Quipux.Application.UseCases.EncolarEnvio;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.EncolarEnvio;

/// <summary>
/// Bug #13194 (P4, D2) — «no se permite enviar al OT trámites sin firmar» en el canal Quipux. Encolar ES
/// el envío: la transición preparado→entregado de Quipux ocurre cuando la secretaría ya registró el
/// documento y ahí no se puede bloquear sin dejar un huérfano, así que el gate corre al encolar.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var handler = new EncolarEnvioQuipuxHandler(instances, types, submissions, consolidado, organismos,
///     tenants, audit, NullLogger&lt;EncolarEnvioQuipuxHandler&gt;.Instance, firmaGate);
/// var r = await handler.HandleAsync(new EncolarEnvioQuipuxCommand { ProcedureInstanceId = id, TenantId = t });
/// // r.Motivo == "firma_pendiente" si alguna parte no tiene identidad aprobada y vigente.
/// </code>
/// </remarks>
public sealed class EncolarEnvioFirmaGateTests
{
    private readonly IProcedureInstanceRepository _instances = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _types = Substitute.For<IProcedureTypeRepository>();
    private readonly IQuipuxSubmissionRepository _submissions = Substitute.For<IQuipuxSubmissionRepository>();
    private readonly IQuipuxConsolidadoMaestroPort _consolidado = Substitute.For<IQuipuxConsolidadoMaestroPort>();
    private readonly IQuipuxOrganismoPort _organismos = Substitute.For<IQuipuxOrganismoPort>();
    private readonly IQuipuxTenantPort _tenants = Substitute.For<IQuipuxTenantPort>();
    private readonly IQuipuxAuditLog _audit = Substitute.For<IQuipuxAuditLog>();
    private readonly ITramiteFirmaGate _firmaGate = Substitute.For<ITramiteFirmaGate>();

    private EncolarEnvioQuipuxHandler Handler(ITramiteFirmaGate? gate) =>
        new(_instances, _types, _submissions, _consolidado, _organismos, _tenants, _audit,
            NullLogger<EncolarEnvioQuipuxHandler>.Instance, gate);

    private ProcedureInstance Preparado()
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013194",
            Status = TramiteEstado.Preparado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _instances.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
            .Returns(instance);
        return instance;
    }

    [Fact]
    public async Task SinFirma_NoSeEncola_ConMotivoFirmaPendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Preparado();
        _firmaGate.PartesSinFirmaAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
            .Returns(["comprador"]);

        var result = await Handler(_firmaGate).HandleAsync(
            new EncolarEnvioQuipuxCommand { ProcedureInstanceId = instance.Id, TenantId = instance.TenantId }, ct);

        result.Status.Should().Be(EncolarEnvioQuipuxStatus.NoElegible);
        result.Motivo.Should().Be(EncolarEnvioQuipuxMotivos.FirmaPendiente);
        result.TieneSubmission.Should().BeFalse();
        // Corta antes de cualquier efecto: ni parametrización, ni consolidado, ni submission.
        await _types.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _consolidado.DidNotReceive().AsegurarAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Firmado_PasaElGate_YSigueConLaElegibilidadNormal()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Preparado();
        _firmaGate.PartesSinFirmaAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await Handler(_firmaGate).HandleAsync(
            new EncolarEnvioQuipuxCommand { ProcedureInstanceId = instance.Id, TenantId = instance.TenantId }, ct);

        // Sin bloque quipux en el tipo: el siguiente gate natural, no el de firma.
        result.Motivo.Should().Be(EncolarEnvioQuipuxMotivos.TipoSinParametrizacionQuipux);
        await _firmaGate.Received(1).PartesSinFirmaAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EstadoNoPreparado_NoConsultaElGateDeFirma()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Preparado();
        instance.Status = TramiteEstado.Borrador;

        var result = await Handler(_firmaGate).HandleAsync(
            new EncolarEnvioQuipuxCommand { ProcedureInstanceId = instance.Id, TenantId = instance.TenantId }, ct);

        result.Motivo.Should().Be(EncolarEnvioQuipuxMotivos.EstadoNoPreparado);
        await _firmaGate.DidNotReceive().PartesSinFirmaAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
