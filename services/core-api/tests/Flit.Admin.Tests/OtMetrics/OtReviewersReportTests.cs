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
/// Informe de revisores del organismo de tránsito.
///
/// <para>Su universo son las DECISIONES del rango, no los trámites recibidos: es el corte que
/// corresponde a la pregunta «qué hizo esta persona en estas fechas», y la diferencia deliberada
/// con el informe del periodo.</para>
///
/// <para>Lo que se prueba aquí es sobre todo que el informe no pueda usarse mal: que el volumen
/// nunca vaya solo, que los tiempos no le carguen a una persona la demora de otra, y que la
/// selección de revisores signifique lo que aparenta.</para>
/// </summary>
public sealed class OtReviewersReportTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtraEmpresa = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ProcedureTypeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid Carla = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Diego = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact] // El universo son las decisiones del RANGO. Un trámite radicado hace meses y aprobado
           // ayer es trabajo de ayer; uno aprobado la semana pasada no cuenta hoy.
    public async Task Universo_SonLasDecisionesDelRango_NoLosTramitesRecibidos()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);

            // Radicado muy anterior al rango, decidido DENTRO: cuenta.
            var viejo = Radicar(seed, "REF-1", TramiteEstado.Aprobado, EnBogota(90, 9));
            Decidir(seed, viejo, TramiteEstado.Aprobado, EnBogota(2, 10), Carla);

            // Radicado dentro, decidido FUERA (antes del rango): no cuenta.
            var fuera = Radicar(seed, "REF-2", TramiteEstado.Aprobado, EnBogota(40, 9));
            Decidir(seed, fuera, TramiteEstado.Aprobado, EnBogota(30, 10), Carla);

            // Radicado dentro y sin decidir: no es trabajo de nadie todavía.
            Radicar(seed, "REF-3", TramiteEstado.Entregado, EnBogota(2, 9));

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7));

        report!.Filas.Should().ContainSingle().Which.Decididos.Should().Be(1);
        report.Resumen.Decididos.Should().Be(1);
    }

    [Fact] // Que el organismo anule lo que la empresa dio de baja no es una decisión de revisión.
           // Contarlo inflaría el volumen de quien tramita bajas.
    public async Task Anulado_NoCuentaComoDecisionDeRevisor()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            var id = Radicar(seed, "REF-1", TramiteEstado.Anulado, EnBogota(3, 9));
            Decidir(seed, id, TramiteEstado.Anulado, EnBogota(2, 10), Carla);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7));

        report!.Filas.Should().BeEmpty();
        report.Resumen.Decididos.Should().Be(0);
    }

    [Fact] // Filtro vacío = TODOS. Devolver cero filas hasta que alguien toque el selector dejaría
           // el informe inservible justo al abrirlo.
    public async Task Filtro_VacioSignificaTodosLosRevisores()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            DecidirNuevo(seed, "REF-1", TramiteEstado.Aprobado, EnBogota(2, 10), Carla);
            DecidirNuevo(seed, "REF-2", TramiteEstado.Aprobado, EnBogota(2, 11), Diego);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7));

        report!.Filas.Should().HaveCount(2);
        report.Resumen.Revisores.Should().Be(2);
    }

    [Fact] // Y con selección, el resumen describe SOLO lo seleccionado: si siguiera describiendo al
           // equipo entero, filtrar por una persona daría porcentajes que no son suyos.
    public async Task Filtro_SeleccionaRevisoresYElResumenSigueLaSeleccion()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            DecidirNuevo(seed, "REF-1", TramiteEstado.Aprobado, EnBogota(2, 10), Carla);
            DecidirNuevo(seed, "REF-2", TramiteEstado.Rechazado, EnBogota(2, 11), Carla);
            DecidirNuevo(seed, "REF-3", TramiteEstado.Aprobado, EnBogota(2, 12), Diego);
            DecidirNuevo(seed, "REF-4", TramiteEstado.Aprobado, EnBogota(2, 13), Diego);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7), userIds: [Carla]);

        report!.Filas.Should().ContainSingle().Which.UserId.Should().Be(Carla);
        report.Resumen.Decididos.Should().Be(2);
        report.Resumen.AprobacionPct.Should().Be(50);
    }

    [Fact] // El reloj arranca en la ÚLTIMA radicación. Medirlo desde la primera le cargaría al
           // revisor los días que la empresa tardó en subsanar, que no son suyos.
    public async Task Tiempos_SeMidenDesdeLaUltimaRadicacion()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);

            var id = Radicar(seed, "REF-1", TramiteEstado.Aprobado, EnBogota(5, 8));
            Decidir(seed, id, TramiteEstado.Rechazado, EnBogota(5, 10), Carla);   // 2 h
            SeedHistory(seed, id, TramiteEstado.Entregado, EnBogota(3, 8));       // vuelve
            Decidir(seed, id, TramiteEstado.Aprobado, EnBogota(3, 11), Carla);    // 3 h

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7));

        var carla = report!.Filas.Should().ContainSingle().Subject;
        carla.Decididos.Should().Be(2);

        // Mediana de {2, 3} = 2,5 — y NO 50 y pico, que es lo que saldría midiendo desde la
        // primera radicación con dos días de subsanación en medio.
        carla.TiempoMedianoHoras.Should().BeApproximately(2.5, 0.1);
        carla.TiempoMaximoHoras.Should().BeApproximately(3, 0.1);
        carla.EnMenosDe24hPct.Should().Be(100);
    }

    [Fact] // La reincidencia es la señal de que el motivo del rechazo no quedó claro. El segundo
           // rechazo casi siempre cae FUERA del rango, así que el historial se lee completo.
    public async Task Reincidencia_MiraRechazosPosterioresAunqueCaiganFueraDelRango()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);

            // Rechazo de Carla dentro del rango, que vuelve a rechazarse HOY (fuera del rango).
            var reincide = Radicar(seed, "REF-1", TramiteEstado.Rechazado, EnBogota(20, 8));
            Decidir(seed, reincide, TramiteEstado.Rechazado, EnBogota(15, 10), Carla);
            SeedHistory(seed, reincide, TramiteEstado.Entregado, EnBogota(3, 8));
            Decidir(seed, reincide, TramiteEstado.Rechazado, EnBogota(1, 9), Diego);

            // Rechazo de Carla que no volvió.
            var limpio = Radicar(seed, "REF-2", TramiteEstado.Rechazado, EnBogota(20, 8));
            Decidir(seed, limpio, TramiteEstado.Rechazado, EnBogota(15, 11), Carla);

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Rango que contiene los rechazos de Carla pero NO el segundo rechazo de Diego.
        var report = await RunAsync(db, Entre(16, 14));

        var carla = report!.Filas.Should().ContainSingle().Subject;
        carla.Rechazados.Should().Be(2);
        carla.VuelvenARechazarsePct.Should().Be(50);
    }

    [Fact] // La productividad se mide por día ACTIVO, no por día del rango: quien estuvo de
           // vacaciones media semana no debe parecer menos productivo por ello.
    public async Task Productividad_SeMidePorDiaActivoNoPorDiaDelRango()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            DecidirNuevo(seed, "REF-1", TramiteEstado.Aprobado, EnBogota(3, 9), Carla);
            DecidirNuevo(seed, "REF-2", TramiteEstado.Aprobado, EnBogota(3, 15), Carla);
            DecidirNuevo(seed, "REF-3", TramiteEstado.Aprobado, EnBogota(2, 9), Carla);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // El rango son 30 días; solo 2 tienen decisiones.
        var report = await RunAsync(db, UltimosDias(30));

        var carla = report!.Filas.Should().ContainSingle().Subject;
        carla.DiasActivos.Should().Be(2);
        carla.DecisionesPorDiaActivo.Should().Be(1.5);
    }

    [Fact] // La mediana del equipo se calcula sobre TODAS las decisiones. Promediar las medianas
           // de cada persona le daría el mismo peso a quien decidió una que a quien decidió cinco.
    public async Task Resumen_LaMedianaEsSobreLasDecisiones_NoElPromedioDeMedianas()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);

            // Diego: una sola decisión, lentísima (100 h).
            var lento = Radicar(seed, "LENTO", TramiteEstado.Aprobado, EnBogota(10, 8));
            Decidir(seed, lento, TramiteEstado.Aprobado, EnBogota(6, 12), Diego);

            // Carla: cinco decisiones de 2 h.
            for (var i = 0; i < 5; i++)
            {
                var id = Radicar(seed, $"RAPIDO-{i}", TramiteEstado.Aprobado, EnBogota(3, 8));
                Decidir(seed, id, TramiteEstado.Aprobado, EnBogota(3, 10), Carla);
            }

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(30));

        // Mediana de {2,2,2,2,2,100} = 2. El promedio de medianas daría 51.
        report!.Resumen.TiempoMedianoHoras.Should().BeApproximately(2, 0.2);
    }

    [Fact] // Un equipo de dos donde uno hace el 80 % no es un equipo de dos. El promedio esconde
           // justo eso, así que la concentración se dice aparte.
    public async Task Resumen_LaConcentracionSeñalaAQuienCargaConElTrabajo()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            for (var i = 0; i < 8; i++)
            {
                DecidirNuevo(seed, $"C-{i}", TramiteEstado.Aprobado, EnBogota(2, 9), Carla);
            }

            for (var i = 0; i < 2; i++)
            {
                DecidirNuevo(seed, $"D-{i}", TramiteEstado.Aprobado, EnBogota(2, 9), Diego);
            }

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7));

        report!.Resumen.ConcentracionTopPct.Should().Be(80);
        report.Resumen.RevisorMasActivo.Should().Be("Carla Revisora");
    }

    [Fact] // Marcar nueve causales en cada rechazo es no marcar ninguna: el promedio lo delata.
    public async Task Causales_SeCuentanPorRechazoParaDelatarAQuienMarcaTodo()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            var r1 = SeedReason(seed, "improntas", "Improntas borrosas");
            var r2 = SeedReason(seed, "soat", "SOAT vencido");

            var uno = Radicar(seed, "REF-1", TramiteEstado.Rechazado, EnBogota(3, 8));
            var evento = Decidir(seed, uno, TramiteEstado.Rechazado, EnBogota(2, 10), Carla);
            Marcar(seed, evento, r1);
            Marcar(seed, evento, r2);

            var dos = Radicar(seed, "REF-2", TramiteEstado.Rechazado, EnBogota(3, 8));
            Marcar(seed, Decidir(seed, dos, TramiteEstado.Rechazado, EnBogota(2, 11), Carla), r1);

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7));

        // 3 marcas sobre 2 rechazos.
        report!.Filas.Should().ContainSingle().Which.CausalesPorRechazo.Should().Be(1.5);
    }

    [Fact] // Un sortBy desconocido llega de una URL editada o un enlace guardado. Perder el informe
           // entero por eso es peor que ordenarlo distinto.
    public async Task Orden_DesconocidoCaeAVolumenYDesempataEstable()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            DecidirNuevo(seed, "REF-1", TramiteEstado.Aprobado, EnBogota(2, 9), Carla);
            DecidirNuevo(seed, "REF-2", TramiteEstado.Aprobado, EnBogota(2, 10), Diego);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var report = await RunAsync(db, UltimosDias(7), sortBy: "por-simpatia");

        // Mismo volumen: manda el desempate por nombre, que es lo que hace el orden reproducible.
        report!.Filas.Select(f => f.DisplayName).Should()
            .ContainInOrder("Carla Revisora", "Diego Revisor");
    }

    [Fact] // El catálogo del filtro no se recorta por rango: si lo hiciera, un revisor de
           // vacaciones desaparecería del selector justo cuando alguien va a comprobar que no
           // decidió nada en esas fechas.
    public async Task Opciones_ListanTodoElHistorico_AunqueNoHayaActividadReciente()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedScope(seed);
            DecidirNuevo(seed, "REF-1", TramiteEstado.Aprobado, EnBogota(400, 9), Diego);
            DecidirNuevo(seed, "REF-2", TramiteEstado.Aprobado, EnBogota(2, 9), Carla);
            DecidirNuevo(seed, "REF-3", TramiteEstado.Rechazado, EnBogota(2, 10), Carla);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtReviewerOptionsHandler(new OtMetricsReadRepository(ctx));
        var options = await handler.HandleAsync(
            OtTenant, transitOfficeIdOverride: null, TestContext.Current.CancellationToken);

        options.Should().HaveCount(2);
        // Ordenadas por volumen: el selector empieza por quien más aparece en los informes.
        options![0].DisplayName.Should().Be("Carla Revisora");
        options[0].Decisiones.Should().Be(2);
        options[1].Decisiones.Should().Be(1);
    }

    // ── Infraestructura de prueba ─────────────────────────────────────────────────────────────

    private static async Task<OtReviewersReportDto?> RunAsync(
        string db,
        OtMetricsFilter filter,
        IEnumerable<Guid>? userIds = null,
        string? sortBy = null)
    {
        await using var ctx = NewContext(db);
        var handler = new GetOtReviewersReportHandler(new OtMetricsReadRepository(ctx));

        return await handler.HandleAsync(
            OtTenant,
            GetOtReviewersReportHandler.BuildQuery(filter, userIds, sortBy, descending: true),
            transitOfficeIdOverride: null,
            TestContext.Current.CancellationToken);
    }

    private static OtMetricsFilter UltimosDias(int dias) =>
        new(Hoy().AddDays(-(dias - 1)), Hoy());

    /// <summary>Rango cerrado entre dos «días atrás», para probar lo que queda fuera del periodo.</summary>
    private static OtMetricsFilter Entre(int desdeDiasAtras, int hastaDiasAtras) =>
        new(Hoy().AddDays(-desdeDiasAtras), Hoy().AddDays(-hastaDiasAtras));

    private static DateOnly Hoy() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Bogota).DateTime);

    private static readonly TimeZoneInfo Bogota =
        TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    /// <summary>
    /// Un instante fijado a una hora concreta del día de Bogotá.
    ///
    /// <para>Se construye así y no restando horas a «ahora» porque el informe agrupa por día
    /// calendario de Bogotá: un test que corriera a las 23:30 vería sus dos decisiones caer en días
    /// distintos y fallaría solo de madrugada.</para>
    /// </summary>
    private static DateTimeOffset EnBogota(int diasAtras, int hora)
    {
        var dia = Hoy().AddDays(-diasAtras);
        return new DateTimeOffset(
            new DateTime(dia.Year, dia.Month, dia.Day, hora, 0, 0, DateTimeKind.Unspecified),
            TimeSpan.FromHours(-5));
    }

    /// <summary>Radica un trámite y devuelve su id.</summary>
    private static Guid Radicar(
        FlitDbContext ctx,
        string reference,
        string status,
        DateTimeOffset radicadoEn,
        Guid? tenantId = null,
        bool prioritario = false)
    {
        var id = Guid.NewGuid();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = id,
            TenantId = tenantId ?? ClientTenant,
            ProcedureTypeId = ProcedureTypeId,
            ReferenceNumber = reference,
            Status = status,
            Prioritario = prioritario,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = Carla,
            CreatedAt = radicadoEn,
        });

        SeedHistory(ctx, id, TramiteEstado.Entregado, radicadoEn);
        return id;
    }

    private static Guid Decidir(
        FlitDbContext ctx,
        Guid instanceId,
        string status,
        DateTimeOffset at,
        Guid reviewer) =>
        SeedHistory(ctx, instanceId, status, at, reviewer);

    /// <summary>Radica y decide en un solo paso, con dos horas de diferencia.</summary>
    private static void DecidirNuevo(
        FlitDbContext ctx,
        string reference,
        string status,
        DateTimeOffset at,
        Guid reviewer)
    {
        var id = Radicar(ctx, reference, status, at.AddHours(-2));
        Decidir(ctx, id, status, at, reviewer);
    }

    private static Guid SeedHistory(
        FlitDbContext ctx,
        Guid instanceId,
        string toStatus,
        DateTimeOffset at,
        Guid? changedBy = null)
    {
        var id = Guid.NewGuid();
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = id,
            TenantId = ClientTenant,
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

    private static void Marcar(FlitDbContext ctx, Guid statusHistoryId, Guid reasonId) =>
        ctx.ProcedureInstanceRejectionReasons.Add(
            new ProcedureInstanceRejectionReason
            {
                Id = Guid.NewGuid(),
                TenantId = ClientTenant,
                ProcedureInstanceId = Guid.NewGuid(),
                StatusHistoryId = statusHistoryId,
                RejectionReasonId = reasonId,
                CreatedAt = DateTimeOffset.UtcNow,
            });

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

        ctx.Users.AddRange(
            new User { Id = Carla, Email = "carla@ot.local", DisplayName = "Carla Revisora" },
            new User { Id = Diego, Email = "diego@ot.local", DisplayName = "Diego Revisor" });
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
