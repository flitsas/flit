using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #14582 (be1-b; contrato-api §1.1 y §1.5; SP-5, B-6) — límites de solicitudes de core-diagnostico: <c>diagnostico-express</c>
/// por usuario en <c>POST /express</c> y <c>POST /express/estimado</c>; <c>diagnostico-confirmacion</c> por usuario y por compañía en
/// <c>POST /diagnosticos/{id}/vehiculos</c>; <c>diagnostico-invitaciones</c> registrada (la aplica be2b). Al superar el límite: 429
/// <c>DEMASIADAS_SOLICITUDES</c> con <c>Retry-After</c>. Auditoría de sondeo: más declaraciones de propietario para la misma placa en
/// el día que el umbral deja un aviso con ids y conteo, sin placa ni documento. Límites bajos por configuración; PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --filter "FullyQualifiedName~LimitesSolicitudesTests"
/// </code>
/// </example>
public sealed class LimitesSolicitudesTests
{
    private const string RutaExpress = Program.Prefijo + "/express";
    private const string RutaEstimado = Program.Prefijo + "/express/estimado";

    // SP-5 / §1.5 — Express: 2 por minuto por usuario; la tercera (incluido el estimado, misma política) es 429 con código y
    // Retry-After. Otro usuario de la misma compañía no comparte el cupo.
    [Fact]
    public async Task Express_por_encima_del_limite_del_usuario_responde_429_con_codigo_y_retry_after()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Diagnostico:LimitesSolicitudes:ExpressPorUsuarioPorMinuto"] = "2",
        }));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        await SembrarAsync(empresa, new ConfiguracionEmpresa(), ct);
        var usuario = Guid.NewGuid().ToString();
        using var cliente = api.Cliente(Token(api, empresa.Id, usuario));
        using var otro = api.Cliente(Token(api, empresa.Id, Guid.NewGuid().ToString()));

        var primera = await cliente.PostAsync(RutaEstimado, Cuerpo(Express("LIM101")), ct);
        var segunda = await cliente.PostAsync(RutaExpress, Cuerpo(Express("LIM101")), ct);
        var tercera = await cliente.PostAsync(RutaExpress, Cuerpo(Express("LIM102")), ct);
        var deOtro = await otro.PostAsync(RutaExpress, Cuerpo(Express("LIM103")), ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK, await primera.Content.ReadAsStringAsync(ct));
        segunda.StatusCode.Should().Be(HttpStatusCode.Created, await segunda.Content.ReadAsStringAsync(ct));
        await Debe429Async(tercera, ct);
        deOtro.StatusCode.Should().Be(HttpStatusCode.Created, "el cupo es por usuario");
    }

    // SP-5 / §1.5 — confirmación: 1 por minuto por usuario y 2 por minuto por compañía, encadenados usuario → compañía (CR-1 del
    // review): el segundo intento de «uno» es 429 por usuario y NO gasta cupo de la compañía, así que «dos» pasa el límite (409:
    // el lote ya está confirmado); «tres» es 429 por compañía; la compañía B no comparte el cupo.
    [Fact]
    public async Task Confirmacion_limita_por_usuario_y_por_compania()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Diagnostico:LimitesSolicitudes:ConfirmacionPorUsuarioPorMinuto"] = "1",
            ["Diagnostico:LimitesSolicitudes:ConfirmacionPorCompaniaPorMinuto"] = "2",
        }));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var (cliente, id) = await Lotes.CrearAsync(api, empresas.A, new ConfiguracionEmpresa(), ct);
        cliente.Dispose();
        using var uno = api.Cliente(Token(api, empresas.A.Id, Guid.NewGuid().ToString()));
        using var dos = api.Cliente(Token(api, empresas.A.Id, Guid.NewGuid().ToString()));
        using var tres = api.Cliente(Token(api, empresas.A.Id, Guid.NewGuid().ToString()));
        using var deB = api.Cliente(Token(api, empresas.B.Id, Guid.NewGuid().ToString()));

        var primera = await Lotes.ConfirmarAsync(uno, id, [Lotes.Fila("LIM201")], ct);
        var segunda = await Lotes.ConfirmarAsync(uno, id, [Lotes.Fila("LIM202")], ct);
        var deDos = await Lotes.ConfirmarAsync(dos, id, [Lotes.Fila("LIM203")], ct);
        var porCompania = await Lotes.ConfirmarAsync(tres, id, [Lotes.Fila("LIM205")], ct);
        var otraCompania = await Lotes.ConfirmarAsync(deB, Guid.NewGuid(), [Lotes.Fila("LIM204")], ct);

        primera.StatusCode.Should().Be(HttpStatusCode.Accepted, await primera.Content.ReadAsStringAsync(ct));
        await Debe429Async(segunda, ct);
        deDos.StatusCode.Should().Be(HttpStatusCode.Conflict, "CR-1: el intento rechazado por usuario no gastó cupo de la compañía");
        await Debe429Async(porCompania, ct);
        otraCompania.StatusCode.Should().Be(HttpStatusCode.NotFound, "B no comparte el cupo de A: pasa el límite y el lote no es suyo");
    }

    // DM-2 — la política diagnostico-invitaciones queda registrada (la aplica be2b): 1 por hora por actor y 2 por hora por compañía,
    // encadenados como la confirmación (CR-1): el segundo intento del actor 1 es 429 y no gasta cupo de la compañía; el actor 2
    // pasa; el actor 3 es 429 por compañía.
    [Fact]
    public async Task Politica_de_invitaciones_queda_registrada()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Ruta = Program.Prefijo + "/prueba-limite-invitaciones";
        await using var api = await ApiDePrueba.ConBaseAsync(
            mapear: app => app.MapPost(Ruta, () => Results.Ok()).RequireRateLimiting(LimitesSolicitudes.Invitaciones),
            configurar: b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Diagnostico:LimitesSolicitudes:InvitacionesPorActorPorHora"] = "1",
                ["Diagnostico:LimitesSolicitudes:InvitacionesPorCompaniaPorHora"] = "2",
            }));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        using var actor1 = api.Cliente(Token(api, empresa.Id, Guid.NewGuid().ToString()));
        using var actor2 = api.Cliente(Token(api, empresa.Id, Guid.NewGuid().ToString()));
        using var actor3 = api.Cliente(Token(api, empresa.Id, Guid.NewGuid().ToString()));

        var primera = await actor1.PostAsync(Ruta, null, ct);
        var segunda = await actor1.PostAsync(Ruta, null, ct);
        var deActor2 = await actor2.PostAsync(Ruta, null, ct);
        var deActor3 = await actor3.PostAsync(Ruta, null, ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        await Debe429Async(segunda, ct);
        deActor2.StatusCode.Should().Be(HttpStatusCode.OK, "CR-1: el rechazo por actor no gastó cupo de la compañía");
        await Debe429Async(deActor3, ct);
        new[] { LimitesSolicitudes.Express, LimitesSolicitudes.Confirmacion, LimitesSolicitudes.Invitaciones }
            .Should().Equal("diagnostico-express", "diagnostico-confirmacion", "diagnostico-invitaciones");
    }

    // SP-5 — auditoría de sondeo: con umbral 2, la tercera declaración del día para la misma placa en la compañía deja un Warning
    // con el diagnóstico, la compañía y el conteo; nunca la placa ni el documento. Por debajo del umbral, ningún aviso.
    [Fact]
    public async Task Declaraciones_de_la_misma_placa_por_encima_del_umbral_dejan_aviso_sin_placa_ni_documento()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new LogsEnMemoria();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Diagnostico:Alertas:DeclaracionesPorPlacaDia"] = "2" });
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<LogsEnMemoria>(null, LogLevel.Trace);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        await SembrarAsync(empresa, new ConfiguracionEmpresa(), ct);
        using var cliente = api.Cliente(Token(api, empresa.Id, Guid.NewGuid().ToString()));
        string[] documentos = ["1020304051", "1020304052", "1020304053"];
        var ids = new List<Guid>();

        foreach (var documento in documentos)
        {
            var respuesta = await cliente.PostAsync(RutaExpress, Cuerpo(Express("SND777", "CC", documento)), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync(ct));
            ids.Add(JsonElement.Parse(await respuesta.Content.ReadAsStringAsync(ct)).GetProperty("id").GetGuid());
            if (ids.Count == 2)
                Avisos(logs).Should().BeEmpty("dos declaraciones no superan el umbral de 2");
        }

        var aviso = Avisos(logs).Should().ContainSingle().Subject;
        aviso.Should().Contain(ids[2].ToString()).And.Contain(empresa.Id.ToString()).And.Contain("3");
        logs.Mensajes.Should().NotContain(m => m.Contains("SND777", StringComparison.Ordinal), "la placa no va al log");
        logs.Mensajes.Should().NotContain(m => documentos.Any(d => m.Contains(d, StringComparison.Ordinal)), "ningún documento va al log");
    }

    private static List<string> Avisos(LogsEnMemoria logs) =>
        [.. logs.Mensajes.Where(m => m.Contains(AuditoriaSondeoPropietario.Marca, StringComparison.Ordinal))];

    private static async Task Debe429Async(HttpResponseMessage respuesta, CancellationToken ct)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, texto);
        respuesta.Headers.RetryAfter.Should().NotBeNull("el 429 lleva Retry-After");
        respuesta.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeGreaterThan(0);
        var json = JsonElement.Parse(texto);
        json.GetProperty("code").GetString().Should().Be(LimitesSolicitudes.CodigoDemasiadasSolicitudes).And.Be("DEMASIADAS_SOLICITUDES");
        json.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static string Token(ApiDePrueba api, Guid empresa, string usuario) =>
        api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosCrear, Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"], sujeto: usuario);

    private static string Express(string identificador, string? tipo = null, string? documento = null) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["identificador"] = identificador,
        ["modo"] = "SIMPLE",
        ["tipo_tramite_codigo"] = null,
        ["alcance"] = new Dictionary<string, string[]> { ["por_vehiculo"] = ["RUNT"], ["por_propietario"] = [], ["por_locatario"] = [] },
        ["tipo_documento_propietario"] = tipo,
        ["documento_propietario"] = documento,
    });
}
