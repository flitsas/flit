using Flit.Admin.Application.Companies.MandateSigners.AssociableCompanies;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// Alcance de compañías cliente por usuario del OT aplicado a los MANDATARIOS (política 3). Un usuario con alcance
/// <c>especificos</c> solo vincula compañías de su alcance (propietarias y asociadas): fuera de él, 403
/// (<see cref="OtClientScopeExceedsActorException"/>) y nada se guarda. Al editar solo se valida lo que AGREGA; lo que el
/// mandatario ya tenía fuera de su alcance (que el actor no ve) se conserva en vez de borrarse por omisión. El directorio de
/// compañías asociables se interseca con su alcance.
///
/// <para>Uso de ejemplo: con <c>FakeScope([Gestora, Hija1])</c>, crear un mandatario con asociada <c>Hija2</c> lanza
/// <see cref="OtClientScopeExceedsActorException"/>; con <c>FakeScope(null)</c> (sin restricción) se guarda como siempre.</para>
/// </summary>
public sealed class MandatarioAlcanceClientesTests
{
    private static readonly Guid Ot = Guid.Parse("eeeeeeee-0000-4000-8000-000000000101");
    private static readonly Guid OtTenant = Guid.Parse("ffffffff-0000-4000-8000-000000000101");
    private static readonly Guid Gestora = Guid.Parse("dddddddd-0000-4000-8000-000000000101");
    private static readonly Guid Hija1 = Guid.Parse("dddddddd-0000-4000-8000-000000000111");
    private static readonly Guid Hija2 = Guid.Parse("dddddddd-0000-4000-8000-000000000112");
    private static readonly Guid Hija3 = Guid.Parse("dddddddd-0000-4000-8000-000000000113");
    private static readonly Guid Ajena = Guid.Parse("dddddddd-0000-4000-8000-000000000121");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeScope(IReadOnlyList<Guid>? restriction) : IOtUserClientScopeResolver
    {
        public Task<IReadOnlyList<Guid>?> GetRestrictionAsync(Guid transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(restriction);

        public Task<IReadOnlyList<Guid>?> GetRestrictionForUserAsync(Guid userId, Guid transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(restriction);
    }

    // ── Alta ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Alta_UnaPropietariaFueraDelAlcance_Es403_YNoSeGuardaNada()
    {
        await using var ctx = await SeedAsync();

        var act = () => Create(ctx, new FakeScope([Gestora])).HandleAsync(Alta(owners: [Hija2]), Ct);

        await act.Should().ThrowAsync<OtClientScopeExceedsActorException>();
        (await ctx.MandateSigners.CountAsync(Ct)).Should().Be(0);
        (await ctx.MandateSignerCompanies.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Alta_UnaAsociadaFueraDelAlcance_Es403_YNoSeGuardaNada()
    {
        await using var ctx = await SeedAsync();

        var act = () => Create(ctx, new FakeScope([Gestora, Hija1]))
            .HandleAsync(Alta(owners: [Gestora], associated: [Hija1, Hija2]), Ct);

        await act.Should().ThrowAsync<OtClientScopeExceedsActorException>();
        (await ctx.MandateSigners.CountAsync(Ct)).Should().Be(0);
        (await ctx.MandateSignerAssociatedCompanies.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Alta_TodoDentroDelAlcance_SeGuarda()
    {
        await using var ctx = await SeedAsync();

        var result = await Create(ctx, new FakeScope([Gestora, Hija1]))
            .HandleAsync(Alta(owners: [Gestora], associated: [Hija1]), Ct);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        (await ctx.MandateSignerAssociatedCompanies.AsNoTracking().Select(a => a.AssociatedCompanyTenantId).ToListAsync(Ct))
            .Should().BeEquivalentTo([Hija1]);
    }

    [Theory]
    [InlineData(false)] // sin resolver
    [InlineData(true)] // resolver sin restricción (Admin OT sin alcance / Super Admin)
    public async Task Alta_SinRestriccion_NadaCambia(bool conResolver)
    {
        await using var ctx = await SeedAsync();

        var result = await Create(ctx, conResolver ? new FakeScope(null) : null)
            .HandleAsync(Alta(owners: [Gestora], associated: [Hija1, Hija2]), Ct);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        (await ctx.MandateSignerAssociatedCompanies.CountAsync(Ct)).Should().Be(2);
    }

    [Fact]
    public async Task Alta_ConAlcanceVacio_NoPuedeVincularNingunaCompania()
    {
        await using var ctx = await SeedAsync();

        var act = () => Create(ctx, new FakeScope([])).HandleAsync(Alta(owners: [Gestora]), Ct);

        await act.Should().ThrowAsync<OtClientScopeExceedsActorException>();
    }

    // ── Edición: valida solo lo que agrega y conserva lo que no ve ─────────────────────────────────────

    [Fact]
    public async Task Edicion_AgregarUnaAsociadaFueraDelAlcance_Es403_YNoSeTocaNada()
    {
        await using var ctx = await SeedAsync();
        var id = await SeedSignerAsync(ctx, owners: [Gestora], associated: [Hija1]);

        var act = () => Update(ctx, new FakeScope([Gestora, Hija1]))
            .HandleAsync(Editar(id, owners: [Gestora], associated: [Hija1, Hija3]), Ct);

        await act.Should().ThrowAsync<OtClientScopeExceedsActorException>();
        (await ActiveAssociatedAsync(ctx)).Should().BeEquivalentTo([Hija1]);
    }

    [Fact]
    public async Task Edicion_ConservaLasAsociadasQueElActorNoVe_AunQueNoVengan()
    {
        await using var ctx = await SeedAsync();
        // Hija2 es una asociación ya existente fuera del alcance del actor ([Gestora, Hija1]): él no la ve, no viene en su cuerpo.
        var id = await SeedSignerAsync(ctx, owners: [Gestora], associated: [Hija1, Hija2]);

        var result = await Update(ctx, new FakeScope([Gestora, Hija1]))
            .HandleAsync(Editar(id, owners: [Gestora], associated: [Hija1]), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated, string.Join("; ", result.Errors.Select(e => e.Message)));
        (await ActiveAssociatedAsync(ctx)).Should().BeEquivalentTo([Hija1, Hija2], "Hija2 no se borra por no verla");
    }

    [Fact]
    public async Task Edicion_QuitarLasQueSiVe_NoArrastraLasQueNoVe()
    {
        await using var ctx = await SeedAsync();
        var id = await SeedSignerAsync(ctx, owners: [Gestora], associated: [Hija1, Hija2]);

        var result = await Update(ctx, new FakeScope([Gestora, Hija1]))
            .HandleAsync(Editar(id, owners: [Gestora], associated: []), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        (await ActiveAssociatedAsync(ctx)).Should().BeEquivalentTo([Hija2], "solo se retiró la que el actor veía");
    }

    [Fact]
    public async Task Edicion_ConservaLasPropietariasFueraDelAlcance_YNoLasRevalida()
    {
        await using var ctx = await SeedAsync();
        var id = await SeedSignerAsync(ctx, owners: [Gestora, Ajena], associated: []);

        var result = await Update(ctx, new FakeScope([Gestora]))
            .HandleAsync(Editar(id, owners: [Gestora], associated: null), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated, string.Join("; ", result.Errors.Select(e => e.Message)));
        (await ctx.MandateSignerCompanies.AsNoTracking().Where(c => c.IsActive).Select(c => c.CompanyTenantId).ToListAsync(Ct))
            .Should().BeEquivalentTo([Gestora, Ajena]);
    }

    [Fact]
    public async Task Edicion_AgregarUnaPropietariaFueraDelAlcance_Es403()
    {
        await using var ctx = await SeedAsync();
        var id = await SeedSignerAsync(ctx, owners: [Gestora], associated: []);

        var act = () => Update(ctx, new FakeScope([Gestora]))
            .HandleAsync(Editar(id, owners: [Gestora, Ajena], associated: null), Ct);

        await act.Should().ThrowAsync<OtClientScopeExceedsActorException>();
        (await ctx.MandateSignerCompanies.AsNoTracking().Where(c => c.IsActive).Select(c => c.CompanyTenantId).ToListAsync(Ct))
            .Should().BeEquivalentTo([Gestora]);
    }

    [Fact]
    public async Task Edicion_SinRestriccion_ReemplazaComoSiempre()
    {
        await using var ctx = await SeedAsync();
        var id = await SeedSignerAsync(ctx, owners: [Gestora], associated: [Hija1, Hija2]);

        var result = await Update(ctx, new FakeScope(null))
            .HandleAsync(Editar(id, owners: [Gestora], associated: [Hija3]), Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated, string.Join("; ", result.Errors.Select(e => e.Message)));
        (await ActiveAssociatedAsync(ctx)).Should().BeEquivalentTo([Hija3]);
    }

    // ── Lectura: el listado no muestra las asociadas fuera del alcance ───────────────────────────────

    [Fact]
    public async Task Listado_ConAlcance_OcultaLasAsociadasFueraDelAlcance()
    {
        await using var ctx = await SeedAsync();
        await SeedSignerAsync(ctx, owners: [], associated: [Hija1, Hija2]);

        var reader = new DbMandateSignerReader(ctx, clientScope: new FakeScope([Gestora, Hija1]));
        var items = await reader.ListByOtAsync(Ot, OtCompanyVisibility.DirectOrWithReceivedProcedures, Ct);

        items.Should().ContainSingle();
        items[0].OfficeCompanies.SelectMany(o => o.AssociatedCompanyTenantIds).Should().BeEquivalentTo([Hija1]);
    }

    // ── Directorio de compañías asociables ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DirectorioAsociables_SeIntersecaConElAlcance()
    {
        await using var ctx = await SeedAsync();
        var associable = Associable(ctx);

        var restringido = await associable.ListForOtAsync(null, 1, 100, [Hija1, Hija3], Ct);
        var sinRestriccion = await associable.ListForOtAsync(null, 1, 100, null, Ct);
        var vacio = await associable.ListForOtAsync(null, 1, 100, [], Ct);

        restringido.Page!.Items.Select(i => i.Id).Should().BeEquivalentTo([Hija1, Hija3]);
        sinRestriccion.Page!.Items.Select(i => i.Id).Should().Contain([Gestora, Hija1, Hija2, Hija3, Ajena]);
        vacio.Page!.Items.Should().BeEmpty("alcance huérfano: ve nada");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static MandatarioAssociableCompanies Associable(FlitDbContext ctx) =>
        new(new ManagingCompanyDirectory(ctx), new CompanyHierarchyRepository(ctx));

    private static CreateMandateSignerHandler Create(FlitDbContext ctx, IOtUserClientScopeResolver? scope) =>
        new(OtOperable(), new DbMandateSignerReader(ctx), new MandateSignerRepository(ctx), null, null, Associable(ctx), scope);

    private static UpdateMandateSignerHandler Update(FlitDbContext ctx, IOtUserClientScopeResolver? scope) =>
        new(OtOperable(), new DbMandateSignerReader(ctx), new MandateSignerRepository(ctx), Associable(ctx), null, null, scope);

    private static CreateMandateSignerCommand Alta(
        IReadOnlyList<Guid> owners, IReadOnlyList<Guid>? associated = null, string documento = "1020304050") =>
        new()
        {
            TransitOfficeId = Ot,
            FullName = "Ana Restrepo",
            DocumentNumber = documento,
            CompanyTenantIds = owners,
            Email = "ana@x.com",
            SignatureMethod = "biometria",
            OfficeCompanies = associated is null ? null : [new MandateSignerOfficeCompanies(Ot, associated)],
            ConfiguredByScope = "organismo",
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

    private static UpdateMandateSignerCommand Editar(
        Guid id, IReadOnlyList<Guid> owners, IReadOnlyList<Guid>? associated) =>
        new()
        {
            TransitOfficeId = Ot,
            MandateSignerId = id,
            FullName = "Ana Restrepo",
            DocumentNumber = "1020304050",
            CompanyTenantIds = owners,
            Email = "ana@x.com",
            SignatureMethod = "biometria",
            OfficeCompanies = associated is null ? null : [new MandateSignerOfficeCompanies(Ot, associated)],
            ConfiguredByScope = "organismo",
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

    /// <summary>Mandatario preexistente creado SIN restricción (como lo haría un Admin OT sin alcance).</summary>
    private static async Task<Guid> SeedSignerAsync(
        FlitDbContext ctx, IReadOnlyList<Guid> owners, IReadOnlyList<Guid> associated)
    {
        var result = await Create(ctx, new FakeScope(null)).HandleAsync(Alta(owners, associated), Ct);
        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        return result.MandateSignerId!.Value;
    }

    private static async Task<List<Guid>> ActiveAssociatedAsync(FlitDbContext ctx) =>
        await ctx.MandateSignerAssociatedCompanies.AsNoTracking()
            .Where(a => a.IsActive).Select(a => a.AssociatedCompanyTenantId).ToListAsync(Ct);

    private static ITransitOfficeOperationalStatusReader OtOperable()
    {
        var reader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        reader.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new TransitOfficeOperationalStatusItem
            {
                Id = Ot, Code = "05001000", Name = "OT de prueba", HasTenant = true, TenantId = OtTenant, EstadoActivo = true,
            });
        return reader;
    }

    private static Tenant NewTenant(Guid id, string nit, Guid? parent = null) => new()
    {
        Id = id,
        Code = "T-" + id.ToString("N")[^8..],
        LegalName = "Compañía " + id.ToString("N")[^4..],
        TaxId = nit,
        TenantType = "CONCESIONARIO",
        IsActive = true,
        ParentTenantId = parent,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<FlitDbContext> SeedAsync()
    {
        var ctx = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-alcance-mandatarios-{Guid.NewGuid()}")
            .Options);
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Ot, Code = "05001000", Name = "OT de prueba", DepartmentCode = "05", CityCode = "05001", IsActive = true,
        });
        Guid[] tenants = [Gestora, Hija1, Hija2, Hija3, Ajena];
        ctx.Tenants.AddRange(
            NewTenant(Gestora, "900000101-1"),
            NewTenant(Hija1, "900000111-1", Gestora),
            NewTenant(Hija2, "900000112-1", Gestora),
            NewTenant(Hija3, "900000113-1", Gestora),
            NewTenant(Ajena, "900000121-1"));
        foreach (var tenant in tenants)
        {
            ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(), TenantId = tenant, TransitOfficeId = Ot, IsEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await ctx.SaveChangesAsync(Ct);
        return ctx;
    }
}
