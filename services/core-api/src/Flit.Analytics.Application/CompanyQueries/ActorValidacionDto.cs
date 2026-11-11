namespace Flit.Analytics.Application.CompanyQueries;

/// <summary>
/// Un actor de un trámite con el resultado de su validación de identidad, para los reportes de la empresa.
///
/// <para>Contrato congelado (JSON camelCase). NO lleva fuente ni mecanismo de validación: el usuario del reporte
/// no ve cómo se validó. <see cref="EstadoValidacion"/> es un código cerrado y <see cref="MotivoRechazo"/> sale ya en
/// español, sin nombres de proveedor.</para>
/// </summary>
/// <param name="Rol">«comprador», «vendedor» o «propietario» (este último derivado de la familia del trámite).</param>
/// <param name="Ordinal">Posición del actor dentro de su rol (<c>ProcedureInstanceActor.Ordinal</c>).</param>
/// <param name="EstadoValidacion">Ver <see cref="ActorValidacionEstados"/>.</param>
/// <param name="MotivoRechazo">Último rechazo del actor, aunque después lo hayan aprobado; null si nunca fue rechazado.</param>
/// <param name="Intentos">Total de resultados recibidos en todos los ciclos de validación del actor.</param>
public sealed record ActorValidacionDto(
    string Rol,
    int Ordinal,
    string Nombre,
    string? TipoDocumento,
    string? Documento,
    string EstadoValidacion,
    string? MotivoRechazo,
    int Intentos);

/// <summary>Máximo de actores por rol sobre TODO el filtro (no solo la página); define el ancho del Excel.</summary>
public sealed record MaxActoresPorRolDto(int Comprador, int Vendedor, int Propietario);

/// <summary>Códigos cerrados de <see cref="ActorValidacionDto.EstadoValidacion"/>.</summary>
public static class ActorValidacionEstados
{
    public const string Aprobado = "aprobado";
    public const string Rechazado = "rechazado";
    public const string EnProceso = "en_proceso";
    public const string Enviado = "enviado";
    public const string PendienteEnvio = "pendiente_envio";
    public const string ErrorEnvio = "error_envio";
    public const string Expirado = "expirado";
    public const string ManualActivo = "manual_activo";
    public const string PendienteRevisionManual = "pendiente_revision_manual";
    public const string SinValidacion = "sin_validacion";
}

/// <summary>Roles que emiten los reportes.</summary>
public static class ActorValidacionRoles
{
    public const string Comprador = "comprador";
    public const string Vendedor = "vendedor";
    public const string Propietario = "propietario";
}
