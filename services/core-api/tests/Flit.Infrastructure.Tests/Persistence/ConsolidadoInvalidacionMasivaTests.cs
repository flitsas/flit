using System.Data.Common;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Services;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12789 (Épica #12760) — <see cref="ConsolidadoInvalidacionMasiva"/>: QUÉ trámites alcanza la
/// invalidación masiva (AC1–AC4, predicado sobre el proveedor en memoria, que no soporta
/// <c>ExecuteUpdate</c>) y CÓMO la emite (AC5: un único <c>UPDATE</c> set-based, capturado con
/// interceptores sobre Npgsql sin servidor real — la conexión y el comando se suprimen).
/// Uso de ejemplo:
/// <code>await new ConsolidadoInvalidacionMasiva(db).InvalidarPorPrelacionOtAsync(otTenantId, tipoId, ct);</code>
/// </summary>
public sealed class ConsolidadoInvalidacionMasivaTests
{
    private static readonly Guid OtTenant = Guid.Parse("12789000-0000-4000-8000-0000000000a1");
    private static readonly Guid OtroOtTenant = Guid.Parse("12789000-0000-4000-8000-0000000000a2");
    private static readonly Guid Oficina = Guid.Parse("12789000-0000-4000-8000-0000000000b1");
    private static readonly Guid OtraOficina = Guid.Parse("12789000-0000-4000-8000-0000000000b2");
    private static readonly Guid Compania = Guid.Parse("12789000-0000-4000-8000-0000000000c1");
    private static readonly Guid OtraCompania = Guid.Parse("12789000-0000-4000-8000-0000000000c2");
    private static readonly Guid Tipo = Guid.Parse("12789000-0000-4000-8000-0000000000d1");
    private static readonly Guid OtroTipo = Guid.Parse("12789000-0000-4000-8000-0000000000d2");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── AC1 — prelación del OT ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_PrelacionOt_AlcanzaSoloTramitesNoFinalesDeEseOtYTipo()
    {
        await using var db = NewInMemory();
        SeedOficinas(db);
        var enCurso = Tramite(db, Compania, Oficina, Tipo, TramiteEstado.Entregado);
        var rechazado = Tramite(db, OtraCompania, Oficina, Tipo, TramiteEstado.Rechazado);
        var preasignacion = Tramite(db, Compania, Oficina, Tipo, TramiteEstado.Preasignacion, soloMaestro: true);
        Tramite(db, Compania, OtraOficina, Tipo, TramiteEstado.Entregado);     // otro OT
        Tramite(db, Compania, Oficina, OtroTipo, TramiteEstado.Entregado);     // otro tipo
        Tramite(db, Compania, oficina: null, Tipo, TramiteEstado.Borrador);    // wizard, sin OT
        await db.SaveChangesAsync(Ct);

        var ids = await ConsolidadoInvalidacionMasiva.CandidatasPorOt(db, OtTenant, Tipo)
            .Select(i => i.Id).ToListAsync(Ct);

        // Cross-tenant a propósito: el trámite es del cliente y la prelación del organismo.
        ids.Should().BeEquivalentTo([enCurso, rechazado, preasignacion]);
    }

    [Fact]
    public async Task AC1_PrelacionOt_TenantSinPerfilDeOficina_NoAlcanzaNada()
    {
        await using var db = NewInMemory();
        SeedOficinas(db);
        Tramite(db, Compania, Oficina, Tipo, TramiteEstado.Entregado);
        await db.SaveChangesAsync(Ct);

        var ids = await ConsolidadoInvalidacionMasiva.CandidatasPorOt(db, Guid.NewGuid(), Tipo)
            .Select(i => i.Id).ToListAsync(Ct);

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task AC1_Contrato_IdsVacios_NoEmitenNingunaOperacion()
    {
        var (db, capturados) = NewNpgsqlCapturando();
        await using (db)
        {
            var sut = new ConsolidadoInvalidacionMasiva(db);

            (await sut.InvalidarPorPrelacionOtAsync(Guid.Empty, Tipo, Ct)).Should().Be(0);
            (await sut.InvalidarPorPrelacionOtAsync(OtTenant, Guid.Empty, Ct)).Should().Be(0);
            (await sut.InvalidarPorCompaniaAsync(Guid.Empty, Ct)).Should().Be(0);
        }

        capturados.Should().BeEmpty();
    }

    // ── AC2 / AC3 — compañía (escritura, RL, firma del baúl) ─────────────────────────────────

    [Fact]
    public async Task AC2_AC3_PorCompania_AlcanzaSoloTramitesNoFinalesDeEseTenant()
    {
        await using var db = NewInMemory();
        var borrador = Tramite(db, Compania, oficina: null, Tipo, TramiteEstado.Borrador, soloWizard: true);
        var entregado = Tramite(db, Compania, Oficina, OtroTipo, TramiteEstado.Entregado);
        Tramite(db, OtraCompania, Oficina, Tipo, TramiteEstado.Entregado);    // otra compañía
        await db.SaveChangesAsync(Ct);

        var ids = await ConsolidadoInvalidacionMasiva.CandidatasPorTenant(db, Compania)
            .Select(i => i.Id).ToListAsync(Ct);

        ids.Should().BeEquivalentTo([borrador, entregado]);
    }

    [Fact]
    public async Task AC2_AC3_PorCompania_NoReescribeTramitesYaInvalidadosNiBorrados()
    {
        // Edge: sin consolidado vigente no hay nada que invalidar; un borrado lógico tampoco se toca.
        await using var db = NewInMemory();
        Tramite(db, Compania, Oficina, Tipo, TramiteEstado.Entregado, vigente: false);
        var borrado = Tramite(db, Compania, Oficina, Tipo, TramiteEstado.Entregado);
        await db.SaveChangesAsync(Ct);
        var fila = await db.ProcedureInstances.SingleAsync(i => i.Id == borrado, Ct);
        fila.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(Ct);

        var ids = await ConsolidadoInvalidacionMasiva.CandidatasPorTenant(db, Compania)
            .Select(i => i.Id).ToListAsync(Ct);

        ids.Should().BeEmpty();
    }

    // ── AC4 — estados finales intactos ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task AC4_EstadosFinales_NuncaSonCandidatosNiPorOtNiPorCompania(string estadoFinal)
    {
        await using var db = NewInMemory();
        SeedOficinas(db);
        Tramite(db, Compania, Oficina, Tipo, estadoFinal);
        var enCurso = Tramite(db, Compania, Oficina, Tipo, TramiteEstado.Entregado);
        await db.SaveChangesAsync(Ct);

        var porOt = await ConsolidadoInvalidacionMasiva.CandidatasPorOt(db, OtTenant, Tipo)
            .Select(i => i.Id).ToListAsync(Ct);
        var porCompania = await ConsolidadoInvalidacionMasiva.CandidatasPorTenant(db, Compania)
            .Select(i => i.Id).ToListAsync(Ct);

        porOt.Should().Equal(enCurso);
        porCompania.Should().Equal(enCurso);
    }

    [Fact]
    public void AC4_Contrato_ElFiltroReutilizaLaColeccionDeFinalesDelDominio()
    {
        // Si mañana se agrega un estado final, entra solo por TramiteEstado.Finales: no hay lista paralela.
        TramiteEstado.Finales.Should().BeEquivalentTo([TramiteEstado.Aprobado, TramiteEstado.Anulado, TramiteEstado.Revocado]);
    }

    // ── AC5 — una única operación set-based ──────────────────────────────────────────────────

    [Fact]
    public async Task AC5_PrelacionOt_EmiteUnSoloUpdateSetBasedConSubconsultaDeOficinas()
    {
        var (db, capturados) = NewNpgsqlCapturando(filasAfectadas: 732);
        int afectadas;
        await using (db)
        {
            afectadas = await new ConsolidadoInvalidacionMasiva(db).InvalidarPorPrelacionOtAsync(OtTenant, Tipo, Ct);
        }

        afectadas.Should().Be(732, "el conteo lo devuelve el UPDATE, no se materializan filas");
        capturados.Should().ContainSingle("una sola operación masiva, sin SELECT previo ni bucle por entidad");
        var sql = capturados[0];
        sql.Should().StartWith("UPDATE tramites.procedure_instances");
        sql.Should().Contain("SET consolidado_wizard_vigente = @");
        sql.Should().Contain("consolidado_maestro_vigente = @");
        sql.Should().Contain("admin.transit_office_profiles", "el OT se resuelve dentro del mismo comando");
        sql.Should().Contain("NOT (p.status = ANY (@finales)", "AC4: los estados finales del dominio quedan fuera");
        sql.Should().NotContain("'aprobado'", "los estados finales viajan como parámetro, no como literal");
    }

    [Fact]
    public async Task AC5_PorCompaniaYPorFirmaBaul_EmitenCadaUnoUnSoloUpdate()
    {
        var (db, capturados) = NewNpgsqlCapturando(filasAfectadas: 501);
        await using (db)
        {
            var sut = new ConsolidadoInvalidacionMasiva(db);
            (await sut.InvalidarPorCompaniaAsync(Compania, Ct)).Should().Be(501);
            (await sut.InvalidarPorFirmaBaulAsync(Compania, Ct)).Should().Be(501);
        }

        capturados.Should().HaveCount(2);
        capturados.Should().AllSatisfy(sql =>
        {
            sql.Should().StartWith("UPDATE tramites.procedure_instances");
            sql.Should().Contain("tenant_id = @");
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static FlitDbContext NewInMemory() =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase($"flit-12789-{Guid.NewGuid()}").Options);

    private static void SeedOficinas(FlitDbContext db)
    {
        db.TransitOfficeProfiles.Add(new TransitOfficeProfile { Id = Guid.NewGuid(), TenantId = OtTenant, TransitOfficeId = Oficina });
        db.TransitOfficeProfiles.Add(new TransitOfficeProfile { Id = Guid.NewGuid(), TenantId = OtroOtTenant, TransitOfficeId = OtraOficina });
    }

    private static Guid Tramite(
        FlitDbContext db,
        Guid tenant,
        Guid? oficina,
        Guid tipo,
        string estado,
        bool vigente = true,
        bool soloMaestro = false,
        bool soloWizard = false)
    {
        var id = Guid.NewGuid();
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            TransitOfficeId = oficina,
            ProcedureTypeId = tipo,
            ReferenceNumber = $"TRM-{id:N}",
            Status = estado,
            CreatedAt = DateTimeOffset.UtcNow,
            ConsolidadoWizardVigente = vigente && !soloMaestro,
            ConsolidadoMaestroVigente = vigente && !soloWizard,
        });
        return id;
    }

    /// <summary>
    /// Contexto Npgsql real (mismo modelo, snake_case) sin servidor: la apertura de la conexión y la
    /// ejecución del comando se suprimen, y el texto SQL de cada comando se captura.
    /// </summary>
    private static (FlitDbContext Db, List<string> Sql) NewNpgsqlCapturando(int filasAfectadas = 0)
    {
        var capturados = new List<string>();
        var options = new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=flit_12789;Username=x;Password=x")
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new SinConexion(), new CapturaComandos(capturados, filasAfectadas))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return (new FlitDbContext(options), capturados);
    }

    private sealed class SinConexion : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());

        public override InterceptionResult ConnectionClosing(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionClosingAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class CapturaComandos(List<string> capturados, int filas) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            capturados.Add(command.CommandText.Trim());
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(filas));
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            // Cualquier lectura (p. ej. cargar entidades) queda registrada y hace fallar el ContainSingle.
            capturados.Add(command.CommandText.Trim());
            throw new InvalidOperationException("La invalidación masiva no debe leer filas.");
        }
    }
}
