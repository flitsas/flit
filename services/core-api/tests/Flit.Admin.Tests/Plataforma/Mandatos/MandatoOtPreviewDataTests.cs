using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Api.Endpoints;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #11719 — la vista previa de UN organismo tiene que nombrar a ESE organismo. Antes se armaba
/// solo con el código de plantilla, así que tomaba el OT canónico de cada redacción y la vista de
/// Bogotá decía «SECRETARIA DE MOVILIDAD DE MEDELLIN … en la ciudad de Medellín».
/// </summary>
public sealed class MandatoOtPreviewDataTests
{
    private static MandateOtConfigView View(
        string templateCode,
        string code = "11001000",
        string name = "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA",
        string assignmentMode = "signer",
        string mandataryFamily = "individuo") =>
        new(
            OfficeId: Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001"),
            Code: code,
            Name: name,
            TemplateCode: templateCode,
            RequiresForNaturalPerson: true,
            MandataryFamily: mandataryFamily,
            InstitutionalMandataryName: null,
            InstitutionalMandataryNit: null,
            ChamberCity: null,
            MandatarySigla: null,
            HasExplicitConfig: true,
            RowVersion: null,
            AssignmentMode: assignmentMode);

    [Fact]
    public void BuildOtPreviewData_NombraElOrganismoDeLaConfiguracion()
    {
        // AC1 y AC2.
        var data = AdminPlataformaMandatosEndpoints.BuildOtPreviewData(View(MandatoTemplateResolver.Generico), null);

        data.Tramite.Organismo.Should().NotBeNull();
        data.Tramite.Organismo!.Codigo.Should().Be("11001000");
        data.Tramite.Organismo.Nombre.Should().Be("SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA");
        data.Tramite.Organismo.Nombre.Should().NotContain("MEDELLIN");
    }

    [Theory]
    [InlineData(MandatoTemplateResolver.Generico)]
    [InlineData(MandatoTemplateResolver.Sabaneta)]
    [InlineData(MandatoTemplateResolver.Bello)]
    [InlineData(MandatoTemplateResolver.Municipio)]
    public void BuildOtPreviewData_NingunaRedaccionArrastraSuOrganismoCanonico(string templateCode)
    {
        // AC1 — la elección de redacción no puede cambiar a QUÉ organismo nombra la vista previa.
        // Es lo que fallaba: «municipio» traía Envigado y la genérica, Medellín.
        var data = AdminPlataformaMandatosEndpoints.BuildOtPreviewData(
            View(templateCode, code: "25286000", name: "STRIA TTOyTTE MCPAL FUNZA"), null);

        data.Tramite.Organismo!.Codigo.Should().Be("25286000");
        data.Tramite.Organismo.Nombre.Should().Be("STRIA TTOyTTE MCPAL FUNZA");
    }

    [Fact]
    public void BuildOtPreviewData_LaCiudadVaComoMarcador()
    {
        // AC3 — catalogs.transit_offices solo guarda el código DIVIPOLA, así que la cláusula de
        // cierre usa el marcador en vez de imprimir un código o un municipio ajeno (HU #11016).
        var data = AdminPlataformaMandatosEndpoints.BuildOtPreviewData(View(MandatoTemplateResolver.Municipio), null);

        data.Tramite.Organismo!.Ciudad.Should().Be(MandatoPreviewSample.PhCiudadOrganismo);
    }

    [Fact]
    public void BuildOtPreviewData_ConservaLosMarcadoresDelMandante()
    {
        // AC5 — la vista previa por OT sigue con marcadores entre corchetes; los datos de MUESTRA
        // son del simulador (HU #11706), y confundir las dos cosas haría pasar una simulación por
        // un documento emitido.
        var data = AdminPlataformaMandatosEndpoints.BuildOtPreviewData(View(MandatoTemplateResolver.Generico), null);

        data.Tramite.Mandante!.Nombre.Should().Be(MandatoPreviewSample.PhRazonSocial);
        data.Tramite.Placa.Should().Be(MandatoPreviewSample.PhPlaca);
    }

    [Fact]
    public void Build_SinOrganismo_SigueUsandoElCanonicoDeLaPlantilla()
    {
        // AC4 — la vista previa POR PLANTILLA (la del catálogo, que no tiene organismo) no cambia.
        var data = MandatoPreviewSample.Build(MandatoTemplateResolver.Municipio);

        data.Tramite.Organismo!.Nombre.Should().Be("STRIA TTEyTTO ENVIGADO");
    }
}
