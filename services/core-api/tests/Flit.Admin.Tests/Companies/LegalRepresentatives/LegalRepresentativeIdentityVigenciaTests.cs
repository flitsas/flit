using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// HU #11765 (ADR-0050) — <c>DbLegalRepresentativeReader</c> deja de leer
/// <c>admin.admin_identity_validations</c> (tabla abandonada) y resuelve la vigencia de identidad
/// EXCLUSIVAMENTE contra el módulo Identidad (<c>tramites.procedure_instance_biometric_validations</c>),
/// vía <see cref="IdentityVigenciaPorDocumentoResolver"/>. Es el fix del defecto raíz de la ola: un
/// operador prevalidaba a una persona en Identidad y el rótulo de su ficha admin no se movía porque este
/// lector seguía apuntando a la tabla vieja.
/// <para>
/// HU #11192 — se conserva la llave de PERSONA (tenant + tipo y número de documento), no la del sujeto:
/// una validación aprobada y vigente de la misma persona hecha en OTRO contexto (otro representante,
/// un mandatario, un actor de trámite) debe seguir siendo visible aquí. Como la tabla nueva ya no tiene
/// noción de "sujeto" (a diferencia de <c>admin.admin_identity_validations.subject_type/subject_ref</c>),
/// esa asimetría es ahora estructuralmente imposible de reintroducir por accidente.
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
    public async Task AC1_ValidacionStandaloneDelDocumento_SeReconoceComoVigente()
    {
        // La prevalidación se hizo desde el módulo Identidad (standalone, sin representante ni
        // mandatario de por medio): mismo documento de la persona, ninguna fila vinculada.
        var db = NewDbName();
        var repId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedRepresentative(seed, repId, Tenant, CompanyA);
            seed.ProcedureInstanceBiometricValidations.Add(
                Validacion(Tenant, DocNumber, BiometricEstados.Aprobado, Now.AddDays(30)));
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
        // La persona es representante en dos compañías del mismo tenant y la validación es del
        // documento, no de ninguna de las dos filas.
        var db = NewDbName();
        var repEnA = Guid.NewGuid();
        var repEnB = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedCompany(seed, Tenant, CompanyB, "Empresa B S.A.S.");
            SeedRepresentative(seed, repEnA, Tenant, CompanyA);
            SeedRepresentative(seed, repEnB, Tenant, CompanyB);
            seed.ProcedureInstanceBiometricValidations.Add(
                Validacion(Tenant, DocNumber, BiometricEstados.Aprobado, Now.AddDays(15)));
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
        // vinculada a este representante: vincularla es lo que hace el endpoint de asociación
        // (HU #11176), que sigue escribiendo IdentityValidationRef aparte.
        var db = NewDbName();
        var repId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedRepresentative(seed, repId, Tenant, CompanyA);
            seed.ProcedureInstanceBiometricValidations.Add(
                Validacion(Tenant, DocNumber, BiometricEstados.Aprobado, Now.AddDays(20)));
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
            seed.ProcedureInstanceBiometricValidations.Add(
                Validacion(Tenant, DocNumber, BiometricEstados.Aprobado, Now.AddDays(-1)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var item = await new DbLegalRepresentativeReader(ctx)
            .GetByIdAsync(Tenant, repId, TestContext.Current.CancellationToken);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Expired,
            "aprobada pero fuera de la ventana de vigencia (ADR-0050: vencida)");
    }

    [Fact]
    public async Task AC6_ValidacionEnCurso_SeMuestraComoPending()
    {
        var db = NewDbName();
        var repId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedRepresentative(seed, repId, Tenant, CompanyA);
            seed.ProcedureInstanceBiometricValidations.Add(
                Validacion(Tenant, DocNumber, BiometricEstados.EnProceso, validUntil: null));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var item = await new DbLegalRepresentativeReader(ctx)
            .GetByIdAsync(Tenant, repId, TestContext.Current.CancellationToken);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Pending,
            "es el defecto raíz de la ola: prevalidar en Identidad debe moverse aquí a 'en curso'");
        item.IdentityValidUntil.Should().BeNull();
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
            seed.ProcedureInstanceBiometricValidations.Add(
                Validacion(OtroTenant, DocNumber, BiometricEstados.Aprobado, Now.AddDays(30)));
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
            seed.ProcedureInstanceBiometricValidations.Add(
                Validacion(Tenant, "9999999999", BiometricEstados.Aprobado, Now.AddDays(30)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var item = await new DbLegalRepresentativeReader(ctx)
            .GetByIdAsync(Tenant, repId, TestContext.Current.CancellationToken);

        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.None);
    }

    [Fact]
    public async Task Coherencia_MismoEstadoQueElResolverDeIdentidad_ADR0050()
    {
        // AC de coherencia entre superficies (HU #11765): el estado que expone la ficha admin (legacy
        // valid/pending/expired/none) debe corresponder EXACTAMENTE al estado ADR-0050 que resuelve
        // IdentityVigenciaPorDocumentoResolver — el mismo componente que consume el endpoint de la
        // HU #11751 — para la MISMA persona. No deben divergir en una futura HU.
        var db = NewDbName();
        var repId = Guid.NewGuid();
        await using (var seed = NewContext(db))
        {
            SeedCompany(seed, Tenant, CompanyA, "Empresa A S.A.S.");
            SeedRepresentative(seed, repId, Tenant, CompanyA);
            seed.ProcedureInstanceBiometricValidations.Add(
                Validacion(Tenant, DocNumber, BiometricEstados.Aprobado, Now.AddDays(30)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var item = await new DbLegalRepresentativeReader(ctx)
            .GetByIdAsync(Tenant, repId, TestContext.Current.CancellationToken);

        var resolver = new IdentityVigenciaPorDocumentoResolver(new ProcedureInstanceRepository(ctx));
        var directo = await resolver.ResolveAsync(
            Tenant, DocType, DocNumber, Now, TestContext.Current.CancellationToken);

        item.Should().NotBeNull();
        var esperado = IdentityVigenciaLegacyMapper.ToLegacyResultado(directo);
        item!.IdentityStatus.Should().Be(esperado.Status);
        item.IdentityValidUntil.Should().Be(esperado.ValidUntil);
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

    private static ProcedureInstanceBiometricValidation Validacion(
        Guid tenantId, string documentNumber, string status, DateTimeOffset? validUntil) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DocumentType = DocType,
            DocumentNumber = documentNumber,
            Name = "Juan Montoya",
            Email = "juan@x.co",
            Status = status,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "hash",
            ExpiresAt = Now.AddHours(1),
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
