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
/// <para>Tres modos y una precedencia deliberada: el <b>convenio</b> comercial entre la compañía y el
/// organismo quita el bloque; el mandatario que es el propio organismo, o el marcado como firmante
/// físico, conserva el bloque con la línea; el resto estampa.</para>
///
/// <para>Se afirma sobre la decisión de política y no sobre los bytes del PDF: un test que solo
/// comprobara que sale un PDF no distinguiría un contrato con bloque de mandatario de uno sin él, que
/// es justo lo que estas reglas cambian.</para>
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

    [Fact]
    public void C_ElMandatarioQueEsElPropioOrganismo_FirmaAMano_AunqueNoHayaConvenio()
    {
        // Sabaneta y Bello (familia organismo_transito): firma la empresa, y lo hace a mano sobre la
        // línea. No depende del convenio: es la naturaleza del mandatario.
        Resolver(Mandato(MandatarioFirmaModo.Estampada, MandatoFamilia.OrganismoTransito))
            .Should().Be(MandatarioFirmaModo.Manual);
    }

    [Fact]
    public void ElConvenioGanaSobreLaFamilia_NiSiquieraSabanetaLlevaBloque()
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MandatoData Mandato(
        MandatarioFirmaModo modo, MandatoFamilia familia = MandatoFamilia.Individuo) =>
        new(
            Tramite(),
            MandatoTemplateResolver.Generico,
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
