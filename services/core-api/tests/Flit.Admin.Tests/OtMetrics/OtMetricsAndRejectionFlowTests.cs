using Flit.Admin.Application.OtClientProcedures;
using Flit.Admin.Application.OtClientProcedures.RejectOtClientProcedure;
using Flit.Admin.Domain.OtMetrics;
using Flit.Admin.Domain.OtProfile;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtMetrics;

/// <summary>
/// Rechazo con causales del catálogo y reportes del organismo de tránsito.
///
/// El organismo usaba FLIT sin ningún instrumento para ver su propia operación: estos son los
/// invariantes del módulo que cierra esa brecha.
/// </summary>
public sealed class OtMetricsAndRejectionFlowTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ProcedureType = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Reviewer = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ── Rechazo con causales ──────────────────────────────────────────────────────────────────

    [Fact] // Marcar varias causales es válido y esperado: un expediente puede llegar con improntas
           // borrosas, sin impronta y sin pago de impuestos a la vez.
    public async Task Rechazo_PersisteVariasCausalesYConservaLaObservacion()
    {
        var db = NewDbName();
        var instanceId = Guid.NewGuid();
        Guid causal1, causal2;

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedProcedure(seed, instanceId, TramiteEstado.Entregado);
            causal1 = SeedReason(seed, "improntas_borrosas", "Improntas están borrosas");
            causal2 = SeedReason(seed, "no_pago_impuesto", "No pago de impuesto departamental");
        }

        await using var ctx = NewContext(db);
        var result = await NewRejectHandler(ctx).HandleAsync(
            new RejectOtClientProcedureCommand
            {
                OtTenantId = OtTenant,
                ProcedureInstanceId = instanceId,
                RejectedBy = Reviewer,
                Request = new RejectOtClientProcedureRequest
                {
                    Reason = "Las improntas 3 y 4 salen movidas; vuelve a tomarlas.",
                    RejectionReasonIds = [causal1, causal2],
                },
            },
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(RejectOtClientProcedureStatus.Rejected);

        var marcas = await ctx.ProcedureInstanceRejectionReasons
            .Where(r => r.ProcedureInstanceId == instanceId)
            .ToListAsync(TestContext.Current.CancellationToken);
        marcas.Should().HaveCount(2);

        // Las causales cuelgan del EVENTO de rechazo, no del trámite: un expediente puede
        // rechazarse varias veces y hay que poder distinguir los ciclos.
        var evento = await ctx.ProcedureInstanceStatusHistories
            .SingleAsync(h => h.ToStatus == TramiteEstado.Rechazado, TestContext.Current.CancellationToken);
        marcas.Should().OnlyContain(m => m.StatusHistoryId == evento.Id);

        // El texto libre NO lo sustituyen las causales: es el contexto de quien va a subsanar.
        evento.Reason.Should().Contain("vuelve a tomarlas");
    }

    [Fact] // Descartarla en silencio dejaría al revisor creyendo que la registró.
    public async Task Rechazo_RechazaCausalDeOtraModalidad()
    {
        var db = NewDbName();
        var instanceId = Guid.NewGuid();
        Guid causalDeTraspaso;

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            // El trámite es de matrícula inicial (modalidad por defecto del seed).
            SeedProcedure(seed, instanceId, TramiteEstado.Entregado);
            causalDeTraspaso = SeedReason(seed, "soat_no_vigente", "SOAT no vigente", "TRASPASO");
        }

        await using var ctx = NewContext(db);
        var result = await NewRejectHandler(ctx).HandleAsync(
            new RejectOtClientProcedureCommand
            {
                OtTenantId = OtTenant,
                ProcedureInstanceId = instanceId,
                RejectedBy = Reviewer,
                Request = new RejectOtClientProcedureRequest
                {
                    Reason = "motivo",
                    RejectionReasonIds = [causalDeTraspaso],
                },
            },
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(RejectOtClientProcedureStatus.ValidationFailed);
    }

    [Fact] // Sin causales el rechazo sigue funcionando: la observación basta para radicar la decisión.
    public async Task Rechazo_SinCausalesSigueSiendoValido()
    {
        var db = NewDbName();
        var instanceId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedProcedure(seed, instanceId, TramiteEstado.Entregado);
        }

        await using var ctx = NewContext(db);
        var result = await NewRejectHandler(ctx).HandleAsync(
            new RejectOtClientProcedureCommand
            {
                OtTenantId = OtTenant,
                ProcedureInstanceId = instanceId,
                RejectedBy = Reviewer,
                Request = new RejectOtClientProcedureRequest { Reason = "Falta el documento del comprador." },
            },
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(RejectOtClientProcedureStatus.Rejected);
    }

    // ── Reporte de motivos ────────────────────────────────────────────────────────────────────

    [Fact] // El porcentaje es sobre RECHAZOS, no sobre marcas: un rechazo con dos causales sigue
           // siendo un rechazo, y por eso la suma puede pasar del 100 %.
    public async Task Motivos_PorcentajeSobreRechazosYPromedioDeCausales()
    {
        var db = NewDbName();
        var conDos = Guid.NewGuid();
        var conUna = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            var borrosas = SeedReason(seed, "improntas_borrosas", "Improntas están borrosas");
            var impuesto = SeedReason(seed, "no_pago_impuesto", "No pago de impuesto departamental");

            SeedProcedure(seed, conDos, TramiteEstado.Rechazado, "REF-1");
            SeedProcedure(seed, conUna, TramiteEstado.Rechazado, "REF-2");

            var evento1 = SeedRejectionEvent(seed, conDos);
            var evento2 = SeedRejectionEvent(seed, conUna);

            SeedMark(seed, conDos, evento1, borrosas);
            SeedMark(seed, conDos, evento1, impuesto);
            SeedMark(seed, conUna, evento2, borrosas);
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var result = await new OtMetricsReadRepository(ctx).GetRejectionReasonsAsync(
            OtTenant, Range(), null, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.TotalRechazos.Should().Be(2);

        // «Improntas borrosas» está en los 2 rechazos → 100 %. «Impuesto» en 1 → 50 %.
        // Suman 150 %: es correcto y por eso la UI lo rotula como «% de rechazos que la incluyen».
        result.Causales.Single(c => c.Code == "improntas_borrosas").Pct.Should().Be(100);
        result.Causales.Single(c => c.Code == "no_pago_impuesto").Pct.Should().Be(50);

        // Indicador de salud: 3 marcas / 2 rechazos.
        result.PromedioCausalesPorRechazo.Should().Be(1.5);
        result.RechazosSinCausal.Should().Be(0);
    }

    [Fact] // Los rechazos anteriores al catálogo solo tienen texto libre: hay que poder verlos
           // como el hueco que son, no como cero rechazos.
    public async Task Motivos_CuentaLosRechazosSinCausal()
    {
        var db = NewDbName();
        var instanceId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedProcedure(seed, instanceId, TramiteEstado.Rechazado);
            SeedRejectionEvent(seed, instanceId);
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var result = await new OtMetricsReadRepository(ctx).GetRejectionReasonsAsync(
            OtTenant, Range(), null, TestContext.Current.CancellationToken);

        result!.TotalRechazos.Should().Be(1);
        result.RechazosSinCausal.Should().Be(1);
        result.PromedioCausalesPorRechazo.Should().Be(0);
    }

    // ── Panel operativo ───────────────────────────────────────────────────────────────────────

    [Fact] // Solo se exponen las esperas accionables por el organismo; el resto va agrupado para
           // poder explicar por qué el desglose no suma el total.
    public async Task Panel_SeparaLoAccionableDeLaEsperaDelCliente()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "REF-1");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "REF-2",
                plateFlowStatus: PlateFlowStatus.Preasignado);
            // Placa asignada = esperando SOAT del cliente; pausado = origen ICT. Ninguno es
            // accionable por el organismo.
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "REF-3",
                plateFlowStatus: PlateFlowStatus.Asignado);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "REF-4", isPaused: true);
            // Un aprobado no está pendiente.
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Aprobado, "REF-5");
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var panel = await new OtMetricsReadRepository(ctx).GetOperationalPanelAsync(
            OtTenant, Range(), null, TestContext.Current.CancellationToken);

        panel.Should().NotBeNull();
        panel!.Movimiento.PendientesTotal.Should().Be(4);
        panel.Cola.PorRevisar.Should().Be(1);
        panel.Cola.EsperandoAsignarPlaca.Should().Be(1);
        panel.Cola.EnEsperaDelCliente.Should().Be(2);
    }

    [Fact] // El prioritario estancado es el peor indicador que puede tener un organismo.
    public async Task Panel_MarcaLosPrioritariosEstancados()
    {
        var db = NewDbName();
        var viejo = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedProcedure(seed, viejo, TramiteEstado.Entregado, "REF-1", prioritario: true);
            SeedDelivery(seed, viejo, DateTimeOffset.UtcNow.AddDays(-9));
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var panel = await new OtMetricsReadRepository(ctx).GetOperationalPanelAsync(
            OtTenant, Range(), null, TestContext.Current.CancellationToken);

        panel!.Antiguedad.MasDe7Dias.Should().Be(1);
        panel.Antiguedad.PrioritariosEstancados.Should().Be(1);
    }

    // ── Desempeño ─────────────────────────────────────────────────────────────────────────────

    [Fact] // Volumen SIEMPRE con calidad: el conteo solo premia a quien decide rápido y mal.
    public async Task Desempeno_ReportaAprobacionYRechazoPorRevisor()
    {
        var db = NewDbName();
        var aprobado = Guid.NewGuid();
        var rechazado = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            seed.Users.Add(new User
            {
                Id = Reviewer,
                Email = "revisor@ot.gov.co",
                DisplayName = "Carla Revisora",
                Status = "active",
                CreatedAt = DateTimeOffset.UtcNow,
            });

            SeedProcedure(seed, aprobado, TramiteEstado.Aprobado, "REF-1");
            SeedProcedure(seed, rechazado, TramiteEstado.Rechazado, "REF-2");
            SeedDelivery(seed, aprobado, DateTimeOffset.UtcNow.AddHours(-6));
            SeedDelivery(seed, rechazado, DateTimeOffset.UtcNow.AddHours(-6));
            SeedDecision(seed, aprobado, TramiteEstado.Aprobado);
            SeedDecision(seed, rechazado, TramiteEstado.Rechazado);
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var performance = await new OtMetricsReadRepository(ctx).GetPerformanceAsync(
            OtTenant, Range(), null, TestContext.Current.CancellationToken);

        var revisor = performance!.Revisores.Should().ContainSingle().Subject;
        revisor.DisplayName.Should().Be("Carla Revisora");
        revisor.Decididos.Should().Be(2);
        revisor.Aprobados.Should().Be(1);
        revisor.AprobacionPct.Should().Be(50);
        revisor.RechazoPct.Should().Be(50);
    }

    // ── Drill-down ────────────────────────────────────────────────────────────────────────────

    [Fact] // El drill-down usa el MISMO predicado que cuenta la tarjeta: la lista de "por revisar"
           // no puede traer un trámite que espera placa, o el número y la lista dejarían de cuadrar.
    public async Task Drilldown_PorRevisarSoloTraeLosQueEsperanRevision()
    {
        var db = NewDbName();
        var porRevisar = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedProcedure(seed, porRevisar, TramiteEstado.Entregado, "REF-1");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "REF-2",
                plateFlowStatus: PlateFlowStatus.Preasignado);
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var drilldown = await new OtMetricsReadRepository(ctx).GetDrilldownAsync(
            OtTenant, Range(), OtDrilldownBuckets.PorRevisar, limit: 100,
            cancellationToken: TestContext.Current.CancellationToken);

        drilldown.Should().NotBeNull();
        drilldown!.Total.Should().Be(1);
        drilldown.Omitidos.Should().Be(0);
        var item = drilldown.Items.Should().ContainSingle().Subject;
        item.ProcedureInstanceId.Should().Be(porRevisar);
        item.ReferenceNumber.Should().Be("REF-1");
        item.ClientTenantName.Should().Be("Flota Andina S.A.S.");
        item.Status.Should().Be(TramiteEstado.Entregado);
    }

    [Fact] // Si hay más filas que el tope, "omitidos" tiene que decirlo: una lista recortada sin
           // avisar aparentaría ser todo lo que hay.
    public async Task Drilldown_RespetaElTopeYReportaLosOmitidos()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "REF-1");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "REF-2");
            SeedProcedure(seed, Guid.NewGuid(), TramiteEstado.Entregado, "REF-3");
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var drilldown = await new OtMetricsReadRepository(ctx).GetDrilldownAsync(
            OtTenant, Range(), OtDrilldownBuckets.Pendientes, limit: 2,
            cancellationToken: TestContext.Current.CancellationToken);

        drilldown!.Total.Should().Be(3);
        drilldown.Items.Should().HaveCount(2);
        drilldown.Omitidos.Should().Be(1);
    }

    [Fact] // "Decididos hoy" sale de las transiciones del día, no del estado actual: un aprobado de
           // ayer no debe aparecer aunque siga en 'aprobado'.
    public async Task Drilldown_DecididosHoySoloTraeLasDecisionesDelDia()
    {
        var db = NewDbName();
        var hoy = Guid.NewGuid();
        var ayer = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedProcedure(seed, hoy, TramiteEstado.Aprobado, "REF-HOY");
            SeedProcedure(seed, ayer, TramiteEstado.Aprobado, "REF-AYER");
            SeedDecision(seed, hoy, TramiteEstado.Aprobado);
            seed.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
            {
                Id = Guid.NewGuid(),
                TenantId = ClientTenant,
                ProcedureInstanceId = ayer,
                FromStatus = TramiteEstado.Entregado,
                ToStatus = TramiteEstado.Aprobado,
                ChangedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ChangedBy = Reviewer,
            });
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var drilldown = await new OtMetricsReadRepository(ctx).GetDrilldownAsync(
            OtTenant, Range(), OtDrilldownBuckets.DecididosHoy, limit: 100,
            cancellationToken: TestContext.Current.CancellationToken);

        drilldown!.Total.Should().Be(1);
        drilldown.Items.Should().ContainSingle(i => i.ReferenceNumber == "REF-HOY");
    }

    [Fact] // Un bucket que no está en la lista cerrada no debe devolver la cola completa: se leería
           // como el detalle de la tarjeta que se pulsó cuando en realidad no lo es.
    public void Drilldown_RechazaUnBucketDesconocido()
    {
        OtDrilldownBuckets.IsKnown("bucket_inventado").Should().BeFalse();
        OtDrilldownBuckets.IsKnown(OtDrilldownBuckets.MasDe7Dias).Should().BeTrue();
    }

    // ── Empresas cliente (filtro del reporte) ────────────────────────────────────────────────────

    [Fact] // El filtro de empresa del reporte se puebla con las que tienen grant activo hacia el
           // organismo; sin esto, la consola no puede ofrecer el filtro por empresa.
    public async Task ClientCompanies_ListaLasEmpresasConGrantHaciaElOrganismo()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var companies = await new OtMetricsReadRepository(ctx).ListClientCompaniesAsync(
            OtTenant, cancellationToken: TestContext.Current.CancellationToken);

        companies.Should().NotBeNull();
        var company = companies!.Should().ContainSingle().Subject;
        company.TenantId.Should().Be(ClientTenant);
        company.Name.Should().Be("Flota Andina S.A.S.");
    }

    // ── Semilla ───────────────────────────────────────────────────────────────────────────────

    private static OtMetricsFilter Range() => new(
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

    private static RejectOtClientProcedureHandler NewRejectHandler(FlitDbContext ctx) =>
        new(
            new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()),
            new AllowAllQuipuxGuard(),
            new RejectionReasonRepository(ctx));

    private static void SeedScope(FlitDbContext ctx)
    {
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            TransitOfficeId = TransitOffice,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            TransitOfficeId = TransitOffice,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.Tenants.Add(new Tenant
        {
            Id = ClientTenant,
            Code = "client",
            LegalName = "Flota Andina S.A.S.",
            TaxId = "900000000",
            TenantType = "client",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Family = "MATRICULAS",
            Id = ProcedureType,
            Code = "MATRICULA_NUEVA",
            Name = "Matrícula inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedProcedure(
        FlitDbContext ctx,
        Guid id,
        string status,
        string reference = "REF-001",
        string? plateFlowStatus = null,
        bool isPaused = false,
        bool prioritario = false,
        string modalidad = "MATRICULAS")
    {
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureTypeId = ProcedureType,
            ReferenceNumber = reference,
            Status = status,
            PlateFlowStatus = plateFlowStatus,
            IsPaused = isPaused,
            Prioritario = prioritario,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = Reviewer,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        ctx.SaveChanges();
    }

    private static Guid SeedReason(
        FlitDbContext ctx,
        string code,
        string description,
        string modalidad = "MATRICULAS")
    {
        var entity = new Flit.Infrastructure.Persistence.Entities.Catalogs.RejectionReason
        {
            Id = Guid.NewGuid(),
            Code = code,
            Description = description,
            Family = modalidad,
            SortOrder = 10,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.RejectionReasons.Add(entity);
        ctx.SaveChanges();
        return entity.Id;
    }

    private static Guid SeedRejectionEvent(FlitDbContext ctx, Guid instanceId)
    {
        var id = Guid.NewGuid();
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            FromStatus = TramiteEstado.Entregado,
            ToStatus = TramiteEstado.Rechazado,
            ChangedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ChangedBy = Reviewer,
        });
        return id;
    }

    private static void SeedMark(FlitDbContext ctx, Guid instanceId, Guid eventId, Guid reasonId) =>
        ctx.ProcedureInstanceRejectionReasons.Add(new ProcedureInstanceRejectionReason
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            StatusHistoryId = eventId,
            RejectionReasonId = reasonId,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });

    private static void SeedDelivery(FlitDbContext ctx, Guid instanceId, DateTimeOffset at) =>
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            FromStatus = TramiteEstado.Preparado,
            ToStatus = TramiteEstado.Entregado,
            ChangedAt = at,
        });

    private static void SeedDecision(FlitDbContext ctx, Guid instanceId, string toStatus) =>
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = instanceId,
            FromStatus = TramiteEstado.Entregado,
            ToStatus = toStatus,
            ChangedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ChangedBy = Reviewer,
        });

    private sealed class AllowAllQuipuxGuard : IQuipuxReadOnlyGuard
    {
        public Task<QuipuxReadOnlyResult> ValidateActionAsync(
            Guid tenantId,
            string action,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(QuipuxReadOnlyResult.Allowed());
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
