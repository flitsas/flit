using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

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

        var result = await _sut.HandleAsync(Guid.NewGuid(), ct);

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
            Status = TramiteEstado.Borrador,
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
            Status = TramiteEstado.Entregado,
            ModalidadEntrada = TramiteModalidadEntradaCodes.Traspaso,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            FieldValues =
            {
                new ProcedureInstanceFieldValue { Id = Guid.NewGuid(), FieldKey = "plate", ValueText = "XYZ789" },
            },
        };

        _repo.ListWithSummaryGraphAsync(tenantId, ListProcedureInstancesHandler.MaxItems, ct)
            .Returns([matriculaParcial, traspasoSubmitted]);

        var result = await _sut.HandleAsync(tenantId, ct);

        result.Should().HaveCount(2);
        // HU #11056 — la razón social se resuelve también para un usuario de compañía (la columna
        // "Gestor" la necesita). Que la columna "Compañía" siga siendo solo del superadmin lo decide
        // el frontend por JWT, no la ausencia del dato.
        await _repo.Received(1).GetTenantNamesAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());

        var m = result.Single(x => x.ReferenceNumber == "TRM-2026-000001");
        m.Modalidad.Should().Be("matricula_inicial");
        m.Estado.Should().Be("borrador");
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
        t.Estado.Should().Be("entregado");
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
            Status = TramiteEstado.Borrador,
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

        var result = await _sut.HandleAsync(tenantId, ct);

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
            Status = TramiteEstado.Borrador,
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

        var result = await _sut.HandleAsync(tenantId, ct);

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
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = TramiteModalidadEntradaCodes.MatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repo.ListWithSummaryGraphAsync(tenantId, ListProcedureInstancesHandler.MaxItems, ct)
            .Returns([instance]);

        var result = await _sut.HandleAsync(tenantId, ct);

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
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = TramiteModalidadEntradaCodes.MatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // tenant null = TODOS (el superadmin ve todo).
        _repo.ListWithSummaryGraphAsync(null, ListProcedureInstancesHandler.MaxItems, ct)
            .Returns([Inst(tenantA, "TRM-2026-000010"), Inst(tenantB, "TRM-2026-000011")]);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [tenantA] = "Empresa A", [tenantB] = "Empresa B" });

        var result = await _sut.HandleAsync(tenantId: null, ct: ct);

        result.Should().HaveCount(2);
        result.Single(x => x.TenantId == tenantA).CompaniaNombre.Should().Be("Empresa A");
        result.Single(x => x.TenantId == tenantB).CompaniaNombre.Should().Be("Empresa B");
    }

    [Fact]
    public async Task HandleAsync_RechazadoConSubsanacion_MapeaMotivoYFlags()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var older = DateTimeOffset.UtcNow.AddDays(-2);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);

        var instance = new ProcedureInstance
        {
            Id = instanceId,
            TenantId = tenantId,
            ReferenceNumber = "TRM-2026-000020",
            Status = TramiteEstado.Rechazado,
            ModalidadEntrada = TramiteModalidadEntradaCodes.Traspaso,
            SubsanacionActiva = true,
            SubsanacionCount = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            StatusHistory =
            {
                new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ProcedureInstanceId = instanceId,
                    TenantId = tenantId,
                    FromStatus = TramiteEstado.Entregado,
                    ToStatus = TramiteEstado.Rechazado,
                    Reason = "Motivo antiguo",
                    ChangedAt = older,
                    ChangedBy = Guid.NewGuid(),
                },
                new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ProcedureInstanceId = instanceId,
                    TenantId = tenantId,
                    FromStatus = TramiteEstado.Entregado,
                    ToStatus = TramiteEstado.Rechazado,
                    Reason = "  Documentación incompleta  ",
                    ChangedAt = newer,
                    ChangedBy = Guid.NewGuid(),
                },
                // Activación de subsanación (rechazado→rechazado): NO debe reemplazar el motivo OT.
                new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ProcedureInstanceId = instanceId,
                    TenantId = tenantId,
                    FromStatus = TramiteEstado.Rechazado,
                    ToStatus = TramiteEstado.Rechazado,
                    Reason = "Subsanación iniciada por el operador",
                    ChangedAt = newer.AddMinutes(5),
                    ChangedBy = Guid.NewGuid(),
                },
                new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ProcedureInstanceId = instanceId,
                    TenantId = tenantId,
                    FromStatus = TramiteEstado.Rechazado,
                    ToStatus = TramiteEstado.Entregado,
                    Reason = "Re-radicación",
                    ChangedAt = newer.AddMinutes(30),
                    ChangedBy = Guid.NewGuid(),
                },
            },
        };

        _repo.ListWithSummaryGraphAsync(tenantId, ListProcedureInstancesHandler.MaxItems, ct)
            .Returns([instance]);

        var result = await _sut.HandleAsync(tenantId, ct);

        var m = result.Single();
        m.Estado.Should().Be(TramiteEstado.Rechazado);
        m.SubsanacionActiva.Should().BeTrue();
        m.SubsanacionCount.Should().Be(2);
        m.UltimoRechazoMotivo.Should().Be("Documentación incompleta");
    }

    // HU #11020 — el dashboard necesita los DOS actores del traspaso para identificar el trámite.
    [Fact]
    public async Task HandleAsync_Traspaso_ExponeVendedorYComprador()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ReferenceNumber = "TR-1",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "traspaso",
            CreatedAt = DateTimeOffset.UtcNow,
            Actors =
            {
                new ProcedureInstanceActor
                {
                    Id = Guid.NewGuid(),
                    ActorType = "comprador",
                    FullName = "Juan Comprador",
                    DocumentNumber = "1020304050",
                },
                new ProcedureInstanceActor
                {
                    Id = Guid.NewGuid(),
                    ActorType = "vendedor",
                    FullName = "Ana Vendedora",
                    DocumentNumber = "9998887776",
                },
            },
        };
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);

        var result = await _sut.HandleAsync(instance.TenantId, ct);

        result.Should().ContainSingle();
        result[0].VendedorNombre.Should().Be("Ana Vendedora");
        result[0].VendedorDocumento.Should().Be("9998887776");
        result[0].CompradorNombre.Should().Be("Juan Comprador");
    }

    [Fact]
    public async Task HandleAsync_MatriculaInicial_VendedorEnNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ReferenceNumber = "MI-1",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
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
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);

        var result = await _sut.HandleAsync(instance.TenantId, ct);

        result[0].VendedorNombre.Should().BeNull();
        result[0].VendedorDocumento.Should().BeNull();
    }

    // ── HU #11056 — columnas de seguimiento del listado ───────────────────────────────

    /// <summary>Traspaso base para los casos de firma/fuente/gestor; sin firmas ni adjuntos.</summary>
    private static ProcedureInstance Traspaso(Guid tenantId, string reference = "TRM-2026-000100") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ReferenceNumber = reference,
        Status = TramiteEstado.Borrador,
        ModalidadEntrada = TramiteModalidadEntradaCodes.Traspaso,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    private static ProcedureInstanceSignature Firma(string parte, string estado, int horasAtras = 1) => new()
    {
        Id = Guid.NewGuid(),
        Parte = parte,
        DocTipo = SignatureDocTipos.Compraventa,
        Estado = estado,
        SolicitadoAt = DateTimeOffset.UtcNow.AddHours(-horasAtras),
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-horasAtras),
    };

    [Fact]
    public async Task HandleAsync_ProyectaUpdatedAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var actualizado = DateTimeOffset.UtcNow.AddHours(-3);
        var instance = Traspaso(Guid.NewGuid());
        instance.UpdatedAt = actualizado;
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);

        var result = await _sut.HandleAsync(instance.TenantId, ct);

        result[0].UpdatedAt.Should().Be(actualizado);
    }

    [Fact]
    public async Task HandleAsync_SinModificar_UpdatedAtEnNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Traspaso(Guid.NewGuid());
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);

        var result = await _sut.HandleAsync(instance.TenantId, ct);

        result[0].UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ResuelveGestorEnLote()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var a = Traspaso(tenantId, "TRM-2026-000101");
        var b = Traspaso(tenantId, "TRM-2026-000102");
        a.CreatedByUserId = userId;
        b.CreatedByUserId = userId;

        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([a, b]);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [tenantId] = "Empresa A" });
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [userId] = "Ana Gestora" });

        var result = await _sut.HandleAsync(tenantId, ct);

        result.Should().OnlyContain(x => x.GestorNombre == "Ana Gestora");
        result.Should().OnlyContain(x => x.CompaniaNombre == "Empresa A");
        // Dos filas del mismo gestor ⇒ UNA sola consulta de nombres (sin N+1).
        await _repo.Received(1).GetUserDisplayNamesAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_GestorDesconocido_QuedaEnNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Traspaso(Guid.NewGuid());
        instance.CreatedByUserId = Guid.NewGuid();
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);

        var result = await _sut.HandleAsync(instance.TenantId, ct);

        result[0].GestorNombre.Should().BeNull();
    }

    [Theory]
    [InlineData(null, false, TramiteFuente.Dashboard)]
    [InlineData("ict", false, TramiteFuente.Integracion)]
    [InlineData("ICT", false, TramiteFuente.Integracion)]
    [InlineData(null, true, TramiteFuente.Migrado)]
    // La migración gana sobre el origen: un trámite importado es una foto de V1.
    [InlineData("ict", true, TramiteFuente.Migrado)]
    public async Task HandleAsync_DerivaFuente(string? origin, bool isMigrated, string esperada)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Traspaso(Guid.NewGuid());
        instance.Origin = origin;
        instance.IsMigrated = isMigrated;
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);

        var result = await _sut.HandleAsync(instance.TenantId, ct);

        result[0].Fuente.Should().Be(esperada);
    }

    // -- Columnas "Firmado": acreditacion por identidad o baul (ajuste del PO) ---------
    //
    // La columna NO habla de la firma electronica de la compraventa: dice como queda ACREDITADA cada
    // parte. Tres estados y nada mas: pendiente | firmado | rechazado.

    /// <summary>Actor con documento propio, para que la llave del baul se pueda resolver.</summary>
    private static ProcedureInstanceActor Actor(string rol, string tipoDoc = "CC", string doc = "123") => new()
    {
        Id = Guid.NewGuid(),
        ActorType = rol,
        DocumentType = tipoDoc,
        DocumentNumber = doc,
        FullName = rol + " de prueba",
    };

    private static ProcedureInstanceBiometricValidation Biometrica(string parte, string estado) => new()
    {
        Id = Guid.NewGuid(),
        PartyRole = parte,
        Status = estado,
    };

    [Fact]
    public async Task Firmado_SinNada_EsPendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Traspaso(Guid.NewGuid());
        instance.Actors.Add(Actor("vendedor", doc: "111"));
        instance.Actors.Add(Actor("comprador", doc: "222"));
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);

        var result = await _sut.HandleAsync(instance.TenantId, ct);

        result[0].FirmaVendedorEstado.Should().Be(FirmaParteEstados.Pendiente);
        result[0].FirmaCompradorEstado.Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task Firmado_IdentidadAprobadaYVigente_EsFirmado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var instance = Traspaso(tenant);
        instance.Actors.Add(Actor("vendedor", doc: "111"));
        instance.Actors.Add(Actor("comprador", doc: "222"));
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);
        // Identidad vigente de la PERSONA (por documento), como la resuelve el listado en lote.
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new HashSet<string>
            {
                BiometricRules.IdentidadKey(tenant, "CC", "111"),
                BiometricRules.IdentidadKey(tenant, "CC", "222"),
            });

        var result = await _sut.HandleAsync(tenant, ct);

        result[0].FirmaVendedorEstado.Should().Be(FirmaParteEstados.Firmado);
        result[0].FirmaCompradorEstado.Should().Be(FirmaParteEstados.Firmado);
    }

    [Fact]
    public async Task Firmado_FirmaDelBaulVigente_EsFirmado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var instance = Traspaso(tenant);
        instance.Actors.Add(Actor("vendedor", doc: "111"));
        instance.Actors.Add(Actor("comprador", doc: "222"));
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), ct)
            .Returns(new Dictionary<string, bool>
            {
                [BiometricRules.IdentidadKey(tenant, "CC", "111")] = true,
            });

        var result = await _sut.HandleAsync(tenant, ct);

        result[0].FirmaVendedorEstado.Should().Be(FirmaParteEstados.Firmado);
        // El comprador no tiene ni identidad ni baul: sigue pendiente.
        result[0].FirmaCompradorEstado.Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task Firmado_FirmaDelBaulVencida_EsRechazado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var instance = Traspaso(tenant);
        instance.Actors.Add(Actor("vendedor", doc: "111"));
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), ct)
            .Returns(new Dictionary<string, bool>
            {
                [BiometricRules.IdentidadKey(tenant, "CC", "111")] = false,
            });

        var result = await _sut.HandleAsync(tenant, ct);

        result[0].FirmaVendedorEstado.Should().Be(FirmaParteEstados.Rechazado);
    }

    [Theory]
    [InlineData(BiometricEstados.Rechazado)]
    [InlineData(BiometricEstados.Expirado)]
    public async Task Firmado_IdentidadRechazadaOExpirada_EsRechazado(string estado)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Traspaso(Guid.NewGuid());
        instance.Actors.Add(Actor("vendedor", doc: "111"));
        instance.BiometricValidations.Add(Biometrica("vendedor", estado));
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);

        var result = await _sut.HandleAsync(instance.TenantId, ct);

        result[0].FirmaVendedorEstado.Should().Be(FirmaParteEstados.Rechazado);
    }

    [Fact]
    public async Task Firmado_IdentidadAprobada_GanaSobreBaulVencido()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var instance = Traspaso(tenant);
        instance.Actors.Add(Actor("vendedor", doc: "111"));
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns(new HashSet<string> { BiometricRules.IdentidadKey(tenant, "CC", "111") });
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), ct)
            .Returns(new Dictionary<string, bool>
            {
                [BiometricRules.IdentidadKey(tenant, "CC", "111")] = false,
            });

        var result = await _sut.HandleAsync(tenant, ct);

        // Lo positivo manda: la parte esta acreditada por identidad, el baul caducado no la degrada.
        result[0].FirmaVendedorEstado.Should().Be(FirmaParteEstados.Firmado);
    }

    [Fact]
    public async Task Firmado_MatriculaInicial_ElVendedorNoAplica()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ReferenceNumber = "MI-2",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = TramiteModalidadEntradaCodes.MatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Actors.Add(Actor("comprador", doc: "222"));
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);

        var result = await _sut.HandleAsync(instance.TenantId, ct);

        // El vendedor no existe en matricula: no aplica. El comprador SI tiene estado.
        result[0].FirmaVendedorEstado.Should().BeNull();
        result[0].FirmaCompradorEstado.Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task HandleAsync_ConsolidadoGenerado_ExponeElAdjuntoDelGestor()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Traspaso(Guid.NewGuid());
        var consolidadoId = Guid.NewGuid();
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            Tipo = "consolidado_maestro",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = consolidadoId,
            Tipo = "consolidado",
            UploadedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);

        var result = await _sut.HandleAsync(instance.TenantId, ct);

        // El consolidado del gestor, NO el consolidado_maestro del organismo de tránsito.
        result[0].ConsolidadoAttachmentId.Should().Be(consolidadoId);
    }

    [Fact]
    public async Task HandleAsync_SinConsolidado_NoExponeAdjunto()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Traspaso(Guid.NewGuid());
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            Tipo = "fur",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.ListWithSummaryGraphAsync(Arg.Any<Guid?>(), Arg.Any<int>(), ct).Returns([instance]);

        var result = await _sut.HandleAsync(instance.TenantId, ct);

        result[0].ConsolidadoAttachmentId.Should().BeNull();
    }
}
