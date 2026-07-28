using System.Globalization;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #10974 (Feature #10972) — <c>runt_consulta_fecha</c>. El "Certificado de vigencia SOAT y RTM"
/// declara en su texto introductorio cuándo se consultó el RUNT, pero ningún mapper producía esa
/// llave (no es un dato de la RESPUESTA del proveedor sino de la EJECUCIÓN de la consulta), así que
/// el certificado se emitía sin fecha.
/// </summary>
public sealed class RunConsultationHandlerConsultaFechaTests
{
    private const string FieldKey = "runt_consulta_fecha";

    /// <summary>Huso de Colombia (UTC-5), el mismo que aplica el handler.</summary>
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    private readonly IProcedureInstanceRepository _instanceRepo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    private readonly IExternalQueryCacheRepository _cacheRepo = Substitute.For<IExternalQueryCacheRepository>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly RunConsultationHandler _sut;

    private static readonly Guid RuntSourceId = Guid.NewGuid();

    public RunConsultationHandlerConsultaFechaTests()
    {
        var cacheService = new ExternalQueryCacheService(_cacheRepo, _consentRepo, _catalogRepo);
        _sut = new RunConsultationHandler(_instanceRepo, _catalogRepo, _registry, cacheService);
    }

    private sealed class FakeProvider(string key, ConsultationResult result) : IConsultationProvider
    {
        public string Key => key;

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string fieldKey, string value)
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
            FieldKey = fieldKey,
            ValueText = value,
            Source = "user",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return instance;
    }

    private static ConsultationTemplate Template(string code, string entityScope, bool withCacheSource = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            EntityScope = entityScope,
            ExternalRefs = """{"provider":"verifik"}""",
            ExternalDataSourceId = RuntSourceId,
            // Sin la entidad navegada, el handler no resuelve sourceCode y omite el cache-aside por
            // completo: así el camino "consulta fresca" se prueba sin stubs de caché.
            ExternalDataSource = withCacheSource
                ? new ExternalDataSource { Id = RuntSourceId, Code = "RUNT", CacheTtlHours = 24 }
                : null,
        };

    private static string HoyEnColombia() =>
        DateTimeOffset.UtcNow.ToOffset(ColombiaOffset).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    [Fact]
    public async Task ConsultaDeVehiculo_PersisteLaFechaDeConsulta()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, "plate_or_vin", "ABC123");
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", ct)
            .Returns(Template("RUNT_VEHICLE", "vehicle"));
        _registry.Resolve("verifik").Returns(new FakeProvider("verifik",
            new ConsultationResult("verifik", "green", [], [])));

        var (_, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct: ct);

        error.Should().BeNull();
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == FieldKey && f.ValueText == HoyEnColombia() && f.Source == "consultation");
    }

    [Fact]
    public async Task ConsultaDeActor_NoEscribeLaFechaDeConsulta()
    {
        // El certificado habla del RUNT del VEHÍCULO: una consulta de persona no debe tocar la llave.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, "document_number", "123456789");
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_ACTOR_NATURAL", ct)
            .Returns(Template("RUNT_ACTOR_NATURAL", "actor"));
        _registry.Resolve("verifik").Returns(new FakeProvider("verifik",
            new ConsultationResult("verifik", "green", [], [])));

        var (_, error) = await _sut.HandleAsync(id, tenantId, "RUNT_ACTOR_NATURAL", ct: ct);

        error.Should().BeNull();
        instance.FieldValues.Should().NotContain(f => f.FieldKey == FieldKey);
    }

    [Fact]
    public async Task ReusoDeCache_DeclaraLaFechaDeLaConsultaOrigen_NoLaDelReuso()
    {
        // El documento debe decir cuándo se consultó el RUNT DE VERDAD, no cuándo se reutilizó el dato.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, "plate_or_vin", "ABC123");
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", ct)
            .Returns(Template("RUNT_VEHICLE", "vehicle", withCacheSource: true));
        _catalogRepo.GetExternalDataSourceByCodeAsync("RUNT", Arg.Any<CancellationToken>())
            .Returns(new ExternalDataSource { Id = RuntSourceId, Code = "RUNT", CacheTtlHours = 24 });

        var consultaOrigen = DateTimeOffset.UtcNow.AddDays(-8);
        _cacheRepo.FindVehicleAsync(tenantId, RuntSourceId, "ABC123", ct).Returns(new ExternalQueryCacheEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalDataSourceId = RuntSourceId,
            SubjectKind = ExternalQueryCacheRules.SubjectKindVehicle,
            VehicleIdentifier = "ABC123",
            Payload = """[{"fieldKey":"vehicle_brand","valueText":"TESLA","valueJson":null}]""",
            QueriedAt = consultaOrigen,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(16),
        });

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct: ct);

        error.Should().BeNull();
        result!.FromCache.Should().BeTrue();

        var esperada = consultaOrigen.ToOffset(ColombiaOffset).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        instance.FieldValues.Should().ContainSingle(f => f.FieldKey == FieldKey && f.ValueText == esperada);
        esperada.Should().NotBe(HoyEnColombia(), "la fecha del reúso no debe suplantar a la de la consulta origen");
    }
}
