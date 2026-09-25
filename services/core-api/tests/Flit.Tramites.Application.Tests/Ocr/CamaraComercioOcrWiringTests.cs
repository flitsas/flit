using Flit.Tramites.Application.Ocr;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Ocr;

/// <summary>
/// HU #12776 — el cableado entre los códigos de adjunto por rol (HU #12774) y el OCR: sin esto el
/// certificado se carga pero nadie lo lee, y la fecha de expedición nunca llega a <c>field_values</c>.
/// </summary>
public sealed class CamaraComercioOcrWiringTests
{
    private static readonly string[] Roles = ["vendedor", "comprador", "locatario"];

    [Fact]
    public void LosTresCodigosPorRolTienenSoporteOcr() =>
        CamaraComercioAttachmentTipo.Todos.Should()
            .OnlyContain(t => DocumentOcrPrompts.IsSupported(t),
                "un adjunto sin prompt se carga pero no se analiza, y en silencio");

    /// <summary>
    /// El documento es el mismo: lo que cambia es a qué parte acredita, y eso ya lo dice el código.
    /// Mismo patrón que <c>inscripcion_prenda</c> / <c>prenda_registro</c>.
    /// </summary>
    [Fact]
    public void LosTresCodigosCompartenElPromptDeCamaraComercio()
    {
        var referencia = DocumentOcrPrompts.PromptFor("camara_comercio");

        referencia.Should().NotBeNull();
        foreach (var tipo in CamaraComercioAttachmentTipo.Todos)
        {
            DocumentOcrPrompts.PromptFor(tipo).Should().BeSameAs(referencia);
        }
    }

    /// <summary>El prompt compartido es el que extrae la fecha sobre la que se calcula la vigencia.</summary>
    [Fact]
    public void ElPromptPideLaFechaDeExpedicion() =>
        DocumentOcrPrompts.PromptFor(CamaraComercioAttachmentTipo.Vendedor)
            .Should().Contain("fecha_expedicion");

    [Fact]
    public void LosTresCodigosSonTiposDocumentalesOfrecidosAlWizard() =>
        CamaraComercioAttachmentTipo.Todos.Should()
            .OnlyContain(t => DocumentOcrPrompts.TiposDocumentales.Contains(t));

    // ── Persistencia de la fecha ─────────────────────────────────────────────

    [Fact]
    public void LosTresCodigosPersistenLoQueElOcrExtrae() =>
        CamaraComercioAttachmentTipo.Todos.Should()
            .OnlyContain(t => PersistOcrFieldsHandler.SoportaPersistencia(t));

    /// <summary>
    /// Una llave por rol: con una sola, en un traspaso entre dos sociedades la fecha del comprador
    /// pisaría la del vendedor y la alerta se calcularía sobre el documento equivocado.
    /// </summary>
    [Fact]
    public void LaLlaveDeLaFechaEsDistintaPorRol()
    {
        var llaves = Roles.Select(CamaraComercioFieldKeys.Expedicion).ToList();

        llaves.Should().OnlyHaveUniqueItems();
        llaves.Should().Contain("camara_comercio_expedicion_vendedor");
    }

    [Theory]
    [InlineData("VENDEDOR")]
    [InlineData("  Vendedor ")]
    public void LaLlaveSeNormaliza(string rol) =>
        CamaraComercioFieldKeys.Expedicion(rol).Should().Be("camara_comercio_expedicion_vendedor");

    /// <summary>
    /// Del prompt solo se persiste la fecha. Razón social, NIT y representante ya los tiene el trámite
    /// por el RUES y por la captura del actor: escribirlos aquí dejaría que un PDF escaneado
    /// compitiera con la fuente oficial.
    /// </summary>
    [Fact]
    public void DelCertificadoSoloSePersisteLaFecha()
    {
        var soportados = PersistOcrFieldsHandler.SoportaPersistencia(CamaraComercioAttachmentTipo.Vendedor);

        soportados.Should().BeTrue();
        // El código histórico sin rol no persiste nada: no tiene actor al que atribuir la fecha.
        PersistOcrFieldsHandler.SoportaPersistencia("camara_comercio").Should().BeFalse();
    }
}
