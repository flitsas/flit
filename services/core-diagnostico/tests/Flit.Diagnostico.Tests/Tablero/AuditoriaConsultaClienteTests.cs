using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #14563 B5 (AC16, AC17) — <c>DetalleCelda.auditoria</c>: inicio, fin, duración e intentos de la consulta para la compañía,
/// saneados. Los intentos MANUAL (llenado de soporte) no aparecen ni cuentan (R-20, D6), y ningún intento trae servicio,
/// transacción, mensaje, usuario, identificador ni persona.
/// </summary>
/// <example>
/// <code>
/// var auditoria = AuditoriaConsultaCliente.Desde(intentos); // null si no hay intentos no manuales
/// </code>
/// </example>
public sealed class AuditoriaConsultaClienteTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    private static readonly string[] Prohibidas =
        ["servicio", "transaccion", "mensaje", "usuario", "identificador", "persona", "origen", "manual"];

    private static IntentoConsulta Intento(int desdeSeg, int duracionSeg, DisparadorIntento disparador, ResultadoIntento estado, bool cache = false) => new()
    {
        ConsultaId = Guid.Empty,
        Fuente = Fuente.SimitVehiculo,
        Servicio = ServicioConsulta.Intempo,
        IdentificadorConsultado = "TST900",
        Disparador = disparador,
        Inicio = T0.AddSeconds(desdeSeg),
        Fin = T0.AddSeconds(desdeSeg + duracionSeg),
        Estado = estado,
        TipoError = estado == ResultadoIntento.Error ? "TIMEOUT" : null,
        MensajeError = estado == ResultadoIntento.Error ? "mensaje tecnico interno" : null,
        TransaccionProveedorId = "TX-INTERNA-900",
        DesdeCache = cache,
        UsuarioId = disparador == DisparadorIntento.Auto ? null : Guid.NewGuid(),
        PersonaId = Guid.NewGuid(),
    };

    // AC16 — intentos automáticos y de reproceso: agregados y lista con n, inicio, fin, duración, resultado, disparador y caché.
    [Fact]
    public void Auditoria_resume_intentos_automaticos_y_de_reproceso()
    {
        var intentos = new[]
        {
            Intento(60, 4, DisparadorIntento.Reproceso, ResultadoIntento.Success, cache: true),
            Intento(0, 2, DisparadorIntento.Auto, ResultadoIntento.Error),
        };

        var auditoria = AuditoriaConsultaCliente.Desde(intentos);

        auditoria.Should().NotBeNull();
        auditoria!.Inicio.Should().Be(T0);
        auditoria.Fin.Should().Be(T0.AddSeconds(64));
        auditoria.DuracionMs.Should().Be(64_000);
        auditoria.Intentos.Should().Be(2);
        auditoria.IntentosDetalle.Should().HaveCount(2);
        auditoria.IntentosDetalle[0].Should().BeEquivalentTo(new IntentoConsultaCliente(1, T0, T0.AddSeconds(2), 2_000, "ERROR", "AUTOMATICO", false, "TIMEOUT"));
        auditoria.IntentosDetalle[1].Should().BeEquivalentTo(new IntentoConsultaCliente(2, T0.AddSeconds(60), T0.AddSeconds(64), 4_000, "EXITOSO", "REPROCESO", true));
    }

    // D15 — el intento en ERROR trae tipo_error: el mismo código de negocio de la grilla (p. ej. TIMEOUT), nunca el mensaje
    // técnico; el exitoso lo trae en null.
    [Fact]
    public void Intento_en_error_trae_tipo_error_como_etiqueta_de_negocio_sin_el_mensaje()
    {
        var intentos = new[]
        {
            Intento(0, 2, DisparadorIntento.Auto, ResultadoIntento.Error),
            Intento(60, 4, DisparadorIntento.Reproceso, ResultadoIntento.Success),
        };

        var json = JsonSerializer.Serialize(AuditoriaConsultaCliente.Desde(intentos));

        using var doc = JsonDocument.Parse(json);
        var detalle = doc.RootElement.GetProperty("intentos_detalle");
        detalle[0].TryGetProperty("tipo_error", out var error).Should().BeTrue();
        error.GetString().Should().Be("TIMEOUT");
        detalle[1].TryGetProperty("tipo_error", out var exito).Should().BeTrue();
        exito.ValueKind.Should().Be(JsonValueKind.Null);
        json.Should().NotContain("mensaje tecnico");
    }

    // AC17 — el último intento fue MANUAL: no aparece ni cuenta, y fin es el del último no manual. Solo manual: sin auditoría.
    [Fact]
    public void Auditoria_excluye_el_intento_manual_y_toma_fin_del_ultimo_no_manual()
    {
        var intentos = new[]
        {
            Intento(0, 3, DisparadorIntento.Auto, ResultadoIntento.Error),
            Intento(120, 1, DisparadorIntento.Manual, ResultadoIntento.Success),
        };

        var auditoria = AuditoriaConsultaCliente.Desde(intentos);

        auditoria!.Intentos.Should().Be(1);
        auditoria.IntentosDetalle.Should().ContainSingle().Which.Disparador.Should().Be("AUTOMATICO");
        auditoria.Fin.Should().Be(T0.AddSeconds(3));
        var json = JsonSerializer.Serialize(auditoria);
        json.Should().NotContain("MANUAL").And.NotContain("TX-INTERNA-900").And.NotContain("TST900").And.NotContain("mensaje tecnico");

        AuditoriaConsultaCliente.Desde([Intento(0, 1, DisparadorIntento.Manual, ResultadoIntento.Success)]).Should().BeNull();
        AuditoriaConsultaCliente.Desde([]).Should().BeNull();
    }

    // AC17 — reflexión R-20 sobre AuditoriaConsulta e IntentoConsultaCliente, y DetalleCeldaDto los expone en "auditoria".
    [Fact]
    public void Reflexion_R20_AuditoriaConsulta_e_IntentoConsultaCliente_sin_datos_internos()
    {
        var nombres = new[] { typeof(AuditoriaConsulta), typeof(IntentoConsultaCliente) }
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name)
            .ToList();

        nombres.Should().BeEquivalentTo(
            ["inicio", "fin", "duracion_ms", "intentos", "intentos_detalle", "n", "inicio", "fin", "duracion_ms", "resultado", "disparador", "desde_cache", "tipo_error"]);
        nombres.Should().NotContain(n => Prohibidas.Any(x => n.Contains(x, StringComparison.OrdinalIgnoreCase)));
        typeof(DetalleCeldaDto).GetProperty("Auditoria")!.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name.Should().Be("auditoria");
    }

    // AC16 (extremo a extremo, PostgreSQL real) — el detalle de la compañía trae la auditoría de la celda; el manual no.
    [Fact]
    public async Task Detalle_de_la_compania_trae_la_auditoria_sin_el_intento_manual()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, vehiculos: [new("TST931", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var celda = lote.Celda("TST931", Fuente.Runt);
        await Tablero.IntentoAsync(empresa, celda, desdeCache: false, ct);
        await Tablero.IntentoAsync(empresa, celda, desdeCache: false, ct, DisparadorIntento.Reproceso);
        await Tablero.IntentoAsync(empresa, celda, desdeCache: false, ct, DisparadorIntento.Manual);

        var detalle = await Tablero.JsonAsync(await empresa.Cliente().GetAsync(Tablero.RutaDetalle(lote.Id, celda.VehiculoId, "RUNT"), ct), ct);

        var auditoria = detalle.GetProperty("auditoria");
        auditoria.GetProperty("intentos").GetInt32().Should().Be(2);
        auditoria.GetProperty("intentos_detalle").EnumerateArray().Select(i => i.GetProperty("disparador").GetString())
            .Should().Equal(["AUTOMATICO", "REPROCESO"]);
        auditoria.GetRawText().Should().NotContain("MANUAL").And.NotContain("INTEMPO");
    }
}
