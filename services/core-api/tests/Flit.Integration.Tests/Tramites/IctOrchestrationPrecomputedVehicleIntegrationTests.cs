using Flit.Api.Grpc;
using Flit.Ict.Grpc.Contracts;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.MarcaBlanca;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #13304 (numeral 3) — el borrador ICT reutiliza la consulta RUNT de la validación ICT
/// (<c>precomputed_vehicle</c>) en vez de re-consultar: con la consulta vigente NO se llama a la cadena
/// de proveedores y el gravamen queda hidratado (<c>runt_tiene_gravamenes</c>); vencida, no se consulta y
/// se avisa con <c>preflight_warning:vehicle_consultation_expired</c>. Contra Postgres REAL porque lo que
/// se verifica es la hidratación persistida de field_values y el snapshot del preflight.
/// <para>Uso de ejemplo: el servicio se activa del contenedor real (<see cref="MarcaBlancaApiFactory"/>)
/// con un <see cref="RunPreflightHandler"/> cuya cadena de vehículo es un contador.</para>
/// </summary>
public sealed class IctOrchestrationPrecomputedVehicleIntegrationTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private static readonly Guid UserId = new("b13304c3-0000-4000-8000-0000000000aa");
    private static readonly Guid SeededInstanceId = new("b13304c3-0000-4000-8000-000000000001");
    private const string Placa = "BCD304";

    [PostgresFact]
    public async Task CreateDraftFromIct_ConConsultaVigente_NoLlamaALaCadenaYDejaElGravamenHidratado()
    {
        await SeedTenantAsync();
        var request = Request("ict-b13304-pre-ok", Precomputed(horasAtras: 1));
        var chain = new CountingChain();

        var reply = await InvokeCreateAsync(request, chain);

        reply.ProcedureInstanceId.Should().NotBeNullOrEmpty();
        (reply.ErrorCode ?? string.Empty).Should().NotContain("vehicle_consultation");
        chain.Calls.Should().Be(0, "el borrador reutiliza la consulta RUNT de ICT");
        var instanceId = Guid.Parse(reply.ProcedureInstanceId);
        (await FieldValueAsync(instanceId, "runt_tiene_gravamenes")).Should().Be("SI");
        (await CountSnapshotsAsync(instanceId)).Should().Be(1);
    }

    [PostgresFact]
    public async Task CreateDraftFromIct_ConConsultaVencida_NoConsultaYDevuelveWarning()
    {
        await SeedTenantAsync();
        var request = Request("ict-b13304-pre-vencida", Precomputed(horasAtras: 48));
        var chain = new CountingChain();

        var reply = await InvokeCreateAsync(request, chain);

        reply.ErrorCode.Should().Contain("preflight_warning:vehicle_consultation_expired");
        chain.Calls.Should().Be(0, "sin consulta vigente no se re-consulta el RUNT");
        var instanceId = Guid.Parse(reply.ProcedureInstanceId);
        (await FieldValueAsync(instanceId, "runt_tiene_gravamenes")).Should().BeNull();
        (await CountSnapshotsAsync(instanceId)).Should().Be(0);
    }

    [PostgresFact]
    public async Task CreateDraftFromIct_ReintentoSinSnapshot_LoCreaConLaConsultaDeIct()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, "ict-b13304-pre-retry");
        var request = Request("ict-b13304-pre-retry", Precomputed(horasAtras: 2));
        var chain = new CountingChain();

        var reply = await InvokeCreateAsync(request, chain);

        reply.ProcedureInstanceId.Should().Be(SeededInstanceId.ToString());
        chain.Calls.Should().Be(0);
        (await CountSnapshotsAsync(SeededInstanceId)).Should().Be(1, "el reintento completa el preflight faltante");
        (await FieldValueAsync(SeededInstanceId, "runt_tiene_gravamenes")).Should().Be("SI");
    }

    [PostgresFact]
    public async Task CreateDraftFromIct_TraspasoQueLevantaConPrendaEnElRunt_RegistraLevantarConElAcreedorDelRunt()
    {
        // Bug #13445 (D3) — traspaso + cuerpo «levantar» + garantía en el RUNT: la decisión se registra sola
        // DESPUÉS del preflight (que es quien deja runt_gravamenes), con el acreedor del RUNT y sin usuario.
        await SeedTenantAsync();
        var precomputed = Precomputed(horasAtras: 1);
        precomputed.SnapshotJson = PreflightVehicleSnapshotJson.Serialize(new PreflightVehicleSnapshot(
            [new PreflightCheckDto("gravamenes", "Gravámenes", "warn", "kyverum_runt", "Prenda vigente")],
            [
                new HydratedField("runt_tiene_gravamenes", "SI", null),
                new HydratedField("runt_tiene_prendas", "SI", null),
                new HydratedField("runt_gravamenes", null,
                    """[{"nombreAcreedor":"BANCO IT 13445","numeroDocumentoAcreedor":"900013445"}]"""),
            ],
            ["kyverum_runt"]));
        var request = Request("ict-b13445-levantar", precomputed);
        request.FieldValues.Add(new FieldValue { FieldKey = "ict_prenda_operacion", ValueText = "1" });
        request.FieldValues.Add(new FieldValue { FieldKey = "cambio_carroceria", ValueText = "true" });

        var reply = await InvokeCreateAsync(request, new CountingChain());

        var instanceId = Guid.Parse(reply.ProcedureInstanceId);
        (reply.ErrorCode ?? string.Empty).Should().NotContain("prenda_");
        await using var ctx = NewContext();
        var prenda = await ctx.Set<ProcedureInstancePrenda>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(p => p.ProcedureInstanceId == instanceId && p.Estado == PrendaEstado.Vigente);
        prenda.Decision.Should().Be(PrendaDecision.Levantar);
        prenda.AcreedorDocumento.Should().Be("900013445");
        prenda.CreatedBy.Should().BeNull();
        (await FieldValueAsync(instanceId, "cambio_carroceria")).Should().Be("true", "la siembra no pisa lo que vino de ICT");
    }

    // ── invocación ───────────────────────────────────────────────────────────

    private async Task<DraftReply> InvokeCreateAsync(CreateDraftFromIctRequest request, CountingChain chain)
    {
        await using var factory = new MarcaBlancaApiFactory(Fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var preflight = ActivatorUtilities.CreateInstance<RunPreflightHandler>(
            scope.ServiceProvider, (IConsultationProviderChainResolver)chain);
        var service = ActivatorUtilities.CreateInstance<IctOrchestrationService>(scope.ServiceProvider, preflight);
        return await service.CreateDraftFromIct(request, new TestCallContext(TestContext.Current.CancellationToken));
    }

    private static PrecomputedVehicleConsultation Precomputed(double horasAtras) => new()
    {
        SnapshotJson = PreflightVehicleSnapshotJson.Serialize(new PreflightVehicleSnapshot(
            [
                new PreflightCheckDto("gravamenes", "Gravámenes", "warn", "kyverum_runt", "Prenda vigente"),
                new PreflightCheckDto("soat", "SOAT", "ok", "kyverum_runt", null),
            ],
            [
                new HydratedField("runt_tiene_gravamenes", "SI", null),
                new HydratedField("vehicle_brand", "MARCA IT", null),
            ],
            ["kyverum_runt"])),
        ConsultedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-horasAtras)),
        Provider = "kyverum_runt",
        Kind = "VehiclePlate",
        QueriedPlate = Placa,
    };

    private static CreateDraftFromIctRequest Request(string externalRef, PrecomputedVehicleConsultation precomputed)
    {
        var request = new CreateDraftFromIctRequest
        {
            TenantId = TenantSeed.LoneId.ToString(),
            ProcedureTypeCode = TramiteTipologiaCatalog.CodigoTraspasoStandard,
            Origin = "ict",
            ExternalRef = externalRef,
            PrecomputedVehicle = precomputed,
        };
        request.FieldValues.Add(new FieldValue { FieldKey = "plate", ValueText = Placa });
        return request;
    }

    // ── datos ────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedTenantAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        await ctx.SaveChangesAsync();
        ctx.Users.Add(new User
        {
            Id = UserId,
            Email = "it-b13304-pre@flit.test",
            DisplayName = "ICT B13304 PRE",
            Status = "active",
            HomeTenantId = TenantSeed.LoneId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();

        return await ctx.ProcedureTypes.AsNoTracking()
            .Where(t => t.Code == TramiteTipologiaCatalog.CodigoTraspasoStandard)
            .Select(t => t.Id)
            .SingleAsync();
    }

    private async Task SeedExistingDraftAsync(Guid typeId, string externalRef)
    {
        await using var ctx = NewContext();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = SeededInstanceId,
            TenantId = TenantSeed.LoneId,
            ProcedureTypeId = typeId,
            CreatedByUserId = UserId,
            Status = TramiteEstado.Borrador,
            Origin = "ict",
            ExternalRef = externalRef,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<string?> FieldValueAsync(Guid instanceId, string key)
    {
        await using var ctx = NewContext();
        return await ctx.Set<ProcedureInstanceFieldValue>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.ProcedureInstanceId == instanceId && f.FieldKey == key)
            .Select(f => f.ValueText)
            .FirstOrDefaultAsync();
    }

    private async Task<int> CountSnapshotsAsync(Guid instanceId)
    {
        await using var ctx = NewContext();
        return await ctx.Set<ProcedureInstancePreflightSnapshot>()
            .IgnoreQueryFilters()
            .CountAsync(s => s.ProcedureInstanceId == instanceId);
    }

    /// <summary>Cadena de vehículo que solo cuenta: si el borrador re-consultara, el contador lo delata.</summary>
    private sealed class CountingChain : IConsultationProviderChainResolver
    {
        public int Calls { get; private set; }

        public IReadOnlyList<string> ResolveChain(ConsultationKind kind, ConsultationTenantOverride? tenantOverride = null) =>
            ["contador"];

        public Task<ConsultationResult> ConsultAsync(
            ConsultationKind kind, ConsultationContext ctx, ConsultationTenantOverride? tenantOverride, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new ConsultationResult("contador", "green", [], []));
        }
    }

    /// <summary><see cref="ServerCallContext"/> mínimo: el servicio solo lee el token de cancelación.</summary>
    private sealed class TestCallContext(CancellationToken ct) : ServerCallContext
    {
        protected override string MethodCore => "/flit.ict.IctOrchestration/CreateDraftFromIct";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:0";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = [];
        protected override CancellationToken CancellationTokenCore => ct;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, []);

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
