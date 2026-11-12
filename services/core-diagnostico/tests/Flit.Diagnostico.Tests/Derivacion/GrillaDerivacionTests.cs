using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TableroPrueba = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 2 (diseño §5.1 última fila, §5.2) — cada fila de <c>GET /diagnosticos/{id}/grilla</c> gana
/// <c>derivacion: { elegible, motivo_no_elegible, estado, tramite: { id, numero, url } | null }</c> con la MISMA regla del
/// individual; con la derivación apagada (por defecto) es null. PostgreSQL real (<see cref="ApiDePrueba"/>).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~GrillaDerivacionTests
/// </code>
/// </example>
public sealed class GrillaDerivacionTests
{
    [Fact]
    public async Task Cada_fila_trae_su_elegibilidad_el_estado_del_último_ítem_y_el_trámite()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos:
        [
            ("TST431", true, "VIABLE"), ("TST432", true, "BLOQUEADO"), ("TST433", false, null), ("TST434", true, "VIABLE"),
            ("TST435", true, "VIABLE"), ("TST436", true, "VIABLE"),
        ]);
        var tramite = Guid.NewGuid();
        await using (var db = empresa.Db())
        {
            var conTramite = await db.Set<Flit.Diagnostico.Api.Modulos.Vehiculos.VehiculoDiagnostico>().SingleAsync(v => v.Id == lote.Vehiculos[3].Id, ct);
            conTramite.TramiteId = tramite;
            conTramite.TramiteNumero = "TR-000900";
            conTramite.TramiteCreadoEn = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        await DerivacionIndividualTests.SembrarItemVivoAsync(empresa, lote, lote.Vehiculos[4].Id, ct);
        falso.Responder = _ => DerivacionTramitesFalso.Rechazo("OT_NO_HABILITADO", "x");
        (await api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa)).PostAsync(DerivacionDePrueba.Ruta(lote.Id, lote.Vehiculos[5].Id), null, ct))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var respuesta = await empresa.Cliente().GetAsync(TableroPrueba.RutaGrilla(lote.Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var filas = (await TableroPrueba.JsonAsync(respuesta, ct)).GetProperty("vehiculos").EnumerateArray()
            .ToDictionary(v => v.GetProperty("identificador").GetString()!, v => v.GetProperty("derivacion"));
        Esperar(filas["TST431"], elegible: true, motivo: null, estado: null);
        Esperar(filas["TST432"], elegible: false, motivo: MotivoDerivacion.NoViable, estado: null);
        Esperar(filas["TST433"], elegible: false, motivo: MotivoDerivacion.NoTerminado, estado: null);
        Esperar(filas["TST434"], elegible: false, motivo: MotivoDerivacion.YaTieneTramite, estado: null);
        var deTramite = filas["TST434"].GetProperty("tramite");
        deTramite.GetProperty("id").GetGuid().Should().Be(tramite);
        deTramite.GetProperty("numero").GetString().Should().Be("TR-000900");
        deTramite.GetProperty("url").GetString().Should().Be($"{DerivacionDePrueba.UrlTramites}/tramites/{tramite}?t={empresa.Id}");
        Esperar(filas["TST435"], elegible: false, motivo: MotivoDerivacion.EnCurso, estado: "ENVIADO");
        Esperar(filas["TST436"], elegible: true, motivo: null, estado: "OMITIDO");
        filas.Where(f => f.Key != "TST434").Should().OnlyContain(f => f.Value.GetProperty("tramite").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Con_la_derivación_apagada_la_fila_trae_derivacion_null()
    {
        await using var api = await DerivacionDePrueba.ApiAsync(new DerivacionTramitesFalso(), habilitada: false);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST437", true, "VIABLE")]);

        var grilla = await TableroPrueba.JsonAsync(await empresa.Cliente().GetAsync(TableroPrueba.RutaGrilla(lote.Id), ct), ct);

        grilla.GetProperty("vehiculos")[0].GetProperty("derivacion").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void La_fila_declara_derivacion_en_snake_case()
    {
        var propiedad = typeof(FilaVehiculoDto).GetProperty(nameof(FilaVehiculoDto.Derivacion))!;
        propiedad.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), true)
            .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>().Single().Name.Should().Be("derivacion");
        var json = JsonSerializer.Serialize(new Flit.Diagnostico.Api.Modulos.Derivacion.DerivacionFilaDto(false, "YA_TIENE_TRAMITE", "CREADO",
            new Flit.Diagnostico.Api.Modulos.Derivacion.TramiteFilaDto(Guid.Empty, "TR-1", "/tramites/x")));
        json.Should().Contain("\"elegible\":false").And.Contain("\"motivo_no_elegible\":\"YA_TIENE_TRAMITE\"").And.Contain("\"estado\":\"CREADO\"")
            .And.Contain("\"tramite\":{\"id\"").And.Contain("\"numero\":\"TR-1\"").And.Contain("\"url\":\"/tramites/x\"");
    }

    private static void Esperar(JsonElement derivacion, bool elegible, string? motivo, string? estado)
    {
        derivacion.ValueKind.Should().Be(JsonValueKind.Object);
        derivacion.GetProperty("elegible").GetBoolean().Should().Be(elegible);
        (derivacion.GetProperty("motivo_no_elegible").ValueKind == JsonValueKind.Null ? null : derivacion.GetProperty("motivo_no_elegible").GetString())
            .Should().Be(motivo);
        (derivacion.GetProperty("estado").ValueKind == JsonValueKind.Null ? null : derivacion.GetProperty("estado").GetString()).Should().Be(estado);
    }
}
