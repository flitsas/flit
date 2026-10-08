using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13384 (Épica #13216, CF-05 y CF-07) — seguridad del lote del Super Admin con la revalidación REAL
/// (<see cref="ConsolidadoLoteAccessChecker"/> sobre el modelo RBAC de <see cref="FlitDbContext"/> en memoria) dentro
/// del <see cref="ProcesarItemLoteHandler"/> real con <see cref="SuperAdminLoteItemOrigen"/>. El entregador es un
/// sustituto: estos tests verifican cuándo NO se le llama.
/// <list type="bullet">
///   <item>AC2: un ítem con <c>tenant_id = B</c> cuyo trámite es de A se omite «Acceso revocado» sin llegar al entregador.</item>
///   <item>AC7: si al Super Admin se le retira la asignación activa del rol durante el lote, los ítems procesados después
///   (vencida la caché de 60 s del lote) se omiten «Acceso revocado».</item>
///   <item>AC1: la compañía contra la que se revalida es siempre la del ítem, nunca la del lote.</item>
/// </list>
/// <para>Uso de ejemplo: <c>await Handler().HandleAsync(ProcesarItemLoteCommand.Con(loteSa, item, settings), ct)</c>.</para>
/// </summary>
public sealed class AccessCheckerSuperAdminTests : IDisposable
{
    private static readonly Guid CompaniaA = Guid.NewGuid();
    private static readonly Guid CompaniaB = Guid.NewGuid();
    private static readonly Guid CompaniaDelRol = Guid.NewGuid();
    private static readonly Guid SuperAdmin = Guid.NewGuid();
    private static readonly Guid RolSuperAdmin = Guid.NewGuid();

    private static readonly LoteItemAdjunto Adjunto = new(Guid.NewGuid(), "fm/maestro-13384", 2048, "sha-13384", "maestro.pdf");

    private readonly string _dbName = Guid.NewGuid().ToString("N");
    private readonly RelojCache _reloj = new(new DateTimeOffset(2026, 10, 7, 18, 0, 0, TimeSpan.Zero));
    private readonly MemoryCache _cache;
    private readonly ILoteItemEntregador _entregador = Substitute.For<ILoteItemEntregador>();
    private readonly IConsolidadoLoteItemProceso _proceso = Substitute.For<IConsolidadoLoteItemProceso>();

    public AccessCheckerSuperAdminTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { Clock = _reloj });
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Incluido(Adjunto, LoteDeliveryMode.Existente));
        _proceso.MarcarIncluidoAsync(Arg.Any<LoteItemIncluido>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.MarcarOmitidoAsync(Arg.Any<LoteItemOmitido>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    public void Dispose() => _cache.Dispose();

    private FlitDbContext Db() =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static async Task<UserRoleAssignment> SeedAsync(FlitDbContext db)
    {
        db.Users.Add(new User { Id = SuperAdmin, Email = "sa13384@it.test", DisplayName = "Super Admin", Status = "active" });
        db.Roles.Add(new Role { Id = RolSuperAdmin, Code = "SuperAdmin", Name = "Super Admin", IsActive = true, ProductCode = "plataforma" });
        var asignacion = new UserRoleAssignment
        {
            Id = Guid.NewGuid(),
            UserId = SuperAdmin,
            RoleId = RolSuperAdmin,
            TenantId = CompaniaDelRol,
            AssignedAt = DateTimeOffset.UtcNow.AddDays(-30),
        };
        db.UserRoleAssignments.Add(asignacion);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return asignacion;
    }

    private ProcesarItemLoteHandler Handler(FlitDbContext db) => new(
        new LoteItemOrigenPorOrigen([new SuperAdminLoteItemOrigen(new ConsolidadoLoteAccessChecker(db, _cache), _entregador)]),
        _proceso,
        NullLogger<ProcesarItemLoteHandler>.Instance,
        LoteActivo());

    /// <summary>HU #13386 — el checkpoint previo al entregador ve el lote activo.</summary>
    private static IConsolidadoLoteRepository LoteActivo()
    {
        var lotes = Substitute.For<IConsolidadoLoteRepository>();
        lotes.GetStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ConsolidadoExportStatus.EnProceso);
        return lotes;
    }

    private static ProcedureInstance Tramite(Guid compania) => new()
    {
        ProcedureType = ProcedureTypeFixture.Matricula,
        Id = Guid.NewGuid(),
        TenantId = compania,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-13384",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ConsolidadoExportBatch LoteSuperAdmin() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = null,
        ScopeTenantId = CompaniaDelRol,
        RequestedByUserId = SuperAdmin,
        RequestedRoleCode = "SuperAdmin",
        Origin = ConsolidadoExportOrigin.Superadmin,
        DocumentType = ConsolidadoExportDocumentType.ConsolidadoMaestro,
        Status = ConsolidadoExportStatus.EnProceso,
    };

    private static ConsolidadoExportBatchItem Item(ConsolidadoExportBatch lote, ProcedureInstance t, Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(),
        BatchId = lote.Id,
        TenantId = tenantId ?? t.TenantId,
        ProcedureInstanceId = t.Id,
        Status = ConsolidadoExportItemStatus.Procesando,
        ReferenceNumber = "TRM-13384",
    };

    private static ProcesarItemLoteCommand Cmd(ConsolidadoExportBatch lote, ConsolidadoExportBatchItem item) =>
        ProcesarItemLoteCommand.Con(lote, item, new ConsolidadoExportSettings { MaxItemAttempts = 3, RetryDelaySeconds = 30 });

    // ── AC2 — ítem con compañía manipulada ──────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_ItemConTenantBYTramiteDeA_QuedaOmitidoAccesoRevocado_YElEntregadorNoSeInvoca()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Db();
        await SeedAsync(db);
        var deA = Tramite(CompaniaA);
        db.ProcedureInstances.Add(deA);
        await db.SaveChangesAsync(ct);
        var lote = LoteSuperAdmin();
        var manipulado = Item(lote, deA, tenantId: CompaniaB);

        var r = await Handler(db).HandleAsync(Cmd(lote, manipulado), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Codigo.Should().Be(ConsolidadoLoteOmisiones.AccesoRevocado);
        r.Motivo.Should().Be("Acceso revocado");
        await _proceso.Received(1).MarcarOmitidoAsync(
            Arg.Is<LoteItemOmitido>(o => o.ItemId == manipulado.Id && o.Codigo == ConsolidadoLoteOmisiones.AccesoRevocado && o.Motivo == "Acceso revocado"),
            Arg.Any<CancellationToken>());
        await _entregador.DidNotReceive().EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_Contrato_ElMismoTramiteConSuCompaniaReal_SiSeEntrega()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Db();
        await SeedAsync(db);
        var deA = Tramite(CompaniaA);
        db.ProcedureInstances.Add(deA);
        await db.SaveChangesAsync(ct);
        var lote = LoteSuperAdmin();

        var r = await Handler(db).HandleAsync(Cmd(lote, Item(lote, deA)), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        await _entregador.Received(1).EntregarAsync(
            Arg.Is<LoteItemEntregaRequest>(q => q.ProcedureInstanceId == deA.Id && q.TenantId == CompaniaA),
            Arg.Any<CancellationToken>());
    }

    // ── AC1 — la revalidación usa la compañía del ítem, nunca la del lote ──────────────────────

    [Fact]
    public async Task AC1_RevalidacionSuperAdmin_UsaLaCompaniaDelItem_AunqueElContextoTraigaUnaCompaniaDeLote()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Db();
        await SeedAsync(db);
        var deA = Tramite(CompaniaA);
        var deB = Tramite(CompaniaB);
        db.ProcedureInstances.AddRange(deA, deB);
        await db.SaveChangesAsync(ct);
        var checker = new ConsolidadoLoteAccessChecker(db, _cache);
        var lote = Guid.NewGuid();

        // Un contexto superadmin con compañía de lote (E5 lo impide en BD): no debe desplazar a la del ítem.
        LoteItemContexto Ctx(ProcedureInstance t, Guid companiaItem) => new(
            lote, Guid.NewGuid(), ConsolidadoExportOrigin.Superadmin, CompaniaB, companiaItem, null,
            SuperAdmin, "SuperAdmin", t.Id, LoteTipoDocumento.ConsolidadoMaestro);

        (await checker.TieneAccesoAsync(Ctx(deA, CompaniaA), ct)).Should().BeTrue("el trámite es de la compañía del ítem");
        (await checker.TieneAccesoAsync(Ctx(deB, CompaniaA), ct)).Should().BeFalse(
            "el trámite es de la compañía del lote, no de la del ítem: se omite");
    }

    // ── AC7 — el Super Admin pierde su rol durante el lote ──────────────────────────────────────

    public static TheoryData<string> Retiros() => new() { "asignacion_borrada", "rol_desactivado", "usuario_inactivo" };

    [Theory]
    [MemberData(nameof(Retiros))]
    public async Task AC7_SeLeRetiraElRolSuperAdminDuranteElLote_LosItemsSiguientesQuedanOmitidosAccesoRevocado(string retiro)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Db();
        var asignacion = await SeedAsync(db);
        var t1 = Tramite(CompaniaA);
        var t2 = Tramite(CompaniaB);
        var t3 = Tramite(CompaniaA);
        db.ProcedureInstances.AddRange(t1, t2, t3);
        await db.SaveChangesAsync(ct);
        var lote = LoteSuperAdmin();
        var (i1, i2, i3) = (Item(lote, t1), Item(lote, t2), Item(lote, t3));
        var handler = Handler(db);

        var r1 = await handler.HandleAsync(Cmd(lote, i1), ct);

        switch (retiro)
        {
            case "asignacion_borrada":
                asignacion.DeletedAt = DateTimeOffset.UtcNow;
                break;
            case "rol_desactivado":
                (await db.Roles.SingleAsync(r => r.Id == RolSuperAdmin, ct)).IsActive = false;
                break;
            default:
                (await db.Users.SingleAsync(u => u.Id == SuperAdmin, ct)).Status = "suspended";
                break;
        }

        await db.SaveChangesAsync(ct);
        _reloj.Avanzar(ConsolidadoLoteAccessChecker.DuracionCache + TimeSpan.FromSeconds(1));

        var r2 = await handler.HandleAsync(Cmd(lote, i2), ct);
        var r3 = await handler.HandleAsync(Cmd(lote, i3), ct);

        r1.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido, "antes del retiro el Super Admin conservaba el rol");
        foreach (var r in new[] { r2, r3 })
        {
            r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
            r.Codigo.Should().Be(ConsolidadoLoteOmisiones.AccesoRevocado);
            r.Motivo.Should().Be("Acceso revocado");
        }

        await _entregador.Received(1).EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>());
        await _entregador.Received(1).EntregarAsync(
            Arg.Is<LoteItemEntregaRequest>(q => q.ProcedureInstanceId == t1.Id), Arg.Any<CancellationToken>());
        await _proceso.Received(2).MarcarOmitidoAsync(
            Arg.Is<LoteItemOmitido>(o => o.Codigo == ConsolidadoLoteOmisiones.AccesoRevocado && (o.ItemId == i2.Id || o.ItemId == i3.Id)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Reloj de la caché del lote: permite vencer los 60 s sin esperar.</summary>
    private sealed class RelojCache(DateTimeOffset inicio) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = inicio;

        public void Avanzar(TimeSpan delta) => UtcNow += delta;
    }
}
