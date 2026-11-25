namespace Flit.Impuestos.Domain.Aprovisionamiento;

/// <summary>Lo que le pasa al aprovisionamiento de una compañía (columna «Evento» de la tabla del plan §3.1).</summary>
public enum EventoAprovisionamiento
{
    /// <summary>La plataforma activó el producto (<c>impuestos.producto.cambiado</c> con <c>enabled</c>).</summary>
    ProductoHabilitado,

    /// <summary>La plataforma desactivó el producto.</summary>
    ProductoDeshabilitado,

    /// <summary>La credencial propia respondió <c>GET /fuentes</c> 200 y hay webhook_secret.</summary>
    CredencialPropiaValidaConSecreto,

    /// <summary>La credencial propia respondió <c>GET /fuentes</c> 200 sin webhook_secret.</summary>
    CredencialPropiaValidaSinSecreto,

    /// <summary>La llave copiada de Diagnóstico es válida (sin secreto de webhook).</summary>
    CopiaDiagnosticoValida,

    /// <summary>La credencial propia o copiada respondió 401 al evaluarla.</summary>
    CredencialRechazada,

    /// <summary>El NIT de la compañía no está en <c>VALIDO</c>.</summary>
    NitNoValido,

    /// <summary>El alta respondió 201.</summary>
    Alta201,

    /// <summary>El alta respondió 409 (la compañía ya existe en el API).</summary>
    Alta409,

    /// <summary>El alta respondió 400 o 415, o la razón social pasa de 200 caracteres.</summary>
    AltaDatosInvalidos,

    /// <summary>El alta respondió 401 o 404 (problema del ambiente).</summary>
    AltaAmbienteInvalido,

    /// <summary>La petición no salió (DNS, conexión rechazada, TLS).</summary>
    NoSalio,

    /// <summary>Timeout, 5xx o corte después de enviar.</summary>
    Incierto,

    /// <summary>El Super Admin pidió reintentar (trabajo de reintento).</summary>
    Reintentar,

    /// <summary>Carga manual válida de api_key y webhook_secret.</summary>
    CargaManualConSecreto,

    /// <summary>Carga manual válida solo de la api_key.</summary>
    CargaManualSinSecreto,

    /// <summary>Un 401 en cualquier llamada con la api_key de una compañía lista.</summary>
    NoAutorizadoEnUso,

    /// <summary>Llegó el primer webhook con firma válida.</summary>
    PrimerWebhookValido,
}

/// <summary>
/// Foto del aprovisionamiento que necesita la máquina de estados. <see cref="Estado"/> null es «sin fila» (la compañía nunca
/// se aprovisionó).
/// </summary>
public sealed record SituacionAprovisionamiento(
    EstadoAprovisionamiento? Estado,
    EstadoAprovisionamiento? EstadoPrevio = null,
    int IntentosAutomaticos = 0,
    bool FirmaConfirmada = false,
    TipoErrorAprovisionamiento? TipoError = null)
{
    /// <summary>La compañía todavía no tiene fila en <c>impuestos.aprovisionamientos</c>.</summary>
    public static SituacionAprovisionamiento SinFila { get; } = new((EstadoAprovisionamiento?)null);
}

/// <summary>Resultado de una transición: la situación nueva y si hay que levantar una alerta operativa.</summary>
public sealed record ResultadoAprovisionamiento(SituacionAprovisionamiento Nueva, bool Alerta);

/// <summary>
/// Tabla de transiciones del aprovisionamiento (plan §3.1), pura y sin efectos: quien la usa guarda la fila, escribe el
/// historial, sube la versión y reporta a la plataforma. Cualquier combinación fuera de la tabla lanza
/// <see cref="TransicionInvalida"/>.
/// </summary>
/// <example>
/// <code>
/// var r = TransicionesAprovisionamiento.Aplicar(actual, EventoAprovisionamiento.Alta201);
/// if (r.Alerta) { /* aviso operativo */ }
/// </code>
/// </example>
public static class TransicionesAprovisionamiento
{
    /// <summary>Reintentos automáticos cuando la petición no salió; el siguiente fallo es <c>ERROR(AMBIENTE)</c>.</summary>
    public const int MaximoIntentosAutomaticos = 5;

    /// <summary>Solo las compañías listas crean liquidaciones.</summary>
    public static bool Liquida(EstadoAprovisionamiento estado) =>
        estado is EstadoAprovisionamiento.Listo or EstadoAprovisionamiento.ListoSinWebhook;

    /// <summary>Solo <c>LISTO</c> envía <c>callbackUrl</c>.</summary>
    public static bool EnviaCallback(EstadoAprovisionamiento estado) => estado == EstadoAprovisionamiento.Listo;

    public static ResultadoAprovisionamiento Aplicar(SituacionAprovisionamiento actual, EventoAprovisionamiento evento)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (actual.Estado is not { } estado)
        {
            return evento == EventoAprovisionamiento.ProductoHabilitado
                ? Ok(Pendiente())
                : throw TransicionInvalida.FueraDeTabla("sin fila", evento.ToString());
        }

        return (estado, evento) switch
        {
            (EstadoAprovisionamiento.Suspendido, EventoAprovisionamiento.ProductoHabilitado) =>
                actual.EstadoPrevio is EstadoAprovisionamiento.Listo or EstadoAprovisionamiento.ListoSinWebhook
                    ? Ok(actual with { Estado = actual.EstadoPrevio, EstadoPrevio = null })
                    : Ok(Pendiente()),
            (not EstadoAprovisionamiento.Suspendido, EventoAprovisionamiento.ProductoDeshabilitado) =>
                Ok(actual with { Estado = EstadoAprovisionamiento.Suspendido, EstadoPrevio = estado }),

            (EstadoAprovisionamiento.Pendiente, EventoAprovisionamiento.CredencialPropiaValidaConSecreto or EventoAprovisionamiento.Alta201) =>
                Ok(Hacia(actual, EstadoAprovisionamiento.Listo) with { FirmaConfirmada = false }),
            (EstadoAprovisionamiento.Pendiente, EventoAprovisionamiento.CredencialPropiaValidaSinSecreto or EventoAprovisionamiento.CopiaDiagnosticoValida) =>
                Ok(Hacia(actual, EstadoAprovisionamiento.ListoSinWebhook) with { FirmaConfirmada = false }),
            (EstadoAprovisionamiento.Pendiente, EventoAprovisionamiento.CredencialRechazada or EventoAprovisionamiento.Alta409) =>
                Ok(Hacia(actual, EstadoAprovisionamiento.RequiereCredenciales)),
            (EstadoAprovisionamiento.Pendiente, EventoAprovisionamiento.NitNoValido) => Ok(Hacia(actual, EstadoAprovisionamiento.RequiereRevisionNit)),
            (EstadoAprovisionamiento.Pendiente, EventoAprovisionamiento.AltaDatosInvalidos) => Ok(ErrorDe(actual, TipoErrorAprovisionamiento.Datos)),
            (EstadoAprovisionamiento.Pendiente, EventoAprovisionamiento.AltaAmbienteInvalido) => ConAlerta(ErrorDe(actual, TipoErrorAprovisionamiento.Ambiente)),
            (EstadoAprovisionamiento.Pendiente, EventoAprovisionamiento.NoSalio) =>
                actual.IntentosAutomaticos < MaximoIntentosAutomaticos
                    ? Ok(actual with { IntentosAutomaticos = actual.IntentosAutomaticos + 1 })
                    : Ok(ErrorDe(actual, TipoErrorAprovisionamiento.Ambiente)),
            (EstadoAprovisionamiento.Pendiente, EventoAprovisionamiento.Incierto) => Ok(Hacia(actual, EstadoAprovisionamiento.Incierto)),

            (EstadoAprovisionamiento.Error or EstadoAprovisionamiento.Incierto or EstadoAprovisionamiento.RequiereRevisionNit, EventoAprovisionamiento.Reintentar) =>
                Ok(Pendiente() with { FirmaConfirmada = actual.FirmaConfirmada }),

            (EstadoAprovisionamiento.RequiereCredenciales or EstadoAprovisionamiento.ListoSinWebhook, EventoAprovisionamiento.CargaManualConSecreto) =>
                Ok(Hacia(actual, EstadoAprovisionamiento.Listo) with { FirmaConfirmada = false }),
            (EstadoAprovisionamiento.RequiereCredenciales or EstadoAprovisionamiento.ListoSinWebhook, EventoAprovisionamiento.CargaManualSinSecreto) =>
                Ok(Hacia(actual, EstadoAprovisionamiento.ListoSinWebhook) with { FirmaConfirmada = false }),

            (EstadoAprovisionamiento.Listo or EstadoAprovisionamiento.ListoSinWebhook, EventoAprovisionamiento.NoAutorizadoEnUso) =>
                ConAlerta(Hacia(actual, EstadoAprovisionamiento.RequiereCredenciales)),
            (EstadoAprovisionamiento.Listo, EventoAprovisionamiento.PrimerWebhookValido) => Ok(actual with { FirmaConfirmada = true }),

            _ => throw TransicionInvalida.FueraDeTabla(estado.ToString(), evento.ToString()),
        };
    }

    private static SituacionAprovisionamiento Pendiente() => new(EstadoAprovisionamiento.Pendiente);

    private static SituacionAprovisionamiento Hacia(SituacionAprovisionamiento actual, EstadoAprovisionamiento estado) =>
        actual with { Estado = estado, TipoError = null };

    private static SituacionAprovisionamiento ErrorDe(SituacionAprovisionamiento actual, TipoErrorAprovisionamiento tipo) =>
        actual with { Estado = EstadoAprovisionamiento.Error, TipoError = tipo };

    private static ResultadoAprovisionamiento Ok(SituacionAprovisionamiento nueva) => new(nueva, Alerta: false);

    private static ResultadoAprovisionamiento ConAlerta(SituacionAprovisionamiento nueva) => new(nueva, Alerta: true);
}
