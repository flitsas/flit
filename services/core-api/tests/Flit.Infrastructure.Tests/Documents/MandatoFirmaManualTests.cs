using System.Reflection;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #11205 (D4) — con EMPRESA RELACIONADA como mandatario, su firma es manual: no se plasma su
/// validación de identidad, pero sí queda la línea sobre su identificación. El MANDANTE no cambia.
///
/// <para>Se afirma sobre la decisión de política (<c>MandatarioEsEmpresa</c>) y sobre el bloque de
/// identificación que se imprime bajo la línea, que es donde se ve a nombre de quién queda la firma.</para>
/// </summary>
public sealed class MandatoFirmaManualTests
{
    private static readonly MethodInfo EsEmpresa = typeof(MandatoPdfGenerator)
        .GetMethod("MandatarioEsEmpresa", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void AC1_ConEmpresaRelacionada_NoSePlasmaLaFirmaDeValidacionDeIdentidad()
    {
        var data = Mandato(MandatoFamilia.OrganismoTransito, instName: "UNION TEMPORAL SETSA");

        EsEmpresa.Invoke(null, [data]).Should().Be(true);
    }

    [Fact]
    public void AC1_TambienAplicaABello_AunqueSuPlantillaTraigaBloqueDeFirmaElectronica()
    {
        // D4: manda la regla. Los .md de Bello y Sabaneta traen "Firmado electrónicamente… Hash:" para el
        // mandatario, pero se usan como fuente del TEXTO LEGAL, no de la política de firma.
        var bello = Mandato(
            MandatoFamilia.OrganismoTransito,
            template: MandatoTemplateResolver.Bello,
            instName: "UNION TEMPORAL MOVILIDAD AVANZADA DE BELLO MAB");

        EsEmpresa.Invoke(null, [bello]).Should().Be(true);
    }

    [Fact]
    public void AC2_QuedaLaIdentificacionDeLaEmpresaBajoLaLineaDeFirma()
    {
        // Quien firma a mano es la empresa: bajo la línea va su razón social y su NIT. Poner ahí la
        // cédula de la persona firmante haría que el documento dijera que firmó alguien distinto.
        var data = Mandato(
            MandatoFamilia.OrganismoTransito,
            instName: "UNION TEMPORAL SETSA",
            instNit: "900273813-7");

        var lineas = MandatoPdfGenerator
            .MandatarioIdentificacion(new MandatarioFirmante("Carlos Ruiz", "70111222"), data)
            .ToList();

        lineas.Should().Contain(l => l.Contains("RAZÓN SOCIAL: UNION TEMPORAL SETSA"));
        lineas.Should().Contain(l => l.Contains("NIT: 900273813-7"));
        lineas.Should().NotContain(l => l.Contains("70111222"));
    }

    [Fact]
    public void AC3_SinEmpresaRelacionada_ElMandatarioFirmaConSuValidacionDeIdentidad()
    {
        var data = Mandato(MandatoFamilia.Individuo);

        EsEmpresa.Invoke(null, [data]).Should().Be(false);

        var lineas = MandatoPdfGenerator
            .MandatarioIdentificacion(new MandatarioFirmante("Carlos Ruiz", "70111222"), data)
            .ToList();

        lineas.Should().Contain(l => l.Contains("NOMBRE: Carlos Ruiz"));
        lineas.Should().Contain(l => l.Contains("70111222"));
    }

    [Fact]
    public void UnOtSinFamiliaConfigurada_PeroConMandatarioInstitucional_SigueSiendoEmpresa()
    {
        // Señal heredada: los OT configurados antes de la HU #11204 no tienen familia, pero sí nombre
        // institucional. Sin esta guarda perderían la firma manual hasta que alguien los reconfigurara.
        var data = Mandato(MandatoFamilia.Individuo, instName: "UNION TEMPORAL SETSA");

        EsEmpresa.Invoke(null, [data]).Should().Be(true);
    }

    [Fact]
    public void AC4_ElMandanteNoCambiaEnNingunoDeLosDosCasos()
    {
        // El bloque del mandante no depende de la política del mandatario: lo pinta RenderMandanteFirma,
        // que no recibe ni la familia ni el mandatario.
        var mandante = typeof(MandatoPdfGenerator)
            .GetMethod("RenderMandanteFirma", BindingFlags.NonPublic | BindingFlags.Static);

        mandante.Should().NotBeNull();
        mandante!.GetParameters().Select(p => p.ParameterType.Name)
            .Should().NotContain(nameof(MandatoData),
                "el bloque del mandante no puede depender de la configuración del mandatario");
    }

    private static MandatoData Mandato(
        MandatoFamilia familia,
        string template = MandatoTemplateResolver.Sabaneta,
        string? instName = null,
        string? instNit = null)
    {
        var parte = new DocumentParte("vendedor", "Renting S.A.S.", "900123456-7", null, "NIT", EsJuridica: true);
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

        return new MandatoData(
            tramite, template, instName, instNit,
            new MandatarioFirmante("Carlos Ruiz", "70111222"), familia);
    }
}
