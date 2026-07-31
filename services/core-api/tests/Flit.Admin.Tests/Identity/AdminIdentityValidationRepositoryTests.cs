using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// Tests de integración (InMemory) de la persistencia y el anclaje del bloque de identidad
/// administrativa (HU #10907, ADR-0034): alta/lectura/actualización de la validación y el linker que
/// setea <c>identity_validation_ref</c> del representante (<c>LegalRepresentative.LinkIdentity</c>),
/// preservando la exclusión firma/identidad.
/// </summary>
public sealed class AdminIdentityValidationRepositoryTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-00000000ee01");
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-admin-identity-{Guid.NewGuid()}")
            .Options);

    private static AdminIdentityValidation NewSent(Guid subjectRef) =>
        AdminIdentityValidation.CreateSent(
            Tenant, AdminIdentitySubjectTypes.LegalRepresentative, subjectRef, "CC", "123456789",
            "Juan Perez", "juan@x.co", AdminIdentityProviders.Kyverum, "https://capture", "kv-1", "enc",
            "pending", "{}", Now);

    [Fact]
    public async Task Add_Then_GetById_And_FindLatest_RoundTrips()
    {
        await using var ctx = NewContext();
        var repo = new AdminIdentityValidationRepository(ctx);
        var subject = Guid.NewGuid();
        var validation = NewSent(subject);

        await repo.AddAsync(validation, Ct);

        var byId = await repo.GetByIdAsync(Tenant, validation.Id, Ct);
        byId.Should().NotBeNull();
        byId!.Status.Should().Be(AdminIdentityEstados.Enviado);
        byId.KyverumVerificationId.Should().Be("kv-1");
        byId.WebhookSecretEncrypted.Should().Be("enc");

        var latest = await repo.FindLatestBySubjectAsync(Tenant, AdminIdentitySubjectTypes.LegalRepresentative, subject, Ct);
        latest.Should().NotBeNull();
        latest!.Id.Should().Be(validation.Id);

        // Aislamiento por tenant: otro tenant no lo ve.
        var otherTenantLookup = await repo.GetByIdAsync(Guid.NewGuid(), validation.Id, Ct);
        otherTenantLookup.Should().BeNull();
    }

    [Fact]
    public async Task Update_Persists_Approval()
    {
        await using var ctx = NewContext();
        var repo = new AdminIdentityValidationRepository(ctx);
        var validation = NewSent(Guid.NewGuid());
        await repo.AddAsync(validation, Ct);

        validation.Approve(Now, "cert-1");
        await repo.UpdateAsync(validation, Ct);

        var reloaded = await repo.GetByIdAsync(Tenant, validation.Id, Ct);
        reloaded!.Status.Should().Be(AdminIdentityEstados.Aprobado);
        reloaded.ValidUntil.Should().Be(AdminIdentityRules.FechaFinVigencia(Now));
        reloaded.CertificateHash.Should().Be("cert-1");
    }

    [Fact]
    public async Task Linker_SetsRepresentativeIdentityRef_AndClearsSignature()
    {
        await using var ctx = NewContext();
        var repRepo = new LegalRepresentativeRepository(ctx);
        var companyId = await repRepo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, "900000000-1", "ACME S.A.S.", null, null, null, null, null), Ct);
        // Representante creado con firma del baúl vinculada.
        var signatureId = Guid.NewGuid();
        var repId = await repRepo.SaveAsync(new SaveLegalRepresentativeData(
            Tenant, null, companyId, "CC", "123456789", "Perez", null, "Juan Perez",
            "juan@x.co", null, null, null, SignatureVaultId: signatureId, IdentityValidationRef: null,
            ProcedureTypeIds: [], ActorBy: null), Ct);

        var linker = new AdminIdentitySubjectLinker(ctx);
        var validationRef = Guid.NewGuid();
        var linked = await linker.LinkAsync(
            Tenant, AdminIdentitySubjectTypes.LegalRepresentative, repId, validationRef, actorBy: null, Ct);

        linked.Should().BeTrue();
        var reader = new DbLegalRepresentativeReader(ctx);
        var rep = await reader.GetByIdAsync(Tenant, repId, Ct);
        rep!.IdentityValidationRef.Should().Be(validationRef);
        rep.SignatureVaultId.Should().BeNull(); // identidad excluye firma del baúl
    }

    [Fact]
    public async Task Linker_UnknownSubjectType_ReturnsFalse()
    {
        await using var ctx = NewContext();
        var linker = new AdminIdentitySubjectLinker(ctx);

        var linked = await linker.LinkAsync(Tenant, "mandatario", Guid.NewGuid(), Guid.NewGuid(), null, Ct);

        linked.Should().BeFalse();
    }
}
