using System.Reflection;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Cómo aparece el MANDATARIO en el recuadro de firmas del contrato de mandato.
///
/// <para>Precedencia: el modo que trae el caller gana (<c>SinBloque</c> institucional/convenio,
/// <c>Manual</c> abierto o firma física). Plantilla Sabaneta sin modo Manual/Estampada explícito →
/// sin bloque. Familia organismo (Bello) → línea manual. El resto estampa.</para>
/// </summary>
public sealed class MandatoFirmaPorConvenioTests
{
    private static readonly MethodInfo Modo = typeof(MandatoPdfGenerator)
        .GetMethod("ModoFirmaMandatario", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static MandatarioFirmaModo Resolver(MandatoData data) =>
        (MandatarioFirmaModo)Modo.Invoke(null, [data])!;

    [Fact]
    public void A_SinConvenio_ElMandatarioFirma()
    {
        // El mandatario es un actor obligatorio del trámite: sin convenio que lo releve, su firma va.
        Resolver(Mandato(MandatarioFirmaModo.Estampada))
            .Should().Be(MandatarioFirmaModo.Estampada);
    }

    [Fact]
    public void B_ConConvenio_NoHayBloqueDeMandatario()
    {
        Resolver(Mandato(MandatarioFirmaModo.SinBloque))
            .Should().Be(MandatarioFirmaModo.SinBloque);
    }

    [Theory]
    [InlineData(MandatarioFirmaModo.Estampada, MandatarioFirmaModo.SinBloque)]
    [InlineData(MandatarioFirmaModo.SinBloque, MandatarioFirmaModo.SinBloque)]
    public void C_Sabaneta_SinModoManual_NoLlevaBloque(MandatarioFirmaModo resuelto, MandatarioFirmaModo esperado)
    {
        // Institucional / default Sabaneta: el mandatario es la UT, no una persona.
        Resolver(Mandato(resuelto, MandatoFamilia.OrganismoTransito, MandatoTemplateResolver.Sabaneta))
            .Should().Be(esperado);
    }

    [Fact]
    public void C2_Sabaneta_ConModoManual_Abierto_ConservaBloqueConLineas()
    {
        // Tipo Abierto sobre plantilla sabaneta: el caller pide Manual → líneas abiertas, no SinBloque.
        Resolver(Mandato(MandatarioFirmaModo.Manual, MandatoFamilia.OrganismoTransito, MandatoTemplateResolver.Sabaneta))
            .Should().Be(MandatarioFirmaModo.Manual);
    }

    [Fact]
    public void Bello_SiConservaElBloque_ConSuLineaParaFirmarAMano()
    {
        // Bello es de la misma familia que Sabaneta pero su plantilla nombra al REPRESENTANTE LEGAL de
        // la unión temporal: hay una persona que puede firmar, así que pierde la estampa pero no el
        // bloque. Agruparlo con Sabaneta le quitaría el espacio de firma a alguien que sí firma.
        Resolver(Mandato(
                MandatarioFirmaModo.Estampada, MandatoFamilia.OrganismoTransito, MandatoTemplateResolver.Bello))
            .Should().Be(MandatarioFirmaModo.Manual);
    }

    [Fact]
    public void UnOtInstitucionalSinPlantillaPropia_FirmaAMano_AunqueNoHayaConvenio()
    {
        // Familia organismo_transito con plantilla genérica: firma la empresa, y lo hace a mano sobre la
        // línea. No depende del convenio: es la naturaleza del mandatario.
        Resolver(Mandato(MandatarioFirmaModo.Estampada, MandatoFamilia.OrganismoTransito))
            .Should().Be(MandatarioFirmaModo.Manual);
    }

    [Fact]
    public void ElConvenioGanaSobreLaFamilia_UnOtInstitucionalConConvenioTampocoLlevaBloque()
    {
        // Con convenio no hay a quién dejarle espacio de firma, sea quien sea el mandatario. Si la
        // familia ganara, un OT institucional con convenio seguiría pintando su recuadro vacío.
        Resolver(Mandato(MandatarioFirmaModo.SinBloque, MandatoFamilia.OrganismoTransito))
            .Should().Be(MandatarioFirmaModo.SinBloque);
    }

    [Fact]
    public void ElMandatarioMarcadoComoFirmanteFisico_ConservaElBloqueConLaLinea()
    {
        Resolver(Mandato(MandatarioFirmaModo.Manual))
            .Should().Be(MandatarioFirmaModo.Manual);
    }

    [Fact]
    public void PorDefecto_ElBloqueSeConserva()
    {
        // Invariante de resguardo: el default del modelo no puede hacer desaparecer a un actor
        // obligatorio si alguien construye el documento sin resolver la política.
        var data = new MandatoData(
            Tramite(), MandatoTemplateResolver.Generico, null, null, null);

        data.ModoFirmaMandatario.Should().Be(MandatarioFirmaModo.Estampada);
        Resolver(data).Should().Be(MandatarioFirmaModo.Estampada);
    }

    [Fact]
    public void ElCuerpoDelContratoSigueNombrandoAlMandatario_AunSinBloqueDeFirma()
    {
        // Lo que quita el convenio es el ESPACIO DE FIRMA, no al actor: el contrato tiene que seguir
        // diciendo a nombre de quién se otorga el mandato.
        var data = Mandato(MandatarioFirmaModo.SinBloque) with
        {
            Mandatario = new MandatarioFirmante("Carlos Ruiz", "70111222", null, null, null),
        };

        var pdf = new MandatoPdfGenerator().GenerateMandato(data);

        pdf.Content.Should().NotBeEmpty();
    }

    [Fact]
    public void Abierto_Generico_CuerpoConPlaceholdersDePersona()
    {
        var build = typeof(MandatoPdfGenerator)
            .GetMethod("BuildParrafos", BindingFlags.NonPublic | BindingFlags.Static)!;
        var data = Mandato(MandatarioFirmaModo.Manual) with { Mandatario = null };
        var parte = data.Tramite.Partes[0];
        var parrafos = (List<IReadOnlyList<MandatoPdfGenerator.ParrafoSegmento>>)build.Invoke(
            null,
            [
                data, parte, false, MandatoVariante.Generico,
                "TRASPASO", "PJS615", "ENVIGADO", "ENVIGADO", "5 de agosto de 2026",
            ])!;
        var texto = string.Join("\n", parrafos.Select(p => string.Concat(p.Select(s => s.Texto))));
        texto.Should().Contain("Y de ___ identificado con la cédula de ciudadanía No ___");
        texto.Should().NotContain("Carlos Ruiz");
    }

    [Fact]
    public void Abierto_Bello_CuerpoConPlaceholdersDePersona()
    {
        var build = typeof(MandatoPdfGenerator)
            .GetMethod("BuildParrafos", BindingFlags.NonPublic | BindingFlags.Static)!;
        var data = Mandato(
                MandatarioFirmaModo.Manual,
                MandatoFamilia.OrganismoTransito,
                MandatoTemplateResolver.Bello) with
            { Mandatario = null };
        var parte = data.Tramite.Partes[0];
        var parrafos = (List<IReadOnlyList<MandatoPdfGenerator.ParrafoSegmento>>)build.Invoke(
            null,
            [
                data, parte, false, MandatoVariante.Bello,
                "MATRICULA", "QXU635", "BELLO", "Bello", "5 de agosto de 2026",
            ])!;
        var texto = string.Join("\n", parrafos.Select(p => string.Concat(p.Select(s => s.Texto))));
        texto.Should().Contain("Y de la otra parte, ___ identificado con la cédula de ciudadanía No ___");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MandatoData Mandato(
        MandatarioFirmaModo modo,
        MandatoFamilia familia = MandatoFamilia.Individuo,
        string plantilla = MandatoTemplateResolver.Generico) =>
        new(
            Tramite(),
            plantilla,
            familia == MandatoFamilia.OrganismoTransito ? "UNION TEMPORAL SETSA" : null,
            familia == MandatoFamilia.OrganismoTransito ? "900273813-7" : null,
            new MandatarioFirmante("Carlos Ruiz", "70111222", null, null, null),
            familia,
            ModoFirmaMandatario: modo);

    private static FurDocumentData Tramite() =>
        new(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "TRM-2026-000001",
            Modalidad: "matricula",
            TipologiaCodigo: "MATRICULA_NUEVA",
            Vehiculo: new VehiculoDatos(null, null, null, null, null, null, null, "VIN123", "ABC123"),
            Organismo: new OrganismoTransito("5631000", "STRIA MOVILIDAD", "Sabaneta"),
            Partes: [new DocumentParte("comprador", "Juan Pérez", "123456", null, "CC")],
            ValorVenta: null,
            Causal: null,
            SellosFirma: [],
            TemplateFormat: FurTemplateFormat.Automotor);
}
