using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.OtRules;

// Tests del amarre del OT destino por nombre RUNT (B11). El RUNT/Verifik solo trae el NOMBRE del
// organismo (sin DIVIPOLA), asi que el match debe tolerar diferencias de tildes, mayusculas y
// espacios entre el nombre del RUNT y el del catalogo. Sin esto, transit_office_id no se fijaba y
// las politicas por-OT (RNMC, restricciones, bloqueo) no aplicaban.
//
// NOTA: los caracteres acentuados se construyen con (char)0x.. — solo ASCII en el fuente — para no
// depender de la codificacion del .cs al compilar (csc lee los archivos sin BOM como ANSI y
// manglaria una vocal acentuada escrita literal). En runtime el RUNT (HTTP) y el catalogo (BD)
// llegan en UTF-8 real, que es lo que el resolver normaliza.
public sealed class TransitOfficeResolverTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SabanetaId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private const char AAcute = (char)0x00E1; // a con tilde
    private const char IAcute = (char)0x00ED; // i con tilde

    [Theory]
    [InlineData("Sabaneta")]           // identico
    [InlineData("SABANETA")]           // mayusculas
    [InlineData("sabaneta")]           // minusculas
    [InlineData("  Sabaneta  ")]       // espacios sobrantes
    [InlineData("Sabaneta ")]          // espacio final
    public async Task Resuelve_TolerandoMayusculasYEspacios(string runtName)
    {
        var (grants, catalog) = Setup(catalogName: "Sabaneta");
        var resolver = new TransitOfficeResolver(grants, catalog);

        var match = await resolver.ResolveEnabledByNameAsync(
            Tenant, runtName, TestContext.Current.CancellationToken);

        match.Should().NotBeNull();
        match!.Id.Should().Be(SabanetaId);
    }

    [Fact] // RUNT con tilde espuria ("Sabaneta" con a acentuada) casa con el catalogo sin tilde.
    public async Task Resuelve_TolerandoTildes()
    {
        var runtName = "S" + AAcute + "baneta"; // "Sabaneta" con la primera a acentuada
        var (grants, catalog) = Setup(catalogName: "Sabaneta");
        var resolver = new TransitOfficeResolver(grants, catalog);

        var match = await resolver.ResolveEnabledByNameAsync(
            Tenant, runtName, TestContext.Current.CancellationToken);

        match.Should().NotBeNull();
        match!.Id.Should().Be(SabanetaId);
    }

    [Fact] // Catalogo con tildes casa con el RUNT sin tildes (caso Sabaneta real).
    public async Task Resuelve_CatalogoConTildes_RuntSinTildes()
    {
        // "Secretaria de Transito de Sabaneta" con las tildes reales del catalogo.
        var catalogName = "Secretar" + IAcute + "a de Tr" + AAcute + "nsito de Sabaneta";
        var (grants, catalog) = Setup(catalogName);
        var resolver = new TransitOfficeResolver(grants, catalog);

        var match = await resolver.ResolveEnabledByNameAsync(
            Tenant, "SECRETARIA DE TRANSITO DE SABANETA", TestContext.Current.CancellationToken);

        match.Should().NotBeNull();
        match!.Id.Should().Be(SabanetaId);
    }

    [Fact] // Un nombre genuinamente distinto NO casa (no se inventa un match).
    public async Task NoResuelve_ConNombreDistinto()
    {
        var (grants, catalog) = Setup(catalogName: "Sabaneta");
        var resolver = new TransitOfficeResolver(grants, catalog);

        var match = await resolver.ResolveEnabledByNameAsync(
            Tenant, "Envigado", TestContext.Current.CancellationToken);

        match.Should().BeNull();
    }

    [Fact] // Sin grants no hay OT habilitado que resolver.
    public async Task NoResuelve_SinGrants()
    {
        var grants = Substitute.For<ITransitGrantRepository>();
        grants.ListEnabledOfficeIdsAsync(Tenant, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([]));
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        var resolver = new TransitOfficeResolver(grants, catalog);

        var match = await resolver.ResolveEnabledByNameAsync(
            Tenant, "Sabaneta", TestContext.Current.CancellationToken);

        match.Should().BeNull();
    }

    private static (ITransitGrantRepository, ITransitOfficeCatalog) Setup(string catalogName)
    {
        var grants = Substitute.For<ITransitGrantRepository>();
        grants.ListEnabledOfficeIdsAsync(Tenant, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([SabanetaId]));

        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.GetById(SabanetaId)
            .Returns(new TransitOfficeEntry(SabanetaId, "05631000", catalogName, "05", "05631"));

        return (grants, catalog);
    }
}
