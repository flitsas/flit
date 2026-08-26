using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10878 (Feature #10862, CF-04, ADR-0031) — captura fail-safe del consentimiento Habeas Data
/// de reúso cross-trámite ampliando <c>PUT actors</c>.
/// </summary>
public sealed class PersonDataConsentCaptureTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly BiometricsProviderOptions _providerOptions = new() { Provider = BiometricProviders.Mock };
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly PutActorsHandler _put;

    private static readonly Guid BuyerEntityId = Guid.NewGuid();

    public PersonDataConsentCaptureTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var kyverumHandler = new IniciarKyverumVerifyHandler(
            _repo,
            Substitute.For<IKyverumVerifyClient>(),
            new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(),
            Substitute.For<IIdentityValidationAuditLog>());
        _put = new PutActorsHandler(_repo, _catalogRepo, _providerOptions, kyverumHandler, _consentRepo);

        _catalogRepo.GetProcedureEntityByCodeAsync("BUYER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = BuyerEntityId, Code = "BUYER", Name = "Comprador" });
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId) => new()
    {
        ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
        Id = id,
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000001",
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Put_AutorizaReutilizacionTrue_SinFilaPrevia_CreaConsentimientoGranted()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));
        // Sin fila previa (NSubstitute -> null por defecto).

        var comprador = new ActorInput(
            "comprador", "CC", "123456789", "Juan Comprador", "c@x.com", null,
            AutorizaReutilizacionDatos: true);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([comprador]), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();

        await _consentRepo.Received(1).AddAsync(
            Arg.Is<PersonDataConsent>(c =>
                c.TenantId == tenant &&
                c.DocumentType == "CC" &&
                c.DocumentNumber == "123456789" &&
                c.Status == PersonDataConsentStatus.Granted &&
                c.SourceProcedureInstanceId == id),
            Arg.Any<CancellationToken>());
        await _consentRepo.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_AutorizaReutilizacionFalseOAusente_NoTocaConsentimientoExistente()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var existing = new PersonDataConsent
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            DocumentType = "CC",
            DocumentNumber = "123456789",
            Status = PersonDataConsentStatus.Granted,
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-5),
        };
        _consentRepo.GetAsync(tenant, "CC", "123456789", ct).Returns(existing);

        // Default: AutorizaReutilizacionDatos = false (no viene en el request).
        var comprador = new ActorInput("comprador", "CC", "123456789", "Juan Comprador", "c@x.com", null);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([comprador]), ct);

        error.Should().BeNull();
        // No debe leer ni tocar el consentimiento en absoluto — no-op total.
        await _consentRepo.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _consentRepo.DidNotReceive().AddAsync(Arg.Any<PersonDataConsent>(), Arg.Any<CancellationToken>());
        await _consentRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        // La fila existente sigue igual (no se degrada un granted previo por accidente).
        existing.Status.Should().Be(PersonDataConsentStatus.Granted);
    }

    [Fact]
    public async Task Put_AutorizaReutilizacionTrue_ConFilaPrevia_ActualizaSinDuplicar()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(Instance(id, tenant));

        var existing = new PersonDataConsent
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            DocumentType = "CC",
            DocumentNumber = "123456789",
            Status = PersonDataConsentStatus.Unknown,
        };
        _consentRepo.GetAsync(tenant, "CC", "123456789", ct).Returns(existing);

        var comprador = new ActorInput(
            "comprador", "CC", "123456789", "Juan Comprador", "c@x.com", null,
            AutorizaReutilizacionDatos: true);

        var (result, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([comprador]), ct);

        error.Should().BeNull();
        existing.Status.Should().Be(PersonDataConsentStatus.Granted);
        existing.GrantedAt.Should().NotBeNull();
        await _consentRepo.DidNotReceive().AddAsync(Arg.Any<PersonDataConsent>(), Arg.Any<CancellationToken>());
    }
}
