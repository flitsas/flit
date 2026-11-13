using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Configuracion;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// Ajustes de Diagnóstico del 2026-11-11, punto 1 (AC 1–7) y punto 6 (AC4): el backoffice se parte en dos políticas.
/// <list type="bullet">
/// <item><b>Configuración</b> (resumen, contrato, diagnósticos —listado, grilla, historial, detalle del dictamen—, intentos,
/// fuentes obligatorias y la lista de empresas): un SuperAdmin de cualquier compañía, o un usuario de la compañía FLIT con
/// <c>diagnostico.backoffice.ver</c>.</item>
/// <item><b>Operación FLIT</b> (override del dictamen y llenado manual): solo la compañía FLIT (permiso o SuperAdmin), como
/// antes.</item>
/// </list>
/// Un usuario no FLIT sin SuperAdmin, aunque tenga el permiso, recibe 403; un token de servicio nunca pasa. La matriz corre sin
/// base: «pasa» es llegar al filtro o al cuerpo del endpoint (400 <c>EMPRESA_REQUERIDA</c>, 503 sin Identidad, 400
/// <c>CUERPO_INVALIDO</c>), «no pasa» es 403. Datos ficticios (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~PoliticasBackofficeMatrizTests
/// </code>
/// </example>
public sealed class PoliticasBackofficeMatrizTests
{
    private const string Base = Program.Prefijo + BackofficeEndpoints.Grupo;

    /// <summary>Permisos del rol <c>backoffice_diagnostico</c> que tocan las secciones de la matriz.</summary>
    private static readonly string[] PermisosBackoffice =
    [
        Permisos.BackofficeVer, Permisos.ConfiguracionContratoEditar, Permisos.AuditoriaVer, Permisos.ConsultasLlenar,
        Permisos.DictamenOverride, Permisos.ConfiguracionVer,
    ];

    private enum Senal
    {
        /// <summary>Llegó al filtro de empresa explícita: 400 <c>EMPRESA_REQUERIDA</c>.</summary>
        EmpresaRequerida,

        /// <summary>Llegó al endpoint de empresas sin Identidad configurada: 503 <c>IDENTIDAD_NO_CONFIGURADA</c>.</summary>
        IdentidadNoConfigurada,

        /// <summary>Llegó a la validación del cuerpo: 400 <c>CUERPO_INVALIDO</c>.</summary>
        CuerpoInvalido,

        /// <summary>Llegó al endpoint (sin base responde un error de la base, nunca 401/403/404).</summary>
        NoDenegada,
    }

    private sealed record Seccion(string Nombre, HttpMethod Metodo, string Url, string? Cuerpo, bool Operacion, Senal Pasa);

    private sealed record Actor(string Nombre, bool Configuracion, bool Operacion, Func<ApiDePrueba, string> Token);

    private static readonly Guid Lote = Guid.NewGuid();
    private static readonly Guid Vehiculo = Guid.NewGuid();
    private static readonly Guid Consulta = Guid.NewGuid();
    private static readonly Guid EmpresaRuta = Guid.NewGuid();
    private static readonly Guid OtraEmpresa = Guid.NewGuid();

    private static readonly Seccion[] Secciones =
    [
        new("resumen", HttpMethod.Get, $"{Base}/resumen", null, false, Senal.EmpresaRequerida),
        new("contrato GET", HttpMethod.Get, $"{Base}/empresas/{EmpresaRuta}/configuracion/contrato?empresa={OtraEmpresa}", null, false, Senal.EmpresaRequerida),
        new("contrato PUT", HttpMethod.Put, $"{Base}/empresas/{EmpresaRuta}/configuracion/contrato?empresa={OtraEmpresa}", """{ "limite_vehiculos_lote": 200 }""", false, Senal.EmpresaRequerida),
        new("diagnosticos", HttpMethod.Get, $"{Base}/diagnosticos", null, false, Senal.EmpresaRequerida),
        new("grilla", HttpMethod.Get, $"{Base}/diagnosticos/{Lote}/grilla", null, false, Senal.EmpresaRequerida),
        new("historial", HttpMethod.Get, $"{Base}/diagnosticos/{Lote}/historial", null, false, Senal.EmpresaRequerida),
        new("detalle (dictamen GET)", HttpMethod.Get, $"{Base}/diagnosticos/{Lote}/vehiculos/{Vehiculo}/dictamen", null, false, Senal.EmpresaRequerida),
        new("versiones del dictamen (B7)", HttpMethod.Get, $"{Base}/diagnosticos/{Lote}/vehiculos/{Vehiculo}/dictamen/versiones", null, false, Senal.EmpresaRequerida),
        new("intentos", HttpMethod.Get, $"{Base}/consultas/{Consulta}/intentos", null, false, Senal.EmpresaRequerida),
        new("empresas", HttpMethod.Get, $"{Base}/empresas", null, false, Senal.IdentidadNoConfigurada),
        new("fuentes obligatorias GET", HttpMethod.Get, $"{Base}/fuentes-obligatorias", null, false, Senal.NoDenegada),
        new("fuentes obligatorias PUT", HttpMethod.Put, $"{Base}/fuentes-obligatorias", "[]", false, Senal.CuerpoInvalido),
        new("override del dictamen PUT", HttpMethod.Put, $"{Base}/diagnosticos/{Lote}/vehiculos/{Vehiculo}/dictamen", "{}", true, Senal.EmpresaRequerida),
        new("llenado manual GET", HttpMethod.Get, $"{Base}/consultas/{Consulta}/manual", null, true, Senal.EmpresaRequerida),
        new("llenado manual PUT", HttpMethod.Put, $"{Base}/consultas/{Consulta}/manual", "{}", true, Senal.EmpresaRequerida),
    ];

    private static readonly Dictionary<string, Actor> Actores = new Actor[]
    {
        new("superadmin_no_flit", Configuracion: true, Operacion: false, a => a.Token(permisos: [], roles: ["SuperAdmin"], tipoEmpresa: "CLIENTE")),
        new("no_flit_con_permiso", Configuracion: false, Operacion: false,
            a => a.Token(permisos: PermisosBackoffice, roles: ["backoffice_diagnostico"], tipoEmpresa: "RENTING")),
        new("flit_con_permiso", Configuracion: true, Operacion: true,
            a => a.Token(permisos: PermisosBackoffice, roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT")),
        new("superadmin_flit", Configuracion: true, Operacion: true, a => a.Token(permisos: [], roles: ["SuperAdmin"], tipoEmpresa: "FLIT")),
        new("token_de_servicio", Configuracion: false, Operacion: false,
            a => a.Token(permisos: PermisosBackoffice, roles: ["SuperAdmin"], tipoEmpresa: "FLIT", sujeto: "svc-diagnostico")),
    }.ToDictionary(a => a.Nombre, StringComparer.Ordinal);

    // AC 1, 3, 4, 5 y 6 (punto 1) y AC4 (punto 6) — cada actor contra cada sección del backoffice.
    [Theory]
    [InlineData("superadmin_no_flit")]
    [InlineData("no_flit_con_permiso")]
    [InlineData("flit_con_permiso")]
    [InlineData("superadmin_flit")]
    [InlineData("token_de_servicio")]
    public async Task Matriz_actor_por_seccion(string nombre)
    {
        var ct = TestContext.Current.CancellationToken;
        var actor = Actores[nombre];
        await using var api = await ApiDePrueba.SinBaseAsync();
        using var cliente = api.Cliente(actor.Token(api));
        var fallos = new List<string>();

        foreach (var seccion in Secciones)
        {
            using var pedido = new HttpRequestMessage(seccion.Metodo, seccion.Url);
            if (seccion.Cuerpo is not null)
                pedido.Content = new StringContent(seccion.Cuerpo, Encoding.UTF8, "application/json");
            using var respuesta = await cliente.SendAsync(pedido, ct);
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            var debePasar = seccion.Operacion ? actor.Operacion : actor.Configuracion;
            var error = debePasar ? Pasa(seccion.Pasa, respuesta.StatusCode, texto) : NoPasa(nombre, respuesta.StatusCode);
            if (error is not null)
                fallos.Add($"{seccion.Nombre} ({seccion.Metodo} {seccion.Url}): {error}");
        }

        fallos.Should().BeEmpty($"{nombre}: configuración={actor.Configuracion}, operación={actor.Operacion}");
    }

    // AC 1, 3 y 5 — GET /sesion: capacidades.backoffice = política de configuración; capacidades.backofficeOperacion = operación FLIT.
    [Theory]
    [InlineData("superadmin_no_flit")]
    [InlineData("no_flit_con_permiso")]
    [InlineData("flit_con_permiso")]
    [InlineData("superadmin_flit")]
    public async Task Sesion_expone_backoffice_y_backofficeOperacion(string nombre)
    {
        var ct = TestContext.Current.CancellationToken;
        var actor = Actores[nombre];
        await using var api = await ApiDePrueba.SinBaseAsync();

        var respuesta = await api.Cliente(actor.Token(api)).GetAsync(Program.Prefijo + "/sesion", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var capacidades = (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("capacidades");
        Booleano(capacidades, "backoffice").Should().Be(actor.Configuracion, "backoffice = política de configuración");
        Booleano(capacidades, "backofficeOperacion").Should().Be(actor.Operacion, "backofficeOperacion = política de operación FLIT");
        if (nombre == "superadmin_no_flit")
        {
            // Lo que la configuración le abre (contrato, historial e intentos) y lo que sigue siendo de FLIT.
            Booleano(capacidades, "editarContrato").Should().BeTrue("el contrato es de la política de configuración");
            Booleano(capacidades, "verAuditoria").Should().BeTrue("historial e intentos son de la política de configuración");
            Booleano(capacidades, "corregirDictamen").Should().BeFalse("el override sigue siendo de FLIT");
            Booleano(capacidades, "llenarConsultas").Should().BeFalse("el llenado manual sigue siendo de FLIT");
        }
    }

    // AC6 — el token de servicio tampoco abre la sesión (sin cambio).
    [Fact]
    public async Task Sesion_con_token_de_servicio_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();

        var respuesta = await api.Cliente(Actores["token_de_servicio"].Token(api)).GetAsync(Program.Prefijo + "/sesion", ct);

        respuesta.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    // AC 1, 2 y 7 — un SuperAdmin de una compañía no FLIT lee el resumen de otra compañía y guarda su contrato y las fuentes
    // obligatorias (200). Queda el usuario y la compañía del actor: la lectura en lecturas_backoffice, el contrato en
    // cambios_contrato (usuario) y los dos cambios en el log de auditoría (usuario, compañía del actor y compañía objetivo).
    [Fact]
    public async Task SuperAdmin_no_FLIT_guarda_contrato_y_fuentes_y_queda_auditado_con_usuario_y_compania()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new CapturaDeEventos();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: (WebApplicationBuilder b) =>
        {
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<CapturaDeEventos>(null, LogLevel.Trace);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var propia = Guid.NewGuid();
        var token = api.Token(empresa: propia, permisos: [], roles: ["SuperAdmin"], tipoEmpresa: "CLIENTE");
        var usuario = Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token).Subject);
        using var cliente = api.Cliente(token);

        var resumen = await cliente.GetAsync($"{Base}/resumen?empresa={empresas.B.Id}", ct);
        resumen.StatusCode.Should().Be(HttpStatusCode.OK, await resumen.Content.ReadAsStringAsync(ct));

        var contrato = await cliente.PutAsync(ContratoBackofficeTests.Url(empresas.B.Id),
            new StringContent("""{ "limite_vehiculos_lote": 200 }""", Encoding.UTF8, "application/json"), ct);
        contrato.StatusCode.Should().Be(HttpStatusCode.OK, await contrato.Content.ReadAsStringAsync(ct));

        var fuentes = await cliente.PutAsync($"{Base}/fuentes-obligatorias",
            new StringContent("""{ "familia": "TRASPASO", "tipo_tramite_codigo": null, "fuentes": ["SIMIT_PROPIETARIO"] }""", Encoding.UTF8, "application/json"), ct);
        fuentes.StatusCode.Should().Be(HttpStatusCode.OK, await fuentes.Content.ReadAsStringAsync(ct));

        await using (var dePropia = DosEmpresas.Contexto(api.BaseDeDatos, propia))
        {
            (await dePropia.Set<LecturaBackoffice>().AsNoTracking().ToListAsync(ct)).Should()
                .ContainSingle(l => l.EmpresaConsultada == empresas.B.Id && l.UsuarioId == usuario && l.TenantId == propia,
                    "la lectura queda con el usuario y la compañía del lector");
        }

        await using (var deB = DosEmpresas.Contexto(api.BaseDeDatos, empresas.B.Id))
        {
            (await deB.Set<CambioContrato>().AsNoTracking().ToListAsync(ct)).Should()
                .ContainSingle(c => c.Campo == "limite_vehiculos_lote" && c.UsuarioId == usuario);
            (await deB.Set<DefinicionFuentesObligatorias>().AsNoTracking().SingleAsync(d => d.Familia == "TRASPASO", ct))
                .ActualizadoPor.Should().Be(usuario);
        }

        var eventos = logs.Eventos.Where(e => e.Evento == "CambioConfiguracionBackoffice").ToList();
        eventos.Should().HaveCount(2, "un evento de auditoría por cada cambio de configuración");
        eventos.Should().ContainSingle(e => Valor(e, "Seccion") == "contrato"
            && Valor(e, "UsuarioId") == usuario.ToString() && Valor(e, "EmpresaActor") == propia.ToString()
            && Valor(e, "EmpresaObjetivo") == empresas.B.Id.ToString());
        eventos.Should().ContainSingle(e => Valor(e, "Seccion") == "fuentes_obligatorias"
            && Valor(e, "UsuarioId") == usuario.ToString() && Valor(e, "EmpresaActor") == propia.ToString());
    }

    private static string? Pasa(Senal senal, HttpStatusCode estado, string texto)
    {
        if (estado is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            return $"debía pasar y respondió {(int)estado}";
        return senal switch
        {
            Senal.EmpresaRequerida => Codigo(estado, texto, HttpStatusCode.BadRequest, EmpresaExplicita.CodigoRequerida),
            Senal.IdentidadNoConfigurada => Codigo(estado, texto, HttpStatusCode.ServiceUnavailable, "IDENTIDAD_NO_CONFIGURADA"),
            Senal.CuerpoInvalido => Codigo(estado, texto, HttpStatusCode.BadRequest, "CUERPO_INVALIDO"),
            _ => null,
        };
    }

    private static string? NoPasa(string actor, HttpStatusCode estado)
    {
        var esperado = actor == "token_de_servicio"
            ? estado is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            : estado == HttpStatusCode.Forbidden;
        return esperado ? null : $"debía responder 403 y respondió {(int)estado}";
    }

    private static string? Codigo(HttpStatusCode estado, string texto, HttpStatusCode esperado, string codigo)
    {
        if (estado != esperado)
            return $"esperaba {(int)esperado} {codigo} y respondió {(int)estado} {texto}";
        try
        {
            var real = JsonElement.Parse(texto).GetProperty("code").GetString();
            return real == codigo ? null : $"esperaba code={codigo} y llegó {real}";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return $"esperaba code={codigo} y el cuerpo no lo trae: {texto}";
        }
    }

    private static bool? Booleano(JsonElement capacidades, string propiedad) =>
        capacidades.TryGetProperty(propiedad, out var valor) ? valor.GetBoolean() : null;

    private static string? Valor(EventoCapturado evento, string clave) =>
        evento.Estado.TryGetValue(clave, out var valor) ? valor : null;

    private sealed record EventoCapturado(string? Evento, IReadOnlyDictionary<string, string?> Estado);

    /// <summary>Proveedor de logs que guarda el nombre del evento y los valores estructurados de cada mensaje.</summary>
    private sealed class CapturaDeEventos : ILoggerProvider
    {
        private readonly ConcurrentQueue<EventoCapturado> _eventos = new();

        public IReadOnlyCollection<EventoCapturado> Eventos => _eventos;

        public ILogger CreateLogger(string categoryName) => new Registro(_eventos);

        public void Dispose()
        {
        }

        private sealed class Registro(ConcurrentQueue<EventoCapturado> eventos) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var valores = state is IEnumerable<KeyValuePair<string, object?>> pares
                    ? pares.GroupBy(p => p.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Value?.ToString(), StringComparer.Ordinal)
                    : new Dictionary<string, string?>(StringComparer.Ordinal);
                eventos.Enqueue(new EventoCapturado(eventId.Name, valores));
            }
        }
    }
}
