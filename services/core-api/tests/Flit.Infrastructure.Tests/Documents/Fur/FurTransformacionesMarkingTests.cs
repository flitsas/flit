using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

/// <summary>
/// HU #11641 — marcación de los SUBTRÁMITES SIMULTÁNEOS en la rejilla "3. TRÁMITE SOLICITADO".
///
/// <para>Hasta esta HU el mapper solo emitía cuatro casillas (1, 2, 11, 12). Un trámite de matrícula
/// con cambio de color declaraba la transformación únicamente en el texto de OBSERVACIONES, así que
/// el organismo de tránsito tenía que deducir el alcance real del trámite leyendo el recuadro en vez
/// de mirar las casillas, que es para lo que están.</para>
///
/// <para>Estas casillas son INDEPENDIENTES del tipo de trámite y de la prenda: una matrícula inicial
/// con cambio de color y constitución de gravamen marca 1 + 5 + 11.</para>
/// </summary>
public sealed class FurTransformacionesMarkingTests
{
    private static FurDocumentData Data(
        FurTransformacionesDeclaradas transformaciones,
        FurPrendaMarking prenda = FurPrendaMarking.Ninguna,
        string modalidad = "matricula_inicial",
        string? combustible = "GASOLINA") => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000001",
        Modalidad: modalidad,
        TipologiaCodigo: modalidad,
        Vehiculo: new VehiculoDatos(
            Marca: "TESLA", Linea: "MODELO Y", Modelo: "2026", Color: "BLANCO",
            Clase: "CAMIONETA", Combustible: combustible, Cilindraje: "0",
            Vin: "LRWYGCFJ7TC495717", Placa: "YYY090"),
        Organismo: new OrganismoTransito("25286000", "OT", "CIUDAD"),
        Partes: [new DocumentParte("comprador", "DANIEL AMADO", "1193552679", null, DocumentType: "CC")],
        ValorVenta: null, Causal: null, SellosFirma: [],
        PrendaMarking: prenda,
        Transformaciones: transformaciones);

    private static string? Casilla(FurDocumentData data, string id) =>
        FurFieldMapper.Map(data).TryGetValue(id, out var v) ? v.Text : null;

    [Fact]
    public void CambioDeColor_MarcaLaCasilla5()
    {
        var dict = FurFieldMapper.Map(Data(new FurTransformacionesDeclaradas(Color: true)));

        dict["requested_process_5"].Text.Should().Be("X");
        dict["requested_process_17"].Text.Should().BeEmpty("no se declaró cambio de carrocería");
        dict["requested_process_18"].Text.Should().BeEmpty("no se declaró cambio de combustible");
    }

    [Fact]
    public void CambioDeCarroceria_MarcaLaCasilla17()
    {
        var dict = FurFieldMapper.Map(Data(new FurTransformacionesDeclaradas(Carroceria: true)));

        dict["requested_process_17"].Text.Should().Be("X");
        dict["requested_process_5"].Text.Should().BeEmpty();
        dict["requested_process_18"].Text.Should().BeEmpty();
    }

    /// <summary>
    /// El formulario oficial no tiene casilla de «cambio de combustible»: va en la 18 «OTROS» por
    /// decisión de negocio (supervisor, 2026-08-19). Esa celda quedó libre al corregir la geometría
    /// de la HU #11640, donde la ocupaba por error el levantamiento de prenda.
    /// </summary>
    [Fact]
    public void CambioDeCombustible_MarcaLaCasilla18Otros()
    {
        var dict = FurFieldMapper.Map(Data(new FurTransformacionesDeclaradas(Combustible: true)));

        dict["requested_process_18"].Text.Should().Be("X");
        dict["requested_process_5"].Text.Should().BeEmpty();
        dict["requested_process_17"].Text.Should().BeEmpty();
    }

    [Fact]
    public void SinTransformaciones_NoMarcaNingunaDeLasTres()
    {
        var dict = FurFieldMapper.Map(Data(default));

        dict["requested_process_5"].Text.Should().BeEmpty();
        dict["requested_process_17"].Text.Should().BeEmpty();
        dict["requested_process_18"].Text.Should().BeEmpty();
    }

    /// <summary>
    /// Las casillas de subtrámite no desplazan ni sustituyen a las de tipo de trámite y prenda: el
    /// formulario debe declarar TODO lo que el trámite incluye, no lo último que se marcó.
    /// </summary>
    [Fact]
    public void MatriculaConColorYPrenda_MarcaLasTresCasillas()
    {
        var dict = FurFieldMapper.Map(Data(
            new FurTransformacionesDeclaradas(Color: true), FurPrendaMarking.Constitucion));

        dict["requested_process_1"].Text.Should().Be("X", "sigue siendo una matrícula inicial");
        dict["requested_process_5"].Text.Should().Be("X", "con cambio de color");
        dict["requested_process_11"].Text.Should().Be("X", "y con constitución de prenda");
        dict["requested_process_2"].Text.Should().BeEmpty("no es un traspaso");
        dict["requested_process_12"].Text.Should().BeEmpty("no es un levantamiento");
    }

    [Fact]
    public void TodasLasTransformaciones_MarcanSusTresCasillas()
    {
        var dict = FurFieldMapper.Map(Data(new FurTransformacionesDeclaradas(true, true, true)));

        dict["requested_process_5"].Text.Should().Be("X");
        dict["requested_process_17"].Text.Should().Be("X");
        dict["requested_process_18"].Text.Should().Be("X");
    }

    [Fact]
    public void Blindaje_MarcaSiYDesmarcaNo()
    {
        var dict = FurFieldMapper.Map(Data(new FurTransformacionesDeclaradas(Blindaje: true)));

        dict["is_armored_vehicle_yes"].Text.Should().Be("X");
        dict["is_armored_vehicle_no"].Text.Should().BeEmpty();
    }

    [Fact]
    public void SinBlindaje_MarcaNo()
    {
        var dict = FurFieldMapper.Map(Data(default));

        dict["is_armored_vehicle_no"].Text.Should().Be("X");
        dict["is_armored_vehicle_yes"].Text.Should().BeEmpty();
    }

    // ── Combustible: valores del catálogo que no marcaban nada ────────────────

    /// <summary>
    /// HIBRIDO comparte casilla con MIXTO: el formulario no tiene casilla de híbrido y «MIXTO» es
    /// literalmente su caso (el vehículo se mueve con más de una fuente de energía). El catálogo del
    /// wizard ofrecía HIBRIDO desde su creación sin que ninguna casilla lo recogiera, de modo que esos
    /// vehículos salían con la sección 7 del FUR en blanco.
    /// </summary>
    [Theory]
    [InlineData("HIBRIDO")]
    [InlineData("MIXTO")]
    public void CombustibleHibridoOMixto_MarcaLaCasillaMixto(string combustible)
    {
        var dict = FurFieldMapper.Map(Data(default, combustible: combustible));

        dict["vehicle_fuel_type_4"].Text.Should().Be("X");
    }

    /// <summary>
    /// Los combustibles con casilla propia siguen marcando la suya y solo la suya: la ampliación de
    /// HIBRIDO no puede arrastrar a MIXTO valores que ya tenían sitio.
    /// </summary>
    [Theory]
    [InlineData("GASOLINA", "vehicle_fuel_type_1")]
    [InlineData("DIESEL", "vehicle_fuel_type_2")]
    [InlineData("GAS NATURAL", "vehicle_fuel_type_3")]
    [InlineData("ELECTRICO", "vehicle_fuel_type_5")]
    [InlineData("HIDROGENO", "vehicle_fuel_type_6")]
    [InlineData("ETANOL", "vehicle_fuel_type_7")]
    [InlineData("BIODIESEL", "vehicle_fuel_type_8")]
    public void CombustiblesConCasillaPropia_MarcanSoloLaSuya(string combustible, string esperada)
    {
        var dict = FurFieldMapper.Map(Data(default, combustible: combustible));

        dict[esperada].Text.Should().Be("X");
        Enumerable.Range(1, 8)
            .Select(i => $"vehicle_fuel_type_{i}")
            .Where(id => id != esperada)
            .Should().OnlyContain(id => dict[id].Text == string.Empty,
                "'{0}' solo puede marcar {1}", combustible, esperada);
    }

    /// <summary>
    /// El catálogo del wizard ofrece «OTRO», que el formulario oficial NO recoge: su sección 7 tiene
    /// ocho combustibles concretos y ninguna casilla comodín. Se deja constancia de la brecha en vez
    /// de forzar una marca falsa: elegir OTRO deja la sección en blanco, y la corrección correcta es
    /// de producto (capturar el combustible real o retirar la opción), no de este mapper.
    /// </summary>
    [Fact]
    public void CombustibleOtro_NoMarcaNinguna_BrechaConocida()
    {
        var dict = FurFieldMapper.Map(Data(default, combustible: "OTRO"));

        Enumerable.Range(1, 8)
            .Select(i => $"vehicle_fuel_type_{i}")
            .Should().OnlyContain(id => dict[id].Text == string.Empty);
    }
}
