using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// HU #11192 — la vigencia de identidad del representante se resuelve por PERSONA (tenant + tipo y
/// número de documento), no por el sujeto de la validación.
/// <para>
/// Antes se filtraba por <c>subject_type = 'legal_representative'</c> y por el id del representante,
/// mientras la firma del baúl se resolvía por documento. Por esa asimetría una validación aprobada y
/// vigente de la misma persona hecha en otro contexto quedaba invisible y el panel decía «Identidad
/// sin validar» teniéndola.
/// </para>
/// Ejercita el lector EF real sobre InMemory, igual patrón que <c>ResolvedDocumentMatrixHandlerTests</c>.
/// </summary>
public sealed class LegalRepresentativeIdentityVigenciaTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-1111-4000-8000-000000000001");
    private static readonly Guid OtroTenant = Guid.Parse("aaaaaaaa-2222-4000-8000-000000000002");
    private static readonly Guid CompanyA = Guid.Parse("bbbbbbbb-1111-4000-8000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("bbbbbbbb-2222-4000-8000-000000000002");
    private const string DocType = "CC";
    private const string DocNumber = "1038409485";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task AC1_ValidacionHechaEnOtroContexto_SeReconoceComoVigente()
    {
        // La validación se creó bajo otro sujeto (un mandatario), con el documento de la misma persona.
        var db = NewDbName();
        var repId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedRepresentative(seed, repId, Tenant, CompanyA);
            seed.AdminIdentityValidations.Add(Validacion(
                Tenant, "mandate_signer", Guid.NewGuid(), DocNumber, "aprobado", Now.AddDays(30)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var item = await new DbLegalRepresentativeReader(ctx)
            .GetByIdAsync(Tenant, repId, TestContext.Current.CancellationToken);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
        item.IdentityValidUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task AC2_MismaPersonaEnOtraCompania_SeReconoceComoVigente()
    {
        // La persona es representante en dos compañías del mismo tenant y la validación se hizo desde
        // la fila de la otra compañía.
        var db = NewDbName();
        var repEnA = Guid.NewGuid();
        var repEnB = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedCompany(seed, Tenant, CompanyB, "Empresa B S.A.S.");
            SeedRepresentative(seed, repEnA, Tenant, CompanyA);
            SeedRepresentative(seed, repEnB, Tenant, CompanyB);
            seed.AdminIdentityValidations.Add(Validacion(
                Tenant, "legal_representative", repEnA, DocNumber, "aprobado", Now.AddDays(15)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var item = await new DbLegalRepresentativeReader(ctx)
            .GetByIdAsync(Tenant, repEnB, TestContext.Current.CancellationToken);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
    }

    [Fact]
    public async Task AC3_VigenteDeLaPersona_SeDistingueDeAsociadaAlRepresentante()
    {
        // El panel debe poder decir «hay identidad vigente de esta persona» sin afirmar que está
        // vinculada a este representante: vincularla es lo que hace el endpoint de asociación.
        var db = NewDbName();
        var repId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedRepresentative(seed, repId, Tenant, CompanyA);
            seed.AdminIdentityValidations.Add(Validacion(
                Tenant, "procedure_actor", Guid.NewGuid(), DocNumber, "aprobado", Now.AddDays(20)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var item = await new DbLegalRepresentativeReader(ctx)
            .GetByIdAsync(Tenant, repId, TestContext.Current.CancellationToken);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid, "la persona sí tiene identidad vigente");
        item.IdentityValidationRef.Should().BeNull("pero todavía no está asociada a este representante");
    }

    [Fact]
    public async Task AC4_SinValidacion_SigueSinValidar()
    {
        var db = NewDbName();
        var repId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedRepresentative(seed, repId, Tenant, CompanyA);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var item = await new DbLegalRepresentativeReader(ctx)
            .GetByIdAsync(Tenant, repId, TestContext.Current.CancellationToken);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.None);
        item.IdentityValidUntil.Should().BeNull();
    }

    [Fact]
    public async Task AC5_ValidacionVencida_NoSeMuestraComoVigente()
    {
        var db = NewDbName();
        var repId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedRepresentative(seed, repId, Tenant, CompanyA);
            seed.AdminIdentityValidations.Add(Validacion(
                Tenant, "legal_representative", repId, DocNumber, "aprobado", Now.AddDays(-1)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var item = await new DbLegalRepresentativeReader(ctx)
            .GetByIdAsync(Tenant, repId, TestContext.Current.CancellationToken);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().NotBe(AdminIdentityVigencia.Valid);
    }

    [Fact]
    public async Task NoSeCruzanTenants_AunConElMismoDocumento()
    {
        // La misma persona puede ser representante en varios tenants y la validación es tenant-scoped:
        // resolver por documento SIN filtrar por tenant filtraría datos de otro cliente.
        var db = NewDbName();
        var repId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedRepresentative(seed, repId, Tenant, CompanyA);
            seed.AdminIdentityValidations.Add(Validacion(
                OtroTenant, "legal_representative", Guid.NewGuid(), DocNumber, "aprobado", Now.AddDays(30)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var item = await new DbLegalRepresentativeReader(ctx)
            .GetByIdAsync(Tenant, repId, TestContext.Current.CancellationToken);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.None,
            "la validación pertenece a otro tenant y no puede alcanzar a este representante");
    }

    [Fact]
    public async Task DocumentoDistinto_NoAlcanzaAlRepresentante()
    {
        var db = NewDbName();
        var repId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedRepresentative(seed, repId, Tenant, CompanyA);
            seed.AdminIdentityValidations.Add(Validacion(
                Tenant, "legal_representative", Guid.NewGuid(), "9999999999", "aprobado", Now.AddDays(30)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var item = await new DbLegalRepresentativeReader(ctx)
            .GetByIdAsync(Tenant, repId, TestContext.Current.CancellationToken);

        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.None);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static void SeedCompany(FlitDbContext ctx, Guid tenantId, Guid companyId, string name) =>
        ctx.RepresentedCompanies.Add(new RepresentedCompanyEntity
        {
            Id = companyId,
            TenantId = tenantId,
            DocumentNumber = "900123456",
            Name = name,
            CreatedAt = Now,
        });

    private static void SeedRepresentative(FlitDbContext ctx, Guid id, Guid tenantId, Guid companyId) =>
        ctx.CompanyLegalRepresentatives.Add(new CompanyLegalRepresentativeEntity
        {
            Id = id,
            TenantId = tenantId,
            RepresentedCompanyId = companyId,
            DocumentType = DocType,
            DocumentNumber = DocNumber,
            FirstLastName = "Montoya",
            Name = "Juan",
            Email = "juan@x.co",
            IsActive = true,
            CreatedAt = Now,
        });

    private static AdminIdentityValidationEntity Validacion(
        Guid tenantId, string subjectType, Guid subjectRef, string documentNumber,
        string status, DateTimeOffset validUntil) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubjectType = subjectType,
            SubjectRef = subjectRef,
            DocumentType = DocType,
            DocumentNumber = documentNumber,
            Name = "Juan Montoya",
            Email = "juan@x.co",
            Status = status,
            Provider = "kyverum",
            ValidatedAt = Now.AddDays(-1),
            ValidUntil = validUntil,
            CreatedAt = Now.AddDays(-1),
        };

    private static string NewDbName() => $"flit-rl-identity-vigencia-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
