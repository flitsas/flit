using System.Reflection;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #14413 — el contrato identifica con razón social y NIT al mandatario de modelo Persona jurídica. Se
/// afirma sobre el texto legal. El mandante es natural con CE para que «cédula de ciudadanía» solo pueda
/// venir del mandatario.
/// Uso de ejemplo: <c>Cuerpo(MandatoTemplateResolver.Municipio, Juridica)</c>.
/// </summary>
public sealed class MandatarioPersonaJuridicaTests
{
    private const string Inst = "UNION TEMPORAL DE PRUEBA";
    private const string InstNit = "900999888-1";
    private const string LiteralNatural = "Carlos Ruiz identificado con la cédula de ciudadanía No 70111222,";

    private static readonly MandatarioFirmante Juridica =
        new("ACME GESTION S.A.S.", "900123456-7", Modelo: MandateSignerOrigins.ModeloJuridica);

    private static readonly MethodInfo BuildParrafos = typeof(MandatoPdfGenerator)
        .GetMethod("BuildParrafos", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Theory]
    [InlineData(MandatoTemplateResolver.Generico)]
    [InlineData(MandatoTemplateResolver.Municipio)]
    public void AC1_GenericoYMunicipio_NombranLaRazonSocialYElNit(string template)
    {
        var texto = Cuerpo(template, Juridica);

        texto.Should().Contain(
            "Y de ACME GESTION S.A.S., con NIT No. 900123456-7, quien para los efectos del presente contrato se denominará EL MANDATARIO, hemos acordado");
        texto.Should().NotContain("cédula de ciudadanía");
    }

    [Fact]
    public void AC2_BelloSinUnionTemporal_IgualQueAC1()
    {
        var texto = Cuerpo(MandatoTemplateResolver.Bello, Juridica);

        texto.Should().Contain(
            "Y de ACME GESTION S.A.S., con NIT No. 900123456-7, quien para los efectos del presente contrato se denominará EL MANDATARIO, hemos acordado");
        texto.Should().NotContain("cédula de ciudadanía");
    }

    [Fact]
    public void AC2_BelloConUnionTemporal_LaEntidadNoEsRepresentanteLegalDeLaUnion()
    {
        var texto = Cuerpo(MandatoTemplateResolver.Bello, Juridica, Inst, InstNit);

        texto.Should().Contain(
            "Y de la otra parte, ACME GESTION S.A.S., con NIT No. 900123456-7, quien para efectos del presente contrato se denominará EL MANDATARIO, hemos acordado");
        texto.Should().NotContain("cédula de ciudadanía");
        texto.Should().NotContain("Representante Legal de");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(Inst, InstNit)]
    public void AC3_ElRecuadroIdentificaALaEntidad_AunqueHayaDatosInstitucionales(string? inst, string? nit)
    {
        var data = Datos(MandatoTemplateResolver.Generico, Juridica, inst, nit);

        MandatoPdfGenerator.MandatarioIdentificacion(Juridica, data)
            .Should().Equal("RAZÓN SOCIAL: ACME GESTION S.A.S.", "NIT: 900123456-7");
    }

    [Fact]
    public void AC3_SinRazonSocialNiNit_SeUsaLaLineaEnBlanco()
    {
        var vacio = new MandatarioFirmante(" ", null, Modelo: MandateSignerOrigins.ModeloJuridica);

        Cuerpo(MandatoTemplateResolver.Generico, vacio).Should().Contain("Y de ___, con NIT No. ___, quien");
        MandatoPdfGenerator.MandatarioIdentificacion(vacio).Should().Equal("RAZÓN SOCIAL: ___", "NIT: ___");
    }

    public static TheoryData<string, string?> FormatosYModelosSinEntidad()
    {
        var data = new TheoryData<string, string?>();
        foreach (var template in new[]
                 {
                     MandatoTemplateResolver.Generico, MandatoTemplateResolver.Municipio, MandatoTemplateResolver.Bello,
                 })
        {
            data.Add(template, MandateSignerOrigins.ModeloNatural); // AC5
            data.Add(template, MandateSignerOrigins.ModeloFormatoBlanco); // AC6
            data.Add(template, null); // AC7
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FormatosYModelosSinEntidad))]
    public void Caracterizacion_AC5_AC6_AC7_SinModeloJuridico_SigueLaCedula(string template, string? modelo)
    {
        var mandatario = new MandatarioFirmante("Carlos Ruiz", "70111222", Modelo: modelo);

        Cuerpo(template, mandatario).Should().Contain(LiteralNatural);
        MandatoPdfGenerator.MandatarioIdentificacion(mandatario, Datos(template, mandatario))
            .Should().Equal("NOMBRE: Carlos Ruiz", "CÉDULA DE CIUDADANÍA: 70111222");
    }

    [Fact]
    public void Caracterizacion_AC5_BelloConUnionTemporal_ConservaElRepresentanteLegal()
    {
        var mandatario = new MandatarioFirmante("Carlos Ruiz", "70111222", Modelo: MandateSignerOrigins.ModeloNatural);

        Cuerpo(MandatoTemplateResolver.Bello, mandatario, Inst, InstNit).Should().Contain(
            $"Y de la otra parte, {LiteralNatural} Representante Legal de {Inst}, con NIT No. {InstNit}, quien para efectos");
    }

    [Fact]
    public void Caracterizacion_AC6_Sabaneta_NombraALaUnionEnCuerpoYRecuadro()
    {
        var data = Datos(MandatoTemplateResolver.Sabaneta, Juridica, Inst, InstNit, MandatarioFirmaModo.Manual);

        var texto = Texto(data);

        texto.Should().Contain($"Y de {Inst}, con NIT N° {InstNit}, quien para efectos");
        texto.Should().NotContain("ACME GESTION");
        MandatoPdfGenerator.MandatarioIdentificacion(Juridica, data)
            .Should().Equal($"RAZÓN SOCIAL: {Inst}", $"NIT: {InstNit}");
    }

    [Fact]
    public void Caracterizacion_AC6_AbiertoEInstitucional_SinFirmante_NoCambian()
    {
        Cuerpo(MandatoTemplateResolver.Generico, null)
            .Should().Contain("Y de ___ identificado con la cédula de ciudadanía No ___,");
        MandatoPdfGenerator.MandatarioIdentificacion(null)
            .Should().Equal("NOMBRE: ___", "CÉDULA DE CIUDADANÍA: ___");
        MandatoPdfGenerator
            .MandatarioIdentificacion(null, Datos(MandatoTemplateResolver.Generico, null, Inst, InstNit))
            .Should().Equal($"RAZÓN SOCIAL: {Inst}", $"NIT: {InstNit}");
    }

    [Theory]
    [InlineData(MandatoTemplateResolver.Generico)]
    [InlineData(MandatoTemplateResolver.Bello)]
    public void Caracterizacion_AC6_ElObjetoYLasClausulasNoDependenDelModelo(string template)
    {
        var natural = new MandatarioFirmante("Carlos Ruiz", "70111222");

        Parrafos(Datos(template, Juridica)).Skip(1)
            .Should().Equal(Parrafos(Datos(template, natural)).Skip(1));
    }

    private static string Cuerpo(
        string template, MandatarioFirmante? mandatario, string? inst = null, string? nit = null) =>
        Texto(Datos(template, mandatario, inst, nit));

    private static string Texto(MandatoData data) => string.Join("\n", Parrafos(data));

    private static List<string> Parrafos(MandatoData data)
    {
        var parrafos = (List<IReadOnlyList<MandatoPdfGenerator.ParrafoSegmento>>)BuildParrafos.Invoke(
            null,
            [
                data, data.Tramite.Partes[0], false, MandatoTemplateResolver.Resolve(data.TemplateCode),
                "MATRÍCULA INICIAL", "ABC123", "Bogotá", "Bogotá", "1 de agosto de 2026",
            ])!;
        return parrafos.Select(p => string.Concat(p.Select(s => s.Texto))).ToList();
    }

    private static MandatoData Datos(
        string template,
        MandatarioFirmante? mandatario,
        string? inst = null,
        string? nit = null,
        MandatarioFirmaModo modo = MandatarioFirmaModo.Estampada)
    {
        var parte = new DocumentParte("vendedor", "Juan Pérez", "123456", "juan@x.com", "CE");
        var tramite = new FurDocumentData(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "REF-2026-1",
            Modalidad: "matricula",
            TipologiaCodigo: "MATRICULA_NUEVA",
            Vehiculo: new VehiculoDatos(null, null, null, null, null, null, null, "VIN123", "ABC123"),
            Organismo: new OrganismoTransito("11001000", "MOVILIDAD DE BOGOTA", "Bogotá"),
            Partes: [parte],
            ValorVenta: null,
            Causal: null,
            SellosFirma: []);
        return new MandatoData(tramite, template, inst, nit, mandatario, ModoFirmaMandatario: modo);
    }
}
