using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Abrir y cerrar la ventana de subsanación NO es una transición de estado.
///
/// <para>Antes, activar la subsanación escribía una fila de historial <c>rechazado → rechazado</c>
/// cuyo único fin era transportar el snapshot de campos usado como baseline del diff de
/// re-radicación; cancelarla escribía otra igual como nota de auditoría. El timeline del trámite
/// pinta todo el historial, así que el operador veía un segundo "Rechazado" por cada clic. El
/// baseline vive ahora en la instancia y no se escribe historial en ninguno de los dos caminos.</para>
/// </summary>
public sealed class SubsanacionSinTransicionTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProcedureInstance Rechazado(Guid id, Guid tenantId, bool subsanacionActiva = false) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000042",
            Status = TramiteEstado.Rechazado,
            ModalidadEntrada = "traspaso",
            TipologiaCodigo = "traspaso_standard",
            SubsanacionActiva = subsanacionActiva,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private void ArrangeCommit(ProcedureInstance instance)
    {
        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
            .Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task Activar_noEscribeNingunaFilaDeHistorial()
    {
        var instance = Rechazado(Guid.NewGuid(), Guid.NewGuid());
        ArrangeCommit(instance);
        var sut = new StartSubsanacionHandler(_repo);

        var (result, error) = await sut.HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), ct: Ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        instance.StatusHistory.Should().BeEmpty("abrir la ventana de subsanación no es una transición");
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceStatusHistory>());
    }

    [Fact]
    public async Task Activar_dejaElTramiteEnRechazadoYEnciendeElFlag()
    {
        var instance = Rechazado(Guid.NewGuid(), Guid.NewGuid());
        ArrangeCommit(instance);
        var sut = new StartSubsanacionHandler(_repo);

        await sut.HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), ct: Ct);

        instance.Status.Should().Be(TramiteEstado.Rechazado);
        instance.SubsanacionActiva.Should().BeTrue();
        instance.SubsanacionCount.Should().Be(1);
    }

    [Fact]
    public async Task Activar_guardaElBaselineDeCamposEnLaInstancia()
    {
        var instance = Rechazado(Guid.NewGuid(), Guid.NewGuid());
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "plate",
            ValueText = "ABC123",
        });
        ArrangeCommit(instance);
        var sut = new StartSubsanacionHandler(_repo);

        await sut.HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), ct: Ct);

        instance.SubsanacionBaseline.Should().NotBeNullOrWhiteSpace();
        // El baseline debe ser legible por el mismo value object que consume el diff de gates.
        SubsanacionObservation.FromJson(instance.SubsanacionBaseline)!
            .FieldSnapshot.Should().NotBeNull();
    }

    [Fact]
    public async Task Activar_dosVeces_esIdempotenteYNoDuplicaElContador()
    {
        var instance = Rechazado(Guid.NewGuid(), Guid.NewGuid(), subsanacionActiva: true);
        instance.SubsanacionCount = 1;
        ArrangeCommit(instance);
        var sut = new StartSubsanacionHandler(_repo);

        await sut.HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), ct: Ct);

        instance.SubsanacionCount.Should().Be(1);
        instance.StatusHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task Activar_sobreUnTramiteQueNoEstaRechazado_noHaceNada()
    {
        var instance = Rechazado(Guid.NewGuid(), Guid.NewGuid());
        instance.Status = TramiteEstado.Entregado;
        ArrangeCommit(instance);
        var sut = new StartSubsanacionHandler(_repo);

        var (result, error) = await sut.HandleAsync(instance.Id, instance.TenantId, null, ct: Ct);

        result.Should().BeNull();
        error.Should().Be("not_rechazado");
        instance.SubsanacionActiva.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelar_noEscribeHistorialYSueltaElBaseline()
    {
        var instance = Rechazado(Guid.NewGuid(), Guid.NewGuid(), subsanacionActiva: true);
        instance.SubsanacionBaseline = """{"fieldSnapshot":{"plate":"ABC123"}}""";
        ArrangeCommit(instance);
        var sut = new CancelSubsanacionHandler(_repo);

        var (result, error) = await sut.HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), Ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        instance.SubsanacionActiva.Should().BeFalse();
        instance.SubsanacionBaseline.Should().BeNull();
        instance.Status.Should().Be(TramiteEstado.Rechazado);
        instance.StatusHistory.Should().BeEmpty();
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceStatusHistory>());
    }
}
