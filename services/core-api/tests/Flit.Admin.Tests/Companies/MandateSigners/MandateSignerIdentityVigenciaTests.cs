using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #11765 (ADR-0050) — <c>DbMandateSignerReader</c> (la FICHA admin de gestión de mandatarios; NO
/// <c>MandateSignerDirectory</c>, que es la ruta de RADICACIÓN y ya migró en la HU #11752) deja de leer
/// <c>admin.admin_identity_validations</c> y resuelve <c>IdentityStatus</c>/<c>IdentityValidUntil</c>
/// contra el módulo Identidad, en el tenant PROPIO del organismo donde está registrado el mandatario
/// (<see cref="ITransitOfficeOperationalStatusReader"/>) — mismo mecanismo que el directorio.
/// </summary>
public sealed class MandateSignerIdentityVigenciaTests
{
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid OtTenant = Guid.NewGuid();
    private static readonly Guid Signer = Guid.NewGuid();
    private const string Documento = "1020304050";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandate-reader-identity-{Guid.NewGuid()}")
            .Options);

    private static async Task<FlitDbContext> SeedAsync()
    {
        var ctx = NewContext();
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = Signer,
            TransitOfficeId = Ot,
            FullName = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = Documento,
            IntegrityHash = new string('a', 64),
            RegisteredAt = Now,
            IsActive = true,
            CreatedAt = Now,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return ctx;
    }

    private static ITransitOfficeOperationalStatusReader ReaderConTenant(Guid officeId, Guid? tenantId)
    {
        var reader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        var item = tenantId is { } t
            ? new TransitOfficeOperationalStatusItem { Id = officeId, HasTenant = true, TenantId = t }
            : null;
        reader.GetByIdAsync(officeId, Arg.Any<CancellationToken>()).Returns(item);
        return reader;
    }

    [Fact]
    public async Task GetByIdAsync_ConAprobadaVigenteEnElModuloIdentidad_MarcaValidYExponeVigencia()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            DocumentType = "CC",
            DocumentNumber = Documento,
            Status = BiometricEstados.Aprobado,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "hash",
            ExpiresAt = Now.AddHours(1),
            ValidatedAt = Now.AddDays(-1),
            ValidUntil = Now.AddDays(29),
            CreatedAt = Now.AddDays(-1),
        });
        await ctx.SaveChangesAsync(ct);

        var reader = new DbMandateSignerReader(ctx, ReaderConTenant(Ot, OtTenant));
        var item = await reader.GetByIdAsync(Signer, ct);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
        item.IdentityValidUntil.Should().BeNull("el mandatario no renueva su identidad (HU #13130b)");
    }

    [Fact]
    public async Task GetByIdAsync_AprobadaHace40Dias_SigueValid_SinRenovacion()
    {
        // HU #13130b (decisión del PO, 01-oct): el mandatario no renueva; la ventana de 30 días es del trámite.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var vieja = Aprobada(OtTenant);
        vieja.ValidatedAt = Now.AddDays(-40);
        vieja.ValidUntil = Now.AddDays(-10);
        vieja.CreatedAt = Now.AddDays(-40);
        ctx.ProcedureInstanceBiometricValidations.Add(vieja);
        await ctx.SaveChangesAsync(ct);

        var item = await new DbMandateSignerReader(ctx, ReaderConTenant(Ot, OtTenant)).GetByIdAsync(Signer, ct);

        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
    }

    [Fact]
    public async Task GetByIdAsync_AprobadaAntiguaYNuevaValidacionEnCurso_SigueValid()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var vieja = Aprobada(OtTenant);
        vieja.ValidatedAt = Now.AddDays(-60);
        vieja.ValidUntil = Now.AddDays(-30);
        vieja.CreatedAt = Now.AddDays(-60);
        ctx.ProcedureInstanceBiometricValidations.Add(vieja);
        ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            DocumentType = "CC",
            DocumentNumber = Documento,
            Status = BiometricEstados.EnProceso,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "hash-2",
            ExpiresAt = Now.AddHours(1),
            CreatedAt = Now.AddDays(-1),
        });
        await ctx.SaveChangesAsync(ct);

        var item = await new DbMandateSignerReader(ctx, ReaderConTenant(Ot, OtTenant)).GetByIdAsync(Signer, ct);

        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
    }

    [Fact]
    public async Task GetByIdAsync_ConValidacionEnCurso_MarcaPending()
    {
        // Es el AC central de la ola: prevalidar en Identidad (queda "en_curso" hasta que Kyverum
        // resuelva) debe reflejarse aquí como "en curso", no seguir mostrando "sin validar".
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            DocumentType = "CC",
            DocumentNumber = Documento,
            Status = BiometricEstados.EnProceso,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "hash",
            ExpiresAt = Now.AddHours(1),
            CreatedAt = Now.AddDays(-1),
        });
        await ctx.SaveChangesAsync(ct);

        var reader = new DbMandateSignerReader(ctx, ReaderConTenant(Ot, OtTenant));
        var item = await reader.GetByIdAsync(Signer, ct);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Pending);
        item.IdentityValidUntil.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_OtSinTenant_QuedaSinValidarYNoConsultaIdentidad()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var identityRepo = Substitute.For<IProcedureInstanceRepository>();
        var reader = new DbMandateSignerReader(
            ctx, ReaderConTenant(Ot, null), new IdentityVigenciaPorDocumentoResolver(identityRepo));

        var item = await reader.GetByIdAsync(Signer, ct);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.None);
        await identityRepo.DidNotReceive().ListLatestBiometricValidationsByPersonsAsync(
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyCollection<(string DocumentTypeNorm, string DocumentNumberNorm)>>(),
            Arg.Any<CancellationToken>());
    }

    private static readonly Guid Compania = Guid.NewGuid();

    private static async Task VincularCompaniaAsync(FlitDbContext ctx, Guid companyTenantId)
    {
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(),
            MandateSignerId = Signer,
            TransitOfficeId = Ot,
            CompanyTenantId = companyTenantId,
            IsActive = true,
            CreatedAt = Now,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static ProcedureInstanceBiometricValidation Aprobada(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        DocumentType = "CC",
        DocumentNumber = Documento,
        Status = BiometricEstados.Aprobado,
        Provider = BiometricProviders.Kyverum,
        TokenHash = "hash",
        ExpiresAt = Now.AddHours(1),
        ValidatedAt = Now.AddDays(-1),
        ValidUntil = Now.AddDays(29),
        CreatedAt = Now.AddDays(-1),
    };

    [Fact]
    public async Task GetByIdAsync_ValidacionRegistradaEnElTenantDeLaCompania_MarcaValidAunqueElOtTengaOtroTenant()
    {
        // HU #13121 AC2: la validación vive en el tenant de la compañía (el OT no puede registrar en Identidad).
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        await VincularCompaniaAsync(ctx, Compania);
        ctx.ProcedureInstanceBiometricValidations.Add(Aprobada(Compania));
        await ctx.SaveChangesAsync(ct);

        var reader = new DbMandateSignerReader(ctx, ReaderConTenant(Ot, OtTenant));
        var item = await reader.GetByIdAsync(Signer, ct);

        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
        item.IdentityValidUntil.Should().BeNull("el mandatario no renueva su identidad (HU #13130b)");
    }

    [Fact]
    public async Task GetByIdAsync_ValidacionSoloEnUnTenantNoRelacionado_QuedaSinValidar()
    {
        // HU #13121 AC3: existe una aprobada, pero en un tenant que ni es de su compañía ni del OT vinculado.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        await VincularCompaniaAsync(ctx, Compania);
        ctx.ProcedureInstanceBiometricValidations.Add(Aprobada(Guid.NewGuid()));
        // Incluso una en el tenant del OT se ignora: con compañías vinculadas manda el tenant de la compañía.
        ctx.ProcedureInstanceBiometricValidations.Add(Aprobada(OtTenant));
        await ctx.SaveChangesAsync(ct);

        var reader = new DbMandateSignerReader(ctx, ReaderConTenant(Ot, OtTenant));
        var item = await reader.GetByIdAsync(Signer, ct);

        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.None);
    }

    [Fact]
    public async Task GetByIdAsync_DosCompaniasConValidacionSoloEnUna_MarcaValid()
    {
        // HU #13121 AC4: regla única — vigente en alguna de sus compañías vinculadas; no depende del OT.
        var ct = TestContext.Current.CancellationToken;
        var otraCompania = Guid.NewGuid();
        await using var ctx = await SeedAsync();
        await VincularCompaniaAsync(ctx, Compania);
        await VincularCompaniaAsync(ctx, otraCompania);
        ctx.ProcedureInstanceBiometricValidations.Add(Aprobada(otraCompania));
        await ctx.SaveChangesAsync(ct);

        var reader = new DbMandateSignerReader(ctx, ReaderConTenant(Ot, null));
        var item = await reader.GetByIdAsync(Signer, ct);

        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
    }

    [Fact]
    public async Task ListByOtAsync_DosMandatariosDelMismoOrganismo_ResuelveEnUnaSolaConsultaBatch()
    {
        // AC de lote (HU #11765): dos mandatarios del MISMO organismo con documentos distintos se
        // resuelven con UNA sola llamada batch al resolver de Identidad, no una por fila.
        var ct = TestContext.Current.CancellationToken;
        var otroSigner = Guid.NewGuid();
        const string otroDocumento = "9998887776";
        await using var ctx = await SeedAsync();
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = otroSigner,
            TransitOfficeId = Ot,
            FullName = "Carlos Pérez",
            DocumentType = "CC",
            DocumentNumber = otroDocumento,
            IntegrityHash = new string('b', 64),
            RegisteredAt = Now,
            IsActive = true,
            CreatedAt = Now,
        });
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(),
            MandateSignerId = Signer,
            TransitOfficeId = Ot,
            IsActive = true,
            CreatedAt = Now,
        });
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(),
            MandateSignerId = otroSigner,
            TransitOfficeId = Ot,
            IsActive = true,
            CreatedAt = Now,
        });
        await ctx.SaveChangesAsync(ct);

        var identityRepo = Substitute.For<IProcedureInstanceRepository>();
        identityRepo.ListLatestBiometricValidationsByPersonsAsync(
                OtTenant,
                Arg.Any<IReadOnlyCollection<(string DocumentTypeNorm, string DocumentNumberNorm)>>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var reader = new DbMandateSignerReader(
            ctx, ReaderConTenant(Ot, OtTenant), new IdentityVigenciaPorDocumentoResolver(identityRepo));

        var items = await reader.ListByOtAsync(Ot, OtCompanyVisibility.WholeNetwork, ct);

        items.Should().HaveCount(2);
        await identityRepo.Received(1).ListLatestBiometricValidationsByPersonsAsync(
            OtTenant,
            Arg.Any<IReadOnlyCollection<(string DocumentTypeNorm, string DocumentNumberNorm)>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListByOtAsync_ConStatusReaderReal_NoRevientaPorTransaccionAnidada()
    {
        // GET /mandate-signers: ListByOtAsync abre tx y LoadIdentityVigenciaAsync llama
        // DbTransitOfficeOperationalStatusReader.GetByIdAsync. En PostgreSQL anidar BeginTransaction
        // fallaba; el reader de estado reutiliza la tx. InMemory no abre tx, pero cubre el camino DI real.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        var reader = new DbMandateSignerReader(ctx, new DbTransitOfficeOperationalStatusReader(ctx));

        var act = async () => await reader.ListByOtAsync(Ot, OtCompanyVisibility.WholeNetwork, ct);

        await act.Should().NotThrowAsync();
    }
}
