using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11643 — prioridad y presupuesto del recuadro OBSERVACIONES del FUR.
///
/// <para>El recuadro admite del orden de 500 caracteres. Antes, el texto libre del gestor —sin tope en
/// ninguna capa— se componía DELANTE de los bloques automáticos, así que al desbordar, lo que el
/// auto-encaje eliminaba con la elipsis era el beneficiario del gravamen o la transformación
/// declarada: información con consecuencias legales desaparecía del formulario mientras sobrevivía un
/// comentario libre.</para>
/// </summary>
public sealed class FurObservacionesComposerTests
{
    private const string Gravamen =
        "GRAVAMEN / PRENDA A FAVOR DE: BANCO FINANCIERO DE COLOMBIA S.A. - NIT 890900608-1.";

    private static string Largo(int n) => string.Join(" ", Enumerable.Repeat("PALABRA", (n / 8) + 2))[..n];

    [Fact]
    public void TextoLibreDesmedido_NoDesplazaLoAutomatico()
    {
        var resultado = FurObservacionesComposer.Componer(Gravamen, Largo(2000));

        resultado.Should().StartWith(Gravamen,
            "lo automático encabeza el recuadro y entra íntegro: es lo que tiene efecto legal");
        resultado!.Length.Should().BeLessThanOrEqualTo(FurObservacionesComposer.PresupuestoCaracteres);
        resultado.Should().EndWith("…", "el recorte del texto libre debe verse, no ser silencioso");
    }

    [Fact]
    public void LoAutomaticoSoloAgotaElPresupuesto_ElTextoLibreDesaparece()
    {
        var autoEnorme = Largo(FurObservacionesComposer.PresupuestoCaracteres + 50);

        var resultado = FurObservacionesComposer.Componer(autoEnorme, "OBSERVACION DEL GESTOR");

        resultado.Should().Be(autoEnorme,
            "el bloque automático nunca se recorta aquí: si no cabe con el texto libre, el que sobra " +
            "es el texto libre");
    }

    [Fact]
    public void CabenAmbos_SeConcatenanConLoAutomaticoDelante()
    {
        var resultado = FurObservacionesComposer.Componer(Gravamen, "VEHICULO VERIFICADO SIN NOVEDADES.");

        resultado.Should().Be($"{Gravamen} VEHICULO VERIFICADO SIN NOVEDADES.");
        resultado.Should().NotContain("…", "cabía entero: no hay nada que recortar");
    }

    [Fact]
    public void SinTextoLibre_DevuelveSoloLoAutomatico()
    {
        FurObservacionesComposer.Componer(Gravamen, null).Should().Be(Gravamen);
        FurObservacionesComposer.Componer(Gravamen, "   ").Should().Be(Gravamen);
    }

    [Fact]
    public void SinNada_DevuelveNull()
    {
        FurObservacionesComposer.Componer(null, null).Should().BeNull();
        FurObservacionesComposer.Componer("  ", "  ").Should().BeNull();
    }

    [Fact]
    public void SoloTextoLibre_SeRecortaAlPresupuesto()
    {
        var resultado = FurObservacionesComposer.Componer(null, Largo(2000));

        resultado!.Length.Should().BeLessThanOrEqualTo(FurObservacionesComposer.PresupuestoCaracteres);
        resultado.Should().EndWith("…");
    }

    /// <summary>
    /// El corte cae en límite de palabra: partir una palabra por la mitad hace dudar de si el dato
    /// está mal escrito o recortado.
    /// </summary>
    [Fact]
    public void ElRecorteRespetaLimitesDePalabra()
    {
        var resultado = FurObservacionesComposer.Componer(Gravamen, Largo(2000));

        var libre = resultado![(Gravamen.Length + 1)..].TrimEnd('…');
        libre.Should().NotBeEmpty();
        libre.Should().NotEndWith(" ");
        // Todas las palabras completas: el relleno son "PALABRA" de 7 letras.
        libre.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().OnlyContain(p => p == "PALABRA");
    }

    /// <summary>
    /// El caso combinado máximo real: gravamen + las tres transformaciones + servicio con vinculadora.
    /// Todo automático, así que debe salir íntegro aunque no quede sitio para el texto libre.
    /// </summary>
    [Fact]
    public void CasoCombinadoMaximo_ConservaTodoLoAutomatico()
    {
        const string automatico =
            "GRAVAMEN / PRENDA A FAVOR DE: BANCO FINANCIERO DE COLOMBIA S.A. - NIT 890900608-1. " +
            "Cambio de color: NEGRO MATE. Cambio de combustible: GAS NATURAL VEHICULAR. " +
            "Cambio de carrocería: FURGON. Servicio: PÚBLICO. Empresa vinculadora: " +
            "COOPERATIVA DE TRANSPORTADORES DEL ORIENTE, NIT 890903938.";

        var resultado = FurObservacionesComposer.Componer(automatico, Largo(1000));

        resultado.Should().StartWith(automatico);
        automatico.Length.Should().BeLessThan(FurObservacionesComposer.PresupuestoCaracteres,
            "el peor caso automático debe caber por sí solo: si no, el recuadro se queda corto para " +
            "información obligatoria y el problema ya no es de prioridad sino de geometría");
    }
}
