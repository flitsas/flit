using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #13799 (F06-H2, F06-C5/C6/C7) — el backoffice FLIT lista los lotes de UNA compañía indicada en <c>?empresa=</c>, abre
/// su grilla con el origen de cada celda y el usuario del llenado manual, y su historial con el disparador y el usuario
/// reales. Cada lectura queda en <c>lecturas_backoffice</c>; sin empresa, 400; quien no es backoffice FLIT (rol en una
/// compañía no FLIT, o el admin de la compañía), 403 sin datos. Postgres real; placas ficticias TST###.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DiagnosticosBackofficeTests
/// </code>
/// </example>
public sealed class DiagnosticosBackofficeTests
{
    private const string Lista = Program.Prefijo + "/backoffice/diagnosticos";

    private static string Grilla(Guid lote, Guid empresa) => $"{Lista}/{lote}/grilla?empresa={empresa}";

    private static string Historial(Guid lote, Guid empresa) => $"{Lista}/{lote}/historial?empresa={empresa}";

    // AC1 — ?empresa=A&con_errores=true devuelve solo el lote de A con celdas en ERROR (nada de B) y la lectura queda auditada.
    [Fact]
    public async Task Lista_lotes_de_la_empresa_pedida_con_errores()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var conError = await Tablero.SembrarAsync(empresas.A, ct, nombre: "Lote con error", vehiculos:
        [
            new("TST601", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2)]),
            new("TST602", [new(Fuente.Runt, EstadoCelda.Error, 3)]),
        ]);
        var sinError = await Tablero.SembrarAsync(empresas.A, ct, nombre: "Lote sin error", estado: EstadoDiagnostico.Completado,
            vehiculos: [new("TST603", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var deB = await Tablero.SembrarAsync(empresas.B, ct, nombre: "Lote de B", vehiculos: [new("TST604", [new(Fuente.Runt, EstadoCelda.Error, 1)])]);
        var flit = Guid.NewGuid();
        var token = TokenBackoffice(api, flit);

        var respuesta = await api.Cliente(token).GetAsync($"{Lista}?empresa={empresas.A.Id}&con_errores=true", ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        cuerpo.Should().NotContainAny([sinError.Id.ToString(), deB.Id.ToString(), empresas.B.Id.ToString()], "solo el lote de A con errores");
        var lista = JsonDocument.Parse(cuerpo).RootElement;
        lista.GetProperty("empresa").GetGuid().Should().Be(empresas.A.Id);
        lista.GetProperty("total").GetInt32().Should().Be(1);
        lista.GetProperty("pagina").GetInt32().Should().Be(1);
        var lote = lista.GetProperty("diagnosticos").EnumerateArray().Should().ContainSingle().Subject;
        lote.GetProperty("id").GetGuid().Should().Be(conError.Id);
        lote.GetProperty("nombre").GetString().Should().Be("Lote con error");
        lote.GetProperty("estado").GetString().Should().Be("EN_PROCESO");
        lote.GetProperty("vehiculos").GetInt32().Should().Be(2);
        lote.GetProperty("celdas_en_error").GetInt32().Should().Be(2);
        lote.TryGetProperty("creado_en", out _).Should().BeTrue();

        var todos = await Tablero.JsonAsync(await api.Cliente(token).GetAsync($"{Lista}?empresa={empresas.A.Id}", ct), ct);
        todos.GetProperty("diagnosticos").EnumerateArray().Select(d => d.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo([conError.Id, sinError.Id], "sin con_errores vienen todos los de A");
        var completados = await Tablero.JsonAsync(await api.Cliente(token).GetAsync($"{Lista}?empresa={empresas.A.Id}&estado=COMPLETADO", ct), ct);
        completados.GetProperty("diagnosticos").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).Should().Equal(sinError.Id);

        var auditoria = await Auditoria(api, flit, ct);
        auditoria.Should().NotBeEmpty();
        auditoria[0].EmpresaConsultada.Should().Be(empresas.A.Id);
        auditoria[0].UsuarioId.Should().Be(Tablero.Usuario(token));
        auditoria[0].Ruta.Should().Be($"{Lista}?empresa={empresas.A.Id}&con_errores=true");
    }

    // AC2 — la grilla del backoffice trae origen=MANUAL y el usuario de la celda llenada a mano; el historial, el disparador MANUAL real.
    [Fact]
    public async Task Grilla_incluye_origen_y_usuario_manual()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await Tablero.SembrarAsync(empresas.A, ct, estado: EstadoDiagnostico.Completado, vehiculos:
        [
            new("TST611", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2)]),
        ]);
        var manual = await Tablero.PayloadAsync(empresas.A, lote.Celda("TST611", Fuente.SimitVehiculo),
            """{"semaforo":"VERDE","cantidad_comparendos":0,"comparendos":[]}""", ct, manual: true);
        var usuarioBackoffice = manual.UsuarioManualId!.Value;
        await using (var db = empresas.A.Db())
        {
            db.Add(new HistorialEstadoDiagnostico
            {
                DiagnosticoId = lote.Id,
                EstadoAnterior = EstadoDiagnostico.Incompleto,
                EstadoNuevo = EstadoDiagnostico.Completado,
                Disparador = DisparadorEstado.Manual,
                UsuarioId = usuarioBackoffice,
                En = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        var token = api.Token(empresa: Guid.NewGuid(), permisos: [Permisos.BackofficeVer, Permisos.AuditoriaVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");
        var respuesta = await api.Cliente(token).GetAsync(Grilla(lote.Id, empresas.A.Id), ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        var grilla = JsonDocument.Parse(cuerpo).RootElement;
        grilla.GetProperty("id").GetGuid().Should().Be(lote.Id);
        grilla.GetProperty("empresa").GetGuid().Should().Be(empresas.A.Id);
        var celdas = Tablero.CeldasDe(grilla);
        var simit = celdas.Single(c => c.GetProperty("fuente").GetString() == "SIMIT_VEHICULO");
        simit.GetProperty("origen").GetString().Should().Be("MANUAL");
        simit.GetProperty("usuario_manual_id").GetGuid().Should().Be(usuarioBackoffice);
        simit.GetProperty("estado").GetString().Should().Be("COMPLETADO");
        var runt = celdas.Single(c => c.GetProperty("fuente").GetString() == "RUNT");
        runt.GetProperty("origen").GetString().Should().Be("API");
        runt.GetProperty("usuario_manual_id").ValueKind.Should().Be(JsonValueKind.Null);
        cuerpo.Should().NotContain("documento_propietario", "el backoffice tampoco recibe el payload en la grilla");

        var historial = await api.Cliente(token).GetAsync(Historial(lote.Id, empresas.A.Id), ct);
        var cuerpoHistorial = await historial.Content.ReadAsStringAsync(ct);
        historial.StatusCode.Should().Be(HttpStatusCode.OK, cuerpoHistorial);
        var ultima = JsonDocument.Parse(cuerpoHistorial).RootElement.GetProperty("transiciones").EnumerateArray().Last();
        ultima.GetProperty("disparador").GetString().Should().Be("MANUAL");
        ultima.GetProperty("usuario_id").GetGuid().Should().Be(usuarioBackoffice);

        var compania = await Tablero.JsonAsync(await empresas.A.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id), ct), ct);
        var clavesCompania = Tablero.CeldasDe(compania).SelectMany(c => c.EnumerateObject().Select(p => p.Name)).ToList();
        clavesCompania.Should().NotBeEmpty().And.NotContain(["origen", "usuario_manual_id"], "el contrato de la compañía no cambia (R-20)");

        (await api.Cliente(token).GetAsync(Grilla(lote.Id, empresas.B.Id), ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "el lote de A pedido como si fuera de B no existe en B");
        var sinAuditoria = api.Token(empresa: Guid.NewGuid(), permisos: [Permisos.BackofficeVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");
        (await api.Cliente(sinAuditoria).GetAsync(Historial(lote.Id, empresas.A.Id), ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "el historial real exige diagnostico.auditoria.ver");
    }

    // AC3 — sin ?empresa= la lista, la grilla y el historial responden 400 EMPRESA_REQUERIDA y nada se audita.
    [Fact]
    public async Task Sin_empresa_responde_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var flit = Guid.NewGuid();
        var token = api.Token(empresa: flit, permisos: [Permisos.BackofficeVer, Permisos.AuditoriaVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");
        var lote = Guid.NewGuid();

        foreach (var url in new[] { Lista, $"{Lista}?con_errores=true", $"{Lista}/{lote}/grilla", $"{Lista}/{lote}/historial" })
        {
            var respuesta = await api.Cliente(token).GetAsync(url, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, url);
            (await Tablero.JsonAsync(respuesta, ct)).GetProperty("code").GetString().Should().Be(EmpresaExplicita.CodigoRequerida, url);
        }

        (await Auditoria(api, flit, ct)).Should().BeEmpty("sin empresa no se lee ni se audita");
    }

    // AC4 — backoffice_diagnostico en una compañía no FLIT pide la grilla de otra compañía: 403 sin datos (el de FLIT sí la ve).
    [Fact]
    public async Task Rol_en_compania_no_FLIT_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await Tablero.SembrarAsync(empresas.A, ct, vehiculos: [new("TST621", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        (await api.Cliente(TokenBackoffice(api, Guid.NewGuid())).GetAsync(Grilla(lote.Id, empresas.A.Id), ct)).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: el backoffice FLIT sí ve la grilla");

        var noFlit = api.Token(empresa: empresas.B.Id, permisos: [Permisos.BackofficeVer, Permisos.AuditoriaVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "CLIENTE");
        foreach (var url in new[] { Grilla(lote.Id, empresas.A.Id), $"{Lista}?empresa={empresas.A.Id}", Historial(lote.Id, empresas.A.Id) })
        {
            var respuesta = await api.Cliente(noFlit).GetAsync(url, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
            (await respuesta.Content.ReadAsStringAsync(ct)).Should().NotContain(lote.Id.ToString(), "403 sin datos");
        }
    }

    // AC5 — el admin de la compañía A llama a las rutas de backoffice con ?empresa=A: 403.
    [Fact]
    public async Task Admin_de_la_compania_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await Tablero.SembrarAsync(empresas.A, ct, vehiculos: [new("TST631", [new(Fuente.Runt, EstadoCelda.Error, 1)])]);
        (await api.Cliente(TokenBackoffice(api, Guid.NewGuid())).GetAsync($"{Lista}?empresa={empresas.A.Id}", ct)).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: el backoffice FLIT sí lista los lotes de A");

        var admin = api.Token(empresa: empresas.A.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ConfiguracionEditar, Permisos.ConsultasReprocesar], roles: ["admin_diagnostico"]);
        foreach (var url in new[] { $"{Lista}?empresa={empresas.A.Id}", Grilla(lote.Id, empresas.A.Id), Historial(lote.Id, empresas.A.Id) })
        {
            var respuesta = await api.Cliente(admin).GetAsync(url, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
            (await respuesta.Content.ReadAsStringAsync(ct)).Should().NotContain(lote.Id.ToString(), "403 sin datos");
        }
    }

    // AC1 (auditoría) — la lista, la grilla y el historial dejan cada uno su fila con usuario, empresa consultada, ruta y fecha.
    [Fact]
    public async Task Cada_lectura_queda_auditada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await Tablero.SembrarAsync(empresas.A, ct, vehiculos: [new("TST641", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var flit = Guid.NewGuid();
        var token = api.Token(empresa: flit, permisos: [Permisos.BackofficeVer, Permisos.AuditoriaVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");
        var antes = DateTimeOffset.UtcNow.AddSeconds(-5);
        string[] urls = [$"{Lista}?empresa={empresas.A.Id}", Grilla(lote.Id, empresas.A.Id), Historial(lote.Id, empresas.A.Id)];

        foreach (var url in urls)
            (await api.Cliente(token).GetAsync(url, ct)).StatusCode.Should().Be(HttpStatusCode.OK, url);

        var auditoria = await Auditoria(api, flit, ct);
        auditoria.Select(l => l.Ruta).Should().Equal(urls);
        auditoria.Should().OnlyContain(l => l.EmpresaConsultada == empresas.A.Id && l.UsuarioId == Tablero.Usuario(token) && l.TenantId == flit);
        auditoria.Should().OnlyContain(l => l.Fecha >= antes && l.Fecha <= DateTimeOffset.UtcNow.AddSeconds(5));
    }

    private static string TokenBackoffice(ApiDePrueba api, Guid flit) =>
        api.Token(empresa: flit, permisos: [Permisos.BackofficeVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");

    private static async Task<List<LecturaBackoffice>> Auditoria(ApiDePrueba api, Guid flit, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, flit);
        return await db.Set<LecturaBackoffice>().AsNoTracking().OrderBy(l => l.Fecha).ThenBy(l => l.Id).ToListAsync(ct);
    }
}
