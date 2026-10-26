using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13727 (F03-H2), AC1, AC2 y AC5 — el encolador real (<see cref="EncoladorConsultas"/>) en la confirmación del lote
/// (<c>POST /diagnosticos/{id}/vehiculos</c>, 202): crea una celda PENDIENTE por vehículo y fuente del alcance (las demás
/// fuentes no), pasa el lote de CREADO a EN_PROCESO con historial AUTO, sube la versión del tablero, todo en la misma
/// transacción que los vehículos; un segundo disparo no crea ni reabre celdas. Desde ADR-0075 (HU #13727) también deja en la
/// outbox, en esa transacción, un trabajo <c>consultas.diagnostico[-express].runt</c> por cada celda RUNT nueva (las diferidas
/// no se publican). Postgres real; placas ficticias (TST###).
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

        // Como la confirmación: la transacción va dentro de la estrategia de reintento de Npgsql.
        await EncolarEnScopeAsync(api, empresa, id, vehiculos, revertir: false, ct);

        var despues = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        despues.Select(c => (c.Id, c.Estado)).Should().BeEquivalentTo(
            [(completada, EstadoCelda.Completado), (na, EstadoCelda.Na), (pendiente, EstadoCelda.Pendiente)]);
        (await Cabeceras.LeerAsync(api, empresa.Id, id, ct)).VersionTablero.Should().Be(versionAntes, "no se creó ninguna celda");
        (await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.historial_estado_diagnostico WHERE estado_nuevo = 'EN_PROCESO'", ct)).Should().Be(1);

        (await Motor.Procesador(api).ProcesarLoteAsync(ct)).Should().Be(1);
        ejecutor.Llamadas.Keys.Should().Equal(pendiente);
        (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single(c => c.Id == completada).Estado.Should().Be(EstadoCelda.Completado);
    }

    // ADR-0075 (HU #13727) — 7 vehículos: 7 trabajos RUNT en la outbox, cada uno con el trabajo_id de su celda, y ninguno de
    // las celdas diferidas (SIMIT_VEHICULO espera a la RUNT).
    [Fact]
    public async Task Confirmar_lote_publica_un_trabajo_RUNT_por_vehiculo_y_ninguna_diferida()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;

        var id = await Motor.LoteConfirmadoAsync(api, empresa, Motor.Placas(7), ct, ["RUNT", "SIMIT_VEHICULO"], [Fuente.Runt, Fuente.SimitVehiculo]);

        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        var vehiculos = (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).ToDictionary(v => v.Id);
        var trabajos = await OutboxDePrueba.TrabajosAsync(api, ct);
        trabajos.Should().HaveCount(7);
        trabajos.Should().OnlyContain(t => t.RoutingKey == "consultas.diagnostico.runt" && t.Sobre.Type == t.RoutingKey
            && t.Sobre.Producer == "diagnostico" && t.Sobre.TenantId == empresa.Id && t.Sobre.Version == 1);
        celdas.Where(c => c.Fuente == Fuente.SimitVehiculo).Should().HaveCount(7).And.OnlyContain(c => c.TrabajoId == null, "las diferidas no se publican");
        var runt = celdas.Where(c => c.Fuente == Fuente.Runt).ToDictionary(c => c.Id);
        foreach (var trabajo in trabajos)
        {
            var celda = runt[trabajo.ConsultaId];
            trabajo.TrabajoId.Should().Be(celda.TrabajoId!.Value);
            trabajo.Data.GetProperty("fuente").GetString().Should().Be(FuentesBus.Runt);
            trabajo.Data.GetProperty("rpc").GetString().Should().Be(RpcConsultas.PorPlaca);
            trabajo.Data.GetProperty("solicitud").GetProperty("placa").GetString().Should().Be(vehiculos[celda.VehiculoId].IdentificadorNorm);
        }

        trabajos.Select(t => t.ConsultaId).Should().BeEquivalentTo(runt.Keys);
    }

    // ADR-0075 (brecha 3) — el Express publica en la cola aparte: la clave lleva -express.
    [Fact]
    public async Task Express_publica_el_trabajo_RUNT_con_la_clave_express()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        await Configuracion.ConfiguracionOperativaTests.SembrarAsync(empresa, new ConfiguracionEmpresa(), ct);
        var cuerpo = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["identificador"] = "TST101",
            ["modo"] = "SIMPLE",
            ["tipo_tramite_codigo"] = null,
            ["alcance"] = new Dictionary<string, string[]> { ["por_vehiculo"] = ["RUNT"], ["por_propietario"] = [], ["por_locatario"] = [] },
            ["tipo_documento_locatario"] = null,
            ["documento_locatario"] = null,
        });

        var respuesta = await api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id))
            .PostAsync(Program.Prefijo + "/express", Configuracion.ConfiguracionOperativaTests.Cuerpo(cuerpo), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync(ct));
        var trabajo = (await OutboxDePrueba.TrabajosAsync(api, ct)).Should().ContainSingle().Subject;
        trabajo.RoutingKey.Should().Be("consultas.diagnostico-express.runt");
        trabajo.Data.GetProperty("solicitud").GetProperty("placa").GetString().Should().Be("TST101");
    }

    // ADR-0075 — un segundo disparo del mismo lote no crea celdas y por eso no publica de nuevo ni cambia el trabajo vigente.
    [Fact]
    public async Task Reencolar_el_mismo_lote_no_duplica_trabajos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await Motor.LoteConfirmadoAsync(api, empresa, Motor.Placas(3), ct);
        var antes = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).ToDictionary(c => c.Id, c => c.TrabajoId);
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, id, ct);

        await EncolarEnScopeAsync(api, empresa, id, vehiculos, revertir: false, ct);

        (await OutboxDePrueba.TrabajosAsync(api, ct)).Should().HaveCount(3);
        (await Motor.CeldasAsync(api, empresa.Id, id, ct)).ToDictionary(c => c.Id, c => c.TrabajoId).Should().Equal(antes);
    }

    // ADR-0075 — el trabajo va en la transacción de la celda: si se revierte, no queda celda ni mensaje.
    [Fact]
    public async Task Un_rollback_del_encolado_no_deja_trabajos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await Motor.CabeceraAsync(api, empresa, ct);
        var vehiculos = Motor.Placas(2).Select(f => (string)f["identificador"]!).Select((placa, i) => new VehiculoDiagnostico
        {
            DiagnosticoId = id,
            IdentificadorOriginal = placa,
            IdentificadorNorm = placa,
            TipoIdentificador = TipoIdentificador.PlacaCarro,
            Placa = placa,
            Orden = i + 1,
        }).ToList();

        var dentro = await EncolarEnScopeAsync(api, empresa, id, vehiculos, revertir: true, ct);

        dentro.Should().Be(2, "dentro de la transacción la outbox tenía los dos trabajos");
        (await OutboxDePrueba.TrabajosAsync(api, ct)).Should().BeEmpty();
        (await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.consultas", ct)).Should().Be(0);
    }

    /// <summary>
    /// Encola en un scope con la empresa fijada (como la confirmación: transacción dentro de la estrategia de Npgsql). Los
    /// vehículos sin guardar se guardan antes; con <paramref name="revertir"/> la transacción se revierte. Devuelve las filas de
    /// la outbox que se veían dentro de la transacción.
    /// </summary>
    private static async Task<int> EncolarEnScopeAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, Guid id, IReadOnlyList<VehiculoDiagnostico> vehiculos, bool revertir, CancellationToken ct)
    {
        await using var scope = OutboxDePrueba.Scope(api, empresa.Id);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var encolador = scope.ServiceProvider.GetRequiredService<IEncoladorConsultas>();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaccion = await db.Database.BeginTransactionAsync(ct);
            var nuevos = vehiculos.Where(v => !db.Set<VehiculoDiagnostico>().AsNoTracking().Any(g => g.Id == v.Id)).ToList();
            if (nuevos.Count > 0)
            {
                db.AddRange(nuevos);
                await db.SaveChangesAsync(ct);
            }

            await encolador.EncolarAsync(id, [.. vehiculos.Select(v => new VehiculoAEncolar(v.Id, v.IdentificadorNorm, v.TipoIdentificador))], ct);
            var dentro = await db.Set<OutboxMessage>().CountAsync(m => m.Exchange == "flit.consultas", ct);
            if (revertir)
                await transaccion.RollbackAsync(ct);
            else
                await transaccion.CommitAsync(ct);
            return dentro;
        });
    }
}
