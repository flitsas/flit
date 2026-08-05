using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Entities;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>HU #11262 — normalización canónica Trim+Upper (D8) sin cablear consumidores.</summary>
public sealed class DocumentCanonicalNormalizationTests
{
    [Theory]
    [InlineData(" cc ", " 1020 ", "CC", "1020")]
    [InlineData("Cc", "abc", "CC", "ABC")]
    [InlineData(null, null, "", "")]
    [InlineData("  ", "  ", "", "")]
    public void Normalize_aplica_solo_trim_y_mayusculas(
        string? tipo, string? numero, string tipoEsperado, string numeroEsperado)
    {
        var (t, n) = DocumentCanonicalNormalization.Normalize(tipo, numero);
        Assert.Equal(tipoEsperado, t);
        Assert.Equal(numeroEsperado, n);
    }

    [Fact]
    public void Normalize_no_quita_puntos_guiones_ni_ceros_izquierda()
    {
        var (t, n) = DocumentCanonicalNormalization.Normalize("NIT", "0.800.123-4");
        Assert.Equal("NIT", t);
        Assert.Equal("0.800.123-4", n);
    }

    [Fact]
    public void IdentidadKey_canonica_coincide_con_BiometricRules_cuando_ambos_aplican_trim_upper()
    {
        var tenant = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var canonical = DocumentCanonicalNormalization.IdentidadKey(tenant, " cc ", " 99 ");
        var legacy = BiometricRules.IdentidadKey(tenant, " cc ", " 99 ");
        Assert.Equal(legacy, canonical);
    }

    [Fact]
    public void MatchesCanonical_true_cuando_solo_difieren_espacios_o_caja()
    {
        Assert.True(DocumentCanonicalNormalization.MatchesCanonical(
            "cc", " 123 ", "CC", "123"));
        Assert.False(DocumentCanonicalNormalization.MatchesExact(
            "cc", " 123 ", "CC", "123"));
    }

    [Fact]
    public void MedicionD9_cuenta_pares_que_solo_empatan_con_regla_canonica()
    {
        // Simula la medición de solo lectura: un actor/validación que NO empataría exacto
        // pero SÍ con Trim+Upper. AC2 exige este conteo antes de activar la precedencia.
        var pairs = new[]
        {
            ("CC", "123", "CC", "123"),       // empatan exacto → no cuentan
            ("cc", "123", "CC", "123"),       // solo canónico → cuentan
            (" CC ", " 99 ", "cc", "99"),     // solo canónico → cuentan
            ("NIT", "1", "CC", "1"),         // no empatan → no cuentan
        };

        var impacto = 0;
        foreach (var (lt, ln, rt, rn) in pairs)
        {
            var exact = DocumentCanonicalNormalization.MatchesExact(lt, ln, rt, rn);
            var canonical = DocumentCanonicalNormalization.MatchesCanonical(lt, ln, rt, rn);
            if (canonical && !exact)
                impacto++;
        }

        Assert.Equal(2, impacto);
    }
}
