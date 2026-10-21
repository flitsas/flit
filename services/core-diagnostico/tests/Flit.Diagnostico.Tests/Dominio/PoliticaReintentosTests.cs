using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13737 (F03-H6), D-12 — política de reintentos: un transitorio con <c>intentos_auto</c> menor que
/// <c>MaxReintentos</c> vuelve a la cola con <c>disponible_desde = ahora + 5 s × 2^(n−1)</c> (n = número del reintento); un
/// definitivo o los reintentos agotados no se reintentan. El instante sale de un reloj inyectable (<see cref="RelojManual"/>),
/// sin esperas. <c>MaxReintentos</c> ∈ [0, 3].
/// </summary>
/// <example>
/// <code>
/// new PoliticaReintentos(3).Decidir("TIMEOUT", intentosAuto: 0, ahora); // Reintentar en ahora + 5 s
/// </code>
/// </example>
public sealed class PoliticaReintentosTests
{
    // D-12 — backoff 5 s, 10 s, 20 s y tope por MaxReintentos (0, 1 y 3).
    [Fact]
    public void Backoff_exponencial_y_tope_por_MaxReintentos()
    {
        var reloj = new RelojManual();
        var ahora = reloj.GetUtcNow();

        PoliticaReintentos.Espera(1).Should().Be(TimeSpan.FromSeconds(5));
        PoliticaReintentos.Espera(2).Should().Be(TimeSpan.FromSeconds(10));
        PoliticaReintentos.Espera(3).Should().Be(TimeSpan.FromSeconds(20));

        var tres = new PoliticaReintentos(3);
        tres.Decidir("TIMEOUT", 0, ahora).Should().Be(new DecisionReintento(true, ahora.AddSeconds(5), 1));
        tres.Decidir("RED", 1, ahora).Should().Be(new DecisionReintento(true, ahora.AddSeconds(10), 2));
        tres.Decidir("HTTP_5XX", 2, ahora).Should().Be(new DecisionReintento(true, ahora.AddSeconds(20), 3));
        tres.Decidir("TIMEOUT", 3, ahora).Should().Be(new DecisionReintento(false, null, 3), "agotados los 3 reintentos queda en ERROR");

        var uno = new PoliticaReintentos(1);
        uno.Decidir("TIMEOUT", 0, ahora).Should().Be(new DecisionReintento(true, ahora.AddSeconds(5), 1));
        uno.Decidir("TIMEOUT", 1, ahora).Should().Be(new DecisionReintento(false, null, 1));

        new PoliticaReintentos(0).Decidir("TIMEOUT", 0, ahora).Should().Be(new DecisionReintento(false, null, 0), "con 0 no se reintenta");

        reloj.Avanzar(TimeSpan.FromMinutes(1));
        tres.Decidir("TIMEOUT", 0, reloj.GetUtcNow()).DisponibleDesde.Should().Be(ahora.AddMinutes(1).AddSeconds(5), "el backoff cuenta desde el reloj dado");
    }

    // D-12 — un definitivo no se reintenta aunque queden reintentos.
    [Theory]
    [InlineData("RESPUESTA_INVALIDA")]
    [InlineData("NO_CONECTADO")]
    [InlineData(null)]
    public void Definitivo_no_se_reintenta(string? codigo) =>
        new PoliticaReintentos(3).Decidir(codigo, 0, DateTimeOffset.UnixEpoch).Should().Be(new DecisionReintento(false, null, 0));

    // D-12 — MaxReintentos fuera de [0, 3] no es una política válida.
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void MaxReintentos_fuera_de_rango_lanza(int max) =>
        FluentActions.Invoking(() => new PoliticaReintentos(max)).Should().Throw<ArgumentOutOfRangeException>();
}
