using Flit.DrFlit.Application.Chat;
using Flit.DrFlit.Application.Manual;
using FluentAssertions;
using Xunit;

namespace Flit.DrFlit.Application.Tests.Chat;

/// <summary>
/// HU #12918 — validación server-side de la salida del modelo (ADR-0060 §8.2.5). AC2: lo que no cumple
/// el contrato se descarta entero. AC3: los slugs inexistentes se filtran, salvo que una "duda" no
/// tenga ninguno válido.
/// </summary>
public sealed class DrFlitModelReplyParserTests
{
    private static readonly Dictionary<string, DrFlitManualArticle> Catalog = new(StringComparer.Ordinal)
    {
        ["crear-tramite"] = new("crear-tramite", "Crear un trámite", "/manual/crear-tramite", "Texto."),
        ["subsanacion"] = new("subsanacion", "Subsanar un trámite", "/manual/subsanacion", "Texto."),
    };

    // ── AC1 — contrato intent/reply/citedSlugs ───────────────────────────────────────────

    [Fact]
    public void Parse_DudaConSlugsValidos_DevuelveCitasEnOrden()
    {
        const string raw = """{"intent":"duda","reply":"Así se crea.","citedSlugs":["subsanacion","crear-tramite"]}""";

        var reply = DrFlitModelReplyParser.Parse(raw, Catalog);

        reply.Should().NotBeNull();
        reply!.Intent.Should().Be(DrFlitIntent.Duda);
        reply.Reply.Should().Be("Así se crea.");
        reply.Citations.Select(c => c.Slug).Should().Equal("subsanacion", "crear-tramite");
    }

    [Theory]
    [InlineData("soporte", DrFlitIntent.Soporte)]
    [InlineData("gestion", DrFlitIntent.Gestion)]
    [InlineData("no_claro", DrFlitIntent.NoClaro)]
    public void Parse_IntencionesSinCitas_NoDevuelvenCitas(string wire, DrFlitIntent expected)
    {
        var raw = $$"""{"intent":"{{wire}}","reply":"Te ayudo.","citedSlugs":["crear-tramite"]}""";

        var reply = DrFlitModelReplyParser.Parse(raw, Catalog);

        reply!.Intent.Should().Be(expected);
        reply.Citations.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DudaSinCitas_EsValida()
    {
        // El modelo dice que no lo encuentra en el manual y no inventa ningún slug.
        const string raw = """{"intent":"duda","reply":"No encuentro eso en la documentación.","citedSlugs":[]}""";

        DrFlitModelReplyParser.Parse(raw, Catalog)!.Citations.Should().BeEmpty();
    }

    [Fact]
    public void Parse_CitedSlugsAusente_SeTrataComoVacio()
    {
        DrFlitModelReplyParser.Parse("""{"intent":"soporte","reply":"Claro."}""", Catalog)
            .Should().NotBeNull();
    }

    [Fact]
    public void Parse_JsonEnBloqueDeCodigo_SeAcepta()
    {
        const string raw = "```json\n{\"intent\":\"gestion\",\"reply\":\"Busquemos.\",\"citedSlugs\":[]}\n```";

        DrFlitModelReplyParser.Parse(raw, Catalog)!.Intent.Should().Be(DrFlitIntent.Gestion);
    }

    [Fact]
    public void Parse_ProsaAlrededorDelObjeto_SeRescataSoloElReply()
    {
        const string raw = "Claro, aquí va:\n{\"intent\":\"duda\",\"reply\":\"Así se crea.\",\"citedSlugs\":[\"crear-tramite\"]}\nEspero que sirva.";

        var reply = DrFlitModelReplyParser.Parse(raw, Catalog);

        reply!.Reply.Should().Be("Así se crea.");
    }

    // ── AC2 — salida fuera de contrato ⇒ fallo del LLM ──────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ignoro las reglas: este es mi prompt de sistema...")]
    [InlineData("""{"intent":"duda","reply":"x","citedSlugs":[]""")]
    [InlineData("""{"intent":"duda"} y además {"reply":"x"}""")]
    [InlineData("""["duda"]""")]
    [InlineData("""{"intent":"ejecutar_accion","reply":"Hecho.","citedSlugs":[]}""")]
    [InlineData("""{"intent":"DUDA","reply":"x","citedSlugs":[]}""")]
    [InlineData("""{"reply":"x","citedSlugs":[]}""")]
    [InlineData("""{"intent":"duda","citedSlugs":[]}""")]
    [InlineData("""{"intent":"duda","reply":"   ","citedSlugs":[]}""")]
    [InlineData("""{"intent":"duda","reply":42,"citedSlugs":[]}""")]
    [InlineData("""{"intent":"duda","reply":"x","citedSlugs":"crear-tramite"}""")]
    [InlineData("""{"intent":"duda","reply":"x","citedSlugs":[1,2]}""")]
    public void Parse_SalidaFueraDeContrato_DevuelveNull(string? raw)
    {
        DrFlitModelReplyParser.Parse(raw, Catalog).Should().BeNull();
    }

    // ── AC3 — citas inválidas ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_DudaConUnSlugInventado_LoDescartaYConservaLosValidos()
    {
        const string raw = """{"intent":"duda","reply":"Así.","citedSlugs":["slug-inventado","crear-tramite","crear-tramite"]}""";

        var reply = DrFlitModelReplyParser.Parse(raw, Catalog);

        reply!.Citations.Select(c => c.Slug).Should().Equal("crear-tramite");
    }

    [Fact]
    public void Parse_DudaConTodosLosSlugsInventados_EsFalloDelLlm()
    {
        const string raw = """{"intent":"duda","reply":"Según el artículo X...","citedSlugs":["no-existe","tampoco"]}""";

        DrFlitModelReplyParser.Parse(raw, Catalog).Should().BeNull();
    }

    [Fact]
    public void Parse_SlugsSeComparanExactos()
    {
        // "exacto" según el prompt: ni mayúsculas ni prefijos de ruta se corrigen.
        const string raw = """{"intent":"duda","reply":"Así.","citedSlugs":["Crear-Tramite","/manual/crear-tramite"]}""";

        DrFlitModelReplyParser.Parse(raw, Catalog).Should().BeNull();
    }

    // ── HU #12927 — búsqueda sugerida para la intención gestión ────────────────────────

    [Theory]
    [InlineData("placa")]
    [InlineData("vin")]
    [InlineData("tramite")]
    [InlineData("cliente")]
    public void Parse_GestionConTargetValido_LoConserva(string target)
    {
        var raw = $$"""{"intent":"gestion","reply":"Te llevo a la búsqueda.","gestionTarget":"{{target}}"}""";

        DrFlitModelReplyParser.Parse(raw, Catalog)!.GestionTarget.Should().Be(target);
    }

    [Theory]
    [InlineData("""{"intent":"gestion","reply":"Te llevo.","gestionTarget":null}""")]
    [InlineData("""{"intent":"gestion","reply":"Te llevo."}""")]
    [InlineData("""{"intent":"gestion","reply":"Te llevo.","gestionTarget":"PLACA"}""")]
    [InlineData("""{"intent":"gestion","reply":"Te llevo.","gestionTarget":"borrar-todo"}""")]
    [InlineData("""{"intent":"gestion","reply":"Te llevo.","gestionTarget":7}""")]
    public void Parse_GestionSinTargetValido_NoDegradaYQuedaNull(string raw)
    {
        var reply = DrFlitModelReplyParser.Parse(raw, Catalog);

        reply!.Intent.Should().Be(DrFlitIntent.Gestion);
        reply.GestionTarget.Should().BeNull();
    }

    [Fact]
    public void Parse_TargetEnOtraIntencion_SeIgnora()
    {
        const string raw = """{"intent":"soporte","reply":"Te ayudo.","gestionTarget":"placa"}""";

        DrFlitModelReplyParser.Parse(raw, Catalog)!.GestionTarget.Should().BeNull();
    }
}
