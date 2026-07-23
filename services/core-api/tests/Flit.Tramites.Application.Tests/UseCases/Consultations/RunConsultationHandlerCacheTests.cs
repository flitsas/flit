using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #10878 (Feature #10862, CF-04, ADR-0030/ADR-0031) — cache-aside en <see cref="RunConsultationHandler"/>.
/// AC1 (hit vigente: no llama al proveedor, precarga field_values), AC2 (vencido: reconsulta y
/// recachea) y el gate de consentimiento fail-safe para el reúso de PERSONA.
/// </summary>
public sealed class RunConsultationHandlerCacheTests
{
    private readonly IProcedureInstanceRepository _instanceRepo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    private readonly IExternalQueryCacheRepository _cacheRepo = Substitute.For<IExternalQueryCacheRepository>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly RunConsultationHandler _sut;

    private static readonly Guid RuntSourceId = Guid.NewGuid();

    public RunConsultationHandlerCacheTests()
    {
        var cacheService = new ExternalQueryCacheService(_cacheRepo, _consentRepo, _catalogRepo);
        _sut = new RunConsultationHandler(_instanceRepo, _catalogRepo, _registry, cacheService);

        _catalogRepo.GetExternalDataSourceByCodeAsync("RUNT", Arg.Any<CancellationToken>())
            .Returns(new ExternalDataSource { Id = RuntSourceId, Code = "RUNT", CacheTtlHours = 24 });
    }

    private sealed class FakeProvider(string key, ConsultationResult result) : IConsultationProvider
    {
        public string Key => key;
        public bool Called { get; private set; }

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.FromResult(result);
        }
    }

    private static ProcedureInstance PersonInstance(Guid id, Guid tenantId, string docType = "CC", string docNumber = "123456789")
    {
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FieldKey = "document_type",
            ValueText = docType,
            Source = "user",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FieldKey = "document_number",
            ValueText = docNumber,
            Source = "user",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return instance;
    }

    private static ConsultationTemplate ActorTemplate() => new()
    {
        Id = Guid.NewGuid(),
        Code = "RUNT_ACTOR_NATURAL",
        EntityScope = "actor",
        ExternalRefs = """{"provider":"verifik_conductor"}""",
        ExternalDataSourceId = RuntSourceId,
        ExternalDataSource = new ExternalDataSource { Id = RuntSourceId, Code = "RUNT", CacheTtlHours = 24 },
    };

    [Fact]
    public async Task HandleAsync_PersonaConCacheVigenteYConsentimiento_NoLlamaProveedor_Precarga()
    {
        // AC1
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = PersonInstance(id, tenantId);
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_ACTOR_NATURAL", ct).Returns(ActorTemplate());

        _consentRepo.GetAsync(tenantId, "CC", "123456789", ct)
            .Returns(new PersonDataConsent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DocumentType = "CC",
                DocumentNumber = "123456789",
                Status = PersonDataConsentStatus.Granted,
                GrantedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });

        var cachedEntry = new ExternalQueryCacheEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalDataSourceId = RuntSourceId,
            SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
            DocumentType = "CC",
            DocumentNumber = "123456789",
            Payload = """[{"fieldKey":"person_full_name","valueText":"JUAN PEREZ","valueJson":null}]""",
            QueriedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(22),
        };
        _cacheRepo.FindPersonAsync(tenantId, RuntSourceId, "CC", "123456789", ct).Returns(cachedEntry);

        var provider = new FakeProvider("verifik_conductor",
            new ConsultationResult("verifik_conductor", "green", [], []));
        _registry.Resolve("verifik_conductor").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_ACTOR_NATURAL", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.FromCache.Should().BeTrue();
        result.HydratedFields.Should().ContainSingle(f => f.FieldKey == "person_full_name" && f.ValueText == "JUAN PEREZ");

        provider.Called.Should().BeFalse(); // AC1: cero llamadas al proveedor externo.
        _registry.DidNotReceive().Resolve(Arg.Any<string>());

        instance.FieldValues.Should().Contain(f =>
            f.FieldKey == "person_full_name" && f.ValueText == "JUAN PEREZ" && f.Source == "consultation");

        cachedEntry.ReuseCount.Should().Be(1);
        cachedEntry.LastReusedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_PersonaConCacheVencida_Reconsulta_Y_Recachea()
    {
        // AC2
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = PersonInstance(id, tenantId);
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_ACTOR_NATURAL", ct).Returns(ActorTemplate());

        _consentRepo.GetAsync(tenantId, "CC", "123456789", ct)
            .Returns(new PersonDataConsent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DocumentType = "CC",
                DocumentNumber = "123456789",
                Status = PersonDataConsentStatus.Granted,
                GrantedAt = DateTimeOffset.UtcNow.AddDays(-40),
            });

        var expiredEntry = new ExternalQueryCacheEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalDataSourceId = RuntSourceId,
            SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
            DocumentType = "CC",
            DocumentNumber = "123456789",
            Payload = """[{"fieldKey":"person_full_name","valueText":"JUAN VIEJO","valueJson":null}]""",
            QueriedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1), // vencida
        };
        _cacheRepo.FindPersonAsync(tenantId, RuntSourceId, "CC", "123456789", ct).Returns(expiredEntry);

        var freshResult = new ConsultationResult(
            "verifik_conductor", "green", [],
            [new HydratedField("person_full_name", "JUAN NUEVO", null)]);
        var provider = new FakeProvider("verifik_conductor", freshResult);
        _registry.Resolve("verifik_conductor").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_ACTOR_NATURAL", ct);

        error.Should().BeNull();
        provider.Called.Should().BeTrue(); // vencida => MISS => reconsulta real.
        result.Should().BeSameAs(freshResult);

        instance.FieldValues.Should().Contain(f =>
            f.FieldKey == "person_full_name" && f.ValueText == "JUAN NUEVO" && f.Source == "consultation");

        // Recachea (upsert sobre la entrada vencida existente).
        await _cacheRepo.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _cacheRepo.DidNotReceive().AddAsync(Arg.Any<ExternalQueryCacheEntry>(), Arg.Any<CancellationToken>());
        expiredEntry.Payload.Should().Contain("JUAN NUEVO");
        expiredEntry.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task HandleAsync_PersonaSinConsentimiento_NuncaPrecarga_NiBloqueaElTramite()
    {
        // Caso crítico ADR-0031: aunque exista una entrada de caché VIGENTE, sin consentimiento
        // granted el reúso NUNCA ocurre — cae a consulta fresca, sin bloquear el trámite.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = PersonInstance(id, tenantId);
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_ACTOR_NATURAL", ct).Returns(ActorTemplate());

        // Sin fila de consentimiento (GetAsync no configurado => NSubstitute devuelve null).
        var vigenteEntry = new ExternalQueryCacheEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalDataSourceId = RuntSourceId,
            SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
            DocumentType = "CC",
            DocumentNumber = "123456789",
            Payload = """[{"fieldKey":"person_full_name","valueText":"NO DEBERIA VERSE","valueJson":null}]""",
            QueriedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(23),
        };
        _cacheRepo.FindPersonAsync(tenantId, RuntSourceId, "CC", "123456789", ct).Returns(vigenteEntry);

        var freshResult = new ConsultationResult(
            "verifik_conductor", "green", [],
            [new HydratedField("person_full_name", "CONSULTA FRESCA", null)]);
        var provider = new FakeProvider("verifik_conductor", freshResult);
        _registry.Resolve("verifik_conductor").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_ACTOR_NATURAL", ct);

        error.Should().BeNull();
        provider.Called.Should().BeTrue(); // sin consent => MISS => se consulta igual (no bloquea).
        result.Should().BeSameAs(freshResult);
        result!.FromCache.Should().BeFalse();

        instance.FieldValues.Should().Contain(f =>
            f.FieldKey == "person_full_name" && f.ValueText == "CONSULTA FRESCA");
        instance.FieldValues.Should().NotContain(f => f.ValueText == "NO DEBERIA VERSE");

        // La entrada vigente jamás se tocó (ni touch de reuse, ni lectura de su payload).
        vigenteEntry.ReuseCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_Vehiculo_NoRequiereConsentimiento_HitVigente_NoLlamaProveedor()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000002",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FieldKey = "plate_or_vin",
            ValueText = "abc123",
            Source = "user",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);

        var vehicleTemplate = new ConsultationTemplate
        {
            Id = Guid.NewGuid(),
            Code = "RUNT_VEHICLE",
            EntityScope = "vehicle",
            ExternalRefs = """{"provider":"verifik"}""",
            ExternalDataSourceId = RuntSourceId,
            ExternalDataSource = new ExternalDataSource { Id = RuntSourceId, Code = "RUNT", CacheTtlHours = 24 },
        };
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", ct).Returns(vehicleTemplate);

        var cachedEntry = new ExternalQueryCacheEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalDataSourceId = RuntSourceId,
            SubjectKind = ExternalQueryCacheRules.SubjectKindVehicle,
            VehicleIdentifier = "ABC123", // normalizado mayúsculas por el servicio
            Payload = """[{"fieldKey":"vehicle_brand","valueText":"TOYOTA","valueJson":null}]""",
            QueriedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(23),
        };
        _cacheRepo.FindVehicleAsync(tenantId, RuntSourceId, "ABC123", ct).Returns(cachedEntry);

        var provider = new FakeProvider("verifik", new ConsultationResult("verifik", "green", [], []));
        _registry.Resolve("verifik").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct);

        error.Should().BeNull();
        result!.FromCache.Should().BeTrue();
        provider.Called.Should().BeFalse();
        // No debe consultarse consentimiento: el vehículo no es dato personal.
        await _consentRepo.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        instance.FieldValues.Should().Contain(f => f.FieldKey == "vehicle_brand" && f.ValueText == "TOYOTA");
    }
}
