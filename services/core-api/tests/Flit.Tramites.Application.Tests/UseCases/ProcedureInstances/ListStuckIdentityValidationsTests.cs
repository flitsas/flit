using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Observabilidad de eventos de identidad atascados (dead-letter, HU #10349 fase 2): el handler mapea las
/// filas del repositorio a DTOs sin PII y reporta el total + el tope de reintentos.
/// </summary>
public sealed class ListStuckIdentityValidationsTests
{
    private readonly IIdentityValidationOutboxRepository _repo =
        Substitute.For<IIdentityValidationOutboxRepository>();

    private static StuckIdentityValidationRow StuckRow() => new(
        Id: Guid.NewGuid(),
        ValidationId: Guid.NewGuid(),
        EventType: IdentityValidationEventTypes.Completed,
        Attempts: IdentityValidationOutbox.MaxDeliveryAttempts,
        OccurredAt: DateTimeOffset.UtcNow.AddMinutes(-30),
        CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
        Nombre: "ABRAHAM ENRIQUE CAÑON",
        TipoDoc: "CC",
        Documento: "1020304050",
        Kind: StuckIdentityValidationKinds.Encadenamiento);

    [Fact]
    public async Task Mapea_filas_con_persona_total_y_tope_de_reintentos()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListStuckIdentityValidationsHandler(_repo);
        var row = StuckRow();
        _repo.ListStuckAsync(tenant, ListStuckIdentityValidationsHandler.MaxRows, ct)
            .Returns(new List<StuckIdentityValidationRow> { row });

        var result = await handler.HandleAsync(tenant, ct);

        result.Total.Should().Be(1);
        result.MaxDeliveryAttempts.Should().Be(IdentityValidationOutbox.MaxDeliveryAttempts);
        result.Stuck.Should().ContainSingle();
        var dto = result.Stuck[0];
        dto.Id.Should().Be(row.Id);
        dto.ValidationId.Should().Be(row.ValidationId);
        dto.EventType.Should().Be(IdentityValidationEventTypes.Completed);
        dto.Attempts.Should().Be(IdentityValidationOutbox.MaxDeliveryAttempts);
        dto.OccurredAt.Should().Be(row.OccurredAt);
        // El DTO trae la persona (la UI muestra nombre + últimos 4 del documento).
        dto.Nombre.Should().Be("ABRAHAM ENRIQUE CAÑON");
        dto.TipoDoc.Should().Be("CC");
        dto.Documento.Should().Be("1020304050");
        dto.Kind.Should().Be(StuckIdentityValidationKinds.Encadenamiento);
    }

    [Fact]
    public async Task Vacio_cuando_no_hay_eventos_atascados()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new ListStuckIdentityValidationsHandler(_repo);
        _repo.ListStuckAsync(tenant, Arg.Any<int>(), ct).Returns(new List<StuckIdentityValidationRow>());

        var result = await handler.HandleAsync(tenant, ct);

        result.Total.Should().Be(0);
        result.Stuck.Should().BeEmpty();
        result.MaxDeliveryAttempts.Should().Be(IdentityValidationOutbox.MaxDeliveryAttempts);
    }

    [Fact]
    public async Task Reencolar_devuelve_null_cuando_existe_el_evento_atascado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var id = Guid.NewGuid();
        var handler = new RequeueStuckIdentityValidationHandler(_repo);
        _repo.RequeueStuckAsync(tenant, id, ct).Returns(true);

        var error = await handler.HandleAsync(tenant, id, ct);

        error.Should().BeNull();
        await _repo.Received(1).RequeueStuckAsync(tenant, id, ct);
    }

    [Fact]
    public async Task Reencolar_devuelve_not_found_cuando_no_hay_evento_atascado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var id = Guid.NewGuid();
        var handler = new RequeueStuckIdentityValidationHandler(_repo);
        _repo.RequeueStuckAsync(tenant, id, ct).Returns(false);

        var error = await handler.HandleAsync(tenant, id, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Reencolar_todos_devuelve_la_cantidad_reencolada()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var handler = new RequeueAllStuckIdentityValidationsHandler(_repo);
        _repo.RequeueAllStuckAsync(tenant, ct).Returns(3);

        var count = await handler.HandleAsync(tenant, ct);

        count.Should().Be(3);
        await _repo.Received(1).RequeueAllStuckAsync(tenant, ct);
    }
}
