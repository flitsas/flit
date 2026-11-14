using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #14563, decisión D23 (2026-11-13): se retira el rol por defecto <c>backoffice_diagnostico</c> y sus capacidades quedan en el
/// SuperAdmin. En la operación FLIT del backoffice (llenado manual, override del dictamen y versiones del dictamen) el SuperAdmin
/// pasa siempre, sin mirar <c>tenant_type</c>; los demás siguen necesitando <c>tenant_type=FLIT</c> y
/// <c>diagnostico.backoffice.ver</c>. Corre sin base: «pasa la política» es llegar al filtro de empresa explícita (400
/// <c>EMPRESA_REQUERIDA</c>); «no pasa» es 403. Datos ficticios (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~SuperAdminOperacionFlitTests
/// </code>
/// </example>
public sealed class SuperAdminOperacionFlitTests
{
    private const string Base = Program.Prefijo + BackofficeEndpoints.Grupo;

    private static readonly Guid Lote = Guid.NewGuid();
    private static readonly Guid Vehiculo = Guid.NewGuid();
    private static readonly Guid Consulta = Guid.NewGuid();

    /// <summary>Las tres rutas de la operación FLIT, sin <c>?empresa=</c> para que la señal de «pasa» sea el filtro.</summary>
    private static readonly (string Nombre, HttpMethod Metodo, string Url, string? Cuerpo)[] Operacion =
    [
        ("llenado manual PUT", HttpMethod.Put, $"{Base}/consultas/{Consulta}/manual", "{}"),
        ("override del dictamen PUT", HttpMethod.Put, $"{Base}/diagnosticos/{Lote}/vehiculos/{Vehiculo}/dictamen", "{}"),
        ("versiones del dictamen GET", HttpMethod.Get, $"{Base}/diagnosticos/{Lote}/vehiculos/{Vehiculo}/dictamen/versiones", null),
    ];

    // Casos 1 y 2 — SuperAdmin de una compañía no FLIT, o sin claim tenant_type: la política de operación no lo frena.
    [Theory]
    [InlineData("CLIENTE")]
    [InlineData("RENTING")]
    [InlineData(null)]
    public async Task SuperAdmin_de_cualquier_compania_pasa_la_operacion_FLIT(string? tipoEmpresa)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        using var cliente = api.Cliente(api.Token(permisos: [], roles: ["SuperAdmin"], tipoEmpresa: tipoEmpresa));

        foreach (var (nombre, metodo, url, cuerpo) in Operacion)
        {
            var (estado, codigo) = await EnviarAsync(cliente, metodo, url, cuerpo, ct);
            estado.Should().NotBe(HttpStatusCode.Forbidden, $"{nombre}: el SuperAdmin pasa siempre (tenant_type={tipoEmpresa ?? "(sin claim)"})");
            (estado, codigo).Should().Be((HttpStatusCode.BadRequest, EmpresaExplicita.CodigoRequerida),
                $"{nombre}: llega a la lógica del endpoint (filtro de empresa explícita)");
        }
    }

    // Caso 3 — capacidades de sesión del SuperAdmin de una compañía no FLIT (o sin tenant_type): toda la operación FLIT.
    [Theory]
    [InlineData("CLIENTE")]
    [InlineData(null)]
    public async Task Sesion_del_SuperAdmin_no_FLIT_trae_todas_las_capacidades_de_operacion(string? tipoEmpresa)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();

        var respuesta = await api.Cliente(api.Token(permisos: [], roles: ["SuperAdmin"], tipoEmpresa: tipoEmpresa))
            .GetAsync(Program.Prefijo + "/sesion", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var capacidades = (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("capacidades");
        foreach (var capacidad in new[] { "backofficeOperacion", "llenarConsultas", "corregirDictamen", "verVolumetria", "backoffice", "verAuditoria" })
            capacidades.GetProperty(capacidad).GetBoolean().Should().BeTrue($"{capacidad}: el SuperAdmin tiene todas las capacidades (D23)");
    }

    // Caso 4 — un usuario que no es SuperAdmin, con backoffice.ver y los permisos de operación, fuera de FLIT: sigue 403.
    [Theory]
    [InlineData("CLIENTE")]
    [InlineData(null)]
    public async Task No_SuperAdmin_con_permisos_fuera_de_FLIT_sigue_en_403(string? tipoEmpresa)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var token = api.Token(
            permisos: [Permisos.BackofficeVer, Permisos.ConsultasLlenar, Permisos.DictamenOverride, Permisos.AuditoriaVer, Permisos.VolumetriaVer],
            roles: ["admin_diagnostico"], tipoEmpresa: tipoEmpresa);
        using var cliente = api.Cliente(token);

        foreach (var (nombre, metodo, url, cuerpo) in Operacion)
        {
            var (estado, _) = await EnviarAsync(cliente, metodo, url, cuerpo, ct);
            estado.Should().Be(HttpStatusCode.Forbidden, $"{nombre}: sin SuperAdmin la operación FLIT exige tenant_type=FLIT");
        }

        var sesion = await cliente.GetAsync(Program.Prefijo + "/sesion", ct);
        var capacidades = (await sesion.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("capacidades");
        foreach (var capacidad in new[] { "backofficeOperacion", "llenarConsultas", "corregirDictamen", "verVolumetria" })
            capacidades.GetProperty(capacidad).GetBoolean().Should().BeFalse($"{capacidad}: es de la compañía FLIT");
    }

    // Caso 5 — el manifiesto ya no trae backoffice_diagnostico; usuario y admin quedan con sus permisos de siempre.
    [Fact]
    public void Roles_por_defecto_son_solo_usuario_y_admin()
    {
        string[] usuario = [Permisos.DiagnosticosCrear, Permisos.DiagnosticosVer, Permisos.ReportesDescargar, Permisos.DerivacionCrear, Permisos.ConfiguracionVer];

        Permisos.RolesPorDefecto.Select(r => r.Codigo).Should().Equal("usuario_diagnostico", "admin_diagnostico");
        Permisos.RolesPorDefecto.Single(r => r.Codigo == "usuario_diagnostico").Permisos.Should().BeEquivalentTo(usuario);
        Permisos.RolesPorDefecto.Single(r => r.Codigo == "admin_diagnostico").Permisos.Should()
            .BeEquivalentTo([.. usuario, Permisos.ConfiguracionEditar, Permisos.ConsultasReprocesar]);
    }

    private static async Task<(HttpStatusCode Estado, string? Codigo)> EnviarAsync(
        HttpClient cliente, HttpMethod metodo, string url, string? cuerpo, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(metodo, url);
        if (cuerpo is not null)
            pedido.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");
        using var respuesta = await cliente.SendAsync(pedido, ct);
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        string? codigo = null;
        if (!string.IsNullOrWhiteSpace(texto))
        {
            try
            {
                codigo = JsonElement.Parse(texto).TryGetProperty("code", out var c) ? c.GetString() : null;
            }
            catch (JsonException)
            {
                codigo = null;
            }
        }

        return (respuesta.StatusCode, codigo);
    }
}
