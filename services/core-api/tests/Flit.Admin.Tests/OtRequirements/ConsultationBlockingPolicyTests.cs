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
/// Tests de la política de bloqueo de preflight por OT (FEATURE 05): resuelve el OT destino y traduce
/// los overrides de <c>admin.tenant_transit_office_blocking_policies</c> al vocabulario de Trámites,
/// con default POR CRITERIO cuando no hay fila (tabla dispersa).
///
/// Se ejercita contra el repositorio REAL (proveedor InMemory) y no contra un doble.
/// </summary>
public sealed class ConsultationBlockingPolicyTests
{
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherTenant = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Office = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact] // Sin filas: cada criterio aplica su default (preserva el comportamiento previo).
    public async Task SinFilas_AplicaDefaultsPorCriterio()
    {
        var rules = await GetAsync(NewDbName(), ClientTenant, Office);

        rules.Blocks(ConsultationBlockingCriteria.Soat).Should().BeTrue();
        rules.Blocks(ConsultationBlockingCriteria.Rtm).Should().BeTrue();
        rules.Blocks(ConsultationBlockingCriteria.EstadoVehiculo).Should().BeTrue();
        rules.Blocks(ConsultationBlockingCriteria.Fines).Should().BeFalse();
        rules.Blocks(ConsultationBlockingCriteria.Rnmc).Should().BeFalse();
    }

    [Fact] // Override blocks=false sobre SOAT → deja de bloquear (solo advierte).
    public async Task OverrideSoatFalse_DejaDeBloquear()
    {
        var db = NewDbName();
        await Seed(db, ClientTenant, Office, BlockingCriteria.Soat, blocks: false);

        var rules = await GetAsync(db, ClientTenant, Office);

        rules.Blocks(ConsultationBlockingCriteria.Soat).Should().BeFalse();
        // Los demás siguen en su default.
        rules.Blocks(ConsultationBlockingCriteria.Rtm).Should().BeTrue();
    }

    [Fact] // Override blocks=true sobre comparendos → pasa a bloquear.
    public async Task OverrideFinesTrue_PasaABloquear()
    {
        var db = NewDbName();
        await Seed(db, ClientTenant, Office, BlockingCriteria.Fines, blocks: true);

        var rules = await GetAsync(db, ClientTenant, Office);

        rules.Blocks(ConsultationBlockingCriteria.Fines).Should().BeTrue();
        rules.Blocks(ConsultationBlockingCriteria.Rnmc).Should().BeFalse();
    }

    [Fact] // Aislamiento: la política de otra compañía no aplica.
    public async Task PoliticaDeOtroTenant_NoAplica()
    {
        var db = NewDbName();
        await Seed(db, OtherTenant, Office, BlockingCriteria.Soat, blocks: false);

        var rules = await GetAsync(db, ClientTenant, Office);

        rules.Blocks(ConsultationBlockingCriteria.Soat).Should().BeTrue(); // default, no el override ajeno
    }

    [Fact] // La política es del par (tenant, OT): otro OT del mismo tenant no se ve afectado.
    public async Task PoliticaDeOtroOt_NoAplica()
    {
        var db = NewDbName();
        var otroOt = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        await Seed(db, ClientTenant, otroOt, BlockingCriteria.Soat, blocks: false);

        var rules = await GetAsync(db, ClientTenant, Office);

        rules.Blocks(ConsultationBlockingCriteria.Soat).Should().BeTrue();
    }

    [Fact] // Sin OT explícito, resuelve el único grant vigente de la empresa.
    public async Task SinOt_ConUnSoloGrant_ResuelvePorEseGrant()
    {
        var db = NewDbName();
        await Seed(db, ClientTenant, Office, BlockingCriteria.EstadoVehiculo, blocks: false);

        var rules = await GetAsync(db, ClientTenant, transitOfficeId: null, grants: [Office]);

        rules.Blocks(ConsultationBlockingCriteria.EstadoVehiculo).Should().BeFalse();
    }

    [Fact] // Sin OT y sin grants → no hay par al que aplicar política: defaults.
    public async Task SinOt_SinGrants_AplicaDefaults()
    {
        var db = NewDbName();
        await Seed(db, ClientTenant, Office, BlockingCriteria.Soat, blocks: false);

        var rules = await GetAsync(db, ClientTenant, transitOfficeId: null, grants: []);

        rules.Blocks(ConsultationBlockingCriteria.Soat).Should().BeTrue();
    }

    private static async Task<ConsultationBlockingRules> GetAsync(
        string db,
        Guid tenantId,
        Guid? transitOfficeId,
        Guid[]? grants = null)
    {
        await using var ctx = NewContext(db);
        var policy = new ConsultationBlockingPolicy(
            new OtBlockingPolicyRepository(ctx, NullAuditContextAccessor.Instance),
            new StubGrants(grants ?? []));

        return await policy.GetAsync(tenantId, transitOfficeId, TestContext.Current.CancellationToken);
    }

    private static async Task Seed(string db, Guid tenantId, Guid officeId, string criterion, bool blocks)
    {
        await using var ctx = NewContext(db);
        await new OtBlockingPolicyRepository(ctx, NullAuditContextAccessor.Instance)
            .SetAsync(tenantId, officeId, criterion, blocks, changedBy: null, correlationId: null,
                TestContext.Current.CancellationToken);
    }

    private static string NewDbName() => $"flit-blocking-policy-{Guid.NewGuid()}";

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
