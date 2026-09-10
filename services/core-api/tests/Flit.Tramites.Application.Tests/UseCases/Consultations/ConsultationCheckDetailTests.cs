using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// Detalle que respalda un check del diagnóstico de requisitos previos.
///
/// <para>Los mapeadores mandaban <c>message = null</c> en los checks OK, así que la tarjeta del panel
/// quedaba con la pastilla verde y el cuerpo vacío: decía «está bien» sin decir de dónde salía. El
/// dato que lo respalda —vencimiento, póliza, aseguradora, CDA— venía en la respuesta del proveedor y
/// se descartaba en el mapeo.</para>
/// </summary>
public sealed class ConsultationCheckDetailTests
{
    [Fact]
    public void Componer_UneLasPartesConSeparador()
    {
        ConsultationCheckDetail
            .Componer("Vigente hasta 2027/01/23", "Póliza 3506349600", "AXA COLPATRIA SEGUROS SA")
            .Should()
            .Be("Vigente hasta 2027/01/23 · Póliza 3506349600 · AXA COLPATRIA SEGUROS SA");
    }

    [Fact]
    public void Componer_OmiteLoQueElProveedorNoTrae()
    {
        // Cada proveedor devuelve un subconjunto distinto: el detalle no puede quedar con separadores
        // sueltos ni con etiquetas sin valor.
        ConsultationCheckDetail
            .Componer("Vigente hasta 2027/01/23", null, "   ")
            .Should()
            .Be("Vigente hasta 2027/01/23");
    }

    [Fact]
    public void Componer_SinNingunDato_EsNull()
    {
        // Null y no cadena vacía: el llamador lo asigna directo al mensaje del check, y así ese check
        // se comporta exactamente como antes de existir el detalle.
        ConsultationCheckDetail.Componer(null, "", "  ").Should().BeNull();
    }

    [Fact]
    public void Campo_SinValor_NoDejaLaEtiquetaSuelta()
    {
        ConsultationCheckDetail.Campo("Póliza", null).Should().BeNull();
        ConsultationCheckDetail.Campo("Póliza", "  ").Should().BeNull();
        ConsultationCheckDetail.Campo("Póliza", " 123 ").Should().Be("Póliza 123");
    }

    [Fact]
    public void Fecha_TraduceLaMarcaIsoDelRuntAlFormatoDeNegocio()
    {
        // Lo que el RUNT devuelve de verdad. Pintarlo crudo es ilegible: milisegundos y desfase
        // horario para una fecha de vencimiento que se lee de un vistazo.
        ConsultationCheckDetail.Fecha("2027-01-23T00:00:00.000-05:00").Should().Be("2027/01/23");
    }

    [Fact]
    public void Fecha_LoQueNoEsFechaSeConservaTalCual()
    {
        // Un proveedor puede mandar un formato que no conocemos: perder el dato sería peor que
        // mostrarlo crudo.
        ConsultationCheckDetail.Fecha("vigencia indefinida").Should().Be("vigencia indefinida");
        ConsultationCheckDetail.Fecha(null).Should().BeNull();
    }

    [Fact]
    public void Normaliza_LosEspaciosSuciosDelRunt()
    {
        // `nombreCda` llega con espacio inicial en capturas reales, y esto se imprime en pantalla.
        ConsultationCheckDetail.Componer("  CDA   NORTE  ").Should().Be("CDA NORTE");
    }

    [Fact]
    public void Datos_OmiteElParCuandoElProveedorNoTraeElValor()
    {
        // Cada proveedor devuelve un subconjunto distinto, y una etiqueta sin valor en pantalla es
        // peor que no mostrarla.
        var datos = ConsultationCheckDetail.Datos(
            ("Vigente hasta", "2027/01/23"),
            ("Póliza", null),
            ("Aseguradora", "   "));

        datos.Should().ContainSingle();
        datos![0].Etiqueta.Should().Be("Vigente hasta");
        datos[0].Valor.Should().Be("2027/01/23");
    }

    [Fact]
    public void Datos_SinNingunValor_EsNull()
    {
        // Null y no lista vacía: el check queda exactamente como antes de existir el respaldo.
        ConsultationCheckDetail.Datos(("Póliza", null), ("Aseguradora", "")).Should().BeNull();
    }

    [Fact]
    public void Datos_NormalizaLosEspaciosSuciosDelRunt()
    {
        var datos = ConsultationCheckDetail.Datos(("CDA", "  CDA   NORTE  "));

        datos!.Should().ContainSingle().Which.Valor.Should().Be("CDA NORTE");
    }

    [Fact]
    public void Resumen_DevuelveLosMismosDatosEnUnaLinea()
    {
        // El respaldo se manda en las DOS formas: separada para que la pantalla la presente como
        // filas, y en una línea por si el campo estructurado se pierde en el camino al navegador —
        // que es exactamente lo que pasó la primera vez que se introdujo.
        var datos = ConsultationCheckDetail.Datos(
            ("Vigente hasta", "2027/01/23"),
            ("Póliza", "3506349600"));

        ConsultationCheckDetail.Resumen(datos)
            .Should().Be("Vigente hasta 2027/01/23 · Póliza 3506349600");
    }

    [Fact]
    public void Resumen_SinDatos_EsNull()
    {
        ConsultationCheckDetail.Resumen(null).Should().BeNull();
        ConsultationCheckDetail.Resumen([]).Should().BeNull();
    }
}
