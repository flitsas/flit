using Flit.Diagnostico.Domain.Saneamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Máquina de estados del saneamiento (plan §3.2, S7, S10; HU #14576 AC9, AC10, AC11 y AC12 en el dominio): cada transición
/// válida con su actor y su evento, toda otra combinación (estado, acción, actor) es inválida, y el motivo exigido cumple el
/// formato de §3.2 y SP-13 (con la decisión E3: sin rechazo por dígitos). Dominio puro, sin base.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~TransicionesSaneamientoTests
/// </code>
/// </example>
public sealed class TransicionesSaneamientoTests
{
    public static TheoryData<EstadoSaneamiento?, AccionSaneamiento, ActorSaneamiento, EstadoSaneamiento, TipoEventoSaneamiento, bool> Validas => new()
    {
        { null, AccionSaneamiento.Solicitar, ActorSaneamiento.Compania, EstadoSaneamiento.Solicitado, TipoEventoSaneamiento.Solicitado, false },
        { EstadoSaneamiento.Solicitado, AccionSaneamiento.Desmarcar, ActorSaneamiento.Compania, EstadoSaneamiento.Anulado, TipoEventoSaneamiento.Desmarcado, false },
        { EstadoSaneamiento.Solicitado, AccionSaneamiento.Tomar, ActorSaneamiento.Flit, EstadoSaneamiento.EnGestion, TipoEventoSaneamiento.Tomado, false },
        { EstadoSaneamiento.Solicitado, AccionSaneamiento.AnularFlit, ActorSaneamiento.Flit, EstadoSaneamiento.Anulado, TipoEventoSaneamiento.AnuladoFlit, true },
        { EstadoSaneamiento.EnGestion, AccionSaneamiento.AnularFlit, ActorSaneamiento.Flit, EstadoSaneamiento.Anulado, TipoEventoSaneamiento.AnuladoFlit, true },
        { EstadoSaneamiento.EnGestion, AccionSaneamiento.CerrarSaneado, ActorSaneamiento.Flit, EstadoSaneamiento.Saneado, TipoEventoSaneamiento.CerradoSaneado, false },
        { EstadoSaneamiento.EnGestion, AccionSaneamiento.CerrarNoProcede, ActorSaneamiento.Flit, EstadoSaneamiento.NoProcede, TipoEventoSaneamiento.CerradoNoProcede, true },
        { EstadoSaneamiento.Saneado, AccionSaneamiento.Reabrir, ActorSaneamiento.Flit, EstadoSaneamiento.EnGestion, TipoEventoSaneamiento.Reabierto, true },
        { EstadoSaneamiento.NoProcede, AccionSaneamiento.Reabrir, ActorSaneamiento.Flit, EstadoSaneamiento.EnGestion, TipoEventoSaneamiento.Reabierto, true },
    };

    // AC9 — cada fila de la tabla de §3.2 con su actor, su evento y si exige motivo.
    [Theory]
    [MemberData(nameof(Validas))]
    public void HU14576_AC9_transicion_valida_con_su_actor(
        EstadoSaneamiento? desde, AccionSaneamiento accion, ActorSaneamiento actor, EstadoSaneamiento hasta, TipoEventoSaneamiento evento, bool motivo)
    {
        var transicion = TransicionesSaneamiento.Resolver(desde, accion, actor);

        transicion.Should().NotBeNull();
        transicion!.Hasta.Should().Be(hasta);
        transicion.Evento.Should().Be(evento);
        transicion.ExigeMotivo.Should().Be(motivo);
    }

    // AC10 y AC11 — toda combinación fuera de la tabla es inválida, incluida la de una fila válida con el actor equivocado.
    [Fact]
    public void HU14576_AC10_AC11_toda_otra_combinacion_es_invalida()
    {
        var validas = Validas.Select(f => ((EstadoSaneamiento?)f.Data.Item1, f.Data.Item2, f.Data.Item3)).ToHashSet();
        var estados = Enum.GetValues<EstadoSaneamiento>().Select(e => (EstadoSaneamiento?)e).Prepend(null).ToList();
        var revisadas = 0;

        foreach (var desde in estados)
            foreach (var accion in Enum.GetValues<AccionSaneamiento>())
                foreach (var actor in Enum.GetValues<ActorSaneamiento>())
                {
                    if (validas.Contains((desde, accion, actor)))
                        continue;
                    revisadas++;
                    TransicionesSaneamiento.Resolver(desde, accion, actor).Should().BeNull($"{desde?.ToString() ?? "(ninguno)"} + {accion} por {actor} no está en §3.2");
                }

        revisadas.Should().Be((6 * 7 * 2) - 9);
    }

    // AC10 — los ejemplos del Gherkin, uno por uno.
    [Theory]
    [InlineData(EstadoSaneamiento.EnGestion, AccionSaneamiento.Tomar)]
    [InlineData(EstadoSaneamiento.Solicitado, AccionSaneamiento.CerrarSaneado)]
    [InlineData(EstadoSaneamiento.Solicitado, AccionSaneamiento.CerrarNoProcede)]
    [InlineData(EstadoSaneamiento.EnGestion, AccionSaneamiento.Reabrir)]
    [InlineData(EstadoSaneamiento.Saneado, AccionSaneamiento.AnularFlit)]
    [InlineData(EstadoSaneamiento.Anulado, AccionSaneamiento.Tomar)]
    [InlineData(EstadoSaneamiento.Anulado, AccionSaneamiento.Reabrir)]
    public void HU14576_AC10_ejemplos_de_transicion_invalida(EstadoSaneamiento desde, AccionSaneamiento accion) =>
        TransicionesSaneamiento.Resolver(desde, accion, ActorSaneamiento.Flit).Should().BeNull();

    // AC11 / S7 — la compañía solo desmarca en SOLICITADO; FLIT no desmarca (anula con motivo) y la compañía no anula ni toma.
    [Fact]
    public void HU14576_AC11_actor_incorrecto_no_transiciona()
    {
        TransicionesSaneamiento.Resolver(EstadoSaneamiento.EnGestion, AccionSaneamiento.Desmarcar, ActorSaneamiento.Compania).Should().BeNull();
        TransicionesSaneamiento.Resolver(EstadoSaneamiento.Solicitado, AccionSaneamiento.Desmarcar, ActorSaneamiento.Flit).Should().BeNull();
        TransicionesSaneamiento.Resolver(EstadoSaneamiento.Solicitado, AccionSaneamiento.Tomar, ActorSaneamiento.Compania).Should().BeNull();
        TransicionesSaneamiento.Resolver(EstadoSaneamiento.EnGestion, AccionSaneamiento.AnularFlit, ActorSaneamiento.Compania).Should().BeNull();
        TransicionesSaneamiento.Resolver(null, AccionSaneamiento.Solicitar, ActorSaneamiento.Flit).Should().BeNull("FLIT no marca la bandera (S2, SP-8)");
    }

    [Fact]
    public void ANULADO_es_el_unico_estado_final()
    {
        Enum.GetValues<EstadoSaneamiento>().Where(TransicionesSaneamiento.EsFinal).Should().Equal(EstadoSaneamiento.Anulado);
    }

    // AC12 / §3.2 — motivo de 10 a 500 puntos de código, sin espacios ni saltos al inicio o al final.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("corto 9 c")]
    [InlineData("\nmotivo con salto inicial")]
    [InlineData("motivo con salto final\n")]
    [InlineData(" motivo con espacio")]
    public void HU14576_AC12_motivo_ausente_o_mal_formado_es_MOTIVO_REQUERIDO(string? motivo) =>
        TextoLibreSaneamiento.CodigoMotivo(motivo).Should().Be(TextoLibreSaneamiento.MotivoRequerido);

    [Fact]
    public void HU14576_AC12_motivo_de_501_es_MOTIVO_REQUERIDO_y_de_500_pasa()
    {
        TextoLibreSaneamiento.CodigoMotivo(new string('a', 501)).Should().Be(TextoLibreSaneamiento.MotivoRequerido);
        TextoLibreSaneamiento.CodigoMotivo(new string('a', 500)).Should().BeNull();
        TextoLibreSaneamiento.CodigoMotivo("El pago no se pudo confirmar\ncon la entidad.").Should().BeNull("un salto interior es válido");
    }

    // SP-13 — largo en puntos de código: 500 emojis pasan, 501 no.
    [Fact]
    public void SP13_el_largo_se_mide_en_puntos_de_codigo()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        TextoLibreSaneamiento.CodigoMotivo(string.Concat(Enumerable.Repeat(emoji, 500))).Should().BeNull();
        TextoLibreSaneamiento.CodigoMotivo(string.Concat(Enumerable.Repeat(emoji, 501))).Should().Be(TextoLibreSaneamiento.MotivoRequerido);
    }

    // SP-13 — controles y marcas bidireccionales se rechazan.
    [Theory]
    [InlineData("motivo con \u202E bidi dentro")]
    [InlineData("motivo con \u2066 aislado")]
    [InlineData("motivo con \u0007 campana")]
    public void SP13_caracter_de_control_o_bidireccional_es_TEXTO_INVALIDO(string motivo) =>
        TextoLibreSaneamiento.CodigoMotivo(motivo).Should().Be(TextoLibreSaneamiento.TextoInvalido);

    [Fact]
    public void SP13_E3_los_digitos_seguidos_se_aceptan_y_el_largo_se_valida()
    {
        TextoLibreSaneamiento.CodigoMotivo("pagó el titular 1234567890 ayer").Should().BeNull("decisión E3: el cliente ve todo");
        TextoLibreSaneamiento.Validar(null, 0, 500).Should().BeNull("un texto opcional puede faltar");
        TextoLibreSaneamiento.Validar("ab", 3, 200).Should().Be(ErrorTextoLibre.Largo);
        TextoLibreSaneamiento.Validar(" abc", 3, 200).Should().Be(ErrorTextoLibre.Formato);
        TextoLibreSaneamiento.Validar("a\u202Ebc", 3, 200).Should().Be(ErrorTextoLibre.CaracterInvalido);
    }
}
