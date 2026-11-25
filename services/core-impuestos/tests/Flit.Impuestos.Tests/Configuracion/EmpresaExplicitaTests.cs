using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Plataforma;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Flit.Impuestos.Tests.Configuracion;

/// <summary>
/// HU #14694 (M08-fix, delta 8 de impuestos.v1.yaml 1.1.0) — regla única de <c>?empresa</c> y del segmento <c>{tenantId}</c>:
/// quien no es Super Admin recibe 403 <c>EMPRESA_SOLO_SUPER_ADMIN</c> (antes que cualquier 400); el Super Admin con un valor que
/// no es uuid, repetido o ausente donde opera sobre una sola compañía recibe 400 <c>EMPRESA_REQUERIDA</c>. Los textos son los
/// literales de <c>x-flit-literales.errores</c>.
/// </summary>
/// <example>
/// <code>
/// var empresa = EmpresaExplicita.Exigir(usuario.EsSuperAdmin, http.Request.RouteValues["tenantId"]?.ToString(), http.Request.Query["empresa"]);
/// var opcional = EmpresaExplicita.Opcional(usuario.EsSuperAdmin, http.Request.Query["empresa"]) ?? usuario.EmpresaPropia();
/// </code>
/// </example>
public sealed class EmpresaExplicitaTests
{
    private const string Requerida = "Elige una compañía.";
    private const string SoloSuperAdmin = "Solo el Super Admin puede elegir la compañía.";

    private static readonly Guid Empresa = Guid.Parse("7d0c3c52-3f1a-4a55-9b7e-2f4d6a1b9c01");
    private static readonly Guid Otra = Guid.Parse("7d0c3c52-3f1a-4a55-9b7e-2f4d6a1b9c02");

    [Fact]
    public void Los_literales_son_los_del_contrato()
    {
        EmpresaExplicita.LiteralRequerida.Should().Be(Requerida);
        EmpresaExplicita.LiteralSoloSuperAdmin.Should().Be(SoloSuperAdmin);
    }

    [Theory]
    [InlineData("7d0c3c52-3f1a-4a55-9b7e-2f4d6a1b9c01")]
    [InlineData("no-es-uuid")]
    public void Quien_no_es_Super_Admin_y_manda_empresa_recibe_403_antes_que_400(string valor)
    {
        DebeSer(() => EmpresaExplicita.Exigir(false, null, new StringValues(valor)), StatusCodes.Status403Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", SoloSuperAdmin);
        DebeSer(() => EmpresaExplicita.Opcional(false, new StringValues(valor)), StatusCodes.Status403Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", SoloSuperAdmin);
    }

    [Fact]
    public void Quien_no_es_Super_Admin_en_una_ruta_con_tenantId_recibe_403()
    {
        DebeSer(() => EmpresaExplicita.Exigir(false, Empresa.ToString(), StringValues.Empty), StatusCodes.Status403Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", SoloSuperAdmin);
    }

    [Fact]
    public void Sin_empresa_quien_no_es_Super_Admin_usa_la_propia()
    {
        EmpresaExplicita.Opcional(false, StringValues.Empty).Should().BeNull();
    }

    [Fact]
    public void El_Super_Admin_sin_empresa_recibe_400_EMPRESA_REQUERIDA()
    {
        DebeSer(() => EmpresaExplicita.Opcional(true, StringValues.Empty), StatusCodes.Status400BadRequest, "EMPRESA_REQUERIDA", Requerida);
        DebeSer(() => EmpresaExplicita.Exigir(true, null, StringValues.Empty), StatusCodes.Status400BadRequest, "EMPRESA_REQUERIDA", Requerida);
    }

    [Fact]
    public void El_Super_Admin_con_valor_invalido_repetido_o_distinto_de_la_ruta_recibe_400()
    {
        DebeSer(() => EmpresaExplicita.Opcional(true, new StringValues("no-es-uuid")), StatusCodes.Status400BadRequest, "EMPRESA_REQUERIDA", Requerida);
        DebeSer(() => EmpresaExplicita.Opcional(true, new StringValues([Empresa.ToString(), Empresa.ToString()])), StatusCodes.Status400BadRequest, "EMPRESA_REQUERIDA", Requerida);
        DebeSer(() => EmpresaExplicita.Exigir(true, Empresa.ToString(), new StringValues(Otra.ToString())), StatusCodes.Status400BadRequest, "EMPRESA_REQUERIDA", Requerida);
        DebeSer(() => EmpresaExplicita.Exigir(true, Guid.Empty.ToString(), StringValues.Empty), StatusCodes.Status400BadRequest, "EMPRESA_REQUERIDA", Requerida);
    }

    [Fact]
    public void El_Super_Admin_con_empresa_valida_la_obtiene()
    {
        EmpresaExplicita.Opcional(true, new StringValues(Empresa.ToString())).Should().Be(Empresa);
        EmpresaExplicita.Exigir(true, Empresa.ToString(), StringValues.Empty).Should().Be(Empresa);
        EmpresaExplicita.Exigir(true, Empresa.ToString(), new StringValues(Empresa.ToString())).Should().Be(Empresa);
    }

    private static void DebeSer(Action accion, int status, string codigo, string literal)
    {
        var error = accion.Should().Throw<ReglaException>().Which;
        error.Status.Should().Be(status);
        error.Codigo.Should().Be(codigo);
        error.Message.Should().Be(literal, "el error es el literal de x-flit-literales.errores");
    }
}
