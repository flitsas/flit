using System.Text.Json.Serialization;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Derivacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>Cuerpo de <c>POST /diagnosticos/{id}/tramites</c>: los vehículos elegidos (en orden) y el tipo, solo en lote Simple.</summary>
public sealed record CrearTramitesEntrada(
    [property: JsonPropertyName("vehiculo_ids")] IReadOnlyList<Guid>? VehiculoIds,
    [property: JsonPropertyName("tipo_tramite_codigo")] string? TipoTramiteCodigo);

/// <summary>Un vehículo de la selección que no se encoló y por qué.</summary>
public sealed record VehiculoNoElegibleDto(
    [property: JsonPropertyName("vehiculo_id")] Guid VehiculoId,
    [property: JsonPropertyName("motivo_codigo")] string MotivoCodigo);

/// <summary>202 del masivo: la operación, cuántos vehículos trae, cuántos se encolaron y los que quedaron OMITIDO de inmediato.</summary>
public sealed record OperacionEncoladaDto(
    [property: JsonPropertyName("operacion_id")] Guid OperacionId,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("encolados")] int Encolados,
    [property: JsonPropertyName("no_elegibles")] IReadOnlyList<VehiculoNoElegibleDto> NoElegibles);

/// <summary>
/// «Crear trámites» de una selección del lote, encolado (Punto 4, incremento 3, diseño §5.1 fila 2, §5.3 punto 1, ADR-0084):
/// <list type="number">
/// <item>Antes de la base: 400 <see cref="CodigoVehiculosRequeridos"/> con la lista vacía y 400 <see cref="CodigoDemasiadosVehiculos"/>
/// con más de <see cref="OpcionesDerivacion.MaxItemsPorOperacion"/> (sin repetidos).</item>
/// <item>Lote visible para el usuario (<see cref="AccesoLote"/>, 404) y tipo del lote o del cuerpo (Simple) validado contra el
/// catálogo (<see cref="ApoyoDerivacion.TipoAsync"/>).</item>
/// <item>En una transacción: revalida cada vehículo con la regla única (<see cref="LecturaElegibilidad"/>); uno que no es del lote →
/// 400 <see cref="CodigoVehiculoFueraDelLote"/>; crea la operación MASIVA con un ítem por vehículo en el orden de la selección
/// (PENDIENTE si es elegible; OMITIDO con su motivo si no) y libera el crédito de la compañía (<see cref="DespachadorCredito"/>).</item>
/// <item>Doble clic: si otro envío creó un ítem vivo del mismo vehículo entre la lectura y el INSERT, el índice
/// <c>ux_derivaciones_vehiculo_vivo</c> lo rechaza y se vuelve a leer: el vehículo queda EN_CURSO, nunca duplicado.</item>
/// </list>
/// No llama a Trámites. Logs: ids y conteos.
/// </summary>
internal sealed partial class DerivacionMasiva(
    DiagnosticoDb db,
    UsuarioActual usuario,
    ICatalogoTiposTramite catalogo,
    DespachadorCredito despachador,
    IOptions<OpcionesDerivacion> opciones,
    TimeProvider reloj,
    ILogger<DerivacionMasiva> log)
{
    public const string CodigoVehiculosRequeridos = "VEHICULOS_REQUERIDOS";
    public const string CodigoDemasiadosVehiculos = "DEMASIADOS_VEHICULOS";
    public const string CodigoVehiculoFueraDelLote = "VEHICULO_FUERA_DEL_LOTE";
    public const string CampoVehiculos = "vehiculo_ids";

    private const int MaxIntentosInsercion = 3;

    /// <summary>Validación del cuerpo que no necesita la base; devuelve los ids sin repetidos en el orden de la selección.</summary>
    public static IReadOnlyList<Guid> ValidarSeleccion(CrearTramitesEntrada? entrada, int maximo)
    {
        var ids = (entrada?.VehiculoIds ?? []).Where(v => v != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            throw new ReglaException(CodigoVehiculosRequeridos, "Selecciona al menos un vehículo.", StatusCodes.Status400BadRequest) { Campo = CampoVehiculos };
        if (ids.Count > maximo)
            throw new ReglaException(CodigoDemasiadosVehiculos, $"Puedes crear trámites de hasta {maximo} vehículos por envío.", StatusCodes.Status400BadRequest) { Campo = CampoVehiculos };
        return ids;
    }

    public async Task<IResult> CrearAsync(Guid id, CrearTramitesEntrada? entrada, CancellationToken ct)
    {
        var ids = ValidarSeleccion(entrada, opciones.Value.MaxItemsPorOperacion);
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        var tipo = await ApoyoDerivacion.TipoAsync(catalogo, cabecera.Modo, cabecera.TipoTramiteCodigo, entrada?.TipoTramiteCodigo, ct).ConfigureAwait(false);

        for (var intento = 1; ; intento++)
        {
            try
            {
                var encolada = await EncolarAsync(cabecera, tipo, ids, ct).ConfigureAwait(false);
                Encolada(log, encolada.OperacionId, id, encolada.Total, encolada.Encolados);
                return Results.Accepted($"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{id}/tramites/operaciones/{encolada.OperacionId}", encolada);
            }
            catch (DbUpdateException ex) when (intento < MaxIntentosInsercion
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ItemDerivacionConfig.IndiceVivo })
            {
                // Doble clic: otro envío ganó la carrera; al releer, esos vehículos quedan EN_CURSO.
                db.ChangeTracker.Clear();
            }
        }
    }

    private Task<OperacionEncoladaDto> EncolarAsync(CabeceraDiagnostico cabecera, string tipo, IReadOnlyList<Guid> ids, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var elegibilidad = await LecturaElegibilidad.LeerAsync(db, cabecera.Id, cabecera.Modo, ids, ct).ConfigureAwait(false);
            if (ids.Any(v => !elegibilidad.ContainsKey(v)))
                throw new ReglaException(CodigoVehiculoFueraDelLote, "Algún vehículo seleccionado no es de este diagnóstico.", StatusCodes.Status400BadRequest) { Campo = CampoVehiculos };

            var ahora = reloj.GetUtcNow();
            var operacion = OperacionDerivacion.Masiva(cabecera.TenantId, cabecera.Id, tipo, usuario.Id, ids.Count, ahora);
            var noElegibles = new List<VehiculoNoElegibleDto>();
            var items = new List<ItemDerivacion>(ids.Count);
            for (var i = 0; i < ids.Count; i++)
            {
                if (elegibilidad[ids[i]].Motivo is { } motivo)
                {
                    items.Add(ItemDerivacion.Omitido(operacion, ids[i], i + 1, motivo, ahora));
                    noElegibles.Add(new VehiculoNoElegibleDto(ids[i], motivo));
                }
                else
                {
                    items.Add(ItemDerivacion.Pendiente(operacion, ids[i], i + 1, ahora));
                }
            }

            operacion.Omitidas = noElegibles.Count;
            operacion.TerminarSiCompleta(ahora);
            db.Add(operacion);
            db.AddRange(items);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await despachador.LiberarAsync(cabecera.TenantId, ct).ConfigureAwait(false);
            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return new OperacionEncoladaDto(operacion.Id, ids.Count, ids.Count - noElegibles.Count, noElegibles);
        });

    [LoggerMessage(EventId = 84350, Level = LogLevel.Information, Message = "Derivación masiva: operación {OperacionId} del lote {DiagnosticoId} con {Total} vehículos, {Encolados} encolados.")]
    private static partial void Encolada(ILogger logger, Guid operacionId, Guid diagnosticoId, int total, int encolados);
}
