using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Configuracion;

/// <summary>
/// HU #13741 (F00.2-H2), escenarios 1 a 6 y 11 — <c>GET/PUT /configuracion</c> de la compañía del token: dos niveles en la
/// lectura (el contractual en solo lectura, sin <c>redaccion_ia_suspendida</c>) y solo el nivel operativo en la edición.
/// Orden de validación del PUT: un campo que no es operativo da 403 sin guardar; un valor fuera de conjunto, 422.
/// Postgres real (base efímera de <see cref="ApiDePrueba"/>).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ConfiguracionOperativaTests
/// </code>
/// </example>
public sealed class ConfiguracionOperativaTests
{
    private const string Ruta = Program.Prefijo + ConfiguracionEndpoints.Ruta;

    // Escenario 1 — lectura de dos niveles, sin redaccion_ia_suspendida.
    [Fact]
    public async Task Get_devuelve_dos_niveles_sin_redaccion_ia_suspendida()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa
        {
            FuentesHabilitadas = [Fuente.Runt, Fuente.Avaluo],
            LimiteVehiculosLote = 200,
            RedaccionIaSuspendida = true,
            VisibilidadUsuario = VisibilidadUsuario.Propios,
            FuentesPorDefecto = [Fuente.Runt],
            EstimadoConsultas = EstimadoConsultas.AlFinalizar,
        }, ct);

        var respuesta = await api.Cliente(TokenVer(api, empresas.A.Id)).GetAsync(Ruta, ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        var json = JsonDocument.Parse(cuerpo).RootElement;
        json.GetProperty("empresa").GetGuid().Should().Be(empresas.A.Id);
        var contrato = json.GetProperty("contrato");
        contrato.GetProperty("solo_lectura").GetBoolean().Should().BeTrue("el nivel contractual es de solo lectura para la compañía");
        Codigos(contrato.GetProperty("fuentes_habilitadas")).Should().Equal("RUNT", "AVALUO");
        contrato.GetProperty("limite_vehiculos_lote").GetInt32().Should().Be(200);
        var operacion = json.GetProperty("operacion");
        operacion.GetProperty("visibilidad_usuario").GetString().Should().Be("PROPIOS");
        Codigos(operacion.GetProperty("fuentes_por_defecto")).Should().Equal("RUNT");
        operacion.GetProperty("estimado_consultas").GetString().Should().Be("AL_FINALIZAR");
        cuerpo.Should().NotContainEquivalentOf("redaccion", "el DTO nunca trae redaccion_ia_suspendida");
    }

    // HU #13863 (F09-H4), AC4 y AC5 (F09-C8) — GET /configuracion trae fuentes_disponibles en el nivel superior, en el orden del
    // enum y con la regla del 422: sin C3 el impuesto no está aunque la compañía lo tenga en fuentes_habilitadas. El PUT
    // operativo devuelve la misma forma.
    [Fact]
    public async Task GET_configuracion_trae_fuentes_disponibles_en_el_orden_del_enum()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.Avaluo, Fuente.ImpuestoDepartamental] }, ct);
        var esperadas = Enum.GetValues<Fuente>().Where(f => f != Fuente.ImpuestoDepartamental).Select(CodigosDeEnum.Codigo).ToList();

        var lectura = await api.Cliente(TokenVer(api, empresas.A.Id)).GetAsync(Ruta, ct);
        var edicion = await api.Cliente(TokenAdmin(api, empresas.A.Id)).PutAsync(Ruta, Cuerpo("""{ "visibilidad_usuario": "PROPIOS" }"""), ct);

        foreach (var respuesta in new[] { lectura, edicion })
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
            var json = JsonDocument.Parse(texto).RootElement;
            Codigos(json.GetProperty("fuentes_disponibles")).Should().Equal(esperadas, texto);
            Codigos(json.GetProperty("contrato").GetProperty("fuentes_habilitadas")).Should().Equal("RUNT", "AVALUO", "IMPUESTO_DEPARTAMENTAL");
        }

        esperadas.Should().Equal(ContratoBackofficeTests.SinImpuesto);
    }

    // Escenario 2 — compañía sin fila: valores por defecto, sin escribir.
    [Fact]
    public async Task Sin_fila_devuelve_valores_por_defecto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);

        var respuesta = await api.Cliente(TokenVer(api, empresas.A.Id)).GetAsync(Ruta, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        Codigos(json.GetProperty("contrato").GetProperty("fuentes_habilitadas")).Should().Equal("RUNT");
        json.GetProperty("contrato").GetProperty("limite_vehiculos_lote").GetInt32().Should().Be(500);
        json.GetProperty("operacion").GetProperty("visibilidad_usuario").GetString().Should().Be("EMPRESA");
        Codigos(json.GetProperty("operacion").GetProperty("fuentes_por_defecto")).Should().Equal("RUNT");
        json.GetProperty("operacion").GetProperty("estimado_consultas").GetString().Should().Be("NO_MOSTRAR");
        await using var db = empresas.A.Db();
        (await db.Set<ConfiguracionEmpresa>().CountAsync(ct)).Should().Be(0, "leer sin fila no escribe la configuración");
    }

    // Escenario 3 — edición operativa válida: solo esos campos, con usuario y fecha.
    [Fact]
    public async Task Put_valido_guarda_solo_nivel_operativo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa
        {
            FuentesHabilitadas = [Fuente.Runt, Fuente.Avaluo],
            FuentesPorDefecto = [Fuente.Runt, Fuente.Avaluo],
            LimiteVehiculosLote = 300,
        }, ct);
        var token = TokenAdmin(api, empresas.A.Id);
        var usuario = Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token).Subject);
        var antes = DateTimeOffset.UtcNow.AddSeconds(-5);

        var respuesta = await api.Cliente(token).PutAsync(Ruta, Cuerpo("""{ "visibilidad_usuario": "PROPIOS", "estimado_consultas": "ANTES_DE_LANZAR" }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("operacion").GetProperty("visibilidad_usuario").GetString().Should().Be("PROPIOS");
        var guardada = await LeerAsync(api, empresas.A.Id, ct);
        guardada.VisibilidadUsuario.Should().Be(VisibilidadUsuario.Propios);
        guardada.EstimadoConsultas.Should().Be(EstimadoConsultas.AntesDeLanzar);
        guardada.FuentesPorDefecto.Should().Equal([Fuente.Runt, Fuente.Avaluo], "un campo que no llega no cambia");
        guardada.FuentesHabilitadas.Should().Equal(Fuente.Runt, Fuente.Avaluo);
        guardada.LimiteVehiculosLote.Should().Be(300);
        guardada.OperativaActualizadaPor.Should().Be(usuario);
        guardada.OperativaActualizadaEn.Should().NotBeNull().And.BeOnOrAfter(antes).And.BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(5));
        guardada.ContratoActualizadoPor.Should().BeNull("la edición operativa no toca el nivel contractual");

        // Sin fila previa, la edición operativa la crea con los valores contractuales por defecto.
        var deB = await api.Cliente(TokenAdmin(api, empresas.B.Id)).PutAsync(Ruta, Cuerpo("""{ "estimado_consultas": "AL_FINALIZAR" }"""), ct);
        deB.StatusCode.Should().Be(HttpStatusCode.OK, await deB.Content.ReadAsStringAsync(ct));
        var deBGuardada = await LeerAsync(api, empresas.B.Id, ct);
        deBGuardada.EstimadoConsultas.Should().Be(EstimadoConsultas.AlFinalizar);
        deBGuardada.LimiteVehiculosLote.Should().Be(500);
        (await LeerAsync(api, empresas.A.Id, ct)).EstimadoConsultas.Should().Be(EstimadoConsultas.AntesDeLanzar, "B no toca la fila de A");
    }

    // Escenario 4 — valor fuera de conjunto: 422.
    [Fact]
    public async Task Put_con_valor_fuera_de_conjunto_responde_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        using var cliente = api.Cliente(TokenAdmin(api, empresas.A.Id));

        foreach (var cuerpo in new[]
                 {
                     """{ "estimado_consultas": "SIEMPRE" }""", """{ "visibilidad_usuario": "TODOS" }""",
                     """{ "fuentes_por_defecto": ["RUNT", "NO_EXISTE"] }""", """{ "estimado_consultas": null }""",
                 })
        {
            var respuesta = await cliente.PutAsync(Ruta, Cuerpo(cuerpo), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, cuerpo);
            (await Codigo(respuesta, ct)).Should().Be("VALOR_FUERA_DE_CONJUNTO", cuerpo);
        }

        await using var db = empresas.A.Db();
        (await db.Set<ConfiguracionEmpresa>().CountAsync(ct)).Should().Be(0, "un 422 no guarda nada");
    }

    // Escenario 5 — campo contractual en la edición operativa: 403 antes que el 422, y no guarda ningún campo.
    [Fact]
    public async Task Put_con_campo_contractual_responde_403_y_no_guarda()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa(), ct);
        using var cliente = api.Cliente(TokenAdmin(api, empresas.A.Id));

        foreach (var cuerpo in new[]
                 {
                     """{ "visibilidad_usuario": "PROPIOS", "limite_vehiculos_lote": 10 }""",
                     """{ "visibilidad_usuario": "PROPIOS", "fuentes_habilitadas": ["RUNT", "AVALUO"] }""",
                     """{ "limite_vehiculos_lote": 10, "estimado_consultas": "SIEMPRE" }""",
                     """{ "visibilidad_usuario": "PROPIOS", "usuarios": [] }""",
                 })
        {
            var respuesta = await cliente.PutAsync(Ruta, Cuerpo(cuerpo), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, cuerpo);
            (await Codigo(respuesta, ct)).Should().Be("CAMPO_NO_EDITABLE", cuerpo);
        }

        var guardada = await LeerAsync(api, empresas.A.Id, ct);
        guardada.VisibilidadUsuario.Should().Be(VisibilidadUsuario.Empresa, "con un 403 no se guarda ni el campo operativo");
        guardada.LimiteVehiculosLote.Should().Be(500);
        guardada.FuentesHabilitadas.Should().Equal(Fuente.Runt);
        guardada.OperativaActualizadaPor.Should().BeNull();
    }

    // Escenario 5 — redaccion_ia_suspendida en la edición operativa: 403.
    [Fact]
    public async Task Put_con_redaccion_ia_suspendida_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa(), ct);

        var respuesta = await api.Cliente(TokenAdmin(api, empresas.A.Id))
            .PutAsync(Ruta, Cuerpo("""{ "estimado_consultas": "AL_FINALIZAR", "redaccion_ia_suspendida": true }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Codigo(respuesta, ct)).Should().Be("CAMPO_NO_EDITABLE");
        var guardada = await LeerAsync(api, empresas.A.Id, ct);
        guardada.RedaccionIaSuspendida.Should().BeFalse();
        guardada.EstimadoConsultas.Should().Be(EstimadoConsultas.NoMostrar, "no guarda ningún campo");
    }

    // Escenario 6 — por defecto fuera de las habilitadas: 422.
    [Fact]
    public async Task Por_defecto_fuera_de_habilitadas_responde_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt] }, ct);

        var respuesta = await api.Cliente(TokenAdmin(api, empresas.A.Id))
            .PutAsync(Ruta, Cuerpo("""{ "fuentes_por_defecto": ["RUNT", "AVALUO"] }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Codigo(respuesta, ct)).Should().Be("POR_DEFECTO_FUERA_DE_HABILITADAS");
        (await LeerAsync(api, empresas.A.Id, ct)).FuentesPorDefecto.Should().Equal(Fuente.Runt);
    }

    // Escenario 11 — edición operativa sin diagnostico.configuracion.editar: 403 (y la lectura sin configuracion.ver, también).
    [Fact]
    public async Task Sin_permiso_editar_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);

        var respuesta = await api.Cliente(TokenVer(api, empresas.A.Id)).PutAsync(Ruta, Cuerpo("""{ "visibilidad_usuario": "PROPIOS" }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var sinVer = api.Token(empresa: empresas.A.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);
        (await api.Cliente(sinVer).GetAsync(Ruta, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "leer exige diagnostico.configuracion.ver");
        await using var db = empresas.A.Db();
        (await db.Set<ConfiguracionEmpresa>().CountAsync(ct)).Should().Be(0);
    }

    // Una violación de CHECK de la base (23514) que se escape a la validación de la aplicación responde 422 de dominio, no 500.
    [Fact]
    public async Task Violacion_de_check_de_la_base_responde_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var http = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var check = new PostgresException("new row violates check constraint", "ERROR", "ERROR", PostgresErrorCodes.CheckViolation);
        var otra = new PostgresException("unique violation", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

        var manejada = await new ManejadorReglas().TryHandleAsync(http, new DbUpdateException("check", check), ct);
        var ajena = await new ManejadorReglas().TryHandleAsync(new DefaultHttpContext(), new DbUpdateException("unique", otra), ct);

        manejada.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        http.Response.Body.Position = 0;
        (await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: ct)).RootElement.GetProperty("code").GetString().Should().Be("VALOR_INVALIDO");
        ajena.Should().BeFalse("solo la violación de CHECK se traduce a 422");
    }

    internal static string TokenVer(ApiDePrueba api, Guid empresa) =>
        api.Token(empresa: empresa, permisos: [Permisos.ConfiguracionVer], roles: ["usuario_diagnostico"]);

    internal static string TokenAdmin(ApiDePrueba api, Guid empresa) =>
        api.Token(empresa: empresa, permisos: [Permisos.ConfiguracionVer, Permisos.ConfiguracionEditar], roles: ["admin_diagnostico"]);

    internal static StringContent Cuerpo(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    internal static async Task SembrarAsync(EmpresaDePrueba empresa, ConfiguracionEmpresa configuracion, CancellationToken ct)
    {
        await using var db = empresa.Db();
        db.Add(configuracion);
        await db.SaveChangesAsync(ct);
    }

    internal static async Task<ConfiguracionEmpresa> LeerAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa);
        return await db.Set<ConfiguracionEmpresa>().AsNoTracking().SingleAsync(ct);
    }

    internal static async Task<string?> Codigo(HttpResponseMessage respuesta, CancellationToken ct) =>
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString();

    internal static IReadOnlyList<string?> Codigos(JsonElement arreglo) => arreglo.EnumerateArray().Select(e => e.GetString()).ToList();
}
