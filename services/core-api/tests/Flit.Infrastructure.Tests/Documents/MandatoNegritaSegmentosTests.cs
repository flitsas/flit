using Flit.Infrastructure.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Fix "negrita inconsistente" del Contrato Privado de Mandato: antes, la negrita se reconstruía
/// buscando coincidencias contra una lista fija de palabras clave (MandatoKeywords) que nunca contenía
/// los valores reales del trámite — por eso el NIT de una empresa podía salir en negrita (por
/// casualidad) mientras la cédula de al lado no. Ahora la negrita es una propiedad de la COMPOSICIÓN:
/// <see cref="MandatoPdfGenerator.Frag(MandatoPdfGenerator.MandatoParrafoHandler)"/> resalta por defecto
/// todo lo que se interpola con <c>{ }</c>. Estos tests prueban esa composición directamente — el
/// generador no expone su texto a un lector de PDF (ver <see cref="MandatoPdfGeneratorTests"/>), así
/// que la verificación visual del resultado renderizado se hizo a mano con el render de diagnóstico
/// (services/core-api/artifacts/render-documentos).
/// </summary>
public sealed class MandatoNegritaSegmentosTests
{
    [Fact]
    public void Frag_MarcaEnNegritaCualquierValorInterpolado_SinImportarSuContenido()
    {
        // El defecto original: un NIT y una cédula, ambos VALORES reales del trámite, deben quedar
        // exactamente igual de resaltados — no depende de que estén en una lista fija.
        var nit = "890903938";
        var cedula = "79795089";

        var segmentos = MandatoPdfGenerator.Frag(
            $"con NIT No. {nit}, según lo acredita... identificado con la cédula de ciudadanía No {cedula}, quien");

        segmentos.Should().ContainSingle(s => s.Texto == nit && s.Negrita);
        segmentos.Should().ContainSingle(s => s.Texto == cedula && s.Negrita);
    }

    [Fact]
    public void Frag_ElTextoLiteralFueraDeLasLlaves_NuncaSeResalta()
    {
        var valor = "Juan Pérez";
        var segmentos = MandatoPdfGenerator.Frag($"Yo, {valor}, mayor de edad, identificado con");

        segmentos.Should().Contain(s => !s.Negrita && s.Texto.Contains("mayor de edad"));
        segmentos.Should().Contain(s => s.Negrita && s.Texto == valor);
    }

    [Fact]
    public void Frag_UnaPalabraEstructuralEntreLlaves_SeResaltaIgualQueUnValor()
    {
        // "EL MANDANTE"/"EL MANDATARIO" y los encabezados de cláusula siguen en negrita: se escriben a
        // propósito dentro de un hueco de interpolación ({"EL MANDANTE"}), así que entran por el mismo
        // mecanismo que un valor real.
        var segmentos = MandatoPdfGenerator.Frag($"quien se denominará {"EL MANDANTE"}.");

        segmentos.Should().ContainSingle(s => s.Texto == "EL MANDANTE" && s.Negrita);
    }

    [Fact]
    public void Frag_ElMarcadorSinDatoTodavia_NoSeResalta()
    {
        // "___" (Val(...) sin dato) no es un valor del trámite: resaltarlo se ve como un guion "raro"
        // más grueso que el resto, sobre todo en el mandato tipo Abierto (mandatario en líneas en blanco).
        var segmentos = MandatoPdfGenerator.Frag($"Y de {"___"} identificado con la cédula No {"___"},");

        segmentos.Where(s => s.Texto == "___").Should().OnlyContain(s => !s.Negrita);
    }

    [Fact]
    public void Plano_EnvuelveUnFragmentoCompuesto_YQuedaSinResaltar()
    {
        // Escape explícito para texto compuesto que NO es un valor del trámite (p. ej. la cita larga de
        // resoluciones, o la sigla de la unión temporal en la cláusula de obligaciones de Sabaneta).
        var citaLegal = "cumpliendo con la Resolución 12379 de 2012";
        var segmentos = MandatoPdfGenerator.Frag($"hemos acordado suscribir {MandatoPdfGenerator.Plano(citaLegal)}");

        segmentos.Should().ContainSingle(s => s.Texto == citaLegal && !s.Negrita);
    }

    [Fact]
    public void Parrafo_ConVariosFragmentos_LosConcatenaConservandoLaNegritaDeCadaUno()
    {
        // Equivalente a unir con "+" como antes, pero sin perder qué segmento es un valor.
        var nombre = "Ana Gómez";
        var parrafo = MandatoPdfGenerator.Parrafo(
            MandatoPdfGenerator.Frag($"Yo, {nombre}, "),
            MandatoPdfGenerator.Frag($"mayor de edad."));

        // Reconstruir el texto plano debe dar exactamente lo mismo que antes con "+": solo cambia que
        // ahora sabemos, segmento a segmento, cuáles de esas partes son un VALOR.
        string.Concat(parrafo.Select(s => s.Texto)).Should().Be("Yo, Ana Gómez, mayor de edad.");
        parrafo.Should().Contain(s => s.Texto == nombre && s.Negrita);
        parrafo.Should().Contain(s => s.Texto == "mayor de edad." && !s.Negrita);
    }

    [Fact]
    public void MarcadoresDelPreviewDePlataforma_SiguenResaltados()
    {
        // El preview usa marcadores [ACÁ VA ...] como "valor" del mandante/mandatario. Al fluir por el
        // mismo mecanismo que cualquier dato real (se interpolan igual), siguen en negrita SIN que el
        // generador tenga que listarlos aparte (a diferencia del MandatoKeywords original).
        var segmentos = MandatoPdfGenerator.Frag(
            $"Yo, {MandatoPreviewSample.PhPnNombre}, identificado con {MandatoPreviewSample.PhPnDocumento}.");

        segmentos.Should().ContainSingle(s => s.Texto == MandatoPreviewSample.PhPnNombre && s.Negrita);
        segmentos.Should().ContainSingle(s => s.Texto == MandatoPreviewSample.PhPnDocumento && s.Negrita);
    }

    [Fact]
    public void SplitPlaceholders_SustituyeElTokenYMarcaElValorEnNegrita_NoElRestoDelTextoLibre()
    {
        // Plantilla personalizada del OT (editor): el token {{...}} es sintaxis nuestra, conocida de
        // antemano — lo que dispara la negrita es el TOKEN, no el contenido del valor sustituido.
        var reemplazos = new (string Token, string Valor)[]
        {
            ("{{placa}}", "ABC123"),
            ("{{mandante_nombre}}", "890903938"), // a propósito: un valor "raro" no debe romper el scan.
        };

        var segmentos = MandatoPdfGenerator.SplitPlaceholders(
            "El vehículo de placas {{placa}} a nombre de {{mandante_nombre}} queda en poder del mandatario.",
            reemplazos);

        segmentos.Should().ContainSingle(s => s.Texto == "ABC123" && s.Negrita);
        segmentos.Should().ContainSingle(s => s.Texto == "890903938" && s.Negrita);
        segmentos.Should().Contain(s => !s.Negrita && s.Texto.Contains("queda en poder del mandatario"));
    }

    [Fact]
    public void SplitPlaceholders_ElMarcadorSinDatoSustituido_NoSeResalta()
    {
        var reemplazos = new (string Token, string Valor)[] { ("{{ciudad}}", "___") };
        var segmentos = MandatoPdfGenerator.SplitPlaceholders("Firmado en {{ciudad}}.", reemplazos);

        segmentos.Should().ContainSingle(s => s.Texto == "___" && !s.Negrita);
    }

    [Fact]
    public void SplitPlaceholders_PlacaVacia_NoEscribeMarcador()
    {
        var reemplazos = new (string Token, string Valor)[] { ("{{placa}}", string.Empty) };
        var segmentos = MandatoPdfGenerator.SplitPlaceholders(
            "Identificado con placas {{placa}}.", reemplazos);

        segmentos.Should().NotContain(s => s.Texto == "___");
        segmentos.Should().NotContain(s => s.Texto == "ABC123");
        string.Concat(segmentos.Select(s => s.Texto)).Should().Be("Identificado con placas .");
    }
}
