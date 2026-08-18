using Flit.Admin.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// AC4 (HU #11504) — <see cref="AdminIdentityRules.KyverumMaxIntentos"/> es una constante PROPIA del
/// lado admin (no hay kernel compartido: <c>Flit.Tramites.Domain</c> no referencia ningún proyecto y
/// <c>Flit.Admin.Domain</c> solo referencia <c>Flit.Queries.Domain</c>), pero su VALOR debe permanecer en
/// paridad con <see cref="Flit.Tramites.Domain.Entities.BiometricRules.KyverumMaxIntentos"/> — el mismo
/// límite observado de reintentos que Kyverum permite dentro de una validación. Este test (que sí puede
/// referenciar ambos dominios, a diferencia del código de producción) es el que sostiene esa paridad: si
/// alguien cambia una constante sin la otra, este test falla.
/// </summary>
public sealed class AdminIdentityMaxAttemptsParityTests
{
    [Fact]
    public void AdminKyverumMaxIntentos_MatchesTramitesKyverumMaxIntentos()
    {
        AdminIdentityRules.KyverumMaxIntentos.Should().Be(
            Flit.Tramites.Domain.Entities.BiometricRules.KyverumMaxIntentos);
    }
}
