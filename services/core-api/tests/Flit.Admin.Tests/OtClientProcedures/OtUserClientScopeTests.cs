using System.Security.Claims;
using Flit.Admin.Application.OtClientScopes;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Api.UseCases.RevocationRequests;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.RevocationRequests;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// Alcance de compañías cliente por usuario del OT (mejora sin HU). Fija tres cosas: el DEFAULT (<c>todos</c> /
/// ausencia de fila) no cambia nada; el modo <c>especificos</c> recorta TODA superficie que pasa por los puntos
/// centrales (bandeja, contadores, detalle, acciones, métricas, revocatorias); y el mantenimiento del alcance
/// (reglas del caso de uso, auditoría, invitaciones).
///
/// <para>Uso de ejemplo del resolver: <c>new FakeScope([ClientA])</c> restringe al usuario a la compañía A;
/// <c>new FakeScope(null)</c> es «todos».</para>
/// </summary>
public sealed class OtUserClientScopeTests
{
    private static readonly Guid OtTenant = Guid.Parse("a0a0a0a0-0000-4000-8000-000000000001");
    private static readonly Guid ClientA = Guid.Parse("a0a0a0a0-0000-4000-8000-0000000000a1");
    private static readonly Guid ClientB = Guid.Parse("a0a0a0a0-0000-4000-8000-0000000000b2");
    private static readonly Guid Office = Guid.Parse("a0a0a0a0-0000-4000-8000-0000000000e1");
    private static readonly Guid ProcType = Guid.Parse("a0a0a0a0-0000-4000-8000-0000000000f1");
    private static readonly Guid ProcA = Guid.Parse("a0a0a0a0-0000-4000-8000-00000000aa01");
    private static readonly Guid ProcB = Guid.Parse("a0a0a0a0-0000-4000-8000-00000000bb02");
    private static readonly Guid UserX = Guid.Parse("a0a0a0a0-0000-4000-8000-0000000000c1");
    private static readonly Guid AdminUser = Guid.Parse("a0a0a0a0-0000-4000-8000-0000000000d1");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeScope(IReadOnlyList<Guid>? restriction) : IOtUserClientScopeResolver
    {
        public Task<IReadOnlyList<Guid>?> GetRestrictionAsync(Guid transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(restriction);
    }

    // ── Default: todos no cambia nada ───────────────────────────────────────────────

    [Theory]
    [InlineData(false)] // sin resolver (tests / procesos sin usuario)
    [InlineData(true)] // resolver que devuelve null = todos
    public async Task Todos_VeAmbasCompanias_EnListaYContadores(bool conResolver)
    {
        var db = SeedDb();
        await using var ctx = NewContext(db);
        var repo = Repo(ctx, conResolver ? new FakeScope(null) : null);

        var page = await repo.ListAsync(OtTenant, new OtClientProcedureFilter(), cancellationToken: Ct);
        var counters = await repo.GetBandejaCountersAsync(OtTenant, null, cancellationToken: Ct);

        page.Items.Select(i => i.Id).Should().BeEquivalentTo([ProcA, ProcB]);
        counters!.PorDecidir.Should().Be(2);
    }

    // ── Específicos: recorta la bandeja y todo lo que cuelga de ella ────────────────

    [Fact]
    public async Task Especificos_ListaYContadoresSoloVenLasAsignadas()
    {
        var db = SeedDb();
        await using var ctx = NewContext(db);
        var repo = Repo(ctx, new FakeScope([ClientA]));

        var page = await repo.ListAsync(OtTenant, new OtClientProcedureFilter(), cancellationToken: Ct);
        var counters = await repo.GetBandejaCountersAsync(OtTenant, null, cancellationToken: Ct);
        var health = await repo.GetDeliveryHealthAsync(OtTenant, cancellationToken: Ct);

        page.Items.Select(i => i.Id).Should().BeEquivalentTo([ProcA]);
        page.TotalCount.Should().Be(1);
        counters!.PorDecidir.Should().Be(1);
        health!.DeliveredTotal.Should().Be(1);
    }

    [Fact]
    public async Task Especificos_CamposDeFiltroNoOfreceLaEmpresaNoAsignada()
    {
        var db = SeedDb();
        await using var ctx = NewContext(db);

        var fields = await Repo(ctx, new FakeScope([ClientA]))
            .GetBandejaFilterFieldsAsync(OtTenant, cancellationToken: Ct);

        var empresas = fields!.Single(f => f.Id == Flit.Admin.Domain.OtQueries.OtBandejaQueryFieldCatalog.Empresa).Options!;
        empresas.Select(o => o.Value).Should().BeEquivalentTo([ClientA.ToString()]);
    }

    [Fact]
    public async Task Especificos_ElDetalleDeUnaCompaniaNoAsignadaEsInexistente()
    {
        var db = SeedDb();
        await using var ctx = NewContext(db);
        var repo = Repo(ctx, new FakeScope([ClientA]));

        (await repo.GetByIdAsync(OtTenant, ProcB, Ct)).Should().BeNull("404: no se filtra ni la existencia");
        (await repo.GetByIdAsync(OtTenant, ProcA, Ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task Especificos_NoSePuedeAprobarNiRechazarUnTramiteDeUnaCompaniaNoAsignada()
    {
        var db = SeedDb();
        await using (var ctx = NewContext(db))
        {
            var repo = Repo(ctx, new FakeScope([ClientA]));

            (await repo.ApproveAsync(OtTenant, ProcB, AdminUser, OtTransitionSource.OtAdmin, cancellationToken: Ct)).Should().BeNull();
            (await repo.RejectAsync(OtTenant, ProcB, "motivo", AdminUser, OtTransitionSource.OtAdmin, cancellationToken: Ct)).Should().BeNull();
        }

        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == ProcB, Ct))
            .Status.Should().Be(TramiteEstado.Entregado, "el trámite ajeno no se tocó");
    }

    [Fact]
    public async Task QuitarUnClienteSurteEfectoDeInmediato_AunqueGestionaraSusTramites()
    {
        var db = SeedDb();

        await using (var ctx = NewContext(db))
        {
            (await Repo(ctx, new FakeScope([ClientA, ClientB])).GetByIdAsync(OtTenant, ProcB, Ct)).Should().NotBeNull();
        }

        // Mismo usuario, la siguiente petición: ya sin la compañía B.
        await using var after = NewContext(db);
        (await Repo(after, new FakeScope([ClientA])).GetByIdAsync(OtTenant, ProcB, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task SeleccionPorLote_SoloResuelveLasAsignadas()
    {
        var db = SeedDb();
        await using var ctx = NewContext(db);
        var repo = Repo(ctx, new FakeScope([ClientA]));

        var porIds = await repo.ListAccessibleRefsAsync(OtTenant, null, [ProcA, ProcB], cancellationToken: Ct);
        var porFiltro = await repo.ListAccessibleRefsAsync(OtTenant, new OtClientProcedureFilter(), null, cancellationToken: Ct);
        var conteo = await repo.CountAccessibleRefsAsync(OtTenant, new OtClientProcedureFilter(), null, cancellationToken: Ct);

        porIds.Select(r => r.Id).Should().BeEquivalentTo([ProcA]);
        porFiltro.Select(r => r.Id).Should().BeEquivalentTo([ProcA]);
        conteo.Should().Be(1);
    }

    // ── Métricas / consultas: intersección con el convenio ──────────────────────────

    [Fact]
    public async Task Metricas_SoloContabilizanLasAsignadas()
    {
        var db = SeedDb(atrasados: true);

        await using (var todos = NewContext(db))
        {
            var snap = await new OtMetricsReadRepository(todos, null, new FakeScope(null))
                .GetAlertSnapshotAsync(OtTenant, windowMinutes: 1440, cancellationToken: Ct);
            snap!.StuckCount.Should().Be(2);
        }

        await using var restringido = NewContext(db);
        var soloA = await new OtMetricsReadRepository(restringido, null, new FakeScope([ClientA]))
            .GetAlertSnapshotAsync(OtTenant, windowMinutes: 1440, cancellationToken: Ct);
        soloA!.StuckCount.Should().Be(1);
    }

    [Fact]
    public async Task Metricas_UnaCompaniaAsignadaSinConvenioDejaDeVerse_Interseccion()
    {
        var db = SeedDb(atrasados: true);
        await using (var seed = NewContext(db))
        {
            var grantA = await seed.TenantTransitOfficeGrants.SingleAsync(g => g.TenantId == ClientA, Ct);
            grantA.IsEnabled = false;
            await seed.SaveChangesAsync(Ct);
        }

        await using var ctx = NewContext(db);
        var snap = await new OtMetricsReadRepository(ctx, null, new FakeScope([ClientA]))
            .GetAlertSnapshotAsync(OtTenant, windowMinutes: 1440, cancellationToken: Ct);

        snap!.StuckCount.Should().Be(0, "asignada pero sin convenio: la intersección la deja fuera");
    }

    // ── Revocatorias ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revocatorias_SoloListaLasDeLasCompaniasAsignadas()
    {
        var db = SeedDb();
        await using (var seed = NewContext(db))
        {
            AddRevocation(seed, ProcA, ClientA);
            AddRevocation(seed, ProcB, ClientB);
            await seed.SaveChangesAsync(Ct);
        }

        await using var ctx = NewContext(db);
        var handler = new ListOtRevocationRequestsHandler(
            Repo(ctx, null), new ProcedureRevocationRequestRepository(ctx), new FakeScope([ClientA]));

        var result = await handler.HandleAsync(
            new ListOtRevocationRequestsQuery(OtTenant, null, null, null, null, null, null), Ct);

        result.Items.Should().ContainSingle().Which.ProcedureInstanceId.Should().Be(ProcA);
    }

    // ── Resolver (usuario del JWT) ──────────────────────────────────────────────────

    [Fact]
    public async Task Resolver_SuperAdminYProcesosSinUsuarioNoSeRestringen()
    {
        var db = SeedDb(scopeFor: UserX, scopeClients: [ClientA]);
        await using var ctx = NewContext(db);

        (await Resolver(ctx, null).GetRestrictionAsync(Office, Ct)).Should().BeNull("sin HTTP");
        (await Resolver(ctx, Principal(UserX, "SuperAdmin")).GetRestrictionAsync(Office, Ct)).Should().BeNull("Super Admin actúa sobre el OT");
    }

    [Fact]
    public async Task Resolver_UsuarioConFilaEspecificos_DevuelveSusCompanias_YElDemasTodos()
    {
        var db = SeedDb(scopeFor: UserX, scopeClients: [ClientA]);
        await using var ctx = NewContext(db);

        (await Resolver(ctx, Principal(UserX, "ot_gestor")).GetRestrictionAsync(Office, Ct)).Should().BeEquivalentTo([ClientA]);
        (await Resolver(ctx, Principal(AdminUser, "ot_admin")).GetRestrictionAsync(Office, Ct)).Should().BeNull("sin fila = todos");
        (await Resolver(ctx, Principal(UserX, "ot_gestor")).GetRestrictionAsync(Guid.NewGuid(), Ct)).Should().BeNull("otro organismo");
    }

    [Fact]
    public async Task Resolver_AlcanceHuerfanoFallaCerrado_VeNada()
    {
        var db = SeedDb();
        await using (var seed = NewContext(db))
        {
            seed.OtUserClientScopes.Add(new OtUserClientScopeEntity
            {
                Id = Guid.NewGuid(), TenantId = OtTenant, UserId = UserX, TransitOfficeId = Office,
                Mode = OtUserClientScopeModes.Especificos, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(Ct);
        }

        await using var ctx = NewContext(db);
        var restriction = await Resolver(ctx, Principal(UserX, "ot_gestor")).GetRestrictionAsync(Office, Ct);
        restriction.Should().NotBeNull().And.BeEmpty();
        (await Repo(ctx, new FakeScope(restriction)).ListAsync(OtTenant, new OtClientProcedureFilter(), cancellationToken: Ct))
            .Items.Should().BeEmpty();
    }

    // ── Mantenimiento: reglas del caso de uso ───────────────────────────────────────

    [Fact]
    public async Task Set_NadieCambiaSuPropioAlcance()
    {
        var db = SeedDb(usuarios: true);
        await using var ctx = NewContext(db);

        var status = await SetHandler(ctx).HandleAsync(
            new SetOtUserClientScopeCommand(OtTenant, null, UserX, UserX, OtClientScopeModes.Especificos, [ClientA]), Ct);

        status.Should().Be(SetOtUserClientScopeStatus.Self);
    }

    [Theory]
    [InlineData("especificos", 0, SetOtUserClientScopeStatus.Empty)]
    [InlineData("otro", 1, SetOtUserClientScopeStatus.InvalidMode)]
    public async Task Set_ValidaModoYMinimoUnaCompania(string mode, int cuantos, SetOtUserClientScopeStatus esperado)
    {
        var db = SeedDb(usuarios: true);
        await using var ctx = NewContext(db);

        var status = await SetHandler(ctx).HandleAsync(
            new SetOtUserClientScopeCommand(OtTenant, null, UserX, AdminUser, mode, cuantos == 0 ? [] : [ClientA]), Ct);

        status.Should().Be(esperado);
    }

    [Fact]
    public async Task Set_RechazaUnaCompaniaQueElOtNoVe_YUnUsuarioAjeno()
    {
        var db = SeedDb(usuarios: true);
        await using var ctx = NewContext(db);
        var handler = SetHandler(ctx);

        (await handler.HandleAsync(
            new SetOtUserClientScopeCommand(OtTenant, null, UserX, AdminUser, OtClientScopeModes.Especificos, [Guid.NewGuid()]), Ct))
            .Should().Be(SetOtUserClientScopeStatus.InvalidClient);
        (await handler.HandleAsync(
            new SetOtUserClientScopeCommand(OtTenant, null, Guid.NewGuid(), AdminUser, OtClientScopeModes.Todos, []), Ct))
            .Should().Be(SetOtUserClientScopeStatus.UserNotFound);
    }

    [Fact]
    public async Task Set_GuardaEspecificos_LeeElDetalle_AuditaYTodosBorraLaFila()
    {
        var db = SeedDb(usuarios: true);

        await using (var ctx = NewContext(db))
        {
            (await SetHandler(ctx).HandleAsync(
                new SetOtUserClientScopeCommand(OtTenant, null, UserX, AdminUser, OtClientScopeModes.Especificos, [ClientA]), Ct))
                .Should().Be(SetOtUserClientScopeStatus.Ok);
        }

        await using (var ctx = NewContext(db))
        {
            var repo = new OtUserClientScopeRepository(ctx);
            var view = await new GetOtUserClientScopeHandler(repo).HandleAsync(OtTenant, UserX, Ct);
            view!.Mode.Should().Be(OtClientScopeModes.Especificos);
            view.Clients.Select(c => c.TenantId).Should().BeEquivalentTo([ClientA]);
            (await repo.GetSummariesAsync(OtTenant, Ct))[UserX].ClientCount.Should().Be(1);

            var audit = await ctx.UserPermissionAudits.AsNoTracking().SingleAsync(Ct);
            audit.Action.Should().Be(OtUserClientScopeRepository.AuditAction);
            audit.ActorUserId.Should().Be(AdminUser);
            audit.TargetUserId.Should().Be(UserX);
            audit.TenantId.Should().Be(OtTenant);
            audit.ReasonCode.Should().Be("todos>especificos:1");
        }

        await using (var ctx = NewContext(db))
        {
            (await SetHandler(ctx).HandleAsync(
                new SetOtUserClientScopeCommand(OtTenant, null, UserX, AdminUser, OtClientScopeModes.Todos, [ClientB]), Ct))
                .Should().Be(SetOtUserClientScopeStatus.Ok);
        }

        await using var verify = NewContext(db);
        (await verify.OtUserClientScopes.CountAsync(Ct)).Should().Be(0, "todos = ausencia de fila");
        (await verify.OtUserClientScopeClients.CountAsync(Ct)).Should().Be(0);
        (await verify.UserPermissionAudits.OrderBy(a => a.CreatedAt).Select(a => a.ReasonCode).ToListAsync(Ct))
            .Should().Equal("todos>especificos:1", "especificos>todos:0");
    }

    [Fact]
    public async Task Set_ReemplazaLaListaSinDuplicar()
    {
        var db = SeedDb(usuarios: true, scopeFor: UserX, scopeClients: [ClientA]);

        await using (var ctx = NewContext(db))
        {
            await SetHandler(ctx).HandleAsync(
                new SetOtUserClientScopeCommand(OtTenant, null, UserX, AdminUser, OtClientScopeModes.Especificos, [ClientB]), Ct);
        }

        await using var verify = NewContext(db);
        (await verify.OtUserClientScopeClients.Select(c => c.ClientTenantId).ToListAsync(Ct)).Should().BeEquivalentTo([ClientB]);
    }

    [Fact]
    public async Task ListaDeCompaniasCliente_SonLasVisiblesDelOt()
    {
        var db = SeedDb();
        await using var ctx = NewContext(db);

        var items = await new OtUserClientScopeRepository(ctx).ListClientCompaniesAsync(OtTenant, null, Ct);

        items.Select(i => i.TenantId).Should().BeEquivalentTo([ClientA, ClientB]);
        items.Single(i => i.TenantId == ClientA).Nit.Should().Be("900000001");
    }

    // ── Invitaciones ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invitacion_ValidaElAlcanceYLoGuardaLigadoALaInvitacion()
    {
        var db = SeedDb();
        var invitationId = Guid.NewGuid();
        await using (var ctx = NewContext(db))
        {
            var validator = new ValidateOtInvitationClientScopeHandler(new OtUserClientScopeRepository(ctx));

            (await validator.HandleAsync(OtTenant, null, "especificos", [], Ct)).Status.Should().Be(SetOtUserClientScopeStatus.Empty);
            (await validator.HandleAsync(OtTenant, null, "especificos", [Guid.NewGuid()], Ct)).Status.Should().Be(SetOtUserClientScopeStatus.InvalidClient);
            (await validator.HandleAsync(OtTenant, null, "todos", [ClientA], Ct)).ClientTenantIds.Should().BeEmpty();

            var (status, ids) = await validator.HandleAsync(OtTenant, null, "especificos", [ClientA, ClientA], Ct);
            status.Should().Be(SetOtUserClientScopeStatus.Ok);
            ids.Should().BeEquivalentTo([ClientA]);

            ctx.UserInvitations.Add(new Flit.Infrastructure.Persistence.Entities.Security.UserInvitation
            {
                Id = invitationId, TenantId = OtTenant, Email = "x@x.test", FullName = "X", TokenHash = "h", InvitedBy = AdminUser,
            });
            await ctx.SaveChangesAsync(Ct);
            await new OtUserClientScopeRepository(ctx).SetInvitationScopeAsync(invitationId, ids, AdminUser, Ct);
        }

        await using var verify = NewContext(db);
        (await new OtUserClientScopeRepository(verify).GetInvitationSummariesAsync(OtTenant, Ct))[invitationId]
            .ClientCount.Should().Be(1);
    }

    [Fact]
    public async Task Activacion_MaterializaElAlcanceDeLaInvitacionParaElUsuarioNuevo()
    {
        var db = SeedDb();
        var invitationId = Guid.NewGuid();
        var newUser = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            seed.OtInvitationClientScopeClients.Add(new OtInvitationClientScopeClientEntity
            {
                InvitationId = invitationId, ClientTenantId = ClientA, CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext(db))
        {
            var data = new ActivationData(invitationId, "n@n.test", "N", "hash", OtTenant, [Guid.NewGuid()], AdminUser, DateTimeOffset.UtcNow);
            await new OtInvitationClientScopeActivationHook(ctx).OnUserCreatedAsync(data, newUser, Ct);
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctx2 = NewContext(db);
        var restriction = await Resolver(ctx2, Principal(newUser, "ot_gestor")).GetRestrictionAsync(Office, Ct);
        restriction.Should().BeEquivalentTo([ClientA]);
    }

    [Fact]
    public async Task Activacion_SinAlcanceEnLaInvitacion_NoCreaNada()
    {
        var db = SeedDb();
        await using var ctx = NewContext(db);
        var data = new ActivationData(Guid.NewGuid(), "n@n.test", "N", "hash", OtTenant, [Guid.NewGuid()], AdminUser, DateTimeOffset.UtcNow);

        await new OtInvitationClientScopeActivationHook(ctx).OnUserCreatedAsync(data, Guid.NewGuid(), Ct);
        await ctx.SaveChangesAsync(Ct);

        (await ctx.OtUserClientScopes.CountAsync(Ct)).Should().Be(0);
    }

    // ── Soporte ─────────────────────────────────────────────────────────────────────

    private static OtClientProcedureRepository Repo(FlitDbContext ctx, IOtUserClientScopeResolver? scope) =>
        new(ctx, new NullTramiteTransitionPublisher(), clientScope: scope);

    private static SetOtUserClientScopeHandler SetHandler(FlitDbContext ctx) =>
        new(new OtUserClientScopeRepository(ctx));

    private static HttpOtUserClientScopeResolver Resolver(FlitDbContext ctx, ClaimsPrincipal? user) =>
        new(ctx, new HttpContextAccessor { HttpContext = user is null ? null : new DefaultHttpContext { User = user } });

    private static ClaimsPrincipal Principal(Guid userId, string role) =>
        new(new ClaimsIdentity(
            [new Claim("sub", userId.ToString()), new Claim(ClaimTypes.Role, role)],
            authenticationType: "test"));

    private static void AddRevocation(FlitDbContext ctx, Guid procedureId, Guid tenantId) =>
        ctx.ProcedureRevocationRequests.Add(new ProcedureRevocationRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = procedureId,
            AttemptNumber = 1,
            Status = ProcedureRevocationRequestStatus.Solicitada,
            Reason = "El trámite se aprobó con datos incorrectos.",
            RequestedBy = UserX,
            RequestedAt = DateTimeOffset.UtcNow,
        });

    /// <summary>OT con dos compañías cliente (A y B) con convenio y un trámite entregado de cada una.</summary>
    private static string SeedDb(
        bool atrasados = false,
        bool usuarios = false,
        Guid? scopeFor = null,
        Guid[]? scopeClients = null)
    {
        var db = Guid.NewGuid().ToString();
        using var ctx = NewContext(db);

        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(), TenantId = OtTenant, TransitOfficeId = Office, OperationMode = "dashboard", CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = ProcType, Code = "matricula_inicial", Name = "Matrícula inicial", Family = "MATRICULAS", IsActive = true,
            PublicationStatus = PublicationStatus.Published, CreatedAt = DateTimeOffset.UtcNow,
        });

        foreach (var (id, code, nit, procId, reference) in new[]
                 {
                     (ClientA, "cli-a", "900000001", ProcA, "R-A"),
                     (ClientB, "cli-b", "900000002", ProcB, "R-B"),
                 })
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = id, Code = code, LegalName = $"Compañía {code}", TaxId = nit, TenantType = "client", CreatedAt = DateTimeOffset.UtcNow,
            });
            ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(), TenantId = id, TransitOfficeId = Office, IsEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
            });
            ctx.ProcedureInstances.Add(new ProcedureInstance
            {
                Id = procId, TenantId = id, ProcedureTypeId = ProcType, ReferenceNumber = reference, Status = TramiteEstado.Entregado,
                TransitOfficeId = Office, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow,
            });
            if (atrasados)
            {
                ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(), TenantId = id, ProcedureInstanceId = procId, FromStatus = TramiteEstado.Preparado,
                    ToStatus = TramiteEstado.Entregado, ChangedAt = DateTimeOffset.UtcNow.AddDays(-9),
                });
            }
        }

        if (usuarios)
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = OtTenant, Code = "ot", LegalName = "OT", TaxId = "800000000", TenantType = "TRANSIT_OFFICE", CreatedAt = DateTimeOffset.UtcNow,
            });
            foreach (var u in new[] { UserX, AdminUser })
            {
                ctx.Users.Add(new User
                {
                    Id = u, Email = $"{u}@ot.test", DisplayName = "U", Status = "active", HomeTenantId = OtTenant, CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        if (scopeFor is { } user)
        {
            var scopeId = Guid.NewGuid();
            ctx.OtUserClientScopes.Add(new OtUserClientScopeEntity
            {
                Id = scopeId, TenantId = OtTenant, UserId = user, TransitOfficeId = Office, Mode = OtUserClientScopeModes.Especificos,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                Clients = [.. (scopeClients ?? []).Select(c => new OtUserClientScopeClientEntity
                {
                    ScopeId = scopeId, ClientTenantId = c, CreatedAt = DateTimeOffset.UtcNow,
                })],
            });
        }

        ctx.SaveChanges();
        return db;
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
