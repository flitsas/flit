using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Campo <c>saneamiento</c> de cada fila de <c>GET /diagnosticos/{id}/grilla</c> (plan §6.1, S12; HU #14576 AC35, parte del lote, y
/// AC30): estado del saneamiento no anulado, elegibilidad y motivo; el sondeo incremental recoge la fila tras marcar y desmarcar.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~GrillaSaneamientoTests
/// </code>
/// </example>
public sealed class GrillaSaneamientoTests
{
    // AC35 — sin saneamiento, no terminado, SOLICITADO, SANEADO y uno solo ANULADO.
    [Fact]
    public async Task HU14576_AC35_cada_fila_trae_estado_elegibilidad_y_motivo()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN301", "SAN302", "SAN303", "SAN304", "SAN305"], ct, abiertas: ["SAN302"]);
        await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[2].Id, EstadoSaneamiento.Solicitado, ct);
        await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[3].Id, EstadoSaneamiento.Saneado, ct);
        await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[4].Id, EstadoSaneamiento.Anulado, ct);

        var respuesta = await empresa.Cliente().GetAsync(Tablero.Tablero.RutaGrilla(lote.Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var filas = Filas(await SaneamientoDePrueba.JsonAsync(respuesta, ct));
        filas["SAN301"].Should().Be((null, true, null));
        filas["SAN302"].Should().Be((null, false, "NO_TERMINADO"));
        filas["SAN303"].Should().Be(("SOLICITADO", false, "YA_SOLICITADO"));
        filas["SAN304"].Should().Be(("SANEADO", false, "YA_SOLICITADO"));
        filas["SAN305"].Should().Be((null, true, null), "tras ANULADO se puede volver a marcar");
    }

    // AC30 — el sondeo desde la versión leída devuelve la fila con el estado nuevo, al marcar y al desmarcar.
    [Fact]
    public async Task HU14576_AC30_el_sondeo_incremental_recoge_la_fila_al_marcar_y_desmarcar()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN306", "SAN307"], ct);
        var vehiculo = lote.Vehiculos[0].Id;
        using var cliente = api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id));
        var v0 = (await SaneamientoDePrueba.JsonAsync(await cliente.GetAsync(Tablero.Tablero.RutaGrilla(lote.Id), ct), ct)).GetProperty("version").GetInt64();

        (await cliente.PutAsync(SaneamientoDePrueba.Ruta(lote.Id, vehiculo), null, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        var trasMarcar = await SaneamientoDePrueba.JsonAsync(await cliente.GetAsync(Tablero.Tablero.RutaGrilla(lote.Id, desde: v0), ct), ct);
        (await cliente.DeleteAsync(SaneamientoDePrueba.Ruta(lote.Id, vehiculo), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var v1 = trasMarcar.GetProperty("version").GetInt64();
        var trasDesmarcar = await SaneamientoDePrueba.JsonAsync(await cliente.GetAsync(Tablero.Tablero.RutaGrilla(lote.Id, desde: v1), ct), ct);

        v1.Should().BeGreaterThan(v0);
        Filas(trasMarcar).Should().ContainSingle().Which.Should().BeEquivalentTo(
            new KeyValuePair<string, (string?, bool, string?)>("SAN306", ("SOLICITADO", false, "YA_SOLICITADO")));
        Filas(trasDesmarcar).Should().ContainSingle().Which.Should().BeEquivalentTo(
            new KeyValuePair<string, (string?, bool, string?)>("SAN306", (null, true, null)));
    }

    private static Dictionary<string, (string? Estado, bool Elegible, string? Motivo)> Filas(JsonElement grilla) =>
        grilla.GetProperty("vehiculos").EnumerateArray().ToDictionary(
            v => v.GetProperty("identificador").GetString()!,
            v =>
            {
                var s = v.GetProperty("saneamiento");
                return (s.GetProperty("estado").GetString(), s.GetProperty("elegible").GetBoolean(), s.GetProperty("motivo_no_elegible").GetString());
            });
}
