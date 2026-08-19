using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Origen de una regeneración documental interna del sistema (Bug #11613). Va en el payload del
/// evento de fallo para que el diagnóstico diga QUÉ flujo se quedó sin documentos regenerados.
/// </summary>
public static class RegeneracionDocumentalOrigen
{
    /// <summary>Aprobación del trámite por el organismo de tránsito (HU #10996).</summary>
    public const string AprobacionOt = "aprobacion_ot";

    /// <summary>Asignación de placa desde la consola del OT, Flujo B (HU #10995).</summary>
    public const string AsignacionPlaca = "asignacion_placa";
}

/// <summary>
/// Resultado de una regeneración documental trazada. <paramref name="Ok"/> false ⇒
/// <paramref name="Error"/> trae el código de negocio devuelto por el generador (p. ej.
/// <c>organismo_requerido</c>) o <c>excepcion</c> si el generador reventó.
/// </summary>
public sealed record RegeneracionDocumentalResultado(bool Ok, string? Error, bool TrazaPersistida)
{
    public static RegeneracionDocumentalResultado Exitosa() => new(true, null, false);
}

/// <summary>
/// Puerto de escritura de la traza de fallo de regeneración documental (Bug #11613).
/// <para>Es un puerto propio —y no <c>repo.Add</c> + <c>SaveChangesAsync</c>— a propósito: la traza se
/// escribe JUSTO DESPUÉS de que el generador falló, cuando el change tracker puede arrastrar cambios a
/// medio hacer del intento fallido (adjuntos borrados/insertados). Un <c>SaveChanges</c> ahí los
/// volcaría a la base. La implementación inserta la fila con SQL parametrizado, sin pasar por el
/// tracker, en la misma conexión; el aislamiento por tenant lo garantiza el <c>tenant_id</c>
/// explícito y un <c>WHERE EXISTS</c> contra el trámite, no el RLS (que en este despliegue no llega a
/// evaluarse).</para>
/// </summary>
public interface IRegeneracionDocumentalTrazaWriter
{
    /// <summary>
    /// Inserta un evento <c>regeneracion_documental_fallida</c> en la bitácora de la instancia.
    /// Devuelve <c>true</c> si la fila quedó escrita.
    /// </summary>
    Task<bool> EscribirFalloAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        string origen,
        string codigoError,
        string? detalle,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación inerte (NUNCA escribe) para tests/DI que no ejercitan la traza. Mismo patrón que el
/// resto de puertos opcionales del módulo.
/// </summary>
public sealed class NullRegeneracionDocumentalTrazaWriter : IRegeneracionDocumentalTrazaWriter
{
    public static NullRegeneracionDocumentalTrazaWriter Instance { get; } = new();

    public Task<bool> EscribirFalloAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        string origen,
        string codigoError,
        string? detalle,
        CancellationToken cancellationToken = default) => Task.FromResult(false);
}

/// <summary>
/// Bug #11613 — envoltura TRAZADA de la regeneración documental en caliente que disparan los flujos
/// internos del OT (aprobar el trámite, asignar la placa).
///
/// <para>El problema que resuelve: <c>GenerarFurHandler.HandleAsync</c> devuelve una tupla
/// <c>(Result, Error)</c> y un error de negocio NO lanza excepción. Los dos puntos de invocación
/// descartaban ese retorno dentro de un <c>try/catch</c>, así que un <c>organismo_requerido</c> dejaba
/// el trámite sin documentos regenerados sin log, sin traza y respondiendo 200 OK.</para>
///
/// <para>Sigue siendo BEST-EFFORT: nunca propaga el fallo (la aprobación / la asignación de placa ya
/// están persistidas y no se revierten), pero ahora el fallo queda (1) logueado a nivel Error y
/// (2) persistido como evento consultable de la instancia, siguiendo el patrón de
/// <c>documento_personalizado_no_disponible</c> en <c>GenerarFurHandler</c>.</para>
/// </summary>
public sealed class RegenerarDocumentosTrazadoHandler(
    IExpedienteHotDocumentsRegenerator regenerador,
    ILogger<RegenerarDocumentosTrazadoHandler> logger,
    IRegeneracionDocumentalTrazaWriter? trazaWriter = null)
{
    /// <summary>Tipo del evento persistido en <c>tramites.procedure_instance_events</c>.</summary>
    public const string EventoFallo = "regeneracion_documental_fallida";

    /// <summary>Código sintético cuando el generador lanzó una excepción en vez de devolver error.</summary>
    public const string ErrorExcepcion = "excepcion";

    private readonly IRegeneracionDocumentalTrazaWriter _traza =
        trazaWriter ?? NullRegeneracionDocumentalTrazaWriter.Instance;

    public async Task<RegeneracionDocumentalResultado> HandleAsync(
        Guid procedureInstanceId,
        Guid tenantId,
        string origen,
        CancellationToken ct = default)
    {
        string? error;
        string? detalle = null;

        try
        {
            error = await regenerador
                .RegenerateHotDocumentsAsync(procedureInstanceId, tenantId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El catch del endpoint seguía existiendo, pero registraba a Warning y sin traza en BD. Aquí
            // el fallo se normaliza al MISMO camino que el error de negocio: Error + evento consultable.
            error = ErrorExcepcion;
            detalle = ex.GetType().Name;
            RegeneracionDocumentalLog.FalloPorExcepcion(logger, ex, procedureInstanceId, origen);
        }

        if (error is null)
            return RegeneracionDocumentalResultado.Exitosa();

        if (detalle is null)
            RegeneracionDocumentalLog.FalloDeNegocio(logger, procedureInstanceId, origen, error);

        var persistida = await EscribirTrazaAsync(tenantId, procedureInstanceId, origen, error, detalle, ct)
            .ConfigureAwait(false);

        return new RegeneracionDocumentalResultado(false, error, persistida);
    }

    /// <summary>
    /// La traza es el único rastro consultable sin acceder a los logs del servidor, pero tampoco puede
    /// tumbar la operación: si la propia escritura falla, se registra y se sigue.
    /// </summary>
    private async Task<bool> EscribirTrazaAsync(
        Guid tenantId, Guid instanceId, string origen, string error, string? detalle, CancellationToken ct)
    {
        try
        {
            return await _traza
                .EscribirFalloAsync(tenantId, instanceId, origen, error, detalle, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RegeneracionDocumentalLog.TrazaNoPersistida(logger, ex, instanceId, origen);
            return false;
        }
    }
}

/// <summary>Logging source-generated (CA1848) de la regeneración documental trazada. Sin PII.</summary>
internal static partial class RegeneracionDocumentalLog
{
    [LoggerMessage(Level = LogLevel.Error,
        Message = "La regeneración documental del trámite {InstanceId} (origen {Origen}) falló con el código de negocio {CodigoError}; el trámite conserva los documentos previos.")]
    public static partial void FalloDeNegocio(ILogger logger, Guid instanceId, string origen, string codigoError);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "La regeneración documental del trámite {InstanceId} (origen {Origen}) lanzó una excepción; el trámite conserva los documentos previos.")]
    public static partial void FalloPorExcepcion(ILogger logger, Exception ex, Guid instanceId, string origen);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "No se pudo persistir la traza del fallo de regeneración del trámite {InstanceId} (origen {Origen}); el diagnóstico queda solo en los logs.")]
    public static partial void TrazaNoPersistida(ILogger logger, Exception ex, Guid instanceId, string origen);
}
