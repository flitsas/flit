namespace Flit.Impuestos.Domain.Aprovisionamiento;

/// <summary>
/// Estado del aprovisionamiento de una compañía en el API de liquidación (plan §3.1). En la base se guarda en mayúsculas con
/// guion bajo (<c>LISTO_SIN_WEBHOOK</c>), como lo valida el CHECK de <c>impuestos.aprovisionamientos.estado</c>.
/// </summary>
public enum EstadoAprovisionamiento
{
    /// <summary>En curso o con reintento automático programado. No liquida.</summary>
    Pendiente,

    /// <summary>Liquida con <c>callbackUrl</c>; la firma queda sin confirmar hasta el primer webhook válido.</summary>
    Listo,

    /// <summary>Liquida sin <c>callbackUrl</c> (consulta por polling).</summary>
    ListoSinWebhook,

    /// <summary>Error de datos o de ambiente (<see cref="TipoErrorAprovisionamiento"/>). No liquida; se reintenta.</summary>
    Error,

    /// <summary>El alta pudo llegar al API pero no se supo la respuesta. No liquida; se reintenta.</summary>
    Incierto,

    /// <summary>Hay que cargar api_key y webhook_secret a mano. No liquida.</summary>
    RequiereCredenciales,

    /// <summary>El NIT de la compañía no es válido para el alta. No liquida.</summary>
    RequiereRevisionNit,

    /// <summary>Producto desactivado: no crea liquidaciones nuevas y termina las que están en curso.</summary>
    Suspendido,
}

/// <summary>Tipo del estado <see cref="EstadoAprovisionamiento.Error"/> (columna <c>error_tipo</c>).</summary>
public enum TipoErrorAprovisionamiento
{
    /// <summary>El API rechazó los datos del alta (400, 415 o razón social de más de 200 caracteres).</summary>
    Datos,

    /// <summary>Problema del ambiente (alta 401 o 404, o la petición no salió tras los reintentos automáticos).</summary>
    Ambiente,
}
