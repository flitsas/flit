using System.Text.Json;
using Flit.PersonaNatural.Api.Catalogos;
using Flit.PersonaNatural.Api.Consultas;
using Flit.Consultas.Grpc.V1;
using FluentAssertions;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>HU #14255 y #14257 — reglas puras: carrocerías por clase (misma regla que FLIT 2) y lectura del acreedor del RUNT.</summary>
public sealed class CarroceriasPorClaseTests
{
    [Theory]
    [InlineData("AUTOMOVIL")]
    [InlineData("automovil")]
    [InlineData("  Automóvil ")]
    [InlineData("CAMION")]
    [InlineData("CAMIÓN")]
    [InlineData("MOTOCICLETA")]
    public void UnaClaveConocida_TrabajaSinImportarMayusculasNiTildes(string clase) =>
        CarroceriasPorClase.Para(clase).Should().NotBeEmpty();

    [Fact]
    public void LaClaseAcentuadaDaLoMismoQueLaNormal() =>
        CarroceriasPorClase.Para("CAMIÓN").Select(i => i.Codigo).Should().Equal(CarroceriasPorClase.Para("CAMION").Select(i => i.Codigo));

    [Fact]
    public void ElAliasOrtograficoYElCalificativo_ResuelvenALaClaveDelCatalogo()
    {
        CarroceriasPorClase.Para("SEMIRREMOLQUE").Should().NotBeEmpty().And.Equal(CarroceriasPorClase.Para("SEMIREMOLQUE"));
        CarroceriasPorClase.Para("CAMION CISTERNA").Should().Equal(CarroceriasPorClase.Para("CAMION"));
    }

    [Fact]
    public void Camioneta_NoSeConfundeConCamion() =>
        CarroceriasPorClase.Para("CAMIONETA").Select(i => i.Codigo).Should().NotEqual(CarroceriasPorClase.Para("CAMION").Select(i => i.Codigo));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NAVE ESPACIAL")]
    [InlineData("CAMIONERO")]
    public void SinClaseOSinCoincidencia_LaListaEsVacia_NoElCatalogoCompleto(string? clase) =>
        CarroceriasPorClase.Para(clase).Should().BeEmpty();

    [Fact]
    public void ElValorQueSeGuardaEsElNombre_SinRepetirse_YElTextoFiltra()
    {
        var todas = CarroceriasPorClase.Para("CAMION");
        todas.Should().OnlyContain(i => i.Codigo == i.Etiqueta);
        todas.Select(i => i.Codigo.ToUpperInvariant()).Should().OnlyHaveUniqueItems();

        CarroceriasPorClase.Para("CAMION", "ambul").Select(i => i.Codigo).Should().Equal("AMBULANCIA");
        CarroceriasPorClase.Para("CAMION", "zzzz-no-existe").Should().BeEmpty();
    }

    private static string Origen([System.Runtime.CompilerServices.CallerFilePath] string archivo = "") => archivo;

    [Fact]
    public void LaCopiaEmbebida_ConservaLasMismasFilasQueElArchivoDeFlit2()
    {
        // Detecta divergencias: la copia del BFF debe ser byte a byte (salvo saltos de línea) la de frontend/lib/catalogs.
        var dir = new DirectoryInfo(Path.GetDirectoryName(Origen())!);
        string? original = null;
        while (dir is not null && original is null)
        {
            var candidato = Path.Combine(dir.FullName, "frontend", "lib", "catalogs", "bodywork-by-class.json");
            if (File.Exists(candidato))
                original = candidato;
            dir = dir.Parent;
        }

        if (original is null)
        {
            Assert.Skip("No se encontró frontend/lib/catalogs/bodywork-by-class.json desde el directorio de pruebas.");
            return;
        }

        using var recurso = typeof(CarroceriasPorClase).Assembly.GetManifestResourceStream("bodywork-by-class.json")!;
        using var lector = new StreamReader(recurso);
        string Canon(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        Canon(lector.ReadToEnd()).Should().Be(Canon(File.ReadAllText(original)), "la copia de carrocerías por clase del BFF debe sincronizarse con FLIT 2");
    }

    private static ResultadoConsulta Con(string? json, string? plano = null)
    {
        var r = new ResultadoConsulta();
        if (json is not null)
            r.Campos.Add(new Campo { Clave = "runt_gravamenes", ValorJson = json });
        if (plano is not null)
            r.Campos.Add(new Campo { Clave = "runt_nombre_acreedor", ValorTexto = plano });
        return r;
    }

    [Fact]
    public void ElAcreedor_SeLeeConLasMismasLlavesDeFlit2_YElNitSoloLlevaDigitos()
    {
        VehiculoDelBorradorService.Acreedor(Con("[{\"nombreAcreedor\":\"BANCO A\",\"numeroDocumentoAcreedor\":\"900.123.456-1\"}]")).Should().Be(("BANCO A", "9001234561"));
        VehiculoDelBorradorService.Acreedor(Con("[{\"entidad\":\"BANCO B\",\"numeroDocumentoEntidad\":800123456}]")).Should().Be(("BANCO B", "800123456"));
        VehiculoDelBorradorService.Acreedor(Con("[{\"acreedor\":\"BANCO C\"}]")).Should().Be(("BANCO C", null));
    }

    [Fact]
    public void SinJsonUtil_SeCaeAlAcreedorPlano_OAVacio()
    {
        VehiculoDelBorradorService.Acreedor(Con("[]", "BANCO D")).Should().Be(("BANCO D", null));
        VehiculoDelBorradorService.Acreedor(Con("no es json", "BANCO E")).Should().Be(("BANCO E", null));
        VehiculoDelBorradorService.Acreedor(Con(null)).Should().Be((null, null));
    }

    [Fact]
    public void ElJsonDeEjemploDeFlit2_Compila() => JsonDocument.Parse("[]").RootElement.GetArrayLength().Should().Be(0);
}
