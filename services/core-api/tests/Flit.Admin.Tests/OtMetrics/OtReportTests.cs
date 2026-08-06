using Flit.Admin.Application.OtMetrics;
using Flit.Admin.Domain.OtMetrics;
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
/// Informe del periodo del organismo de tránsito.
///
/// <para>El panel operativo responde «cómo vamos ahora»; el informe responde una pregunta cerrada
/// sobre un rango — qué recibí, en qué acabó y cuánto tardé. Los invariantes que se prueban aquí son
/// los que hacen que ese informe sea defendible frente a quien lo lee.</para>
/// </summary>
public sealed class OtReportTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtraEmpresa = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ProcedureTypeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Reviewer = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact] // El desglose del informe SUMA el total. Es su diferencia con el panel operativo, cuyo
           // desglose no cierra porque solo expone lo accionable: un informe que no cuadra no se
           // puede llevar a una reunión.
    public async Task Resumen_ElDesgloseCierraContraElTotal()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "REF-1", TramiteEstado.Aprobado, DiasAtras(3), decision: DiasAtras(2));
            Radicar(seed, "REF-2", TramiteEstado.Entregado, DiasAtras(3));
            Radicar(seed, "REF-3", TramiteEstado.Rechazado, DiasAtras(4), decision: DiasAtras(1),
                decisionStatus: TramiteEstado.Rechazado, subsanacionActiva: true);
            Radicar(seed, "REF-4", TramiteEstado.Rechazado, DiasAtras(4), decision: DiasAtras(1),
                decisionStatus: TramiteEstado.Rechazado);
            Radicar(seed, "REF-5", TramiteEstado.Entregado, DiasAtras(2),
                plateFlowStatus: PlateFlowStatus.Preasignado);
            Radicar(seed, "REF-6", TramiteEstado.Entregado, DiasAtras(2), isPaused: true);
            Radicar(seed, "REF-7", TramiteEstado.Anulado, DiasAtras(5), decision: null);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7));

        report!.Resumen.Total.Should().Be(7);

        var suma = report.Resumen.EnRevision
            + report.Resumen.EsperandoPlaca
            + report.Resumen.EsperandoCliente
            + report.Resumen.Aprobados
            + report.Resumen.EnSubsanacion
            + report.Resumen.Rechazados
            + report.Resumen.Anulados
            + report.Resumen.Otros;

        suma.Should().Be(report.Resumen.Total);

        report.Resumen.Aprobados.Should().Be(1);
        report.Resumen.EnRevision.Should().Be(1);
        report.Resumen.EsperandoPlaca.Should().Be(1);
        report.Resumen.EsperandoCliente.Should().Be(1);
        report.Resumen.Anulados.Should().Be(1);
    }

    [Fact] // Un rechazo con subsanación abierta vuelve al organismo; uno sin ella se quedó ahí.
           // Contarlos juntos escondería cuánto trabajo tiene de vuelta.
    public async Task Resumen_SeparaSubsanacionDeRechazoCerrado()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "REF-1", TramiteEstado.Rechazado, DiasAtras(3), decision: DiasAtras(2),
                decisionStatus: TramiteEstado.Rechazado, subsanacionActiva: true);
            Radicar(seed, "REF-2", TramiteEstado.Rechazado, DiasAtras(3), decision: DiasAtras(2),
                decisionStatus: TramiteEstado.Rechazado);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7));

        report!.Resumen.EnSubsanacion.Should().Be(1);
        report.Resumen.Rechazados.Should().Be(1);
    }

    [Fact] // El universo son los RECIBIDOS en el rango, no los decididos. Si entrara lo decidido, el
           // desglose por estado dejaría de cerrar: el trámite estaría contado sin haber sido recibido.
    public async Task Universo_ExcluyeLoRadicadoFueraDelRango()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            // Radicado hace 20 días, decidido ayer: la decisión cae dentro, la radicación no.
            Radicar(seed, "REF-VIEJO", TramiteEstado.Aprobado, DiasAtras(20), decision: DiasAtras(1));
            Radicar(seed, "REF-NUEVO", TramiteEstado.Entregado, DiasAtras(2));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7));

        report!.Resumen.Total.Should().Be(1);
        report.Filas.Should().ContainSingle().Which.ReferenceNumber.Should().Be("REF-NUEVO");
    }

    [Fact] // El reloj arranca en la ÚLTIMA radicación. Medir desde la primera metería en el mismo
           // número el tiempo que el gestor tardó en subsanar, y el organismo cargaría con una
           // demora que no es suya.
    public async Task Tiempos_SeMidenDesdeLaUltimaRadicacionNoDesdeLaPrimera()
    {
        var db = NewDbName();
        var instanceId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedInstance(seed, instanceId, "REF-1", TramiteEstado.Aprobado);
            SeedHistory(seed, instanceId, TramiteEstado.Entregado, HorasAtras(100));
            SeedHistory(seed, instanceId, TramiteEstado.Rechazado, HorasAtras(90), Reviewer);
            SeedHistory(seed, instanceId, TramiteEstado.Entregado, HorasAtras(50));
            SeedHistory(seed, instanceId, TramiteEstado.Aprobado, HorasAtras(48), Reviewer);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(10));

        var fila = report!.Filas.Should().ContainSingle().Subject;
        fila.HorasHastaDecision.Should().BeApproximately(2, 0.1);
        fila.Devoluciones.Should().Be(1);

        // Los días EN el organismo sí cuentan el ciclo completo: es lo que la empresa esperó.
        fila.DiasEnOrganismo.Should().BeApproximately(52 / 24d, 0.1);
    }

    [Fact] // Una gráfica que omite los periodos sin actividad miente dos veces: los huecos se leen
           // como continuidad y un único periodo activo se dibuja como un punto suelto sin línea.
    public async Task Serie_EmiteTodosLosPeriodosIncluidosLosVacios()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "REF-1", TramiteEstado.Entregado, DiasAtras(2));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7));

        report!.Resumen.Granularidad.Should().Be(OtReportGranularity.Dia);
        report.Resumen.Serie.Should().HaveCount(7);
        report.Resumen.Serie.Sum(p => p.Radicados).Should().Be(1);
        report.Resumen.Serie.Should().Contain(p => p.Radicados == 0);
    }

    [Theory] // Un año por día son 365 puntos ilegibles; una semana por mes es un solo punto, que no
             // es una tendencia sino un número disfrazado de gráfica.
    [InlineData(7, OtReportGranularity.Dia)]
    [InlineData(31, OtReportGranularity.Dia)]
    [InlineData(60, OtReportGranularity.Semana)]
    [InlineData(200, OtReportGranularity.Mes)]
    public async Task Serie_AdaptaLaGranularidadAlAnchoDelRango(int dias, string esperada)
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(dias));

        report!.Resumen.Granularidad.Should().Be(esperada);
        report.Resumen.Serie.Should().NotBeEmpty();
    }

    [Fact] // Los límites de cada punto son lo que convierte la gráfica en un control de navegación:
           // pinchar una columna acota el informe a ese periodo. Si el primer periodo empezara antes
           // del rango, ese clic mostraría más trámites de los que dibujaba la barra.
    public async Task Serie_RecortaLosLimitesDeCadaPeriodoContraElRango()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // 60 días fuerza granularidad semanal, que es donde el recorte se nota: las semanas empiezan
        // en lunes y el rango casi nunca lo hace.
        var filtro = UltimosDias(60);
        var report = await RunAsync(db, filtro);

        var serie = report!.Resumen.Serie;
        serie.Should().NotBeEmpty();
        serie[0].Desde.Should().Be(filtro.From.ToString("yyyy-MM-dd"));
        serie[^1].Hasta.Should().Be(filtro.To.ToString("yyyy-MM-dd"));

        // Sin huecos ni solapes: la unión de los periodos es exactamente el rango.
        foreach (var (previo, siguiente) in serie.Zip(serie.Skip(1)))
        {
            DateOnly.Parse(previo.Hasta).AddDays(1).Should().Be(DateOnly.Parse(siguiente.Desde));
            DateOnly.Parse(previo.Desde).Should().BeOnOrBefore(DateOnly.Parse(previo.Hasta));
        }
    }

    [Fact] // El resumen describe el universo, no la página. Si describiera la página, pasar a la
           // segunda cambiaría los totales y el informe se contradiría a sí mismo.
    public async Task Paginacion_ElResumenDescribeElUniversoNoLaPagina()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            for (var i = 1; i <= 12; i++)
            {
                Radicar(seed, $"REF-{i:D2}", TramiteEstado.Entregado, DiasAtras(3));
            }

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7), page: 2, pageSize: 5);

        report!.Total.Should().Be(12);
        report.Resumen.Total.Should().Be(12);
        report.Filas.Should().HaveCount(5);
        report.Page.Should().Be(2);
    }

    [Fact] // Un sortBy desconocido cae al orden por defecto en vez de devolver 400: rechazarlo
           // rompería la consola cada vez que se retire una columna, y el usuario perdería el
           // informe entero por un detalle que no eligió.
    public void Orden_UnCampoDesconocidoCaeAlOrdenPorDefecto()
    {
        var query = GetOtReportHandler.BuildQuery(
            UltimosDias(7), page: null, pageSize: null,
            sortBy: "columna_inventada", descending: true);

        query.SortBy.Should().Be(OtReportSort.Radicado);
        query.Page.Should().Be(1);
        query.PageSize.Should().Be(OtReportLimits.DefaultPageSize);
    }

    [Fact] // El tope de página no es negociable: sin él, un pageSize enorme volcaría el histórico
           // del organismo en una sola respuesta.
    public void Paginacion_RecortaElTamanoDePaginaAlTope()
    {
        var query = GetOtReportHandler.BuildQuery(
            UltimosDias(7), page: 0, pageSize: 100_000,
            sortBy: null, descending: true);

        query.PageSize.Should().Be(OtReportLimits.MaxPageSize);
        query.Page.Should().Be(1);
    }

    [Fact] // El filtro por empresa es el input principal del informe: si no recortara el universo,
           // el resumen que se le entrega a una empresa incluiría trámites de otra.
    public async Task Filtro_PorEmpresaRecortaElUniverso()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            Radicar(seed, "REF-A", TramiteEstado.Entregado, DiasAtras(2));
            Radicar(seed, "REF-B", TramiteEstado.Entregado, DiasAtras(2), tenantId: OtraEmpresa);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var todas = await RunAsync(db, UltimosDias(7));
        todas!.Resumen.Total.Should().Be(2);

        var soloUna = await RunAsync(db, UltimosDias(7) with { ClientTenantId = OtraEmpresa });
        soloUna!.Resumen.Total.Should().Be(1);
        soloUna.Filas.Should().ContainSingle().Which.ClientTenantName.Should().Be("Transportes Zeta S.A.S.");
    }

    [Fact] // La fila trae el nombre de la causal, no su id: el informe se lee fuera de FLIT y un
           // UUID ahí no le dice nada a nadie.
    public async Task Fila_TraeLasCausalesDelUltimoRechazoConSuDescripcion()
    {
        var db = NewDbName();
        var instanceId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            SeedInstance(seed, instanceId, "REF-1", TramiteEstado.Rechazado);
            SeedHistory(seed, instanceId, TramiteEstado.Entregado, DiasAtras(3));
            var eventId = SeedHistory(seed, instanceId, TramiteEstado.Rechazado, DiasAtras(2), Reviewer);
            var reasonId = SeedReason(seed, "improntas_borrosas", "Improntas están borrosas");
            seed.ProcedureInstanceRejectionReasons.Add(new ProcedureInstanceRejectionReason
            {
                Id = Guid.NewGuid(),
                TenantId = ClientTenant,
                ProcedureInstanceId = instanceId,
                StatusHistoryId = eventId,
                RejectionReasonId = reasonId,
                CreatedAt = DiasAtras(2),
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7));

        var fila = report!.Filas.Should().ContainSingle().Subject;
        fila.CausalesUltimoRechazo.Should().ContainSingle().Which.Should().Be("Improntas están borrosas");
        fila.DecididoPor.Should().Be("Ana Revisora");
    }

    // ── Andamiaje ─────────────────────────────────────────────────────────────────────────────

    private static async Task<OtReportDto?> RunAsync(
        string db,
        OtMetricsFilter filter,
        int page = 1,
        int pageSize = OtReportLimits.DefaultPageSize)
    {
        await using var ctx = NewContext(db);
        var handler = new GetOtReportHandler(new OtMetricsReadRepository(ctx));

        return await handler.HandleAsync(
            OtTenant,
            GetOtReportHandler.BuildQuery(filter, page, pageSize, null, descending: true),
            transitOfficeIdOverride: null,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Rango que termina hoy. Se construye con el día de Bogotá, que es el huso del reporte.</summary>
    private static OtMetricsFilter UltimosDias(int dias) =>
        new(Hoy().AddDays(-(dias - 1)), Hoy());

    private static DateOnly Hoy() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("America/Bogota")).DateTime);

    private static DateTimeOffset DiasAtras(int dias) => DateTimeOffset.UtcNow.AddDays(-dias);

    private static DateTimeOffset HorasAtras(int horas) => DateTimeOffset.UtcNow.AddHours(-horas);

    /// <summary>Radica un trámite y, opcionalmente, lo decide: el ciclo mínimo que ve el organismo.</summary>
    private static void Radicar(
        FlitDbContext ctx,
        string reference,
        string status,
        DateTimeOffset radicadoEn,
        DateTimeOffset? decision = null,
        string decisionStatus = TramiteEstado.Aprobado,
        string? plateFlowStatus = null,
        bool isPaused = false,
        bool subsanacionActiva = false,
        Guid? tenantId = null)
    {
        var id = Guid.NewGuid();
        SeedInstance(ctx, id, reference, status, plateFlowStatus, isPaused, subsanacionActiva, tenantId);
        SeedHistory(ctx, id, TramiteEstado.Entregado, radicadoEn, tenantId: tenantId);

        if (decision is DateTimeOffset at)
        {
            SeedHistory(ctx, id, decisionStatus, at, Reviewer, tenantId);
        }
    }

    private static void SeedInstance(
        FlitDbContext ctx,
        Guid id,
        string reference,
        string status,
        string? plateFlowStatus = null,
        bool isPaused = false,
        bool subsanacionActiva = false,
        Guid? tenantId = null) =>
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId ?? ClientTenant,
            ProcedureTypeId = ProcedureTypeId,
            ReferenceNumber = reference,
            Status = status,
            ModalidadEntrada = "matricula_inicial",
            PlateFlowStatus = plateFlowStatus,
            IsPaused = isPaused,
            SubsanacionActiva = subsanacionActiva,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = Reviewer,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });

    private static Guid SeedHistory(
        FlitDbContext ctx,
        Guid instanceId,
        string toStatus,
        DateTimeOffset at,
        Guid? changedBy = null,
        Guid? tenantId = null)
    {
        var id = Guid.NewGuid();
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = id,
            TenantId = tenantId ?? ClientTenant,
            ProcedureInstanceId = instanceId,
            FromStatus = null,
            ToStatus = toStatus,
            ChangedAt = at,
            ChangedBy = changedBy,
        });
        return id;
    }

    private static Guid SeedReason(FlitDbContext ctx, string code, string description)
    {
        var entity = new Flit.Infrastructure.Persistence.Entities.Catalogs.RejectionReason
        {
            Id = Guid.NewGuid(),
            Code = code,
            Description = description,
            Modalidad = "matricula_inicial",
            SortOrder = 10,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.RejectionReasons.Add(entity);
        return entity.Id;
    }

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

        foreach (var tenantId in new[] { ClientTenant, OtraEmpresa })
        {
            ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransitOfficeId = TransitOffice,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        ctx.Tenants.AddRange(
            new Tenant
            {
                Id = ClientTenant,
                Code = "client",
                LegalName = "Flota Andina S.A.S.",
                TaxId = "900000000",
                TenantType = "client",
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new Tenant
            {
                Id = OtraEmpresa,
                Code = "zeta",
                LegalName = "Transportes Zeta S.A.S.",
                TaxId = "900000001",
                TenantType = "client",
                CreatedAt = DateTimeOffset.UtcNow,
            });

        ctx.Users.Add(new User
        {
            Id = Reviewer,
            Email = "ana@organismo.gov.co",
            DisplayName = "Ana Revisora",
        });

        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = ProcedureTypeId,
            Code = "MATRICULA_NUEVA",
            Name = "Matrícula inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        ctx.SaveChanges();
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
