using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #13798 (F06-H1), AC6 (F06-C5, F06-C12, decisión Q5 = a) — <c>GET /diagnosticos/{id}/historial</c> para la compañía: una
/// transición con disparador <c>MANUAL</c> (llenado del backoffice) se presenta como <c>AUTO</c> y sin usuario; las demás
/// (cierre del panel, reproceso) conservan su disparador y su usuario. El backoffice ve el real en F06-H2.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Tablero.HistorialLoteTests
/// </code>
/// </example>
public sealed class HistorialLoteTests
{
    // AC6 — el admin de la compañía consulta el historial de un lote con una transición MANUAL: aparece AUTO y sin usuario.
    [Fact]
    public async Task Manual_se_presenta_como_auto_sin_usuario_para_la_compania()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var admin = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ConfiguracionEditar], roles: ["admin_diagnostico"]);
        var lote = await Tablero.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Completado, vehiculos: [new("TST481", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var usuarioPanel = Guid.NewGuid();
        var usuarioReproceso = Guid.NewGuid();
        var usuarioBackoffice = Guid.NewGuid();
        var inicio = DateTimeOffset.UtcNow.AddHours(-1);
        await using (var db = empresa.Db())
        {
            db.AddRange(
                Transicion(lote.Id, null, EstadoDiagnostico.Creado, DisparadorEstado.Auto, null, inicio),
                Transicion(lote.Id, EstadoDiagnostico.Creado, EstadoDiagnostico.EnProceso, DisparadorEstado.Auto, null, inicio.AddMinutes(1)),
                Transicion(lote.Id, EstadoDiagnostico.EnProceso, EstadoDiagnostico.Incompleto, DisparadorEstado.CierrePanel, usuarioPanel, inicio.AddMinutes(2)),
                Transicion(lote.Id, EstadoDiagnostico.Incompleto, EstadoDiagnostico.EnProceso, DisparadorEstado.Reproceso, usuarioReproceso, inicio.AddMinutes(3)),
                Transicion(lote.Id, EstadoDiagnostico.EnProceso, EstadoDiagnostico.Completado, DisparadorEstado.Manual, usuarioBackoffice, inicio.AddMinutes(4)));
            await db.SaveChangesAsync(ct);
        }

        var respuesta = await api.Cliente(admin).GetAsync(Tablero.RutaHistorial(lote.Id), ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        cuerpo.Should().NotContainAny(["MANUAL", usuarioBackoffice.ToString()], "la compañía no ve el llenado manual (Q5)");
        var transiciones = JsonDocument.Parse(cuerpo).RootElement.GetProperty("transiciones").EnumerateArray().ToList();
        transiciones.Select(t => t.GetProperty("disparador").GetString()).Should().Equal(["AUTO", "AUTO", "CIERRE_PANEL", "REPROCESO", "AUTO"]);
        transiciones[^1].GetProperty("usuario_id").ValueKind.Should().Be(JsonValueKind.Null, "el MANUAL va sin usuario");
        transiciones[^1].GetProperty("estado_nuevo").GetString().Should().Be("COMPLETADO", "la transición se conserva, solo cambia cómo se presenta");
        transiciones[2].GetProperty("usuario_id").GetGuid().Should().Be(usuarioPanel, "el cierre del panel conserva su usuario");
        transiciones[3].GetProperty("usuario_id").GetGuid().Should().Be(usuarioReproceso, "el reproceso conserva su usuario");
    }

    private static HistorialEstadoDiagnostico Transicion(
        Guid lote, EstadoDiagnostico? anterior, EstadoDiagnostico nuevo, DisparadorEstado disparador, Guid? usuario, DateTimeOffset en) => new()
        {
            DiagnosticoId = lote,
            EstadoAnterior = anterior,
            EstadoNuevo = nuevo,
            Disparador = disparador,
            UsuarioId = usuario,
            En = en,
        };
}
