using Flit.Diagnostico.Domain.Personas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #14582 AC1–AC5 (contrato §1.4, delta D-3, SP-3) — <see cref="ComparacionPropietario"/>: el propietario actual declarado por
/// el cliente frente a los propietarios del RUNT. <c>false</c> si coincide con alguno, <c>true</c> si no coincide con ninguno y
/// todos eran homologables, <c>null</c> si no se puede decidir. D-3 (H-A1=b, H-A2=b): LEASING sin trato especial (solo
/// propietarios) y CC ≠ NIT. Documentos del rango ficticio 99999####.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ComparacionPropietarioTests
/// </code>
/// </example>
public sealed class ComparacionPropietarioTests
{
    private static readonly (string Tipo, string Numero)[] SinPropietarios = [];

    private static bool? Comparar((string, string)? declarado, params (string, string)[] propietarios) =>
        ComparacionPropietario.Comparar(declarado, propietarios);

    // AC1 — mismo tipo y número: coincide (sin cambio de propietario).
    [Fact]
    public void HU14582_AC1_declarado_igual_al_propietario_RUNT_da_false() =>
        Comparar(("CC", "9999900001"), ("CC", "9999900001")).Should().BeFalse();

    // AC1 — otro número: no coincide (hubo cambio de propietario).
    [Fact]
    public void HU14582_AC1_declarado_distinto_del_propietario_RUNT_da_true() =>
        Comparar(("CC", "9999900077"), ("CC", "9999900001")).Should().BeTrue();

    // AC2 — tipos abreviados distintos de la misma persona: C (Intempo) y CC (captura) son el mismo canónico.
    [Theory]
    [InlineData("CC", "C")]
    [InlineData("C", "CC")]
    [InlineData("NIT", "N")]
    [InlineData("ce", "E")]
    public void HU14582_AC2_tipos_abreviados_coinciden(string tipoDeclarado, string tipoRunt) =>
        Comparar((tipoDeclarado, "999990002"), (tipoRunt, "999990002")).Should().BeFalse();

    // AC2 — normalización del número: puntos, guiones, espacios y ceros a la izquierda no cuentan.
    [Theory]
    [InlineData("9.999.900.001")]
    [InlineData("9999900-001")]
    [InlineData(" 9999900001 ")]
    [InlineData("009999900001")]
    public void HU14582_AC2_numero_normalizado_coincide(string numero) =>
        Comparar(("CC", numero), ("C", "9999900001")).Should().BeFalse();

    // AC3 — NIT con dígito de verificación de un lado y sin él del otro: coincide.
    [Theory]
    [InlineData("999990002", "9999900025")]
    [InlineData("9999900025", "999990002")]
    [InlineData("999.990.002-5", "999990002")]
    public void HU14582_AC3_NIT_con_y_sin_digito_de_verificacion_coincide(string declarado, string runt) =>
        Comparar(("NIT", declarado), ("N", runt)).Should().BeFalse();

    // AC3 — el DV solo se ignora con el más corto de al menos 8 dígitos y exactamente un dígito más.
    [Theory]
    [InlineData("1234567", "12345678")]
    [InlineData("999990002", "99999000255")]
    public void HU14582_AC3_NIT_corto_o_con_mas_de_un_digito_extra_no_coincide(string declarado, string runt) =>
        Comparar(("NIT", declarado), ("NIT", runt)).Should().BeTrue();

    // AC3 — la regla del DV es solo del NIT: una CC con un dígito más no coincide.
    [Fact]
    public void HU14582_AC3_CC_con_un_digito_mas_no_coincide() =>
        Comparar(("CC", "999990001"), ("CC", "9999900011")).Should().BeTrue();

    // AC4 — sin declarado, sin propietarios RUNT o declarado no homologable: null (sin icono).
    [Fact]
    public void HU14582_AC4_sin_declarado_da_null() => Comparar(null, ("CC", "9999900001")).Should().BeNull();

    [Fact]
    public void HU14582_AC4_sin_propietarios_RUNT_da_null() =>
        ComparacionPropietario.Comparar(("CC", "9999900001"), SinPropietarios).Should().BeNull();

    [Fact]
    public void HU14582_AC4_declarado_con_tipo_no_homologable_da_null() =>
        Comparar(("XX", "9999900001"), ("CC", "9999900001")).Should().BeNull();

    // AC4 (SP-3) — ningún propietario coincide y alguno no era homologable: null (conservador), nunca true.
    [Fact]
    public void HU14582_AC4_sin_coincidencia_y_un_propietario_no_homologable_da_null() =>
        Comparar(("CC", "9999900077"), ("CC", "9999900001"), ("7", "9999900002")).Should().BeNull();

    // AC4 — un propietario no homologable no impide coincidir con otro que sí lo es.
    [Fact]
    public void HU14582_AC4_coincide_con_uno_aunque_otro_no_sea_homologable() =>
        Comparar(("CC", "9999900001"), ("7", "9999900002"), ("C", "9999900001")).Should().BeFalse();

    // AC4 — número con letras en un tipo que no es pasaporte: no comparable.
    [Fact]
    public void HU14582_AC4_numero_con_letras_en_CC_da_null() =>
        Comparar(("CC", "99999AB001"), ("CC", "9999900001")).Should().BeNull();

    // AC5 — copropiedad: coincide con el segundo propietario.
    [Fact]
    public void HU14582_AC5_varios_propietarios_coincide_con_cualquiera() =>
        Comparar(("CC", "9999900202"), ("CC", "9999900201"), ("C", "9999900202"), ("CC", "999990203")).Should().BeFalse();

    // AC5 — copropiedad sin coincidencia con ninguno, todos homologables: true.
    [Fact]
    public void HU14582_AC5_varios_propietarios_sin_coincidencia_da_true() =>
        Comparar(("CC", "9999900077"), ("CC", "9999900201"), ("NIT", "999990203")).Should().BeTrue();

    // Pasaporte: alfanumérico, en mayúsculas y sin quitar ceros a la izquierda.
    [Fact]
    public void HU14582_AC2_pasaporte_compara_alfanumerico_sin_quitar_ceros()
    {
        Comparar(("PAS", "ab01234"), ("PA", "AB01234")).Should().BeFalse();
        Comparar(("PAS", "0AB1234"), ("PAS", "AB1234")).Should().BeTrue();
    }

    // D-3 (H-A1=b) — LEASING sin trato especial: el declarado igual al LOCATARIO no coincide con el propietario (la entidad
    // financiera), así que hay cambio de propietario. Los locatarios no entran en la comparación.
    [Fact]
    public void HU14582_D3_leasing_declarado_igual_al_locatario_da_true() =>
        Comparar(("CC", "9999900013"), ("NIT", "999990003")).Should().BeTrue();

    // D-3 (H-A2=b) — CC y NIT no son equivalentes: el mismo número con otro tipo no coincide.
    [Fact]
    public void HU14582_D3_CC_declarado_contra_NIT_del_RUNT_con_el_mismo_numero_da_true() =>
        Comparar(("CC", "999990002"), ("NIT", "999990002")).Should().BeTrue();
}
