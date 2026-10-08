using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13420 (AC1) — parámetros vigentes del motor de descarga masiva para la pantalla del Super Admin. Todos los
/// campos de <c>tramites.consolidado_export_settings</c> salvo <c>id</c>/<c>created_*</c>; <see cref="UpdatedByName"/>
/// es el nombre visible de quien cambió (null si nadie la ha editado o el usuario ya no existe) y
/// <see cref="RowVersion"/> es el token que el <c>PUT</c> debe devolver. <see cref="Limites"/> publica los rangos del
/// DDL 133 para que la pantalla no repita los literales.
/// </summary>
public sealed record ParametrosMotorLoteDto(
    int MaxItemsPerBatch,
    int MaxPdfsPerPart,
    int MaxMbPerPart,
    int ItemSlots,
    int ItemTimeoutSeconds,
    int ItemLeaseSeconds,
    int MaxItemAttempts,
    int RetryDelaySeconds,
    int PartTimeoutSeconds,
    int PartLeaseSeconds,
    int MaxPartAttempts,
    int RetentionHours,
    bool IsActive,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy,
    string? UpdatedByName,
    long RowVersion,
    IReadOnlyList<ParametroMotorLoteLimiteDto> Limites)
{
    /// <summary>Proyección de la fila leída.</summary>
    public static ParametrosMotorLoteDto De(ConsolidadoExportSettingsLeidos leidos)
    {
        ArgumentNullException.ThrowIfNull(leidos);
        var s = leidos.Settings;
        return new(
            s.MaxItemsPerBatch, s.MaxPdfsPerPart, s.MaxMbPerPart, s.ItemSlots, s.ItemTimeoutSeconds, s.ItemLeaseSeconds,
            s.MaxItemAttempts, s.RetryDelaySeconds, s.PartTimeoutSeconds, s.PartLeaseSeconds, s.MaxPartAttempts,
            s.RetentionHours, s.IsActive, s.UpdatedAt, s.UpdatedBy, leidos.ActualizadoPorNombre, s.RowVersion,
            LimitesDto);
    }

    /// <summary>Rangos del DDL 133 en el formato del contrato (<c>maximo</c> null si el CHECK solo pone mínimo).</summary>
    public static IReadOnlyList<ParametroMotorLoteLimiteDto> LimitesDto { get; } =
    [
        .. ConsolidadoExportSettingsRangos.Todos.Select(r =>
            new ParametroMotorLoteLimiteDto(r.Campo, r.Minimo, r.SinMaximo ? null : r.Maximo, null)),
        .. ConsolidadoExportSettingsRangos.ReglasLease.Select(r =>
            new ParametroMotorLoteLimiteDto(r.CampoLease, null, null, r.CampoTimeout)),
    ];
}

/// <summary>
/// HU #13420 — límite de un campo: <see cref="Minimo"/>/<see cref="Maximo"/> inclusivos (null = sin límite propio) y
/// <see cref="MayorQue"/>, el campo cuyo valor debe superar (solo en los leases).
/// </summary>
public sealed record ParametroMotorLoteLimiteDto(string Campo, int? Minimo, int? Maximo, string? MayorQue);

/// <summary>
/// HU #13420 (AC2–AC4, AC6) — cuerpo del <c>PUT</c>: los trece valores editables y el <see cref="RowVersion"/> leído,
/// todos obligatorios (un nulo es 400 en su campo: un <c>isActive</c> ausente no puede apagar el motor por defecto).
/// </summary>
public sealed record ActualizarParametrosMotorLoteCommand(
    int? MaxItemsPerBatch,
    int? MaxPdfsPerPart,
    int? MaxMbPerPart,
    int? ItemSlots,
    int? ItemTimeoutSeconds,
    int? ItemLeaseSeconds,
    int? MaxItemAttempts,
    int? RetryDelaySeconds,
    int? PartTimeoutSeconds,
    int? PartLeaseSeconds,
    int? MaxPartAttempts,
    int? RetentionHours,
    bool? IsActive,
    long? RowVersion,
    Guid? UsuarioId);

/// <summary>HU #13420 — desenlace del <c>PUT</c>.</summary>
public enum ParametrosMotorLoteEstado
{
    Actualizado,
    Invalido,
    NoEncontrado,
    Conflicto,
}

/// <summary>HU #13420 — resultado del <c>PUT</c>; <see cref="Errores"/> por campo (camelCase) en <see cref="ParametrosMotorLoteEstado.Invalido"/>.</summary>
public sealed record ParametrosMotorLoteResultado(
    ParametrosMotorLoteEstado Estado,
    ParametrosMotorLoteDto? Parametros,
    IReadOnlyDictionary<string, string[]> Errores)
{
    internal static readonly IReadOnlyDictionary<string, string[]> SinErrores = new Dictionary<string, string[]>();
}

/// <summary>
/// HU #13420 (AC1) — <c>GET</c> de los parámetros. <c>null</c> si la fila no existe (la siembra la migración; sin ella
/// la creación de lotes ya responde 503 <c>motor_inactivo</c>): el endpoint responde 404 en vez de inventar valores.
/// <para>Uso de ejemplo: <c>var dto = await handler.HandleAsync(ct);</c></para>
/// </summary>
public sealed class ObtenerParametrosMotorLoteHandler(IConsolidadoExportSettingsRepository repositorio)
{
    public async Task<ParametrosMotorLoteDto?> HandleAsync(CancellationToken ct = default) =>
        await repositorio.ObtenerAsync(ct).ConfigureAwait(false) is { } leidos ? ParametrosMotorLoteDto.De(leidos) : null;
}

/// <summary>
/// HU #13420 (AC2–AC4, AC6) — <c>PUT</c> de los parámetros: obligatorios → rangos y leases del DDL 133
/// (<see cref="ConsolidadoExportSettingsRangos"/>) → escritura con <c>row_version</c>. Nada se escribe si hay un error.
/// <para>Uso de ejemplo: <c>var r = await handler.HandleAsync(command, ct);</c></para>
/// </summary>
public sealed partial class ActualizarParametrosMotorLoteHandler(
    IConsolidadoExportSettingsRepository repositorio,
    TimeProvider? reloj = null,
    ILogger<ActualizarParametrosMotorLoteHandler>? logger = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<ActualizarParametrosMotorLoteHandler>.Instance;

    /// <summary>Mensaje del campo ausente o nulo.</summary>
    public const string MensajeObligatorio = "Es obligatorio.";

    public async Task<ParametrosMotorLoteResultado> HandleAsync(
        ActualizarParametrosMotorLoteCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Obligatorios: un nulo nunca se interpreta como 0/false (un isActive ausente no apaga el motor).
        var faltan = new[]
            {
                ("maxItemsPerBatch", command.MaxItemsPerBatch is null), ("maxPdfsPerPart", command.MaxPdfsPerPart is null),
                ("maxMbPerPart", command.MaxMbPerPart is null), ("itemSlots", command.ItemSlots is null),
                ("itemTimeoutSeconds", command.ItemTimeoutSeconds is null),
                ("itemLeaseSeconds", command.ItemLeaseSeconds is null),
                ("maxItemAttempts", command.MaxItemAttempts is null),
                ("retryDelaySeconds", command.RetryDelaySeconds is null),
                ("partTimeoutSeconds", command.PartTimeoutSeconds is null),
                ("partLeaseSeconds", command.PartLeaseSeconds is null),
                ("maxPartAttempts", command.MaxPartAttempts is null), ("retentionHours", command.RetentionHours is null),
                ("isActive", command.IsActive is null), ("rowVersion", command.RowVersion is null),
            }
            .Where(c => c.Item2)
            .ToDictionary(c => c.Item1, _ => new[] { MensajeObligatorio });
        if (faltan.Count > 0)
            return Invalido(faltan);

        // 2. Rangos y leases idénticos a los CHECK del DDL 133.
        var valores = new ConsolidadoExportSettingsValores(
            command.MaxItemsPerBatch!.Value, command.MaxPdfsPerPart!.Value, command.MaxMbPerPart!.Value,
            command.ItemSlots!.Value, command.ItemTimeoutSeconds!.Value, command.ItemLeaseSeconds!.Value,
            command.MaxItemAttempts!.Value, command.RetryDelaySeconds!.Value, command.PartTimeoutSeconds!.Value,
            command.PartLeaseSeconds!.Value, command.MaxPartAttempts!.Value, command.RetentionHours!.Value,
            command.IsActive!.Value);
        var errores = ConsolidadoExportSettingsRangos.Validar(valores);
        if (errores.Count > 0)
            return Invalido(errores
                .GroupBy(e => e.Campo, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Mensaje).ToArray(), StringComparer.Ordinal));

        // 3. Escritura con concurrencia optimista.
        var r = await repositorio
            .ActualizarAsync(valores, command.RowVersion!.Value, command.UsuarioId, _reloj.GetUtcNow(), ct)
            .ConfigureAwait(false);
        switch (r.Estado)
        {
            case ActualizarSettingsEstado.Actualizado:
                LogActualizados(_logger, command.UsuarioId, r.Leidos!.Settings.RowVersion, valores.IsActive,
                    valores.MaxItemsPerBatch);
                return new(ParametrosMotorLoteEstado.Actualizado, ParametrosMotorLoteDto.De(r.Leidos), ParametrosMotorLoteResultado.SinErrores);
            case ActualizarSettingsEstado.Conflicto:
                LogConflicto(_logger, command.UsuarioId, command.RowVersion.Value);
                return new(ParametrosMotorLoteEstado.Conflicto, null, ParametrosMotorLoteResultado.SinErrores);
            case ActualizarSettingsEstado.NoEncontrado:
                return new(ParametrosMotorLoteEstado.NoEncontrado, null, ParametrosMotorLoteResultado.SinErrores);
            default:
                // Defensa en profundidad: la validación de arriba replica los CHECK; si la base rechaza igual, el 400
                // señala el campo de la restricción (o «parametros» si no es de esta tabla).
                var campo = ConsolidadoExportSettingsRangos.CampoDeRestriccion(r.Restriccion) ?? "parametros";
                return Invalido(new Dictionary<string, string[]>
                {
                    [campo] = ["La base de datos rechazó el valor."],
                });
        }
    }

    private static ParametrosMotorLoteResultado Invalido(IReadOnlyDictionary<string, string[]> errores) =>
        new(ParametrosMotorLoteEstado.Invalido, null, errores);

    [LoggerMessage(EventId = 13420, Level = LogLevel.Information,
        Message = "Parámetros del motor de lotes actualizados por {UsuarioId}: rowVersion {RowVersion}, activo {Activo}, tope {Tope}")]
    private static partial void LogActualizados(ILogger logger, Guid? usuarioId, long rowVersion, bool activo, int tope);

    [LoggerMessage(EventId = 13421, Level = LogLevel.Information,
        Message = "Parámetros del motor de lotes: conflicto de row_version para {UsuarioId} (leída {RowVersion})")]
    private static partial void LogConflicto(ILogger logger, Guid? usuarioId, long rowVersion);
}
