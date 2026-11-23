using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #14634 — el evento <c>identidad_manual_rechazada</c> (emitido por <see cref="RechazarValidacionManualHandler"/>)
/// se proyecta en el tracking del trámite con el motivo YA homologado a texto (lista cerrada, HU #13299): no revela
/// el mecanismo, así que se expone a cualquiera con acceso al módulo (a diferencia de `rejectionReasonCode` en
/// <c>BiometricValidationDto</c>, que <c>ocultar-manual.ts</c> sigue disfrazando junto con provider/estado).
/// </summary>
public sealed class GetProcedureInstanceQueryBug14634Tests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _procedureInstanceId = Guid.NewGuid();
    private readonly Guid _reviewerId = Guid.NewGuid();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private ProcedureInstanceEvent Evento(string payload) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenant,
        ProcedureInstanceId = _procedureInstanceId,
        Tipo = "identidad_manual_rechazada",
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedBy = _reviewerId,
        Payload = payload,
    };

    [Fact]
    public async Task Tracking_ProyectaElMotivoHomologadoYElRolDeLaParte()
    {
        var evt = Evento("""{"party_role":"vendedor","rejection_reason_code":"rostro_no_coincide","rejection_reason_label":"El rostro no coincide con el documento"}""");

        var dtos = await GetProcedureInstanceHandler.BuildEventsAsync(_repo, [evt], _ct);

        var dto = dtos.Should().ContainSingle().Subject;
        dto.Tipo.Should().Be("identidad_manual_rechazada");
        dto.PartyRole.Should().Be("vendedor");
        dto.RejectionReasonLabel.Should().Be("El rostro no coincide con el documento");
    }

    [Fact]
    public async Task EventosIrrelevantes_NoEntranAlTracking_PeroElDeIdentidadRechazadaSi()
    {
        var irrelevante = new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant,
            ProcedureInstanceId = _procedureInstanceId,
            Tipo = "cambio_estado",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = "{}",
        };
        var relevante = Evento("""{"party_role":"comprador","rejection_reason_code":"imagen_borrosa","rejection_reason_label":"Imagen borrosa"}""");

        var dtos = await GetProcedureInstanceHandler.BuildEventsAsync(_repo, [irrelevante, relevante], _ct);

        var dto = dtos.Should().ContainSingle().Subject;
        dto.Tipo.Should().Be("identidad_manual_rechazada");
        dto.PartyRole.Should().Be("comprador");
    }
}
