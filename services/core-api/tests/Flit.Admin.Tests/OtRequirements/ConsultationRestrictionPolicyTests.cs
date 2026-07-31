using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtRequirements;

/// <summary>
/// Tests de la política de restricciones de consulta por OT (HU #10760): resuelve el OT destino y
/// traduce los kinds inhabilitados de <c>admin.tenant_transit_office_consultation_restrictions</c>
/// al vocabulario de Trámites, con default permisivo (tabla dispersa).
///
/// Se ejercita contra el repositorio REAL (proveedor InMemory) y no contra un doble: el filtrado por
/// tenant/OT/enabled vive en la query del repositorio, así que un stub no probaría nada de lo que
/// esta política depende.
/// </summary>
public sealed class ConsultationRestrictionPolicyTests
{
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherTenant = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Office = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact] // AC1 — fila enabled=false → el kind queda inhabilitado.
    public async Task KindInhabilitado_QuedaEnDisabledKinds()
    {
        var db = NewDbName();
        await Seed(db, ClientTenant, Office, RestrictedConsultationKinds.Rnmc, enabled: false);

        var restrictions = await GetAsync(db, ClientTenant, Office);

        restrictions.IsDisabled(ConsultationRestrictionKinds.Rnmc).Should().BeTrue();
        restrictions.IsDisabled(ConsultationRestrictionKinds.Fines).Should().BeFalse();
    }

    [Fact] // AC1 — fila enabled=true (flip de vuelta) → NO restringe.
    public async Task KindHabilitado_NoRestringe()
    {
        var db = NewDbName();
        await Seed(db, ClientTenant, Office, RestrictedConsultationKinds.Fines, enabled: true);

        var restrictions = await GetAsync(db, ClientTenant, Office);

        restrictions.IsDisabled(ConsultationRestrictionKinds.Fines).Should().BeFalse();
        restrictions.DisabledKinds.Should().BeEmpty();
    }

    [Fact] // AC1 — tabla dispersa: sin filas, nada restringido (permisivo).
    public async Task SinFilas_DevuelveNone()
    {
        var restrictions = await GetAsync(NewDbName(), ClientTenant, Office);

        restrictions.DisabledKinds.Should().BeEmpty();
    }

    [Fact] // AC1 — ambos kinds inhabilitados a la vez.
    public async Task AmbosKindsInhabilitados_QuedanLosDos()
    {
        var db = NewDbName();
        await Seed(db, ClientTenant, Office, RestrictedConsultationKinds.Rnmc, enabled: false);
        await Seed(db, ClientTenant, Office, RestrictedConsultationKinds.Fines, enabled: false);

        var restrictions = await GetAsync(db, ClientTenant, Office);

        restrictions.IsDisabled(ConsultationRestrictionKinds.Rnmc).Should().BeTrue();
        restrictions.IsDisabled(ConsultationRestrictionKinds.Fines).Should().BeTrue();
    }

    [Fact] // AC5 — aislamiento: la restricción de otra compañía no filtra.
    public async Task RestriccionDeOtroTenant_NoAplica()
    {
        var db = NewDbName();
        await Seed(db, OtherTenant, Office, RestrictedConsultationKinds.Rnmc, enabled: false);

        var restrictions = await GetAsync(db, ClientTenant, Office);

        restrictions.DisabledKinds.Should().BeEmpty();
    }

    [Fact] // La restricción es del par (tenant, OT): otro OT del mismo tenant no se ve afectado.
    public async Task RestriccionDeOtroOt_NoAplica()
    {
        var db = NewDbName();
        var otroOt = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        await Seed(db, ClientTenant, otroOt, RestrictedConsultationKinds.Rnmc, enabled: false);

        var restrictions = await GetAsync(db, ClientTenant, Office);

        restrictions.DisabledKinds.Should().BeEmpty();
    }

    [Fact] // Sin OT explícito, resuelve el único grant vigente de la empresa.
    public async Task SinOt_ConUnSoloGrant_ResuelvePorEseGrant()
    {
        var db = NewDbName();
        await Seed(db, ClientTenant, Office, RestrictedConsultationKinds.Fines, enabled: false);

        var restrictions = await GetAsync(db, ClientTenant, transitOfficeId: null, grants: [Office]);

        restrictions.IsDisabled(ConsultationRestrictionKinds.Fines).Should().BeTrue();
    }

    [Fact] // Sin OT y sin grants → no hay par al que aplicar política: permisivo.
    public async Task SinOt_SinGrants_DevuelveNone()
    {
        var db = NewDbName();
        await Seed(db, ClientTenant, Office, RestrictedConsultationKinds.Fines, enabled: false);

        var restrictions = await GetAsync(db, ClientTenant, transitOfficeId: null, grants: []);

        restrictions.DisabledKinds.Should().BeEmpty();
    }

    [Fact] // Sin OT y con ≥2 grants el destino es ambiguo → permisivo (no se adivina).
    public async Task SinOt_ConVariosGrants_DevuelveNone()
    {
        var db = NewDbName();
        var otroOt = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        await Seed(db, ClientTenant, Office, RestrictedConsultationKinds.Fines, enabled: false);

        var restrictions = await GetAsync(db, ClientTenant, transitOfficeId: null, grants: [Office, otroOt]);

        restrictions.DisabledKinds.Should().BeEmpty();
    }

    private static async Task<ConsultationRestrictions> GetAsync(
        string db,
        Guid tenantId,
        Guid? transitOfficeId,
        Guid[]? grants = null)
    {
        await using var ctx = NewContext(db);
        var policy = new ConsultationRestrictionPolicy(
            new OtConsultationRestrictionRepository(ctx, NullAuditContextAccessor.Instance),
            new StubGrants(grants ?? []));

        return await policy.GetAsync(tenantId, transitOfficeId, TestContext.Current.CancellationToken);
    }

    private static async Task Seed(string db, Guid tenantId, Guid officeId, string kind, bool enabled)
    {
        await using var ctx = NewContext(db);
        await new OtConsultationRestrictionRepository(ctx, NullAuditContextAccessor.Instance)
            .SetAsync(tenantId, officeId, kind, enabled, changedBy: null, correlationId: null,
                TestContext.Current.CancellationToken);
    }

    private static string NewDbName() => $"flit-consultation-restriction-policy-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    /// <summary>Stub de grants: devuelve los ids configurados como habilitados.</summary>
    private sealed class StubGrants(params Guid[] offices) : ITransitGrantRepository
    {
        public Task<IReadOnlyList<Guid>> ListEnabledOfficeIdsAsync(
            Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(offices);

        public Task<bool> AddGrantAsync(
            Guid tenantId, Guid transitOfficeId, Guid? createdBy, Guid? correlationId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> RemoveGrantAsync(
            Guid tenantId, Guid transitOfficeId, Guid? changedBy, Guid? correlationId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
