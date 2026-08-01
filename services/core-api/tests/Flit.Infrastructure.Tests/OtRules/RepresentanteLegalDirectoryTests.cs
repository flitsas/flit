using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.OtRules;

/// <summary>
/// HU #11195 — regla de "representante utilizable" del directorio de Admin, ejercitada contra el
/// adaptador REAL con EF InMemory. Es una regla de datos (tres tablas cruzadas), así que probarla contra
/// dobles no diría nada: lo que hay que verificar es que el cruce escrituras × representantes × compañía
/// sea el correcto.
/// </summary>
public sealed class RepresentanteLegalDirectoryTests
{
    private const string Nit = "900123456";
    private const string DocType = "CC";
    private const string Doc = "1090123456";

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 8, 1);

    private readonly ISignatureVaultReader _vault = Substitute.For<ISignatureVaultReader>();
    private readonly IRepresentativeIdentityLookup _identidad = Substitute.For<IRepresentativeIdentityLookup>();

    [Fact]
    public async Task AC1_CompaniaSinNingunRepresentante_NoEsUtilizable()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        SeedCompania(context);
        await context.SaveChangesAsync(ct);

        var utilizable = await Directorio(context).TieneRepresentanteUtilizableAsync(Tenant, Nit, Hoy, ct);

        utilizable.Should().BeFalse();
    }

    [Fact]
    public async Task AC2_RepresentanteSinEscritura_NoEsUtilizable()
    {
        // Tiene firma vigente, pero nada lo acredita como representante de esa compañía.
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        var (compania, representante) = SeedRepresentante(context);
        await context.SaveChangesAsync(ct);
        ConFirmaVigente();

        var utilizable = await Directorio(context).TieneRepresentanteUtilizableAsync(Tenant, Nit, Hoy, ct);

        utilizable.Should().BeFalse();
        compania.Should().NotBe(Guid.Empty);
        representante.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task AC2_EscrituraVencida_NoEsUtilizable()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        var (compania, representante) = SeedRepresentante(context);
        SeedEscritura(context, compania, representante, desde: new(2025, 1, 1), hasta: new(2026, 7, 31));
        await context.SaveChangesAsync(ct);
        ConFirmaVigente();

        var utilizable = await Directorio(context).TieneRepresentanteUtilizableAsync(Tenant, Nit, Hoy, ct);

        utilizable.Should().BeFalse();
    }

    [Fact]
    public async Task AC2_EscrituraVigentePeroSinFirmaNiIdentidad_NoEsUtilizable()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        var (compania, representante) = SeedRepresentante(context);
        SeedEscritura(context, compania, representante);
        await context.SaveChangesAsync(ct);
        // Sin firma y sin identidad: los sustitutos devuelven null por defecto.

        var utilizable = await Directorio(context).TieneRepresentanteUtilizableAsync(Tenant, Nit, Hoy, ct);

        utilizable.Should().BeFalse();
    }

    [Fact]
    public async Task AC3_EscrituraVigenteMasFirmaVigente_EsUtilizable()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        var (compania, representante) = SeedRepresentante(context);
        SeedEscritura(context, compania, representante);
        await context.SaveChangesAsync(ct);
        ConFirmaVigente();

        var utilizable = await Directorio(context).TieneRepresentanteUtilizableAsync(Tenant, Nit, Hoy, ct);

        utilizable.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_EscrituraVigenteMasIdentidadVigente_EsUtilizable()
    {
        // La identidad basta aunque no haya firma del baúl: son mecanismos alternativos.
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        var (compania, representante) = SeedRepresentante(context);
        SeedEscritura(context, compania, representante);
        await context.SaveChangesAsync(ct);
        _identidad.FindVigenteIdentityRefAsync(
                Tenant, DocType, Doc, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        var utilizable = await Directorio(context).TieneRepresentanteUtilizableAsync(Tenant, Nit, Hoy, ct);

        utilizable.Should().BeTrue();
    }

    [Fact]
    public async Task EscrituraDeOtroRepresentante_NoLoAcredita()
    {
        // La escritura acredita a UNA persona: la de otro representante no habilita a este. Sin esto,
        // cualquier escritura viva de la compañía haría pasar la compuerta.
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        var (compania, _) = SeedRepresentante(context);
        SeedEscritura(context, compania, representanteId: Guid.NewGuid());
        await context.SaveChangesAsync(ct);
        ConFirmaVigente();

        var utilizable = await Directorio(context).TieneRepresentanteUtilizableAsync(Tenant, Nit, Hoy, ct);

        utilizable.Should().BeFalse();
    }

    [Fact]
    public async Task EscrituraLegadaSinRepresentante_NoAcreditaANadie()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        var (compania, _) = SeedRepresentante(context);
        SeedEscritura(context, compania, representanteId: null);
        await context.SaveChangesAsync(ct);
        ConFirmaVigente();

        var utilizable = await Directorio(context).TieneRepresentanteUtilizableAsync(Tenant, Nit, Hoy, ct);

        utilizable.Should().BeFalse();
    }

    [Fact]
    public async Task RepresentanteInactivo_NoCuenta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        var (compania, representante) = SeedRepresentante(context, activo: false);
        SeedEscritura(context, compania, representante);
        await context.SaveChangesAsync(ct);
        ConFirmaVigente();

        var utilizable = await Directorio(context).TieneRepresentanteUtilizableAsync(Tenant, Nit, Hoy, ct);

        utilizable.Should().BeFalse();
    }

    [Fact]
    public async Task EscrituraDeOtraCompaniaDelMismoRepresentante_NoLoAcreditaAqui()
    {
        // Un representante puede representar a varias compañías (HU #10932) con escrituras distintas.
        // La escritura de otra compañía no lo faculta para firmar por esta.
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();
        var (_, representante) = SeedRepresentante(context);
        SeedEscritura(context, companiaId: Guid.NewGuid(), representante);
        await context.SaveChangesAsync(ct);
        ConFirmaVigente();

        var utilizable = await Directorio(context).TieneRepresentanteUtilizableAsync(Tenant, Nit, Hoy, ct);

        utilizable.Should().BeFalse();
    }

    [Fact]
    public async Task SinNit_NoDisparaLaCompuerta()
    {
        // Sin dato que consultar se responde "sí tiene": la compuerta no debe enviarle un correo de
        // validación a nadie por un NIT que no se pudo resolver.
        var ct = TestContext.Current.CancellationToken;
        using var context = NewContext();

        var utilizable = await Directorio(context).TieneRepresentanteUtilizableAsync(Tenant, "  ", Hoy, ct);

        utilizable.Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"rep-directory-{Guid.NewGuid()}")
            .Options);

    private RepresentanteLegalDirectory Directorio(FlitDbContext context) =>
        new(context, _vault, _identidad);

    private static Guid SeedCompania(FlitDbContext context)
    {
        var id = Guid.NewGuid();
        context.RepresentedCompanies.Add(new RepresentedCompanyEntity
        {
            Id = id,
            TenantId = Tenant,
            DocumentType = "NIT",
            DocumentNumber = Nit,
            Name = "Empresa Compradora SAS",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return id;
    }

    private static (Guid CompaniaId, Guid RepresentativeId) SeedRepresentante(
        FlitDbContext context, bool activo = true)
    {
        var companiaId = SeedCompania(context);
        var representanteId = Guid.NewGuid();

        context.CompanyLegalRepresentatives.Add(new CompanyLegalRepresentativeEntity
        {
            Id = representanteId,
            TenantId = Tenant,
            RepresentedCompanyId = companiaId,
            DocumentType = DocType,
            DocumentNumber = Doc,
            Name = "Ana",
            FirstLastName = "Representante",
            Email = "rep@empresa.com",
            IsActive = activo,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        context.LegalRepresentativeCompanies.Add(new LegalRepresentativeCompanyEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            RepresentativeId = representanteId,
            RepresentedCompanyId = companiaId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return (companiaId, representanteId);
    }

    private static void SeedEscritura(
        FlitDbContext context,
        Guid companiaId,
        Guid? representanteId,
        DateOnly? desde = null,
        DateOnly? hasta = null)
    {
        var escrituraId = Guid.NewGuid();
        context.CompanyDeeds.Add(new CompanyDeedEntity
        {
            Id = escrituraId,
            TenantId = Tenant,
            RepresentativeId = representanteId,
            Description = "Escritura 123",
            StoragePath = "deeds/123.pdf",
            StorageSha256 = "sha",
            VigenciaDesde = desde ?? new DateOnly(2026, 1, 1),
            VigenciaHasta = hasta ?? new DateOnly(2026, 12, 31),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        context.CompanyDeedCompanies.Add(new CompanyDeedCompanyEntity
        {
            Id = Guid.NewGuid(),
            DeedId = escrituraId,
            RepresentedCompanyId = companiaId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private void ConFirmaVigente() =>
        _vault.FindActiveByDocumentAsync(Tenant, DocType, Doc, Arg.Any<CancellationToken>())
            .Returns(SignatureVault.Rehydrate(
                Guid.NewGuid(),
                Tenant,
                DocType,
                Doc,
                nitEmpresa: Nit,
                fullName: "Ana Representante",
                signatureHash: "hash",
                storagePath: "vault/firma.png",
                storageSha256: "sha",
                estado: SignatureVaultEstado.Activa,
                vigenciaDesde: new DateOnly(2026, 1, 1),
                vigenciaHasta: new DateOnly(2026, 12, 31),
                mandateSignerId: null,
                codigoHash: null));
}
