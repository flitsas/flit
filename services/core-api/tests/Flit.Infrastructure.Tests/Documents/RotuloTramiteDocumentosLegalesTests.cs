using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// ADR-0050 — el rótulo del trámite en los documentos legales es el NOMBRE del tipo.
/// <para>El mandato y la solicitud virtual elegían entre dos literales: todo lo que no fuera
/// traspaso se firmaba como "MATRÍCULA INICIAL". El mandato de un blindaje o de un levantamiento de
/// prenda nombraba un trámite distinto del que se estaba radicando, en un documento que el
/// otorgante firma y el organismo archiva.</para>
/// </summary>
public sealed class RotuloTramiteDocumentosLegalesTests
{
    private static FurDocumentData Data(string? tipoNombre, bool requiereVendedor) =>
        new(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "TRM-2026-000123",
            Modalidad: "OTROS",
            TipologiaCodigo: "BLINDAJE",
            Vehiculo: new VehiculoDatos(
                Marca: "BAJAJ", Linea: "PULSAR", Modelo: "2024", Color: "NEGRO",
                Clase: "MOTOCICLETA", Combustible: "GASOLINA", Cilindraje: "200",
                Vin: "9BWZZZ377VT004251", Placa: "IWL38D"),
            Organismo: new OrganismoTransito("11001", "Secretaría de Movilidad", "Bogotá"),
            Partes: [],
            ValorVenta: null,
            Causal: null,
            SellosFirma: [],
            TipoNombre: tipoNombre,
            RequiereVendedor: requiereVendedor);

    [Theory]
    [InlineData("Blindaje", "BLINDAJE")]
    [InlineData("Levantamiento de prenda", "LEVANTAMIENTO DE PRENDA")]
    [InlineData("Duplicado de tarjeta", "DUPLICADO DE TARJETA")]
    [InlineData("Cambio de color", "CAMBIO DE COLOR")]
    public void ElMandatoNombraElTramiteReal(string nombre, string esperado)
    {
        MandatoPdfGenerator.RotuloTramite(Data(nombre, requiereVendedor: false))
            .Should().Be(esperado);
    }

    [Fact]
    public void SinNombreDelTipo_CaeAlRotuloHeredado()
    {
        // Respaldo para los documentos que aún no traen el nombre; la capacidad decide cuál de los dos.
        MandatoPdfGenerator.RotuloTramite(Data(null, requiereVendedor: true))
            .Should().Be("TRASPASO DE PROPIEDAD");
        MandatoPdfGenerator.RotuloTramite(Data(null, requiereVendedor: false))
            .Should().Be("MATRÍCULA INICIAL");
    }

    [Fact]
    public void UnTramiteDeOtrosYaNoSeFirmaComoMatriculaInicial()
    {
        // El defecto concreto que ADR-0050 corrige.
        MandatoPdfGenerator.RotuloTramite(Data("Blindaje", requiereVendedor: false))
            .Should().NotBe("MATRÍCULA INICIAL");
    }

    [Fact]
    public void ElNombreSeNormalizaAMayusculas_ComoElRestoDelDocumento()
    {
        MandatoPdfGenerator.RotuloTramite(Data("  traspaso unilateral  ", requiereVendedor: true))
            .Should().Be("TRASPASO UNILATERAL");
    }
}
