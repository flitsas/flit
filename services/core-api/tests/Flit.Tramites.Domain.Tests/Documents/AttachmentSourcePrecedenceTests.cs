using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Documents;

/// <summary>
/// Precedencia por grado de especificidad del origen de un adjunto (DT-4, Feature #11309, HU #11319).
/// Es la regla que reemplaza "gana el más reciente" —no determinista cuando dos filas del mismo tipo
/// se escriben con el mismo <c>now</c>— y que el llamador (<c>ConsolidadoCommand</c>) solo aplica a
/// <c>mandato</c> y <c>tramite_virtual</c>: aquí se prueba la regla en aislamiento, sin esa acotación.
/// </summary>
public sealed class AttachmentSourcePrecedenceTests
{
    private sealed record Adjunto(string Source, DateTimeOffset UploadedAt);

    private static readonly DateTimeOffset Ayer = new(2026, 8, 9, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset Hoy = new(2026, 8, 10, 10, 0, 0, TimeSpan.FromHours(-5));

    // AC1 — Precedencia determinista: gana el grado más alto (hecho del trámite) con independencia
    // de cuál se escribió más reciente.
    [Fact]
    public void AC1_GradoAlto_GanaSobreGradoMedio_AunqueSeaMasVieja()
    {
        var user = new Adjunto("user", Ayer);
        var company = new Adjunto("company", Hoy);

        var ganador = AttachmentSourcePrecedence.SelectWinner(
            [company, user], a => a.Source, a => a.UploadedAt);

        ganador.Should().Be(user, "'user' es grado 1 (hecho del trámite) y 'company' es grado 2");
    }

    [Fact]
    public void AC1_ElResultadoNoDependeDelOrdenDeEscritura()
    {
        var user = new Adjunto("user", Ayer);
        var company = new Adjunto("company", Hoy);

        AttachmentSourcePrecedence.SelectWinner([company, user], a => a.Source, a => a.UploadedAt)
            .Should().Be(user);
        AttachmentSourcePrecedence.SelectWinner([user, company], a => a.Source, a => a.UploadedAt)
            .Should().Be(user);
    }

    // AC2 — Determinismo con fechas idénticas: 20 ejecuciones con el orden de lectura mezclado dan
    // siempre el mismo resultado (el de mayor grado), sin depender del orden de la base de datos.
    [Fact]
    public void AC2_ConFechasIdenticas_20EjecucionesConOrdenMezclado_SiemprePicaElMismo()
    {
        var system = new Adjunto("system", Hoy);
        var company = new Adjunto("company", Hoy);
        var user = new Adjunto("user", Hoy);
        var candidatosBase = new[] { system, company, user };
        var random = new Random(20260810);

        for (var i = 0; i < 20; i++)
        {
            var mezclado = candidatosBase.OrderBy(_ => random.Next()).ToArray();

            AttachmentSourcePrecedence.SelectWinner(mezclado, a => a.Source, a => a.UploadedAt)
                .Should().Be(user, $"ejecución #{i}: 'user' es grado 1 con independencia del orden de lectura");
        }
    }

    // AC3 — Un origen no declarado no gana por accidente: cae al grado de "configuración de
    // compañía" y nunca desplaza un hecho del trámite.
    [Theory]
    [InlineData("user")]
    [InlineData("ot")]
    [InlineData("ict")]
    [InlineData("portal")]
    [InlineData("ocr")]
    [InlineData("consultation")]
    public void AC3_OrigenNoDeclarado_NuncaDesplazaUnHechoDelTramite(string origenDeclarado)
    {
        var declarado = new Adjunto(origenDeclarado, Ayer);
        var desconocido = new Adjunto("un_origen_que_no_existe_en_la_tabla", Hoy);

        var ganador = AttachmentSourcePrecedence.SelectWinner(
            [desconocido, declarado], a => a.Source, a => a.UploadedAt);

        ganador.Should().Be(declarado, "un origen desconocido cae en grado 2, nunca en grado 1");
    }

    [Fact]
    public void AC3_OrigenNoDeclarado_TieneElMismoGradoQueCompany()
    {
        AttachmentSourcePrecedence.Weight("un_origen_desconocido")
            .Should().Be(AttachmentSourcePrecedence.Weight("company"));
    }

    [Fact]
    public void AC3_OrigenNoDeclarado_SiGanaSobreLaPlantillaDelSistema()
    {
        // Grado 2 SÍ gana sobre grado 3 (plantilla): el origen desconocido no cae "por debajo" de
        // system solo por ser desconocido.
        var desconocido = new Adjunto("un_origen_que_no_existe_en_la_tabla", Ayer);
        var system = new Adjunto("system", Hoy);

        AttachmentSourcePrecedence.SelectWinner([system, desconocido], a => a.Source, a => a.UploadedAt)
            .Should().Be(desconocido);
    }

    [Fact]
    public void Weight_LosTresGrados_QuedanEnOrdenAscendenteDeEspecificidad()
    {
        AttachmentSourcePrecedence.Weight("user").Should().BeLessThan(AttachmentSourcePrecedence.Weight("company"));
        AttachmentSourcePrecedence.Weight("company").Should().BeLessThan(AttachmentSourcePrecedence.Weight("system"));
    }

    [Theory]
    [InlineData("ot")]
    [InlineData("user")]
    [InlineData("ict")]
    [InlineData("portal")]
    [InlineData("ocr")]
    [InlineData("consultation")]
    public void Weight_OrigenesDeclaradosDeGradoAlto_PesanUno(string source) =>
        AttachmentSourcePrecedence.Weight(source).Should().Be(1);

    [Fact]
    public void Weight_ElComparadorDeOrigenEsInsensibleAMayusculas() =>
        AttachmentSourcePrecedence.Weight("USER").Should().Be(AttachmentSourcePrecedence.Weight("user"));

    [Fact]
    public void SelectWinner_AIgualGrado_GanaLoMasReciente()
    {
        var companyVieja = new Adjunto("company", Ayer);
        var companyNueva = new Adjunto("company", Hoy);

        AttachmentSourcePrecedence.SelectWinner(
                [companyVieja, companyNueva], a => a.Source, a => a.UploadedAt)
            .Should().Be(companyNueva);
    }

    [Fact]
    public void SelectWinner_ConUnSoloCandidato_LoDevuelveTalCual()
    {
        var unico = new Adjunto("system", Hoy);

        AttachmentSourcePrecedence.SelectWinner([unico], a => a.Source, a => a.UploadedAt)
            .Should().Be(unico);
    }
}
