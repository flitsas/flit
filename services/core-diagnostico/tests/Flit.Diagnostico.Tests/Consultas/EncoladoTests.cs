using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13727 (F03-H2), AC1, AC2 y AC5 — el encolador real (<see cref="EncoladorConsultas"/>) en la confirmación del lote
/// (<c>POST /diagnosticos/{id}/vehiculos</c>, 202): crea una celda PENDIENTE por vehículo y fuente del alcance (las demás
/// fuentes no), pasa el lote de CREADO a EN_PROCESO con historial AUTO, sube la versión del tablero, todo en la misma
/// transacción que los vehículos; un segundo disparo no crea ni reabre celdas. Postgres real; placas ficticias (TST###).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EncoladoTests
/// </code>
/// </example>
public sealed class EncoladoTests
{
    // AC1 (F03-C1) — 3 vehículos con alcance RUNT: 3 celdas RUNT PENDIENTE (SIMIT habilitado pero no elegido no nace), 202,
    // lote EN_PROCESO con la transición AUTO en el historial y la versión del tablero copiada en las celdas.
    [Fact]
    public async Task Confirmar_lote_crea_celdas_solo_del_alcance_y_responde_202()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await Motor.CabeceraAsync(api, empresa, ct, ["RUNT"], [Fuente.Runt, Fuente.SimitVehiculo]);

        var respuesta = await Lotes.ConfirmarAsync(api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id)), id, Motor.Placas(3), ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, texto);
        JsonDocument.Parse(texto).RootElement.GetProperty("confirmados").GetInt32().Should().Be(3);
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, id, ct);
        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        celdas.Should().HaveCount(3).And.OnlyContain(c =>
            c.Fuente == Fuente.Runt && c.Estado == EstadoCelda.Pendiente && c.LeaseHasta == null && c.TenantId == empresa.Id && c.IntentosAuto == 0);
        celdas.Select(c => c.VehiculoId).Should().BeEquivalentTo(vehiculos.Select(v => v.Id));
        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, id, ct);
        cabecera.Estado.Should().Be(EstadoDiagnostico.EnProceso);
        cabecera.VersionTablero.Should().Be(1);
        celdas.Should().OnlyContain(c => c.VersionTablero == cabecera.VersionTablero);
        await using var db = empresa.Db();
        var historial = await db.Set<HistorialEstadoDiagnostico>().AsNoTracking().Where(h => h.DiagnosticoId == id && h.EstadoNuevo == EstadoDiagnostico.EnProceso).ToListAsync(ct);
        historial.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            EstadoAnterior = (EstadoDiagnostico?)EstadoDiagnostico.Creado,
            Disparador = DisparadorEstado.Auto,
            UsuarioId = (Guid?)null,
            TenantId = empresa.Id,
        });
    }

    // Orquestación por vehículo: la celda RUNT queda disponible ya; las demás del alcance nacen diferidas (las libera F03-H4).
    [Fact]
    public async Task Celdas_que_no_son_RUNT_nacen_diferidas_hasta_que_RUNT_termine()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;

        var id = await Motor.LoteConfirmadoAsync(api, empresa, Motor.Placas(2), ct, ["RUNT", "SIMIT_VEHICULO"], [Fuente.Runt, Fuente.SimitVehiculo, Fuente.Avaluo]);

        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        celdas.Should().HaveCount(4, "2 vehículos × (RUNT + SIMIT_VEHICULO); AVALUO está habilitado pero no en el alcance");
        celdas.Should().OnlyContain(c => c.Estado == EstadoCelda.Pendiente);
        celdas.Where(c => c.Fuente == Fuente.Runt).Should().HaveCount(2).And.OnlyContain(c => c.DisponibleDesde <= DateTimeOffset.UtcNow.AddSeconds(5));
        celdas.Where(c => c.Fuente == Fuente.SimitVehiculo).Should().HaveCount(2).And.OnlyContain(c => c.DisponibleDesde > DateTimeOffset.UtcNow.AddYears(100));
    }

    // AC2 (F03-C1) — si insertar las celdas falla (trigger de prueba en la base), no queda vehículo, celda ni transición.
    [Fact]
    public async Task Falla_al_crear_celdas_revierte_vehiculos_y_cola()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await Motor.CabeceraAsync(api, empresa, ct);
        await Motor.EjecutarAsync(api, """
            CREATE FUNCTION diagnostico.prueba_falla_celdas() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'falla de prueba al insertar celdas'; END $$;
            CREATE TRIGGER tg_prueba_falla_celdas BEFORE INSERT ON diagnostico.consultas
            FOR EACH ROW EXECUTE FUNCTION diagnostico.prueba_falla_celdas();
            """, ct);

        var respuesta = await Lotes.ConfirmarAsync(api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id)), id, Motor.Placas(3), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(0);
        (await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.consultas", ct)).Should().Be(0);
        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, id, ct);
        cabecera.Estado.Should().Be(EstadoDiagnostico.Creado);
        cabecera.VersionTablero.Should().Be(0);
        cabecera.EstimadoConsultas.Should().BeNull();
        (await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.historial_estado_diagnostico WHERE estado_nuevo = 'EN_PROCESO'", ct)).Should().Be(0);
    }

    // AC5 (F03-C3) — con una celda COMPLETADO y otra NA, un segundo disparo del mismo lote no crea ni reabre celdas, no
    // repite la transición y el procesador solo llama a la fuente por la celda que seguía PENDIENTE.
    [Fact]
    public async Task Segundo_encolado_no_reabre_celdas_COMPLETADO_ni_NA()
    {
        var ct = TestContext.Current.CancellationToken;
        var ejecutor = new EjecutorDePrueba();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => ejecutor.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await Motor.LoteConfirmadoAsync(api, empresa, Motor.Placas(3), ct);
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, id, ct);
        var celdas = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).ToDictionary(c => c.VehiculoId);
        var completada = celdas[vehiculos[0].Id].Id;
        var na = celdas[vehiculos[1].Id].Id;
        var pendiente = celdas[vehiculos[2].Id].Id;
        await Motor.EjecutarAsync(api, $"""
            UPDATE diagnostico.consultas SET estado = 'COMPLETADO', origen = 'API', payload = '{"{}"}', dato_obtenido_en = now() WHERE id = '{completada}';
            UPDATE diagnostico.consultas SET estado = 'NA', motivo_na = 'SIN_DOCUMENTO_LOCATARIO' WHERE id = '{na}';
            """, ct);
        var versionAntes = (await Cabeceras.LeerAsync(api, empresa.Id, id, ct)).VersionTablero;

        await using (var db = empresa.Db())
        {
            // Como la confirmación: la transacción va dentro de la estrategia de reintento de Npgsql.
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaccion = await db.Database.BeginTransactionAsync(ct);
                await new EncoladorConsultas(db, TimeProvider.System).EncolarAsync(
                    id, [.. vehiculos.Select(v => new VehiculoAEncolar(v.Id, v.IdentificadorNorm, v.TipoIdentificador))], ct);
                await transaccion.CommitAsync(ct);
            });
        }

        var despues = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        despues.Select(c => (c.Id, c.Estado)).Should().BeEquivalentTo(
            [(completada, EstadoCelda.Completado), (na, EstadoCelda.Na), (pendiente, EstadoCelda.Pendiente)]);
        (await Cabeceras.LeerAsync(api, empresa.Id, id, ct)).VersionTablero.Should().Be(versionAntes, "no se creó ninguna celda");
        (await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.historial_estado_diagnostico WHERE estado_nuevo = 'EN_PROCESO'", ct)).Should().Be(1);

        (await Motor.Procesador(api).ProcesarLoteAsync(ct)).Should().Be(1);
        ejecutor.Llamadas.Keys.Should().Equal(pendiente);
        (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single(c => c.Id == completada).Estado.Should().Be(EstadoCelda.Completado);
    }
}
