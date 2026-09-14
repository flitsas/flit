using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Admin.Application.Companies.LegalRepresentatives.FindByNit;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Infrastructure.Tramites;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Tramites;

/// <summary>
/// El adaptador del directorio para la carga masiva (HU #12538) monta sobre el MISMO caso de uso
/// de la precarga por NIT del wizard. Lo que se comprueba es la traducción: el primario va de
/// primero, el nombre se compone como lo muestra el paso de actores y el contacto de la compañía
/// viaja para rellenar lo que la fila no trae.
/// </summary>
public sealed class BulkTramitesLegalRepresentativeDirectoryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly ILegalRepresentativeReader _reader = Substitute.For<ILegalRepresentativeReader>();
    private readonly ISignatureVaultReader _vault = Substitute.For<ISignatureVaultReader>();
    private readonly IRepresentativeIdentityLookup _identity = Substitute.For<IRepresentativeIdentityLookup>();

    private BulkTramitesLegalRepresentativeDirectory Directorio() =>
        new(new FindRepresentativeByNitHandler(_reader, _vault, _identity, TimeProvider.System));

    private static LegalRepresentativeItem Representante(
        string documento, string nombres, string primerApellido, string? segundoApellido, string? email) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        CompanyDocumentNumber = "900123456",
        CompanyName = "TRANSPORTES DEMO S.A.S.",
        DocumentType = "CC",
        DocumentNumber = documento,
        Name = nombres,
        FirstLastName = primerApellido,
        SecondLastName = segundoApellido,
        Email = email,
        Phone = "3111111111",
        IsActive = true,
        Companies =
        [
            new LegalRepresentativeCompanySummary(Guid.NewGuid(), "900123456", "TRANSPORTES DEMO S.A.S.")
            {
                IsPrimary = true,
                Email = "contacto@demo.com",
                City = "Medellín",
                Address = "Cra 1 # 2-3",
                Phone = "6041234567",
            },
        ],
    };

    [Fact]
    public async Task NitSinRepresentantesActivos_DevuelveNull()
    {
        _reader.ListActiveByCompanyNitAsync(Tenant, "900123456", Arg.Any<CancellationToken>())
            .Returns([]);

        var entrada = await Directorio().FindByNitAsync(Tenant, "900123456", TestContext.Current.CancellationToken);

        entrada.Should().BeNull();
    }

    [Fact]
    public async Task ConRepresentantes_ElPrimarioVaPrimero_ConNombreCompuestoYContactoDeLaCompania()
    {
        _reader.ListActiveByCompanyNitAsync(Tenant, "900123456", Arg.Any<CancellationToken>())
            .Returns(
            [
                Representante("1020304050", "HECTOR DE JESUS", "CARDENAS", "LARREA", "hector@example.com"),
                Representante("52000111", "MARIA", "LOPEZ", null, null),
            ]);

        var entrada = await Directorio().FindByNitAsync(Tenant, "900123456", TestContext.Current.CancellationToken);

        entrada.Should().NotBeNull();
        entrada!.Email.Should().Be("contacto@demo.com");
        entrada.Ciudad.Should().Be("Medellín");
        entrada.Direccion.Should().Be("Cra 1 # 2-3");
        entrada.Telefono.Should().Be("6041234567");
        entrada.Representantes.Should().HaveCount(2);
        entrada.Representantes[0].NumeroDocumento.Should().Be("1020304050");
        entrada.Representantes[0].NombreCompleto.Should().Be("HECTOR DE JESUS CARDENAS LARREA");
        entrada.Representantes[0].Email.Should().Be("hector@example.com");
        entrada.Representantes[1].NombreCompleto.Should().Be("MARIA LOPEZ");
        entrada.Representantes[1].Email.Should().BeNull();
    }
}
