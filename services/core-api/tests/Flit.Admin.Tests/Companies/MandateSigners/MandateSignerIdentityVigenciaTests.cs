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

    private static readonly Guid Compania = Guid.NewGuid();

    /// <summary>Validación lanzada PARA el mandatario (party_role mandatario + su ficha), en el tenant de la compañía.</summary>
    private static ProcedureInstanceBiometricValidation Propia(
        string status = BiometricEstados.Aprobado,
        DateTimeOffset? creada = null,
        string numero = Documento,
        Guid? signer = null,
        Guid? tenant = null)
    {
        var creadaEn = creada ?? Now.AddDays(-1);
        return new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenant ?? Compania,
            PartyRole = BiometricRules.ParteMandatario,
            MandateSignerId = signer ?? Signer,
            DocumentType = "CC",
            DocumentNumber = numero,
            Status = status,
            Provider = BiometricProviders.Kyverum,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = Now.AddHours(1),
            ValidatedAt = status == BiometricEstados.Aprobado ? creadaEn.AddMinutes(5) : null,
            CreatedAt = creadaEn,
        };
    }

    /// <summary>Validación de OTRO rol con el mismo documento (comprador, vendedor o prevalidación standalone).</summary>
    private static ProcedureInstanceBiometricValidation DeOtroRol(string? rol, Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        PartyRole = rol,
        DocumentType = "CC",
        DocumentNumber = Documento,
        Status = BiometricEstados.Aprobado,
        Provider = BiometricProviders.Kyverum,
        TokenHash = Guid.NewGuid().ToString("N"),
        ExpiresAt = Now.AddHours(1),
        ValidatedAt = Now.AddDays(-1),
        ValidUntil = Now.AddDays(29),
        CreatedAt = Now.AddDays(-1),
    };

    private static DbMandateSignerReader NewReader(FlitDbContext ctx) =>
        new(ctx, ReaderConTenant(Ot, OtTenant));

    [Fact]
    public async Task GetByIdAsync_ConValidacionPropiaAprobada_MarcaValid_SinFechaDeFin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.ProcedureInstanceBiometricValidations.Add(Propia());
        await ctx.SaveChangesAsync(ct);

        var item = await NewReader(ctx).GetByIdAsync(Signer, ct);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
        item.IdentityValidUntil.Should().BeNull("el mandatario no renueva su identidad (HU #13130b)");
    }

    [Fact]
    public async Task GetByIdAsync_PropiaAprobadaHace40Dias_SigueValid_SinRenovacion()
    {
        // HU #13130b: la ventana de 30 días es del trámite; el mandatario no renueva.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.ProcedureInstanceBiometricValidations.Add(Propia(creada: Now.AddDays(-40)));
        await ctx.SaveChangesAsync(ct);

        (await NewReader(ctx).GetByIdAsync(Signer, ct))!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
    }

    [Theory]
    [InlineData("comprador")]
    [InlineData("vendedor")]
    [InlineData(null)] // prevalidación standalone
    public async Task GetByIdAsync_AprobacionDeOtroRolConElMismoDocumento_NoCuenta_QuedaSinValidar(string? rol)
    {
        // HU #13247 AC2/AC5: la biometría de un comprador, un vendedor o una prevalidación con la misma cédula no habilita la
        // firma del mandatario, sea cual sea el tenant; sin validación propia queda pendiente de validación, sin backfill.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        await VincularCompaniaAsync(ctx, Compania);
        ctx.ProcedureInstanceBiometricValidations.Add(DeOtroRol(rol, Compania));
        ctx.ProcedureInstanceBiometricValidations.Add(DeOtroRol(rol, OtTenant));
        await ctx.SaveChangesAsync(ct);

        (await NewReader(ctx).GetByIdAsync(Signer, ct))!.IdentityStatus.Should().Be(AdminIdentityVigencia.None);
    }

    [Fact]
    public async Task GetByIdAsync_ValidacionPropiaDeOtraFicha_NoCuenta()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.ProcedureInstanceBiometricValidations.Add(Propia(signer: Guid.NewGuid()));
        await ctx.SaveChangesAsync(ct);

        (await NewReader(ctx).GetByIdAsync(Signer, ct))!.IdentityStatus.Should().Be(AdminIdentityVigencia.None);
    }

    [Fact]
    public async Task GetByIdAsync_PropiaAprobadaDeUnDocumentoAnterior_NoCuenta()
    {
        // HU #13247 AC3: tras editar el documento, la validación del documento anterior no cuenta.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.ProcedureInstanceBiometricValidations.Add(Propia(numero: "999000111"));
        await ctx.SaveChangesAsync(ct);

        (await NewReader(ctx).GetByIdAsync(Signer, ct))!.IdentityStatus.Should().Be(AdminIdentityVigencia.None);
    }

    [Fact]
    public async Task GetByIdAsync_PropiaAprobadaAntiguaYNuevaEnCurso_LaAnteriorDejaDeContar_MarcaPending()
    {
        // HU #13246 AC3: reenviar lanza una nueva y la anterior deja de contar hasta que la nueva se apruebe.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.ProcedureInstanceBiometricValidations.Add(Propia(creada: Now.AddDays(-60)));
        ctx.ProcedureInstanceBiometricValidations.Add(Propia(BiometricEstados.EnProceso, Now.AddDays(-1)));
        await ctx.SaveChangesAsync(ct);

        (await NewReader(ctx).GetByIdAsync(Signer, ct))!.IdentityStatus.Should().Be(AdminIdentityVigencia.Pending);
    }

    [Fact]
    public async Task GetByIdAsync_ConValidacionPropiaEnCurso_MarcaPending()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        ctx.ProcedureInstanceBiometricValidations.Add(Propia(BiometricEstados.EnProceso));
        await ctx.SaveChangesAsync(ct);

        var item = await NewReader(ctx).GetByIdAsync(Signer, ct);

        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Pending);
        item.IdentityValidUntil.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_MandatarioExistenteSinValidacionPropia_QuedaPendienteDeValidacion_SinBackfill()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();

        (await NewReader(ctx).GetByIdAsync(Signer, ct))!.IdentityStatus.Should().Be(AdminIdentityVigencia.None);
        (await ctx.ProcedureInstanceBiometricValidations.CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task GetByIdAsync_LaValidacionPropiaNoDependeDelTenantDelOrganismoNiDeLaCompaniaVinculada()
    {
        // La validación es de la ficha, no del documento: con o sin compañías vinculadas, en el tenant que sea.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeedAsync();
        await VincularCompaniaAsync(ctx, Compania);
        ctx.ProcedureInstanceBiometricValidations.Add(Propia(tenant: Compania));
        await ctx.SaveChangesAsync(ct);

        (await new DbMandateSignerReader(ctx, ReaderConTenant(Ot, null)).GetByIdAsync(Signer, ct))!
            .IdentityStatus.Should().Be(AdminIdentityVigencia.Valid);
    }

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

    [Fact]
    public async Task ListByOtAsync_DosMandatariosDelMismoOrganismo_ResuelveEnUnaSolaConsultaBatch()
    {
        // AC de lote (HU #11765): dos mandatarios del MISMO organismo se resuelven con UNA sola lectura de sus validaciones
        // propias, no una por fila.
        var ct = TestContext.Current.CancellationToken;
        var otroSigner = Guid.NewGuid();
        await using var ctx = await SeedAsync();
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = otroSigner,
            TransitOfficeId = Ot,
            FullName = "Carlos Pérez",
            DocumentType = "CC",
            DocumentNumber = "9998887776",
            IntegrityHash = new string('b', 64),
            RegisteredAt = Now,
            IsActive = true,
            CreatedAt = Now,
        });
        foreach (var id in new[] { Signer, otroSigner })
        {
            ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
            {
                Id = Guid.NewGuid(),
                MandateSignerId = id,
                TransitOfficeId = Ot,
                IsActive = true,
                CreatedAt = Now,
            });
        }

        await ctx.SaveChangesAsync(ct);

        var identityRepo = Substitute.For<IProcedureInstanceRepository>();
        identityRepo.ListMandatarioValidationsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var reader = new DbMandateSignerReader(
            ctx, ReaderConTenant(Ot, OtTenant), new IdentityVigenciaPorDocumentoResolver(identityRepo));

        var items = await reader.ListByOtAsync(Ot, OtCompanyVisibility.WholeNetwork, ct);

        items.Should().HaveCount(2);
        await identityRepo.Received(1).ListMandatarioValidationsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), Arg.Any<CancellationToken>());
        await identityRepo.DidNotReceive().ListLatestBiometricValidationsByPersonsAsync(
            Arg.Any<Guid>(),
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
