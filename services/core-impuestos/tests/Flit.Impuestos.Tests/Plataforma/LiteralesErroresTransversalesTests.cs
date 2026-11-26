using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Reportes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using E = Flit.Impuestos.Tests.Reportes.EntornoReportes;

namespace Flit.Impuestos.Tests.Plataforma;

/// <summary>
/// HU #14694 (contrato impuestos.v1.yaml 1.2.2) — los códigos transversales que antes salían sin literal en el contrato:
/// <c>CONFLICTO_CONCURRENCIA</c>, <c>ESCRITURA_ENTRE_EMPRESAS</c>, <c>SIN_USUARIO</c> y <c>VALOR_INVALIDO</c>. El contrato
/// declara cada uno en <c>x-flit-literales.errores</c> con el texto exacto, y el <c>error</c> de la respuesta es ese literal: en el
/// manejador global (<see cref="ManejadorReglas"/>) y en la lectura auditada del Super Admin sin <c>sub</c> uuid.
/// </summary>
/// <example>
/// <code>
/// var (codigo, error) = await ManejarAsync(new EscrituraEntreEmpresasException());
/// error.Should().Be("No puedes modificar datos de otra compañía.");
/// </code>
/// </example>
public sealed partial class LiteralesErroresTransversalesTests(ApiReportesFixture fixture) : IClassFixture<ApiReportesFixture>
{
    public const string ConflictoConcurrencia = "Otro usuario cambió este registro al mismo tiempo. Recarga e inténtalo de nuevo.";
    public const string EscrituraEntreEmpresas = "No puedes modificar datos de otra compañía.";
    public const string SinUsuario = "Tu sesión no identifica a un usuario. Vuelve a iniciar sesión.";
    public const string ValorInvalido = "Un valor enviado no es válido. Revisa los datos e inténtalo de nuevo.";

    private ApiDePrueba Api => fixture.Api;

    [Theory]
    [InlineData("CONFLICTO_CONCURRENCIA", ConflictoConcurrencia)]
    [InlineData("ESCRITURA_ENTRE_EMPRESAS", EscrituraEntreEmpresas)]
    [InlineData("SIN_USUARIO", SinUsuario)]
    [InlineData("VALOR_INVALIDO", ValorInvalido)]
    public void El_contrato_declara_el_literal_de_cada_codigo(string codigo, string literal)
    {
        var errores = ErroresDelContrato();

        errores.Should().ContainKey(codigo);
        errores[codigo].Should().Be(literal);
    }

    [Fact]
    public void El_contrato_esta_en_la_1_2_2_o_posterior()
    {
        var lineas = File.ReadAllLines(RutaContrato());
        var version = lineas.Single(l => l.StartsWith("  version: ", StringComparison.Ordinal))["  version: ".Length..].Trim('"');

        Version.Parse(version).Should().BeGreaterThanOrEqualTo(new Version(1, 2, 2), "los literales transversales llegaron en la 1.2.2");
    }

    [Fact]
    public async Task El_manejador_global_responde_con_el_literal_del_contrato()
    {
        var entre = await ManejarAsync(new EscrituraEntreEmpresasException("detalle interno"));
        var concurrencia = await ManejarAsync(new DbUpdateConcurrencyException("detalle interno"));
        var check = await ManejarAsync(new DbUpdateException("detalle interno",
            new PostgresException("detalle de la base", "ERROR", "ERROR", PostgresErrorCodes.CheckViolation)));

        entre.Should().Be((StatusCodes.Status403Forbidden, "ESCRITURA_ENTRE_EMPRESAS", EscrituraEntreEmpresas));
        concurrencia.Should().Be((StatusCodes.Status409Conflict, "CONFLICTO_CONCURRENCIA", ConflictoConcurrencia));
        check.Should().Be((StatusCodes.Status422UnprocessableEntity, "VALOR_INVALIDO", ValorInvalido));
    }

    [Fact]
    public async Task El_Super_Admin_sin_usuario_en_el_token_recibe_SIN_USUARIO_con_el_literal()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, empresa, ct);
        var sa = Api.Cliente(Api.Token(superAdmin: true, tipoEmpresa: "FLIT", usuario: Guid.Empty));

        await LiquidacionIndividualEndpointsTests.EsperarError(await sa.GetAsync($"{E.Dashboard}?empresa={empresa}", ct),
            HttpStatusCode.Forbidden, "SIN_USUARIO", SinUsuario);
    }

    private static async Task<(int Status, string? Codigo, string? Error)> ManejarAsync(Exception ex)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        using var cuerpo = new MemoryStream();
        http.Response.Body = cuerpo;

        var manejada = await new ManejadorReglas().TryHandleAsync(http, ex, CancellationToken.None);

        manejada.Should().BeTrue();
        using var json = JsonDocument.Parse(cuerpo.ToArray());
        return (http.Response.StatusCode, json.RootElement.GetProperty("code").GetString(), json.RootElement.GetProperty("error").GetString());
    }

    private static string RutaContrato()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "openapi", "impuestos.v1.yaml")))
            dir = dir.Parent;
        dir.Should().NotBeNull("las pruebas corren dentro del repo");
        return Path.Combine(dir!.FullName, "contracts", "openapi", "impuestos.v1.yaml");
    }

    /// <summary><c>x-flit-literales.errores</c> (raíz) de <c>contracts/openapi/impuestos.v1.yaml</c>.</summary>
    private static Dictionary<string, string> ErroresDelContrato()
    {
        var lineas = File.ReadAllLines(RutaContrato());
        var raiz = Array.FindIndex(lineas, l => l.TrimEnd() == "x-flit-literales:");
        var errores = Array.FindIndex(lineas, raiz, l => l.TrimEnd() == "  errores:");
        var resultado = new Dictionary<string, string>();
        for (var i = errores + 1; i < lineas.Length && lineas[i].StartsWith("    ", StringComparison.Ordinal); i++)
        {
            var m = Literal().Match(lineas[i]);
            if (m.Success)
                resultado[m.Groups[1].Value] = m.Groups[2].Value.Trim().Trim('"');
        }

        return resultado;
    }

    [GeneratedRegex("^    ([A-Z_]+):\\s*(.+)$")]
    private static partial Regex Literal();
}
