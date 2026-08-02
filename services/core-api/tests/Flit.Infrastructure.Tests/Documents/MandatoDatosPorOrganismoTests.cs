using System.Reflection;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #11204 — datos propios del organismo en la configuración, no en el código. Se afirma sobre el
/// TEXTO LEGAL que produce el generador (los párrafos), porque es ahí donde el dato aparece: un test
/// que solo comprobara que sale un PDF no distinguiría una ciudad de otra.
/// </summary>
public sealed class MandatoDatosPorOrganismoTests
{
    private static readonly MethodInfo BuildParrafos = typeof(MandatoPdfGenerator)
        .GetMethod("BuildParrafos", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void AC3_LaCiudadDeLaCamaraSaleDeLaConfiguracionDelOrganismo()
    {
        var texto = Parrafos(MandatoTemplateResolver.Generico, chamberCity: "Bogotá");

        texto.Should().Contain("Comercio de Bogotá");
        texto.Should().NotContain("Comercio de Medellín");
    }

    [Fact]
    public void AC5_SinConfiguracion_LaCiudadDeLaCamaraQuedaComoHastaAhora()
    {
        var texto = Parrafos(MandatoTemplateResolver.Generico);

        texto.Should().Contain("Comercio de Medellín");
    }

    [Fact]
    public void AC2_LaSiglaDeLaUnionTemporalSaleDeLaConfiguracion()
    {
        var texto = Parrafos(MandatoTemplateResolver.Sabaneta, sigla: "UT-XYZ");

        texto.Should().Contain("indemne a la UT-XYZ");
        texto.Should().NotContain("indemne a la UT-SETSA");
    }

    [Fact]
    public void AC5_SinSiglaConfigurada_SeConservaLaDeSiempre()
    {
        var texto = Parrafos(MandatoTemplateResolver.Sabaneta);

        texto.Should().Contain("indemne a la UT-SETSA");
    }

    [Fact]
    public void AC2_ElMandatarioEmpresaSeNombraConSuRazonSocialYNit()
    {
        var texto = Parrafos(
            MandatoTemplateResolver.Sabaneta,
            instName: "UNION TEMPORAL DE PRUEBA",
            instNit: "900999888-1");

        texto.Should().Contain("UNION TEMPORAL DE PRUEBA");
        texto.Should().Contain("900999888-1");
    }

    [Fact]
    public void AC3_UnOrganismoNuevoReutilizaUnaRedaccionConSusPropiosDatos()
    {
        // Es el caso que la HU viene a habilitar: mismo formato que Sabaneta, otros datos, sin código.
        var texto = Parrafos(
            MandatoTemplateResolver.Sabaneta,
            chamberCity: "Cali",
            sigla: "UT-NUEVA",
            instName: "UNION TEMPORAL NUEVA",
            instNit: "901000000-2");

        texto.Should().Contain("Comercio de Cali");
        texto.Should().Contain("indemne a la UT-NUEVA");
        texto.Should().Contain("UNION TEMPORAL NUEVA");
    }

    [Fact]
    public void AC4_PersonaNaturalYJuridicaUsanCadaUnaSuRedaccionDelMandante()
    {
        var juridica = Parrafos(MandatoTemplateResolver.Sabaneta);
        var natural = Parrafos(MandatoTemplateResolver.Sabaneta, esJuridica: false);

        juridica.Should().Contain("en representación legal de");
        natural.Should().NotContain("en representación legal de");
        natural.Should().Contain("en representación propia");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Texto legal completo que produce el generador para esa configuración de OT.</summary>
    private static string Parrafos(
        string template,
        string? chamberCity = null,
        string? sigla = null,
        string? instName = null,
        string? instNit = null,
        bool esJuridica = true)
    {
        var parte = esJuridica
            ? new DocumentParte(
                "vendedor", "Renting S.A.S.", "900123456-7", "info@renting.com", "NIT", null,
                EsJuridica: true,
                RepresentanteLegalNombre: "Ana Gómez",
                RepresentanteLegalTipoDoc: "CC",
                RepresentanteLegalDocumento: "52123456")
            : new DocumentParte("vendedor", "Juan Pérez", "123456", "juan@x.com", "CC");

        var tramite = new FurDocumentData(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "REF-2026-1",
            Modalidad: "matricula",
            TipologiaCodigo: "MATRICULA_NUEVA",
            Vehiculo: new VehiculoDatos(null, null, null, null, null, null, null, "VIN123", "ABC123"),
            Organismo: new OrganismoTransito("5631000", "STRIA MOVILIDAD SABANETA", "Sabaneta"),
            Partes: [parte],
            ValorVenta: null,
            Causal: null,
            SellosFirma: [],
            FirmasVisibles: true);

        var data = new MandatoData(
            tramite,
            template,
            instName,
            instNit,
            new MandatarioFirmante("Carlos Ruiz", "70111222"),
            MandatoFamilia.OrganismoTransito,
            chamberCity,
            sigla);

        var parrafos = (List<string>)BuildParrafos.Invoke(
            null,
            [
                data, parte, esJuridica, MandatoTemplateResolver.Resolve(template),
                "MATRÍCULA INICIAL", "ABC123", "Sabaneta", "Sabaneta", "1 de agosto de 2026",
            ])!;

        return string.Join("\n", parrafos);
    }
}
