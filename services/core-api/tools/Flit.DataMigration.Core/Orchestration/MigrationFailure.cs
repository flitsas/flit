namespace Flit.DataMigration.V1.Orchestration;

/// <summary>
/// Clase de fallo previo a tocar un solo trámite.
/// <para>
/// Los valores SON los códigos de salida históricos de la consola, no una coincidencia: la
/// consola hace <c>(int)Kind</c> y no hay tabla de conversión que se pueda desalinear.
/// </para>
/// </summary>
public enum MigrationFailureKind
{
    /// <summary>Un valor de configuración es inválido (hoy: exit 2).</summary>
    BadConfiguration = 2,

    /// <summary>Falta configuración, o el entorno destino no está listo (hoy: exit 3).</summary>
    EnvironmentNotReady = 3,
}

/// <summary>
/// Fallo que impide arrancar la migración, contado de forma independiente del host.
/// <para>
/// <see cref="Code"/> es el código de error estable (el API lo usa como <c>title</c> del
/// RFC7807). <see cref="Message"/> es EXACTAMENTE el texto que la consola imprime tras «✗ »,
/// palabra por palabra: los runbooks de operación citan estos mensajes.
/// </para>
/// </summary>
public sealed record MigrationFailure(MigrationFailureKind Kind, string Code, string Message);
