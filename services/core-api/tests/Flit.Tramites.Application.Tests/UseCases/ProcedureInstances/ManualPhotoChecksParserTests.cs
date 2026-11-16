using Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #14310 — <see cref="ManualPhotoChecksParser"/>: el campo público <c>validacionFotos</c> solo deja pasar la lista cerrada;
/// cualquier otra cosa degrada esa foto a «no_verificada / sin_resultado» y jamás lanza.
/// </summary>
public sealed class ManualPhotoChecksParserTests
{
    [Fact]
    public void Contrato_completo_se_mapea_foto_por_foto()
    {
        var r = ManualPhotoChecksParser.Parse(
            """{"rostro":{"estado":"verificada","motivo":null},"anverso":{"estado":"no_verificada","motivo":"lado_incorrecto"},"reverso":{"estado":"no_verificada","motivo":"sin_soporte"}}""");

        r.Face.Should().Be(ManualPhotoCheck.Verified);
        r.IdFront.Should().Be(ManualPhotoCheck.NotVerified("lado_incorrecto"));
        r.IdBack.Should().Be(ManualPhotoCheck.NotVerified("sin_soporte"));
    }

    [Theory]
    [InlineData("sin_rostro")]
    [InlineData("varios_rostros")]
    [InlineData("sin_documento")]
    [InlineData("lado_incorrecto")]
    [InlineData("sin_soporte")]
    [InlineData("sin_resultado")]
    public void Acepta_cada_motivo_de_la_lista_cerrada(string motivo)
    {
        var r = ManualPhotoChecksParser.Parse($$$"""{"rostro":{"estado":"no_verificada","motivo":"{{{motivo}}}"}}""");

        r.Face.Reason.Should().Be(motivo);
        r.IdFront.Should().Be(ManualPhotoCheck.Unknown, "la foto ausente queda sin resultado");
    }

    [Theory]
    [InlineData("""{"rostro":{"estado":"aprobada"}}""")]
    [InlineData("""{"rostro":{"estado":"no_verificada","motivo":"<script>alert(1)</script>"}}""")]
    [InlineData("""{"rostro":{"estado":"no_verificada"}}""")]
    [InlineData("""{"rostro":{"estado":"no_verificada","motivo":5}}""")]
    [InlineData("""{"rostro":{"estado":true}}""")]
    [InlineData("""{"rostro":"verificada"}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("null")]
    [InlineData("{ no es json")]
    public void Estado_o_motivo_desconocido_o_forma_invalida_deja_la_foto_sin_resultado(string raw)
    {
        var r = ManualPhotoChecksParser.Parse(raw);

        r.Face.Should().Be(ManualPhotoCheck.Unknown);
    }

    [Fact]
    public void Verificada_ignora_un_motivo_enviado_por_el_cliente()
    {
        var r = ManualPhotoChecksParser.Parse("""{"rostro":{"estado":"verificada","motivo":"texto libre"}}""");

        r.Face.Should().Be(ManualPhotoCheck.Verified);
        r.Face.Reason.Should().BeNull();
    }

    [Fact]
    public void Campo_mas_grande_que_el_tope_se_descarta_sin_parsear()
    {
        var grande = """{"rostro":{"estado":"verificada"}}""" + new string(' ', ManualPhotoChecksParser.MaxLength);

        ManualPhotoChecksParser.Parse(grande).Should().Be(ManualPhotoChecks.AllUnknown);
    }

    [Fact]
    public void Json_con_anidacion_excesiva_no_lanza()
    {
        var raw = new string('[', 500) + new string(']', 500);

        ManualPhotoChecksParser.Parse(raw).Should().Be(ManualPhotoChecks.AllUnknown);
    }
}
