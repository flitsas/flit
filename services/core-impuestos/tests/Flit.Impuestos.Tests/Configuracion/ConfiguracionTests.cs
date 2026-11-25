using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Credenciales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Impuestos.Tests.Configuracion;

/// <summary>
/// HU #14694 (M08, D-CONFIG, plan §5.1; AC-BE-77, AC-BE-78, AC-BE-79) — configuración en dos niveles:
/// <list type="bullet">
///   <item><c>GET/PUT /configuracion</c>: el Administrador Impuestos lee el contractual y edita el operativo (máximo por lote ≤
///   contractual, 422 <c>TOPE_SUPERA_CONTRACTUAL</c> con <c>tope</c>; correos del aviso). Sin fila, los valores por defecto.</item>
///   <item><c>PUT /backoffice/empresas/{tenantId}/configuracion-contractual</c>: solo el Super Admin; departamentos ⊆ <c>GET
///   /fuentes</c> (422 <c>DEPARTAMENTO_NO_DISPONIBLE</c>); compañía en Listo o Listo sin webhook (409); sin Impuestos, 404.</item>
/// </list>
/// Los errores llevan el literal exacto de <c>x-flit-literales.errores</c> del contrato.
/// </summary>
/// <example>
/// <code>
/// var r = await api.Cliente(ConfiguracionTests.TokenAdmin(api, empresa)).PutAsJsonAsync(ConfiguracionTests.Ruta, new { maxRegistrosLote = 300, correosAviso = new[] { "a@b.co" } }, ct);
/// </code>
/// </example>
public sealed class ConfiguracionTests(ApiConfiguracionFixture fixture) : IClassFixture<ApiConfiguracionFixture>
{
    public const string Ruta = Program.Prefijo + "/configuracion";

    private static readonly string[] CorreosConEspacios = ["avisos@empresa.test", " otro@empresa.test "];
    private static readonly string[] CorreosRepetidos = ["a@empresa.test", "A@Empresa.test"];
    private static readonly string[] DepartamentosConRepetido = ["41", "05", "05"];
    private static readonly string[] DepartamentosConAjeno = ["05", "99"];

    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public async Task Sin_configuracion_guardada_devuelve_los_valores_por_defecto()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();

        using var r = await Api.Cliente(TokenAdmin(Api, empresa)).GetAsync(Ruta, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        var raiz = json.RootElement;
        raiz.GetProperty("contractual").GetProperty("maxRegistrosLote").GetInt32().Should().Be(500, "F5a-CF4: tope sembrado por defecto");
        raiz.GetProperty("contractual").GetProperty("departamentosHabilitados").ValueKind.Should().Be(JsonValueKind.Null, "null = todos los activos");
        raiz.GetProperty("operativa").GetProperty("maxRegistrosLote").ValueKind.Should().Be(JsonValueKind.Null, "null = el contractual");
        raiz.GetProperty("operativa").GetProperty("correosAviso").GetArrayLength().Should().Be(0);
        raiz.GetProperty("departamentosDisponibles").ValueKind.Should().Be(JsonValueKind.Array);
        await using var db = Api.Contexto(empresa);
        (await db.Set<ConfiguracionEmpresa>().CountAsync(ct)).Should().Be(0, "leer no escribe la configuración");
    }

    [Fact]
    public async Task El_operativo_mayor_que_el_contractual_es_422_con_el_tope_y_300_con_correos_se_acepta()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var cliente = Api.Cliente(TokenAdmin(Api, empresa, admin));

        using var rechazo = await cliente.PutAsJsonAsync(Ruta, new { maxRegistrosLote = 600, correosAviso = Array.Empty<string>() }, ct);

        rechazo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using (var error = JsonDocument.Parse(await rechazo.Content.ReadAsStringAsync(ct)))
        {
            error.RootElement.GetProperty("code").GetString().Should().Be("TOPE_SUPERA_CONTRACTUAL");
            error.RootElement.GetProperty("error").GetString().Should().Be("El máximo por lote no puede superar el tope contractual de 500.");
            error.RootElement.GetProperty("tope").GetInt32().Should().Be(500);
            error.RootElement.GetProperty("campo").GetString().Should().Be("maxRegistrosLote");
        }

        await using (var db = Api.Contexto(empresa))
            (await db.Set<ConfiguracionEmpresa>().CountAsync(ct)).Should().Be(0, "un rechazo no guarda nada");

        using var acepta = await cliente.PutAsJsonAsync(Ruta, new { maxRegistrosLote = 300, correosAviso = CorreosConEspacios }, ct);

        acepta.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var cuerpo = JsonDocument.Parse(await acepta.Content.ReadAsStringAsync(ct)))
        {
            var operativa = cuerpo.RootElement.GetProperty("operativa");
            operativa.GetProperty("maxRegistrosLote").GetInt32().Should().Be(300);
            operativa.GetProperty("correosAviso").EnumerateArray().Select(c => c.GetString()).Should().Equal("avisos@empresa.test", "otro@empresa.test");
            cuerpo.RootElement.GetProperty("contractual").GetProperty("maxRegistrosLote").GetInt32().Should().Be(500);
        }

        await using (var db = Api.Contexto(empresa))
        {
            var fila = await db.Set<ConfiguracionEmpresa>().AsNoTracking().SingleAsync(ct);
            fila.MaxRegistrosLoteOperativo.Should().Be(300);
            fila.MaxRegistrosLoteContractual.Should().Be(500);
            fila.ActualizadoPor.Should().Be(admin);
            fila.Version.Should().Be(1);
        }

        using var segundo = await cliente.PutAsJsonAsync(Ruta, new { maxRegistrosLote = 500, correosAviso = Array.Empty<string>() }, ct);
        segundo.StatusCode.Should().Be(HttpStatusCode.OK, "igual al contractual se acepta");
        await using (var db = Api.Contexto(empresa))
            (await db.Set<ConfiguracionEmpresa>().AsNoTracking().SingleAsync(ct)).Version.Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public async Task El_operativo_fuera_de_1_a_5000_es_TOPE_INVALIDO(int maximo)
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).PutAsJsonAsync(Ruta, new { maxRegistrosLote = maximo, correosAviso = Array.Empty<string>() }, ct);

        await DebeSerRegla(r, HttpStatusCode.UnprocessableEntity, "TOPE_INVALIDO", "El máximo por lote debe ser un número entre 1 y 5000.", "maxRegistrosLote", ct);
    }

    [Theory]
    [InlineData("no-es-un-correo")]
    [InlineData("falta@")]
    [InlineData("")]
    public async Task Un_correo_invalido_es_CORREO_INVALIDO(string correo)
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid()))
            .PutAsJsonAsync(Ruta, new { maxRegistrosLote = (int?)null, correosAviso = new[] { "bien@empresa.test", correo } }, ct);

        await DebeSerRegla(r, HttpStatusCode.UnprocessableEntity, "CORREO_INVALIDO", "Un correo no es válido.", "correosAviso", ct);
    }

    [Fact]
    public async Task Mas_de_20_correos_es_CORREO_INVALIDO()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var correos = Enumerable.Range(1, 21).Select(i => $"aviso{i}@empresa.test").ToArray();

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).PutAsJsonAsync(Ruta, new { maxRegistrosLote = 10, correosAviso = correos }, ct);

        await DebeSerRegla(r, HttpStatusCode.UnprocessableEntity, "CORREO_INVALIDO", "Un correo no es válido.", "correosAviso", ct);
    }

    [Fact]
    public async Task El_operativo_null_usa_el_contractual_y_los_correos_repetidos_quedan_una_vez()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid()))
            .PutAsJsonAsync(Ruta, new { maxRegistrosLote = (int?)null, correosAviso = CorreosRepetidos }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        cuerpo.RootElement.GetProperty("operativa").GetProperty("maxRegistrosLote").ValueKind.Should().Be(JsonValueKind.Null);
        cuerpo.RootElement.GetProperty("operativa").GetProperty("correosAviso").EnumerateArray().Select(c => c.GetString()).Should().Equal("a@empresa.test");
    }

    [Fact]
    public async Task El_gestor_no_ve_ni_edita_la_configuracion()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var gestor = Api.Cliente(Api.Token(empresa: Guid.NewGuid(), permisos: [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear]));

        using var lectura = await gestor.GetAsync(Ruta, ct);
        using var escritura = await gestor.PutAsJsonAsync(Ruta, new { maxRegistrosLote = 10, correosAviso = Array.Empty<string>() }, ct);

        lectura.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        escritura.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Solo_el_Super_Admin_guarda_el_contractual()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await SembrarListaAsync(Api, empresa, ct);
        var admin = Api.Token(empresa: empresa, roles: ["admin_impuestos"], permisos: [.. Permisos.RolesPorDefecto[1].Permisos]);
        var conPermisoSinFlit = Api.Token(empresa: empresa, roles: [], permisos: [Permisos.ConfiguracionContratoEditar, Permisos.BackofficeVer]);

        using var deAdmin = await Api.Cliente(admin).PutAsJsonAsync(RutaContractual(empresa), new { maxRegistrosLote = 800, departamentosHabilitados = (string[]?)null }, ct);
        using var deOtro = await Api.Cliente(conPermisoSinFlit).PutAsJsonAsync(RutaContractual(empresa), new { maxRegistrosLote = 800, departamentosHabilitados = (string[]?)null }, ct);

        deAdmin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deOtro.StatusCode.Should().Be(HttpStatusCode.Forbidden, "un permiso de solo SA fuera de FLIT no basta");
        await using var db = Api.Contexto(empresa);
        (await db.Set<ConfiguracionEmpresa>().CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task El_contractual_acepta_departamentos_de_fuentes_y_rechaza_los_ajenos()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await SembrarListaAsync(Api, empresa, ct);
        var sa = Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api));

        using var acepta = await sa.PutAsJsonAsync(RutaContractual(empresa), new { maxRegistrosLote = 800, departamentosHabilitados = DepartamentosConRepetido }, ct);

        acepta.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var cuerpo = JsonDocument.Parse(await acepta.Content.ReadAsStringAsync(ct)))
        {
            var contractual = cuerpo.RootElement.GetProperty("contractual");
            contractual.GetProperty("maxRegistrosLote").GetInt32().Should().Be(800);
            contractual.GetProperty("departamentosHabilitados").EnumerateArray().Select(d => d.GetString()).Should().Equal("05", "41");
            cuerpo.RootElement.GetProperty("departamentosDisponibles").EnumerateArray().Select(d => d.GetProperty("codigo").GetString())
                .Should().Contain(["05", "41"]);
        }

        using var rechazo = await sa.PutAsJsonAsync(RutaContractual(empresa), new { maxRegistrosLote = 800, departamentosHabilitados = DepartamentosConAjeno }, ct);

        await DebeSerRegla(rechazo, HttpStatusCode.UnprocessableEntity, "DEPARTAMENTO_NO_DISPONIBLE", "Un departamento no está en la lista del proveedor.", "departamentosHabilitados", ct);
        await using var db = Api.Contexto(empresa);
        var fila = await db.Set<ConfiguracionEmpresa>().AsNoTracking().SingleAsync(ct);
        fila.DepartamentosHabilitados.Should().Equal("05", "41");
        fila.MaxRegistrosLoteContractual.Should().Be(800);
        fila.ActualizadoPor.Should().Be(CargaManualCredencialesTests.ActorSuperAdmin);
    }

    [Theory]
    [InlineData(EstadoAprovisionamiento.Pendiente)]
    [InlineData(EstadoAprovisionamiento.RequiereCredenciales)]
    [InlineData(EstadoAprovisionamiento.Error)]
    [InlineData(EstadoAprovisionamiento.Suspendido)]
    public async Task El_contractual_exige_la_compania_en_Listo_o_Listo_sin_webhook(EstadoAprovisionamiento estado)
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, empresa, ct, estado);

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api))
            .PutAsJsonAsync(RutaContractual(empresa), new { maxRegistrosLote = 800, departamentosHabilitados = (string[]?)null }, ct);

        await DebeSerRegla(r, HttpStatusCode.Conflict, "APROVISIONAMIENTO_NO_LISTO",
            "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.", null, ct);
    }

    [Fact]
    public async Task Con_Listo_sin_webhook_y_departamentos_null_se_guarda_null_y_ajusta_el_operativo()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await SembrarListaAsync(Api, empresa, ct, EstadoAprovisionamiento.ListoSinWebhook);
        using (var previa = await Api.Cliente(TokenAdmin(Api, empresa)).PutAsJsonAsync(Ruta, new { maxRegistrosLote = 450, correosAviso = Array.Empty<string>() }, ct))
            previa.StatusCode.Should().Be(HttpStatusCode.OK);

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api))
            .PutAsJsonAsync(RutaContractual(empresa), new { maxRegistrosLote = 200, departamentosHabilitados = (string[]?)null }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        cuerpo.RootElement.GetProperty("contractual").GetProperty("departamentosHabilitados").ValueKind.Should().Be(JsonValueKind.Null);
        cuerpo.RootElement.GetProperty("operativa").GetProperty("maxRegistrosLote").GetInt32().Should().Be(200, "el operativo nunca supera el contractual");
        await using var db = Api.Contexto(empresa);
        var fila = await db.Set<ConfiguracionEmpresa>().AsNoTracking().SingleAsync(ct);
        fila.DepartamentosHabilitados.Should().BeNull();
        fila.MaxRegistrosLoteOperativo.Should().Be(200);
        fila.Version.Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public async Task El_contractual_fuera_de_1_a_5000_es_TOPE_INVALIDO(int maximo)
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await SembrarListaAsync(Api, empresa, ct);

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api))
            .PutAsJsonAsync(RutaContractual(empresa), new { maxRegistrosLote = maximo, departamentosHabilitados = (string[]?)null }, ct);

        await DebeSerRegla(r, HttpStatusCode.UnprocessableEntity, "TOPE_INVALIDO", "El máximo por lote debe ser un número entre 1 y 5000.", "maxRegistrosLote", ct);
    }

    [Fact]
    public async Task El_contractual_de_una_compania_sin_Impuestos_es_404()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api))
            .PutAsJsonAsync(RutaContractual(Guid.NewGuid()), new { maxRegistrosLote = 800, departamentosHabilitados = (string[]?)null }, ct);

        await DebeSerRegla(r, HttpStatusCode.NotFound, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos.", null, ct);
    }

    [Fact]
    public async Task La_empresa_explicita_es_solo_del_Super_Admin_y_su_lectura_queda_auditada()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, empresa, ct);
        var sa = Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api));

        using var deAdmin = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).GetAsync($"{Ruta}?empresa={empresa}", ct);
        using var saSinEmpresa = await sa.GetAsync(Ruta, ct);
        using var saConEmpresa = await sa.GetAsync($"{Ruta}?empresa={empresa}", ct);

        await DebeSerRegla(deAdmin, HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.", null, ct);
        await DebeSerRegla(saSinEmpresa, HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.", null, ct);
        saConEmpresa.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = Api.Contexto(empresa);
        (await db.Set<LecturaBackoffice>().AsNoTracking().CountAsync(l => l.Recurso == "configuracion", ct)).Should().Be(1);
    }

    [Fact]
    public async Task El_PUT_de_configuracion_admite_empresa_solo_del_Super_Admin_y_le_es_obligatoria()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await SembrarListaAsync(Api, empresa, ct);
        var sa = Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api));
        var cuerpo = new { maxRegistrosLote = 120, correosAviso = new[] { "sa@empresa.test" } };

        using var deAdmin = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).PutAsJsonAsync($"{Ruta}?empresa={empresa}", cuerpo, ct);
        using var saSinEmpresa = await sa.PutAsJsonAsync(Ruta, cuerpo, ct);
        using var saNoUuid = await sa.PutAsJsonAsync($"{Ruta}?empresa=no-es-uuid", cuerpo, ct);
        using var saRepetida = await sa.PutAsJsonAsync($"{Ruta}?empresa={empresa}&empresa={Guid.NewGuid()}", cuerpo, ct);

        await DebeSerRegla(deAdmin, HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.", null, ct);
        await DebeSerRegla(saSinEmpresa, HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.", null, ct);
        await DebeSerRegla(saNoUuid, HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.", null, ct);
        await DebeSerRegla(saRepetida, HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.", null, ct);
        await using (var db = Api.Contexto(empresa))
            (await db.Set<ConfiguracionEmpresa>().CountAsync(ct)).Should().Be(0, "ningún rechazo guarda");

        using var saConEmpresa = await sa.PutAsJsonAsync($"{Ruta}?empresa={empresa}", cuerpo, ct);

        saConEmpresa.StatusCode.Should().Be(HttpStatusCode.OK, await saConEmpresa.Content.ReadAsStringAsync(ct));
        await using var lectura = Api.Contexto(empresa);
        var fila = await lectura.Set<ConfiguracionEmpresa>().AsNoTracking().SingleAsync(ct);
        fila.MaxRegistrosLoteOperativo.Should().Be(120);
        fila.CorreosAviso.Should().Equal("sa@empresa.test");
        fila.ActualizadoPor.Should().Be(CargaManualCredencialesTests.ActorSuperAdmin);
    }

    [Fact]
    public async Task El_Super_Admin_con_empresa_sin_Impuestos_recibe_404_en_GET_y_PUT_sin_sembrar_ni_auditar()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var inexistente = Guid.NewGuid();
        var sa = Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api));

        using var lectura = await sa.GetAsync($"{Ruta}?empresa={inexistente}", ct);
        using var escritura = await sa.PutAsJsonAsync($"{Ruta}?empresa={inexistente}", new { maxRegistrosLote = 10, correosAviso = Array.Empty<string>() }, ct);

        await DebeSerRegla(lectura, HttpStatusCode.NotFound, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos.", null, ct);
        await DebeSerRegla(escritura, HttpStatusCode.NotFound, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos.", null, ct);
        await using var db = Api.Contexto(inexistente);
        (await db.Set<ConfiguracionEmpresa>().CountAsync(ct)).Should().Be(0, "no se siembra la fila de una compañía sin Impuestos");
        (await db.Set<LecturaBackoffice>().CountAsync(ct)).Should().Be(0, "no se audita la lectura de una compañía sin Impuestos");
    }

    [Fact]
    public async Task El_contractual_con_tenantId_invalido_o_empresa_distinta_es_400_EMPRESA_REQUERIDA()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var sa = Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api));
        var cuerpo = new { maxRegistrosLote = 800, departamentosHabilitados = (string[]?)null };

        using var noUuid = await sa.PutAsJsonAsync($"{Program.Prefijo}/backoffice/empresas/no-es-uuid/configuracion-contractual", cuerpo, ct);
        using var distinta = await sa.PutAsJsonAsync($"{RutaContractual(empresa)}?empresa={Guid.NewGuid()}", cuerpo, ct);

        await DebeSerRegla(noUuid, HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.", null, ct);
        await DebeSerRegla(distinta, HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.", null, ct);
    }

    public static string RutaContractual(Guid empresa) => $"{Program.Prefijo}/backoffice/empresas/{empresa}/configuracion-contractual";

    public static string TokenAdmin(ApiDePrueba api, Guid empresa, Guid? usuario = null) =>
        api.Token(empresa: empresa, roles: ["admin_impuestos"], permisos: [Permisos.ConfiguracionVer, Permisos.ConfiguracionEditar], usuario: usuario);

    /// <summary>Compañía aprovisionada en <paramref name="estado"/> con su api_key guardada (la de las pruebas del cliente).</summary>
    internal static async Task SembrarListaAsync(ApiDePrueba api, Guid empresa, CancellationToken ct, EstadoAprovisionamiento estado = EstadoAprovisionamiento.Listo)
    {
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(api, empresa, ct, estado);
        using var scope = api.App.Services.CreateScope();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenCredenciales>();
        await using var db = almacen.ContextoDe(empresa);
        almacen.Escribir(db, null, empresa, new CredencialesEnClaro(ClienteApiLiquidacionTests.ApiKey, null), OrigenCredencial.Alta, null);
        await db.SaveChangesAsync(ct);
    }

    internal static async Task DebeSerRegla(HttpResponseMessage r, HttpStatusCode status, string codigo, string literal, string? campo, CancellationToken ct)
    {
        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(status, texto);
        using var json = JsonDocument.Parse(texto);
        json.RootElement.GetProperty("code").GetString().Should().Be(codigo);
        json.RootElement.GetProperty("error").GetString().Should().Be(literal, "el error es el literal de x-flit-literales.errores");
        if (campo is not null)
            json.RootElement.GetProperty("campo").GetString().Should().Be(campo);
    }
}

/// <summary>Una API con base para toda la clase (las pruebas usan compañías nuevas) y el API de liquidación de prueba.</summary>
public sealed class ApiConfiguracionFixture : IAsyncLifetime
{
    public ApiLiquidacionDePrueba Doble { get; } = new();

    public ApiDePrueba Api { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Api = await CargaManualCredencialesTests.ConApiAsync(Doble);

    public async ValueTask DisposeAsync() => await Api.DisposeAsync();
}
