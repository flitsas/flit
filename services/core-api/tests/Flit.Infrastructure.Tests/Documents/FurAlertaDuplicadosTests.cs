using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Numeral 20 DATOS DE ALERTA marcado por TIPO de trámite: duplicado de placa y duplicado de tarjeta
/// marcan OTRO (<c>alert_data_code_4</c>) sin gravamen de por medio, y dejan A FAVOR DE vacía porque
/// no hay acreedor (<c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c>, numeral 20).
/// <para>Hasta aquí esa columna solo se marcaba desde <see cref="FurDocumentData.PrendaMarking"/>, así
/// que estos dos trámites salían con el numeral 20 en blanco. Cada caso afirma también que las demás
/// columnas (HURTO, LIM. PROPIEDAD, EMBARGO) siguen vacías: marcar de más en el formulario oficial
/// devuelve el trámite igual que marcar de menos.</para>
/// </summary>
public sealed class FurAlertaDuplicadosTests
{
    private static FurDocumentData Data(string tipologiaCodigo, FurTemplateFormat format) => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000009",
        Modalidad: "OTROS",
        TipologiaCodigo: tipologiaCodigo,
        Vehiculo: new VehiculoDatos(
            Marca: "TESLA", Linea: "MODELO Y", Modelo: "2026", Color: "BLANCO",
            Clase: "CAMIONETA", Combustible: "ELECTRICO", Cilindraje: "0",
            Vin: "LRWYGCFJ7TC495717", Placa: "YYY090"),
        Organismo: new OrganismoTransito("25286000", "OT", "CIUDAD"),
        Partes: [new DocumentParte("comprador", "DANIEL AMADO", "1193552679", null, DocumentType: "CC")],
        ValorVenta: null, Causal: null, SellosFirma: [],
        TemplateFormat: format);

    public static IEnumerable<object[]> DuplicadosPorFormato()
    {
        foreach (var format in new[] { FurTemplateFormat.Automotor, FurTemplateFormat.Maquinaria, FurTemplateFormat.Remolques })
        foreach (var (codigo, casilla) in new[] { ("DUPLICADO_PLACA", 15), ("DUPLICADO_TARJETA", 10) })
            yield return [codigo, casilla, format];
    }

    [Theory]
    [MemberData(nameof(DuplicadosPorFormato))]
    public void Duplicado_MarcaOtroEnElNumeral20YConservaSuCasillaDelNumeral3(
        string codigo, int casilla, FurTemplateFormat format)
    {
        var campos = FurFieldMapper.Map(Data(codigo, format));

        campos[$"requested_process_{casilla}"].Text.Should().Be("X",
            "{0} marca su casilla del numeral 3 en {1}", codigo, format);
        campos["alert_data_code_4"].Text.Should().Be("X",
            "{0} marca OTRO en el numeral 20", codigo);
        campos["alert_data_code_1"].Text.Should().BeEmpty("HURTO no se marca: el motivo del duplicado no se captura");
        campos["alert_data_code_2"].Text.Should().BeEmpty("no hay gravamen");
        campos["alert_data_code_3"].Text.Should().BeEmpty("no hay embargo");
        campos["alert_data_code_5"].Text.Should().BeEmpty("sin acreedor, A FAVOR DE queda vacía");
    }

    [Theory]
    [InlineData("duplicado_placa")]
    [InlineData("Duplicado_Tarjeta")]
    public void Duplicado_SeReconoceSinImportarLaCajaDelCodigo(string codigo)
    {
        var campos = FurFieldMapper.Map(Data(codigo, FurTemplateFormat.Automotor));

        campos["alert_data_code_4"].Text.Should().Be("X");
    }

    [Theory]
    [InlineData("CAMBIO_COLOR")]
    [InlineData("CAMBIO_CARROCERIA")]
    [InlineData("MATRICULA_NUEVA")]
    public void OtrosTipos_SinGravamen_DejanElNumeral20EnBlanco(string codigo)
    {
        var campos = FurFieldMapper.Map(Data(codigo, FurTemplateFormat.Automotor));

        campos["alert_data_code_4"].Text.Should().BeEmpty(
            "solo marcan OTRO por tipo los duplicados y el radicado de cuenta");
        campos["alert_data_code_5"].Text.Should().BeEmpty();
    }

    [Fact]
    public void RadicadoCuenta_MarcaOtro_ConAFavorDeVacia()
    {
        // Entra por la misma vía que los duplicados: marca OTRO por el TIPO de trámite, no por un
        // gravamen. El organismo de destino se declara en el párrafo 23, no en «A FAVOR DE».
        var campos = FurFieldMapper.Map(Data("RADICADO_CUENTA", FurTemplateFormat.Automotor));

        campos["alert_data_code_4"].Text.Should().Be("X");
        campos["alert_data_code_5"].Text.Should().BeEmpty("no hay acreedor que escribir");
        campos["alert_data_code_1"].Text.Should().BeEmpty();
        campos["alert_data_code_2"].Text.Should().BeEmpty();
        campos["alert_data_code_3"].Text.Should().BeEmpty();
    }

    [Fact]
    public void RadicadoCuenta_MarcaLasDosCasillasDelNumeral3()
    {
        // La 4 dice que se radica una cuenta; la 18 (Otros) acompaña, porque el formulario no tiene
        // casilla para «a otro organismo», que es lo que el trámite hace.
        var campos = FurFieldMapper.Map(Data("RADICADO_CUENTA", FurTemplateFormat.Automotor));

        campos["requested_process_4"].Text.Should().Be("X");
        campos["requested_process_18"].Text.Should().Be("X");
    }

    [Fact]
    public void TrasladoCuenta_MarcaOtro_ConAFavorDeVacia()
    {
        var campos = FurFieldMapper.Map(Data("TRASLADO_CUENTA", FurTemplateFormat.Automotor));

        campos["alert_data_code_4"].Text.Should().Be("X");
        campos["alert_data_code_5"].Text.Should().BeEmpty("no hay acreedor que escribir");
    }

    [Fact]
    public void TrasladoCuenta_MarcaLasDosCasillasDelNumeral3()
    {
        // La 3 dice que la matrícula se traslada; la 18 (Otros) acompaña, porque el formulario no
        // tiene casilla para «a qué organismo», que es el dato del trámite.
        var campos = FurFieldMapper.Map(Data("TRASLADO_CUENTA", FurTemplateFormat.Automotor));

        campos["requested_process_3"].Text.Should().Be("X");
        campos["requested_process_18"].Text.Should().Be("X");
        campos["requested_process_4"].Text.Should().BeEmpty("la 4 es del radicado, no del traslado");
    }
}