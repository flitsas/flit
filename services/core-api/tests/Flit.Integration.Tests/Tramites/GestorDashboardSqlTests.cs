using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Inicio del Gestor (rediseño de los 3 dashboards) contra PostgreSQL REAL: el SQL de
/// <see cref="GestorDashboardReadRepository"/> (cola de gestión). Dos compañías: lo de B nunca cuenta para A, ni en los
/// trámites ni en el historial de estados, aunque la fila de historial de B apunte a un trámite de A.
/// <para>Uso de ejemplo: <c>repo.GetColaInactividadAsync(A, ["borrador"], null, 2)</c> → los 2 más quietos de A y el
/// total de A.</para>
/// </summary>
public sealed class GestorDashboardSqlTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid A = new("6b6b6b6b-0001-4000-8000-00000000a001");
    private static readonly Guid B = new("6b6b6b6b-0002-4000-8000-00000000b002");
    private static readonly Guid Gestor = new("6b6b6b6b-0003-4000-8000-00000000c003");
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;
    private static readonly IReadOnlyList<string> SoloBorrador = [TramiteEstado.Borrador];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Trámites de A (borrador salvo que se diga). Días de creación entre paréntesis.
    private readonly Guid _m1 = Guid.NewGuid(); // matrícula (20)
    private readonly Guid _m2 = Guid.NewGuid(); // matrícula (18)
    private readonly Guid _m3 = Guid.NewGuid(); // matrícula (16)
    private readonly Guid _t1 = Guid.NewGuid(); // traspaso (14)
    private readonly Guid _t2 = Guid.NewGuid(); // traspaso (12)
    private readonly Guid _m4 = Guid.NewGuid(); // matrícula (30), con un cambio de estado de ayer
    private readonly Guid _borrado = Guid.NewGuid(); // matrícula (40), borrada → no existe
    private readonly Guid _preparado = Guid.NewGuid(); // matrícula (50), preparada → fuera del filtro de borrador
    private readonly Guid _deB = Guid.NewGuid(); // de B (60), borrador

    // ── Cola de gestión ────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Cola_ordena_por_ultimo_movimiento_y_el_total_usa_el_mismo_filtro_que_la_pagina()
    {
        await SembrarAsync();
        await using var ctx = NewContext();
        var repo = new GestorDashboardReadRepository(ctx);

        var completa = await repo.GetColaInactividadAsync(A, SoloBorrador, null, 20, Ct);
        completa.Total.Should().Be(6, "los 6 borradores vivos de A; ni el borrado, ni el preparado, ni el de B");
        completa.Items.Select(i => i.Id).Should().Equal(_m1, _m2, _m3, _t1, _t2, _m4);
        completa.Items.Should().NotContain(i => i.Id == _deB || i.Id == _borrado || i.Id == _preparado);

        var m1 = completa.Items[0];
        m1.DiasSinMovimiento.Should().Be(20, "el cambio de estado que B registró sobre M1 no cuenta para A");
        m1.Familia.Should().Be("matriculas");
        completa.Items[^1].DiasSinMovimiento.Should().Be(1, "M4 se creó hace 30 días pero su último cambio de estado fue ayer");

        var pagina = await repo.GetColaInactividadAsync(A, SoloBorrador, null, 2, Ct);
        pagina.Items.Select(i => i.Id).Should().Equal(_m1, _m2);
        pagina.Total.Should().Be(6, "el total no depende del límite");

        var traspasos = await repo.GetColaInactividadAsync(A, SoloBorrador, ["TRASPASO"], 1, Ct);
        traspasos.Items.Should().ContainSingle().Which.Id.Should().Be(_t1);
        traspasos.Total.Should().Be(2, "total y página salen del mismo filtro de familia");
    }

    [PostgresFact]
    public async Task Cola_de_B_no_ve_nada_de_A()
    {
        await SembrarAsync();
        await using var ctx = NewContext();
        var repo = new GestorDashboardReadRepository(ctx);

        var b = await repo.GetColaInactividadAsync(B, [TramiteEstado.Borrador, TramiteEstado.Preparado], null, 20, Ct);
        b.Items.Should().ContainSingle().Which.Id.Should().Be(_deB);
        b.Total.Should().Be(1);
    }

    // ── Siembra ────────────────────────────────────────────────────────────────────────────────────

    private async Task SembrarAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(A, "IT-GD-A", isGroupParent: false, parentId: null));
            ctx.Tenants.Add(TenantSeed.New(B, "IT-GD-B", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync(Ct);
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-gestor-dashboard@flit.test",
                DisplayName = "Gestor IT",
                Status = "active",
                HomeTenantId = A,
                CreatedAt = Ahora,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            async Task<Guid> TipoDe(string familia) => await ctx.ProcedureTypes.AsNoTracking()
                .Where(t => t.Family == familia).Select(t => t.Id).FirstAsync(Ct);
            var matricula = await TipoDe("MATRICULAS");
            var traspaso = await TipoDe("TRASPASO");

            ctx.ProcedureInstances.AddRange(
                Tramite(_m1, A, matricula, 20),
                Tramite(_m2, A, matricula, 18),
                Tramite(_m3, A, matricula, 16),
                Tramite(_t1, A, traspaso, 14),
                Tramite(_t2, A, traspaso, 12),
                Tramite(_m4, A, matricula, 30),
                Tramite(_borrado, A, matricula, 40, borrado: true),
                Tramite(_preparado, A, matricula, 50, estado: TramiteEstado.Preparado),
                Tramite(_deB, B, matricula, 60));
            await ctx.SaveChangesAsync(Ct);

            ctx.ProcedureInstanceStatusHistories.AddRange(
                // M4 se movió ayer (en A).
                Historial(A, _m4, Ahora.AddDays(-1)),
                // B registra un cambio de hoy sobre M1 de A: no debe «despertarlo».
                Historial(B, _m1, Ahora));
            await ctx.SaveChangesAsync(Ct);
        }
    }

    private static ProcedureInstance Tramite(
        Guid id, Guid tenant, Guid tipo, int dias, string estado = TramiteEstado.Borrador, bool borrado = false) => new()
    {
        Id = id,
        TenantId = tenant,
        ProcedureTypeId = tipo,
        ReferenceNumber = $"ITGD-{id.ToString()[..8]}",
        Status = estado,
        Vin = $"VINGD{id.ToString("N")[..12]}",
        CreatedByUserId = Gestor,
        CreatedAt = Ahora.AddDays(-dias).AddMinutes(-1),
        DeletedAt = borrado ? Ahora.AddDays(-dias) : null,
    };

    private static ProcedureInstanceStatusHistory Historial(Guid tenant, Guid tramite, DateTimeOffset cuando) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant,
        ProcedureInstanceId = tramite,
        FromStatus = TramiteEstado.Preparado,
        ToStatus = TramiteEstado.Borrador,
        ChangedAt = cuando,
        ChangedBy = Gestor,
    };
}
