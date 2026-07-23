using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

public sealed class RunConsultationHandlerTests
{
    private readonly IProcedureInstanceRepository _instanceRepo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    // HU #10878: dependencia nueva (cache-aside). Los repos internos de la caché quedan sin stub
    // (NSubstitute auto-completa Task<T> con default => null): sin ExternalDataSource en el template
    // de estos tests (Template() no lo setea), el handler nunca la toca — el resto de la suite queda
    // exactamente igual (ver RunConsultationHandlerCacheTests para la cobertura del cache-aside).
    private readonly IExternalQueryCacheRepository _cacheRepo = Substitute.For<IExternalQueryCacheRepository>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly ExternalQueryCacheService _cacheService;
    private readonly RunConsultationHandler _sut;

    public RunConsultationHandlerTests()
    {
        _cacheService = new ExternalQueryCacheService(_cacheRepo, _consentRepo, _catalogRepo);
        _sut = new RunConsultationHandler(_instanceRepo, _catalogRepo, _registry, _cacheService);
    }

    private sealed class FakeProvider(string key, ConsultationResult result) : IConsultationProvider
    {
        public string Key => key;
        public ConsultationContext? CapturedContext { get; private set; }

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            CapturedContext = ctx;
            return Task.FromResult(result);
        }
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status = TramiteEstado.Borrador) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ConsultationTemplate Template(string code, string externalRefs) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            ExternalRefs = externalRefs,
        };

    [Fact]
    public async Task HandleAsync_InstanceNotFound_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        _instanceRepo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct)
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), "RUNT_VEHICLE", ct);

        error.Should().Be("instance_not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_TemplateNotFound_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", ct)
            .Returns((ConsultationTemplate?)null);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct);

        error.Should().Be("template_not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ProviderNotResolved_WhenExternalRefsHasNoProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", ct)
            .Returns(Template("RUNT_VEHICLE", "{}"));

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct);

        error.Should().Be("provider_not_resolved");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ProviderNotFound_WhenRegistryReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", ct)
            .Returns(Template("RUNT_VEHICLE", """{"provider":"verifik"}"""));
        _registry.Resolve("verifik").Returns((IConsultationProvider?)null);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct);

        error.Should().Be("provider_not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_HappyPath_PersistsHydratedFieldsWithConsultationSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", ct)
            .Returns(Template("RUNT_VEHICLE", """{"provider":"verifik"}"""));

        // vehicle_brand es una clave NO-form (loose, derivada de consulta): debe persistir
        // con FormFieldId = null sin violar la FK (bug #1 del fix loose field-values).
        var providerResult = new ConsultationResult(
            "verifik",
            "green",
            [new ConsultationCheck("estado_vehiculo", "Estado", "ok", "verifik", null)],
            [new HydratedField("plate", "ABC123", null), new HydratedField("vehicle_brand", "TOYOTA", null)]);
        var provider = new FakeProvider("verifik", providerResult);
        _registry.Resolve("verifik").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct);

        error.Should().BeNull();
        result.Should().BeSameAs(providerResult);

        instance.FieldValues.Should().Contain(f => f.FieldKey == "plate" && f.ValueText == "ABC123" && f.Source == "consultation");
        instance.FieldValues.Should().Contain(f => f.FieldKey == "vehicle_brand" && f.ValueText == "TOYOTA" && f.Source == "consultation");
        // Valores hidratados desde consulta son "loose": no atados a un form_field.
        instance.FieldValues.Should().OnlyContain(f => f.FormFieldId == null);

        // La instancia está trackeada: el change tracker persiste los adds vía
        // SaveChangesAsync. UpdateAsync marcaría el grafo Modified y los nuevos
        // field_values se emitirían como UPDATE de 0 filas (no INSERT), por lo
        // que NO debe llamarse.
        await _instanceRepo.DidNotReceive().UpdateAsync(Arg.Any<ProcedureInstance>(), Arg.Any<CancellationToken>());
        // Los field_values NUEVOS hidratados se marcan Added explícito → INSERT
        // (PK store-generated con Id ya seteado).
        _instanceRepo.Received(2).Add(Arg.Any<ProcedureInstanceFieldValue>());
        await _instanceRepo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_PassesCurrentFieldValuesToProviderContext()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FieldKey = "vin",
            ValueText = "1HGCM82633A004352",
            Source = "user",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", ct)
            .Returns(Template("RUNT_VEHICLE", """{"provider":"verifik"}"""));

        var provider = new FakeProvider("verifik",
            new ConsultationResult("verifik", "green", [], []));
        _registry.Resolve("verifik").Returns(provider);

        await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct);

        provider.CapturedContext.Should().NotBeNull();
        provider.CapturedContext!.FieldValues.Should().ContainKey("vin");
        provider.CapturedContext.FieldValues["vin"].Should().Be("1HGCM82633A004352");
        provider.CapturedContext.TemplateCode.Should().Be("RUNT_VEHICLE");
    }

    [Fact]
    public async Task HandleAsync_NotDraftViolationOnSave_ReturnsNotDraft()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", ct)
            .Returns(Template("RUNT_VEHICLE", """{"provider":"verifik"}"""));
        _registry.Resolve("verifik").Returns(
            new FakeProvider("verifik", new ConsultationResult("verifik", "green", [], [])));
        _instanceRepo.SaveChangesAsync(ct)
            .Returns<Task>(_ => throw new InvalidOperationException("23514: check_violation on field_values"));

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct);

        error.Should().Be("not_draft");
        result.Should().BeNull();
    }
}
