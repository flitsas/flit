using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Derivacion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;
using TableroPrueba = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// HU #14576 AC25, escenario 1 (S16; code review CR-1): crear el trámite de un vehículo VIABLE con un saneamiento SOLICITADO o
/// EN_GESTION es igual que sin saneamiento. La fila de la grilla trae la misma elegibilidad, la derivación individual responde lo
/// mismo y ni el saneamiento (estado, versión, historial) ni <c>vehiculos.saneamiento_estado</c> cambian. PostgreSQL real y el doble
/// del rpc de Trámites (<see cref="DerivacionTramitesFalso"/>). Placas ficticias TST45#.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DerivacionConSaneamientoTests
/// </code>
/// </example>
public sealed class DerivacionConSaneamientoTests
{
    [Fact]
    public async Task HU14576_AC25_crear_tramite_con_saneamiento_SOLICITADO_y_EN_GESTION()
    {
        var falso = new DerivacionTramitesFalso { Responder = s => DerivacionTramitesFalso.Creado(Guid.NewGuid(), "TR-" + s.Placa) };
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST451", true, "VIABLE"), ("TST452", true, "VIABLE"), ("TST453", true, "VIABLE")]);
        var (sinSaneamiento, solicitado, enGestion) = (lote.Vehiculos[0].Id, lote.Vehiculos[1].Id, lote.Vehiculos[2].Id);
        var sSolicitado = await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, solicitado, EstadoSaneamiento.Solicitado, ct);
        var sEnGestion = await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, enGestion, EstadoSaneamiento.EnGestion, ct);
        var antes = new[] { await FotoAsync(empresa, sSolicitado.Id, ct), await FotoAsync(empresa, sEnGestion.Id, ct) };
        using var compania = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));

        var grilla = await compania.GetAsync(TableroPrueba.RutaGrilla(lote.Id), ct);
        var respuestas = new Dictionary<Guid, HttpResponseMessage>();
        foreach (var vehiculo in new[] { sinSaneamiento, solicitado, enGestion })
            respuestas[vehiculo] = await compania.PostAsync(DerivacionDePrueba.Ruta(lote.Id, vehiculo), null, ct);

        grilla.StatusCode.Should().Be(HttpStatusCode.OK, await grilla.Content.ReadAsStringAsync(ct));
        var derivacion = (await TableroPrueba.JsonAsync(grilla, ct)).GetProperty("vehiculos").EnumerateArray()
            .ToDictionary(v => v.GetProperty("identificador").GetString()!, v => v.GetProperty("derivacion").GetRawText());
        derivacion["TST451"].Should().Be("""{"elegible":true,"motivo_no_elegible":null,"estado":null,"tramite":null}""");
        derivacion["TST452"].Should().Be(derivacion["TST451"], "SOLICITADO no cambia la elegibilidad de derivación (S16)");
        derivacion["TST453"].Should().Be(derivacion["TST451"], "EN_GESTION no cambia la elegibilidad de derivación (S16)");

        var formas = new List<string>();
        foreach (var (vehiculo, respuesta) in respuestas)
        {
            respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync(ct));
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
            cuerpo.GetProperty("ya_existia").GetBoolean().Should().BeFalse();
            formas.Add(string.Join(",", cuerpo.EnumerateObject().Select(p => $"{p.Name}:{p.Value.ValueKind}")));
            var guardado = await DerivacionDePrueba.VehiculoAsync(empresa, vehiculo, ct);
            guardado.TramiteId.Should().Be(cuerpo.GetProperty("tramite_id").GetGuid());
            (await DerivacionDePrueba.ItemsAsync(empresa, vehiculo, ct)).Should().ContainSingle().Which.Estado.Should().Be(EstadoItemDerivacion.Creado);
        }

        formas.Distinct().Should().ContainSingle("la respuesta tiene la misma forma con y sin saneamiento");
        falso.Llamadas.Select(l => l.Solicitud.Placa).Should().BeEquivalentTo(["TST451", "TST452", "TST453"]);
        (await FotoAsync(empresa, sSolicitado.Id, ct)).Should().Be(antes[0], "crear el trámite no toca el saneamiento SOLICITADO ni la columna del vehículo");
        (await FotoAsync(empresa, sEnGestion.Id, ct)).Should().Be(antes[1], "crear el trámite no toca el saneamiento EN_GESTION ni la columna del vehículo");
        (await DerivacionDePrueba.VehiculoAsync(empresa, sinSaneamiento, ct)).SaneamientoEstado.Should().BeNull();
    }

    /// <summary>Estado, versión (xmin) y eventos del saneamiento, más la copia del estado en el vehículo.</summary>
    private static async Task<string> FotoAsync(EmpresaDePrueba empresa, Guid sid, CancellationToken ct)
    {
        await using var db = empresa.Db();
        var s = await db.Set<SaneamientoEntidad>().AsNoTracking().SingleAsync(x => x.Id == sid, ct);
        var eventos = await db.Set<Flit.Diagnostico.Api.Modulos.Saneamiento.EventoSaneamiento>().AsNoTracking().CountAsync(e => e.SaneamientoId == sid, ct);
        var renglones = await db.Set<Flit.Diagnostico.Api.Modulos.Saneamiento.RenglonSaneamiento>().AsNoTracking().CountAsync(r => r.SaneamientoId == sid, ct);
        var columna = await db.Set<Flit.Diagnostico.Api.Modulos.Vehiculos.VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.Id == s.VehiculoId).Select(v => v.SaneamientoEstado).SingleAsync(ct);
        return $"{s.Estado}|xmin={s.Xmin}|eventos={eventos}|renglones={renglones}|columna={columna}|tomado={s.TomadoEn:O}|cerrado={s.CerradoEn:O}";
    }
}
