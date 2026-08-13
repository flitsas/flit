using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>HU #11270 — listado agrupado por persona (CF-05).</summary>
public sealed class ListTenantBiometricPersonsHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IIdentityValidationOutboxRepository _outbox =
        Substitute.For<IIdentityValidationOutboxRepository>();

    private ListTenantBiometricPersonsHandler Handler() => new(_repo, _outbox);

    public ListTenantBiometricPersonsHandlerTests()
    {
        _outbox.ListStuckAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        // Los KPIs de esta grilla cuentan PERSONAS (una fila por documento), no validaciones: 7
        // validaciones de la misma persona son 1 persona aprobada.
        _repo.CountBiometricPersonsByEstadoAsync(
                Arg.Any<Guid>(), Arg.Any<BiometricPersonGroupFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { [BiometricEstados.Aprobado] = 1 });
    }

    [Fact]
    public async Task Agrupa_SieteValidaciones_MismaPersona_UnaFilaConCount7()
    {
        var tenantId = Guid.NewGuid();
        var latestId = Guid.NewGuid();
        _repo.ListBiometricValidationsGroupedByPersonAsync(
                tenantId, 0, 20, Arg.Any<BiometricPersonGroupFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((
                (IReadOnlyList<BiometricPersonGroupProjection>)
                [
                    new BiometricPersonGroupProjection
                    {
                        LatestValidationId = latestId,
                        DocumentType = "CC",
                        DocumentNumber = "123",
                        DocumentTypeNorm = "CC",
                        DocumentNumberNorm = "123",
                        Name = "Ana",
                        Status = BiometricEstados.Aprobado,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                        Email = "a@b.co",
                        Provider = BiometricProviders.Mock,
                        ValidationCount = 7,
                    },
                ],
                1));

        _repo.ListBiometricValidationsForPersonAlertScanAsync(
                tenantId, Arg.Any<IReadOnlyCollection<(string, string)>>(), 30, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var (result, error) = await Handler().HandleAsync(tenantId, ct: TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Persons.Should().HaveCount(1);
        result.Persons[0].ValidationCount.Should().Be(7);
        result.Persons[0].LatestValidationId.Should().Be(latestId);
        // 7 validaciones, 1 sola persona: el KPI cuadra con la única fila de la grilla.
        result.Stats.Total.Should().Be(1);
        result.Stats.Aprobadas.Should().Be(1);
        result.Total.Should().Be(1);
    }

    [Fact]
    public async Task PeorAlerta_PriorizaAtascadaSobreRechazada()
    {
        var tenantId = Guid.NewGuid();
        var stuckId = Guid.NewGuid();
        var rejectedId = Guid.NewGuid();

        _repo.ListBiometricValidationsGroupedByPersonAsync(
                tenantId, 0, 20, Arg.Any<BiometricPersonGroupFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((
                (IReadOnlyList<BiometricPersonGroupProjection>)
                [
                    new BiometricPersonGroupProjection
                    {
                        LatestValidationId = rejectedId,
                        DocumentType = "CC",
                        DocumentNumber = "999",
                        DocumentTypeNorm = "CC",
                        DocumentNumberNorm = "999",
                        Name = "Bob",
                        Status = BiometricEstados.Rechazado,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                        Email = "b@b.co",
                        Provider = BiometricProviders.Mock,
                        ValidationCount = 2,
                    },
                ],
                1));

        _repo.ListBiometricValidationsForPersonAlertScanAsync(
                tenantId, Arg.Any<IReadOnlyCollection<(string, string)>>(), 30, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ProcedureInstanceBiometricValidation
                {
                    Id = stuckId,
                    TenantId = tenantId,
                    DocumentType = "CC",
                    DocumentNumber = "999",
                    Name = "Bob",
                    Email = "b@b.co",
                    Status = BiometricEstados.ErrorEnvio,
                    TokenHash = "h",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                },
                new ProcedureInstanceBiometricValidation
                {
                    Id = rejectedId,
                    TenantId = tenantId,
                    DocumentType = "CC",
                    DocumentNumber = "999",
                    Name = "Bob",
                    Email = "b@b.co",
                    Status = BiometricEstados.Rechazado,
                    TokenHash = "h2",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            ]);

        _outbox.ListStuckAsync(tenantId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new StuckIdentityValidationRow(
                    Guid.NewGuid(), stuckId, "identity.validation.send", 5,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Bob", "CC", "999",
                    StuckIdentityValidationKinds.Envio),
            ]);

        var (result, error) = await Handler().HandleAsync(tenantId, ct: TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Persons[0].WorstAlertKind.Should().Be(IdentityValidationAlertKinds.Atascada);
    }

    /// <summary>
    /// HU #11505 (AC3) — el DTO de cada fila del listado agrupado incluye Intentos/MaxIntentos, con los
    /// nombres exactos del contrato (para que la serialización camelCase produzca intentos/maxIntentos).
    /// </summary>
    [Fact]
    public async Task Fila_expone_Intentos_y_MaxIntentos_de_la_validacion_mas_reciente()
    {
        var tenantId = Guid.NewGuid();
        var latestId = Guid.NewGuid();
        _repo.ListBiometricValidationsGroupedByPersonAsync(
                tenantId, 0, 20, Arg.Any<BiometricPersonGroupFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((
                (IReadOnlyList<BiometricPersonGroupProjection>)
                [
                    new BiometricPersonGroupProjection
                    {
                        LatestValidationId = latestId,
                        DocumentType = "CC",
                        DocumentNumber = "444",
                        DocumentTypeNorm = "CC",
                        DocumentNumberNorm = "444",
                        Name = "Carla",
                        Status = BiometricEstados.EnProceso,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                        Email = "c@b.co",
                        Provider = BiometricProviders.Mock,
                        ValidationCount = 3,
                        Attempts = 2,
                        MaxAttempts = 5,
                    },
                ],
                1));

        _repo.ListBiometricValidationsForPersonAlertScanAsync(
                tenantId, Arg.Any<IReadOnlyCollection<(string, string)>>(), 30, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var (result, error) = await Handler().HandleAsync(tenantId, ct: TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Persons[0].Intentos.Should().Be(2);
        result.Persons[0].MaxIntentos.Should().Be(5);
    }

    /// <summary>
    /// HU #11505 (AC1) — el valor del listado agrupado coincide con el que expone el endpoint de
    /// DETALLE para esa misma validación. El detalle (<c>BiometricaCommand.ToDto</c>) construye
    /// <c>BiometricValidationDto.Intentos/MaxIntentos</c> leyendo <c>v.Attempts</c>/<c>v.MaxAttempts</c>
    /// directamente de la entidad (ver BiometricaCommand.cs) — la misma fuente que esta proyección debe
    /// usar para que ambas vistas nunca diverjan.
    /// </summary>
    [Fact]
    public async Task Intentos_coincide_con_el_que_muestra_el_detalle_para_la_misma_validacion()
    {
        var tenantId = Guid.NewGuid();
        var validation = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DocumentType = "CC",
            DocumentNumber = "555",
            Name = "Diego",
            Email = "d@b.co",
            Status = BiometricEstados.EnProceso,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
            Attempts = 3,
            MaxAttempts = 5,
        };

        // Equivalente a lo que expondría el detalle para esta misma validación (mismos campos de origen).
        var detalleIntentos = validation.Attempts;
        var detalleMaxIntentos = validation.MaxAttempts;

        _repo.ListBiometricValidationsGroupedByPersonAsync(
                tenantId, 0, 20, Arg.Any<BiometricPersonGroupFilter?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((
                (IReadOnlyList<BiometricPersonGroupProjection>)
                [
                    new BiometricPersonGroupProjection
                    {
                        LatestValidationId = validation.Id,
                        DocumentType = validation.DocumentType,
                        DocumentNumber = validation.DocumentNumber,
                        DocumentTypeNorm = "CC",
                        DocumentNumberNorm = "555",
                        Name = validation.Name,
                        Status = validation.Status,
                        CreatedAt = validation.CreatedAt,
                        ExpiresAt = validation.ExpiresAt,
                        Email = validation.Email,
                        Provider = validation.Provider,
                        ValidationCount = 1,
                        // Misma fuente que el detalle: Attempts/MaxAttempts de la entidad.
                        Attempts = validation.Attempts,
                        MaxAttempts = validation.MaxAttempts,
                    },
                ],
                1));

        _repo.ListBiometricValidationsForPersonAlertScanAsync(
                tenantId, Arg.Any<IReadOnlyCollection<(string, string)>>(), 30, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var (result, error) = await Handler().HandleAsync(tenantId, ct: TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Persons[0].Intentos.Should().Be(detalleIntentos);
        result.Persons[0].MaxIntentos.Should().Be(detalleMaxIntentos);
    }
}
