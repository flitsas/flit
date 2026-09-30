using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #11752 (ADR-0050) — <c>MandateSignerDirectory</c> deja de leer
/// <c>admin.admin_identity_validations</c> y resuelve <c>IdentityVigente</c>/<c>CertificadoIdentidad</c>/
/// <c>IdentityValidUntil</c> contra el módulo Identidad (<c>tramites.procedure_instance_biometric_validations</c>),
/// en el tenant PROPIO del organismo de tránsito donde está registrado el mandatario — resuelto vía
/// <see cref="ITransitOfficeOperationalStatusReader"/>, el MISMO mecanismo que usaba el disparo admin ya
/// retirado.
///
/// <para>Uso de ejemplo:
/// <c>new MandateSignerDirectory(ctx, otStatusReader, identityResolver).GetCandidatesAsync(ot, empresa, null, ct)</c>
/// ⇒ <c>candidatos[0].IdentityVigente == true</c> cuando el resolver de Identidad devuelve
/// <c>aprobada_vigente</c> para el documento del mandatario en el tenant del OT.</para>
/// </summary>
public sealed class MandateSignerDirectoryIdentityVigenciaTests
{
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid OtTenant = Guid.NewGuid();
    private static readonly Guid Gestora = Guid.NewGuid();
    private static readonly Guid Signer = Guid.NewGuid();
    private const string Documento = "1020304050";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandate-identity-{Guid.NewGuid()}")
            .Options);

    private static async Task<FlitDbContext> SeedAsync()
    {
        var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = Signer,
            TransitOfficeId = Ot,
            FullName = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = Documento,
            IntegrityHash = new string('a', 64),
            RegisteredAt = now,
            IsActive = true,
            CreatedAt = now,
        });
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(),
            MandateSignerId = Signer,
            TransitOfficeId = Ot,
            CompanyTenantId = Gestora,
            IsActive = true,
            CreatedAt = now,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return ctx;
    }

    private static ITransitOfficeOperationalStatusReader ReaderConTenant(Guid? tenantId) =>
        StubReader(tenantId is { } t
            ? new TransitOfficeOperationalStatusItem { Id = Ot, HasTenant = true, TenantId = t }
            : null);

    private static ITransitOfficeOperationalStatusReader StubReader(TransitOfficeOperationalStatusItem? item)
    {
        var reader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        reader.GetByIdAsync(Ot, Arg.Any<CancellationToken>()).Returns(item);
        return reader;
    }

    /// <summary>La validación vive en el tenant indicado (por defecto el de la compañía gestora, HU #13121).</summary>
    private static IProcedureInstanceRepository RepoStub(
        ProcedureInstanceBiometricValidation? latest, Guid? tenant = null)
    {
        IReadOnlyList<ProcedureInstanceBiometricValidation> rows =
            latest is null ? [] : [latest];
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.ListBiometricValidationsByPersonAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((Array.Empty<ProcedureInstanceBiometricValidation>(), 0, false));
        repo.ListBiometricValidationsByPersonAsync(
                tenant ?? Gestora, "CC", Documento, 0, 1, Arg.Any<CancellationToken>())
            .Returns((rows, rows.Count, false));
        return repo;
    }

    private static ProcedureInstanceBiometricValidation Aprobada() => new()
    {
        Status = BiometricEstados.Aprobado,
        DocumentType = "CC",
        DocumentNumber = Documento,
        ValidatedAt = Now.AddDays(-1),
        ValidUntil = Now.AddDays(29),
        CertificateHash = "hash-mandatario",
    };

    [Fact]
    public async Task GetCandidatesAsync_ValidacionSoloEnElTenantDelOt_NoHaySello()
    {
        // HU #13121 AC3: la validación existe, pero en un tenant que no es el de la compañía del mandatario
        // (aquí el del OT): no se estampa sello y no hay error.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(OtTenant),
            new IdentityVigenciaPorDocumentoResolver(RepoStub(Aprobada(), tenant: OtTenant)));

        var candidatos = await directorio.GetCandidatesAsync(Ot, Gestora, null, ct);

        var candidato = candidatos.Should().ContainSingle().Subject;
        candidato.IdentityVigente.Should().BeFalse();
        candidato.CertificadoIdentidad.Should().BeNull();
    }

    [Fact]
    public async Task GetCandidatesAsync_ValidacionVigenteEnOtraCompaniaVinculada_MarcaVigente()
    {
        // HU #13121 AC4: vinculado a dos compañías con validación vigente solo en la otra: una única regla
        // (vigente en alguna de sus compañías) y no depende del tenant del OT.
        var ct = TestContext.Current.CancellationToken;
        var otraCompania = Guid.NewGuid();
        await using var ctx = await SeedAsync();
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(),
            MandateSignerId = Signer,
            TransitOfficeId = Ot,
            CompanyTenantId = otraCompania,
            IsActive = true,
            CreatedAt = Now,
        });
        await ctx.SaveChangesAsync(ct);
        var repo = RepoStub(null);
        repo.ListBiometricValidationsByPersonAsync(
                otraCompania, "CC", Documento, 0, 1, Arg.Any<CancellationToken>())
            .Returns((new[] { Aprobada() } as IReadOnlyList<ProcedureInstanceBiometricValidation>, 1, false));
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(null), new IdentityVigenciaPorDocumentoResolver(repo));

        var candidatos = await directorio.GetCandidatesAsync(Ot, Gestora, null, ct);

        var candidato = candidatos.Should().ContainSingle().Subject;
        candidato.IdentityVigente.Should().BeTrue();
        candidato.CertificadoIdentidad.Should().Be("hash-mandatario");
    }

    [Fact]
    public async Task GetCandidatesAsync_ConIdentidadAprobadaVigenteEnElTenantDeLaCompania_MarcaVigente()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var aprobada = new ProcedureInstanceBiometricValidation
        {
            Status = BiometricEstados.Aprobado,
            DocumentType = "CC",
            DocumentNumber = Documento,
            ValidatedAt = Now.AddDays(-1),
            ValidUntil = Now.AddDays(29),
            CertificateHash = "hash-mandatario",
        };
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(OtTenant),
            new IdentityVigenciaPorDocumentoResolver(RepoStub(aprobada)));

        var candidatos = await directorio.GetCandidatesAsync(Ot, Gestora, null, ct);

        var candidato = candidatos.Should().ContainSingle().Subject;
        candidato.IdentityVigente.Should().BeTrue();
        candidato.CertificadoIdentidad.Should().Be("hash-mandatario");
        candidato.IdentityValidUntil.Should().Be(aprobada.ValidUntil);
    }

    [Fact]
    public async Task GetCandidatesAsync_SinValidacionEnElModuloIdentidad_NoQuedaVigente()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(OtTenant),
            new IdentityVigenciaPorDocumentoResolver(RepoStub(null)));

        var candidatos = await directorio.GetCandidatesAsync(Ot, Gestora, null, ct);

        var candidato = candidatos.Should().ContainSingle().Subject;
        candidato.IdentityVigente.Should().BeFalse();
        candidato.CertificadoIdentidad.Should().BeNull();
    }

    [Fact]
    public async Task GetCandidatesAsync_AprobadaPeroVencida_NoCuentaComoVigente()
    {
        // La clasificación "vencida" (HU #11751) no debe apalancar la firma: es exactamente el defecto
        // que el ADR-0050 quiere evitar.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var vencida = new ProcedureInstanceBiometricValidation
        {
            Status = BiometricEstados.Aprobado,
            DocumentType = "CC",
            DocumentNumber = Documento,
            ValidatedAt = Now.AddDays(-40),
            ValidUntil = Now.AddDays(-10),
        };
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(OtTenant),
            new IdentityVigenciaPorDocumentoResolver(RepoStub(vencida)));

        var candidatos = await directorio.GetCandidatesAsync(Ot, Gestora, null, ct);

        candidatos.Should().ContainSingle().Which.IdentityVigente.Should().BeFalse();
    }

    [Fact]
    public async Task GetCandidatesAsync_OtSinTenant_NoConsultaIdentidad_YQuedaSinVigencia()
    {
        // Mandatario SIN compañías vinculadas y OT sin tenant: no hay contra qué resolver identidad, se
        // degrada a "sin vigencia" sin lanzar y el resolver de Identidad NUNCA se invoca (repo.DidNotReceive).
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.MandateSignerCompanies.RemoveRange(ctx.MandateSignerCompanies);
        await ctx.SaveChangesAsync(ct);
        var repo = Substitute.For<IProcedureInstanceRepository>();
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(null), new IdentityVigenciaPorDocumentoResolver(repo));

        var candidato = await directorio.GetByIdAsync(Signer, ct);

        candidato.Should().NotBeNull();
        candidato!.IdentityVigente.Should().BeFalse();
        await repo.DidNotReceive().ListBiometricValidationsByPersonAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_ResuelveIdentidadPorElTenantDeLaCompaniaDelMandatario()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var aprobada = Aprobada();
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(OtTenant),
            new IdentityVigenciaPorDocumentoResolver(RepoStub(aprobada)));

        var signer = await directorio.GetByIdAsync(Signer, ct);

        signer.Should().NotBeNull();
        signer!.IdentityVigente.Should().BeTrue();
        signer.CertificadoIdentidad.Should().Be("hash-mandatario");
    }

    [Fact]
    public async Task NingunaConsultaVaHaciaAdminIdentityValidations()
    {
        // Guardrail explícito del AC de la HU #11752: no debe quedar ninguna lectura de
        // admin.admin_identity_validations en la cascada del directorio.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.AdminIdentityValidations.Add(new AdminIdentityValidationEntity
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            SubjectType = "mandate_signer",
            SubjectRef = Signer,
            Name = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = Documento,
            Email = "sin-correo@flit.local",
            Status = "aprobado",
            ValidUntil = Now.AddDays(60),
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await ctx.SaveChangesAsync(ct);

        // El resolver de Identidad no ve NINGUNA validación (fuente única, HU #11751): aunque la tabla
        // admin diga "aprobado y vigente", el candidato debe salir SIN vigencia.
        var directorio = new MandateSignerDirectory(
            ctx, ReaderConTenant(OtTenant),
            new IdentityVigenciaPorDocumentoResolver(RepoStub(null)));

        var candidatos = await directorio.GetCandidatesAsync(Ot, Gestora, null, ct);

        candidatos.Should().ContainSingle().Which.IdentityVigente.Should().BeFalse();
    }
}
