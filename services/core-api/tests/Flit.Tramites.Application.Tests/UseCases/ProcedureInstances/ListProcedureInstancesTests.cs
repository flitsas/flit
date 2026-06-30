using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class ListProcedureInstancesTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ListProcedureInstancesHandler _sut;

    public ListProcedureInstancesTests()
    {
        _sut = new ListProcedureInstancesHandler(_repo);
    }

    [Fact]
    public async Task HandleAsync_Empty_ReturnsEmptyList()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct)
            .Returns([]);

        var result = await _sut.HandleAsync(Guid.NewGuid(), isSuperAdmin: false, ct);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_MapsFieldsAndComputesPaso()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();

        // Matrícula en draft, parcialmente completa: VIN consultado + comprador presente, pero
        // sin documentos / biométrica / FUR → solo el paso 1 (consulta VIN) queda complete. La
        // frontera (primer paso incompleto) es Documentos (paso 2) → PasoActual = 2 (donde reanuda
        // el wizard, Track B).
        var matriculaParcial = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReferenceNumber = "TRM-2026-000001",
            Status = ProcedureInstanceStatus.Draft,
            ModalidadEntrada = TramiteModalidadEntradaCodes.MatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow,
            FieldValues =
            {
                new ProcedureInstanceFieldValue { Id = Guid.NewGuid(), FieldKey = "vin", ValueText = "VIN123" },
                new ProcedureInstanceFieldValue { Id = Guid.NewGuid(), FieldKey = "plate", ValueText = "ABC123" },
                new ProcedureInstanceFieldValue { Id = Guid.NewGuid(), FieldKey = "vehicle_brand", ValueText = "Toyota" },
                new ProcedureInstanceFieldValue { Id = Guid.NewGuid(), FieldKey = "vehicle_line", ValueText = "Corolla" },
            },
            Actors =
            {
                new ProcedureInstanceActor
                {
                    Id = Guid.NewGuid(),
                    ActorType = "comprador",
                    FullName = "Juan Comprador",
                    DocumentNumber = "1020304050",
                },
            },
        };

        // Traspaso ya radicado (submitted) → PasoActual reporta TotalPasos (6).
        var traspasoSubmitted = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReferenceNumber = "TRM-2026-000002",
            Status = ProcedureInstanceStatus.Submitted,
            ModalidadEntrada = TramiteModalidadEntradaCodes.Traspaso,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            FieldValues =
            {
                new ProcedureInstanceFieldValue { Id = Guid.NewGuid(), FieldKey = "plate", ValueText = "XYZ789" },
            },
        };

        _repo.ListWithSummaryGraphAsync(tenantId, ListProcedureInstancesHandler.MaxItems, ct)
            .Returns([matriculaParcial, traspasoSubmitted]);

        var result = await _sut.HandleAsync(tenantId, isSuperAdmin: false, ct);

        result.Should().HaveCount(2);
        // Usuario de compañía: no se resuelve el nombre de compañía (columna oculta en el front).
        result.Should().OnlyContain(x => x.CompaniaNombre == null);
        await _repo.DidNotReceive().GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());

        var m = result.Single(x => x.ReferenceNumber == "TRM-2026-000001");
        m.Modalidad.Should().Be("matricula_inicial");
        m.Estado.Should().Be("draft");
        m.Placa.Should().Be("ABC123");
        m.Vin.Should().Be("VIN123");
        m.VehiculoMarca.Should().Be("Toyota");
        m.VehiculoLinea.Should().Be("Corolla");
        m.CompradorNombre.Should().Be("Juan Comprador");
        m.CompradorDocumento.Should().Be("1020304050");
        m.TotalPasos.Should().Be(5);
        m.PasoActual.Should().Be(2); // frontera = Documentos (paso 1 completo, paso 2 pendiente)

        var t = result.Single(x => x.ReferenceNumber == "TRM-2026-000002");
        t.Modalidad.Should().Be("traspaso");
        t.Estado.Should().Be("submitted");
        t.Placa.Should().Be("XYZ789");
        t.Vin.Should().BeNull();
        t.CompradorNombre.Should().BeNull();
        t.TotalPasos.Should().Be(6);
        t.PasoActual.Should().Be(6); // submitted → todos los pasos resueltos
    }

    // HU #10350 (AC3) — derivación de las pistas async que alimentan los chips del listado.

    [Fact]
    public async Task HandleAsync_DraftFinalizado_IdentidadEnProceso_MapeaPistasAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var finalizadoAt = DateTimeOffset.UtcNow.AddHours(-1);

        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReferenceNumber = "TRM-2026-000010",
            Status = ProcedureInstanceStatus.Draft,
            ModalidadEntrada = TramiteModalidadEntradaCodes.MatriculaInicial,
            DraftFinalizedAt = finalizadoAt,
            CreatedAt = DateTimeOffset.UtcNow,
            BiometricValidations =
            {
                new ProcedureInstanceBiometricValidation
                {
                    Id = Guid.NewGuid(),
                    PartyRole = "comprador",
                    Status = BiometricEstados.EnProceso,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            },
        };

        _repo.ListWithSummaryGraphAsync(tenantId, ListProcedureInstancesHandler.MaxItems, ct)
            .Returns([instance]);

        var result = await _sut.HandleAsync(tenantId, isSuperAdmin: false, ct);

        var m = result.Single();
        m.DraftFinalizedAt.Should().Be(finalizadoAt);
        m.IdentityValidationStatus.Should().Be(BiometricEstados.EnProceso);
        m.SignaturePending.Should().BeFalse(); // matrícula no lleva firma de compraventa
    }

    [Fact]
    public async Task HandleAsync_DraftFinalizado_IdentidadAprobada_MapeaAprobado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();

        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReferenceNumber = "TRM-2026-000011",
            Status = ProcedureInstanceStatus.Draft,
            ModalidadEntrada = TramiteModalidadEntradaCodes.MatriculaInicial,
            DraftFinalizedAt = DateTimeOffset.UtcNow.AddHours(-2),
            CreatedAt = DateTimeOffset.UtcNow,
            BiometricValidations =
            {
                new ProcedureInstanceBiometricValidation
                {
                    Id = Guid.NewGuid(),
                    PartyRole = "comprador",
                    Status = BiometricEstados.Aprobado,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            },
        };

        _repo.ListWithSummaryGraphAsync(tenantId, ListProcedureInstancesHandler.MaxItems, ct)
            .Returns([instance]);

        var result = await _sut.HandleAsync(tenantId, isSuperAdmin: false, ct);

        result.Single().IdentityValidationStatus.Should().Be(BiometricEstados.Aprobado);
    }

    [Fact]
    public async Task HandleAsync_SinBiometrica_IdentityStatusNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();

        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReferenceNumber = "TRM-2026-000012",
            Status = ProcedureInstanceStatus.Draft,
            ModalidadEntrada = TramiteModalidadEntradaCodes.MatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repo.ListWithSummaryGraphAsync(tenantId, ListProcedureInstancesHandler.MaxItems, ct)
            .Returns([instance]);

        var result = await _sut.HandleAsync(tenantId, isSuperAdmin: false, ct);

        var m = result.Single();
        m.DraftFinalizedAt.Should().BeNull();
        m.IdentityValidationStatus.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_SuperAdmin_ListaTodoYResuelveCompania()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        ProcedureInstance Inst(Guid tenant, string reference) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ReferenceNumber = reference,
            Status = ProcedureInstanceStatus.Draft,
            ModalidadEntrada = TramiteModalidadEntradaCodes.MatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // tenant null = TODOS (el superadmin ve todo).
        _repo.ListWithSummaryGraphAsync(null, ListProcedureInstancesHandler.MaxItems, ct)
            .Returns([Inst(tenantA, "TRM-2026-000010"), Inst(tenantB, "TRM-2026-000011")]);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [tenantA] = "Empresa A", [tenantB] = "Empresa B" });

        var result = await _sut.HandleAsync(tenantId: null, isSuperAdmin: true, ct);

        result.Should().HaveCount(2);
        result.Single(x => x.TenantId == tenantA).CompaniaNombre.Should().Be("Empresa A");
        result.Single(x => x.TenantId == tenantB).CompaniaNombre.Should().Be("Empresa B");
    }
}
