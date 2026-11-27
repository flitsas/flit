namespace Flit.Impuestos.Domain.Liquidaciones;

/// <summary>Lo que le pasa a un ítem (columna «Evento» de la tabla del plan §3.2).</summary>
public abstract record EventoItem
{
    /// <summary>El despachador le asignó crédito.</summary>
    public sealed record CreditoAsignado : EventoItem;

    /// <summary>RUNT respondió, el payload está completo y el departamento está habilitado.</summary>
    public sealed record RuntCompleto : EventoItem;

    /// <summary>Falta un dato del payload o no hay homologación.</summary>
    public sealed record RuntDatoFaltante(string Campo) : EventoItem;

    /// <summary>El departamento del vehículo no está en el contrato de la compañía.</summary>
    public sealed record DepartamentoFueraDeContrato : EventoItem;

    /// <summary>El RUNT reporta el vehículo como de servicio público (<see cref="ServicioVehiculo.EsPublico"/>): no paga impuesto.</summary>
    public sealed record ServicioPublico : EventoItem;

    /// <summary>El RUNT falló de forma transitoria.</summary>
    public sealed record RuntTransitorio : EventoItem;

    /// <summary>El RUNT no encontró la placa.</summary>
    public sealed record RuntPlacaNoEncontrada : EventoItem;

    /// <summary>Venció <c>proximo_intento_en</c>.</summary>
    public sealed record VenceReintento : EventoItem;

    /// <summary>El API aceptó la consulta (202).</summary>
    public sealed record Aceptado : EventoItem;

    /// <summary>El API rechazó la consulta (400, 409 o 422) con su código.</summary>
    public sealed record Rechazado(string CodigoApi) : EventoItem;

    /// <summary>El API respondió 401 a la api_key de la compañía.</summary>
    public sealed record NoAutorizado : EventoItem;

    /// <summary>La petición no salió (se reintenta en local con el mismo token).</summary>
    public sealed record NoSalio : EventoItem;

    /// <summary>Timeout o 5xx después de enviar.</summary>
    public sealed record FallaTrasEnviar : EventoItem;

    /// <summary>Resultado final por webhook o GET: <c>succeeded</c>, <c>partial</c> o <c>failed</c> con su código.</summary>
    public sealed record Final(string Status, string? CodigoError) : EventoItem;

    /// <summary>Venció el plazo del incierto; <paramref name="TokenHallado"/> si el token apareció en el listado del API.</summary>
    public sealed record PlazoInciertoVencido(bool TokenHallado) : EventoItem;

    /// <summary>Un usuario relanza el ítem: abre un nuevo intento del mismo ítem (R3).</summary>
    public sealed record Relanzar : EventoItem;

    /// <summary>Se cerró el lote a mano.</summary>
    public sealed record CierreManual : EventoItem;
}

/// <summary>
/// Foto del ítem que necesita la máquina de estados. <paramref name="ErrorCodigo"/> y <paramref name="ErrorMensaje"/> son los
/// guardados en el ítem; solo los lee <see cref="EventoItem.Relanzar"/> (placa no inscrita, <see cref="ClasificacionErrores.EsPlacaNoInscrita"/>).
/// </summary>
public sealed record SituacionItem(
    EstadoItem Estado,
    int IntentosRunt = 0,
    int IntentosReenvio = 0,
    ClaseError? ClaseError = null,
    string? ErrorCodigo = null,
    string? ErrorMensaje = null);

/// <summary>
/// Resultado de una transición del ítem: la situación nueva y lo que quien la aplica debe hacer o guardar (código y campo del
/// error, marcas de parcial y tardío, adoptar el <c>consulta.id</c>, token nuevo, nuevo intento del relanzamiento, aviso al
/// aprovisionamiento).
/// </summary>
public sealed record ResultadoItem(
    SituacionItem Nueva,
    string? ErrorCodigo = null,
    string? CampoFaltante = null,
    bool Parcial = false,
    bool ResultadoTardio = false,
    bool AdoptaConsulta = false,
    bool TokenNuevo = false,
    bool NuevoIntento = false,
    bool AprovisionamientoRequiereCredenciales = false);

/// <summary>
/// Tabla de transiciones del ítem (plan §3.2), pura y sin efectos. Cualquier combinación fuera de la tabla lanza
/// <see cref="TransicionInvalida"/>; relanzar algo que no es relanzable lanza el código <see cref="CodigoNoRelanzable"/>.
/// </summary>
/// <example>
/// <code>
/// var r = TransicionesItem.Aplicar(new SituacionItem(EstadoItem.Enviado), new EventoItem.Final("failed", "rate_limited"));
/// // r.Nueva.Estado == EstadoItem.ReintentoProgramado, r.TokenNuevo == true
/// </code>
/// </example>
public static class TransicionesItem
{
    /// <summary>Intentos del RUNT y reenvíos por <c>rate_limited</c> (DA-5): al tercer fallo, error reintentable.</summary>
    public const int MaximoIntentos = 3;

    /// <summary>El RUNT siguió fallando de forma transitoria tras <see cref="MaximoIntentos"/> intentos.</summary>
    public const string CodigoRuntNoDisponible = "RUNT_NO_DISPONIBLE";

    /// <summary>El RUNT no encontró la placa.</summary>
    public const string CodigoRuntPlacaNoEncontrada = "RUNT_PLACA_NO_ENCONTRADA";

    /// <summary>Código del ítem <c>NO_APLICA</c> por vehículo de servicio público.</summary>
    public const string CodigoServicioPublico = "servicio_publico";

    /// <summary>Mensaje del ítem <c>NO_APLICA</c> por vehículo de servicio público (literal del usuario, 2026-11-26).</summary>
    public const string MensajeServicioPublico =
        "El RUNT reporta el vehículo como de servicio público. Los vehículos de servicio público no pagan impuesto vehicular.";

    /// <summary>El API respondió 401 a la api_key de la compañía al enviar.</summary>
    public const string CodigoCredencialRechazada = "CREDENCIAL_RECHAZADA";

    /// <summary>
    /// Relanzar un ítem que no es <c>INCIERTO_REQUIERE_DECISION</c>, <c>DATOS_INCOMPLETOS</c>, <c>ERROR</c> reintentable ni
    /// <c>ERROR</c> de placa no inscrita (409 de la API).
    /// </summary>
    public const string CodigoNoRelanzable = "NO_RELANZABLE";

    private const string Exitosa = "succeeded";
    private const string Parcial = "partial";
    private const string Fallida = "failed";
    private const string LimiteDeTasa = "rate_limited";

    /// <summary>Un aviso con <c>attempt</c> menor que el mayor ya recibido se registra y no se aplica.</summary>
    public static bool AplicaAviso(int attempt, int attemptMaximo) => attempt >= attemptMaximo;

    public static ResultadoItem Aplicar(SituacionItem actual, EventoItem evento)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(evento);
        var estado = actual.Estado;
        return (estado, evento) switch
        {
            (EstadoItem.Pendiente, EventoItem.CreditoAsignado) => Hacia(actual, EstadoItem.ConsultandoRunt),
            (EstadoItem.ConsultandoRunt, EventoItem.RuntCompleto) => Hacia(actual, EstadoItem.Enviando),
            (EstadoItem.ConsultandoRunt, EventoItem.RuntDatoFaltante f) => Hacia(actual, EstadoItem.DatosIncompletos) with { CampoFaltante = f.Campo },
            (EstadoItem.ConsultandoRunt, EventoItem.DepartamentoFueraDeContrato) => Hacia(actual, EstadoItem.DepartamentoNoHabilitado),
            (EstadoItem.ConsultandoRunt, EventoItem.RuntTransitorio) => RuntTransitorio(actual),
            (EstadoItem.ConsultandoRunt, EventoItem.ServicioPublico) => Hacia(actual, EstadoItem.NoAplica) with { ErrorCodigo = CodigoServicioPublico },
            (EstadoItem.ConsultandoRunt, EventoItem.RuntPlacaNoEncontrada) => ErrorCon(actual, CodigoRuntPlacaNoEncontrada, ClaseError.CorregirDatos),
            (EstadoItem.ReintentoProgramado, EventoItem.VenceReintento) => Hacia(actual, EstadoItem.Pendiente),

            (EstadoItem.Enviando, EventoItem.Aceptado) => Hacia(actual, EstadoItem.Enviado),
            (EstadoItem.Enviando, EventoItem.Rechazado r) => ErrorCon(actual, r.CodigoApi, ClasificacionErrores.De(r.CodigoApi)),
            (EstadoItem.Enviando, EventoItem.NoAutorizado) =>
                ErrorCon(actual, CodigoCredencialRechazada, ClaseError.Depende) with { AprovisionamientoRequiereCredenciales = true },
            (EstadoItem.Enviando, EventoItem.NoSalio) => Hacia(actual, EstadoItem.Enviando),
            (EstadoItem.Enviando, EventoItem.FallaTrasEnviar) => Hacia(actual, EstadoItem.Incierto),

            (EstadoItem.Enviado, EventoItem.Final f) => AplicarFinal(actual, f, tardio: false),
            (EstadoItem.Incierto, EventoItem.Final f) => AplicarFinal(actual, f, tardio: false) with { AdoptaConsulta = true },
            (EstadoItem.Incierto, EventoItem.PlazoInciertoVencido p) => p.TokenHallado
                ? Hacia(actual, EstadoItem.Enviado) with { AdoptaConsulta = true }
                : Hacia(actual, EstadoItem.InciertoRequiereDecision),
            (EstadoItem.SinResultadoLoteCerrado, EventoItem.Final f) => AplicarFinal(actual, f, tardio: true),

            (_, EventoItem.Relanzar) => Relanzar(actual),
            (_, EventoItem.CierreManual) when !EstadosItem.EsFinal(estado) => Hacia(actual, EstadoItem.SinResultadoLoteCerrado),

            _ => throw TransicionInvalida.FueraDeTabla(estado.ToString(), evento.GetType().Name),
        };
    }

    private static ResultadoItem RuntTransitorio(SituacionItem actual)
    {
        var intentos = actual.IntentosRunt + 1;
        return intentos < MaximoIntentos
            ? new ResultadoItem(actual with { Estado = EstadoItem.ReintentoProgramado, IntentosRunt = intentos })
            : ErrorCon(actual with { IntentosRunt = intentos }, CodigoRuntNoDisponible, ClaseError.Reintentable);
    }

    private static ResultadoItem AplicarFinal(SituacionItem actual, EventoItem.Final final, bool tardio)
    {
        switch (final.Status)
        {
            case Exitosa or Parcial:
                return Hacia(actual, EstadoItem.Exitoso) with { Parcial = final.Status == Parcial, ResultadoTardio = tardio };
            case Fallida when !tardio && final.CodigoError == LimiteDeTasa && actual.IntentosReenvio + 1 < MaximoIntentos:
                return new ResultadoItem(actual with { Estado = EstadoItem.ReintentoProgramado, IntentosReenvio = actual.IntentosReenvio + 1 }, TokenNuevo: true);
            case Fallida:
                var codigo = final.CodigoError ?? string.Empty;
                var siguiente = !tardio && codigo == LimiteDeTasa ? actual with { IntentosReenvio = actual.IntentosReenvio + 1 } : actual;
                return ErrorCon(siguiente, codigo, ClasificacionErrores.De(codigo)) with { ResultadoTardio = tardio };
            default:
                throw TransicionInvalida.FueraDeTabla(actual.Estado.ToString(), $"resultado {final.Status}");
        }
    }

    /// <summary>
    /// Se relanza desde <c>INCIERTO_REQUIERE_DECISION</c>, <c>DATOS_INCOMPLETOS</c> (faltó un dato del RUNT), <c>ERROR</c>
    /// <see cref="ClaseError.Reintentable"/> o <c>ERROR</c> de placa no inscrita (<see cref="ClasificacionErrores.EsPlacaNoInscrita"/>,
    /// clase <see cref="ClaseError.Depende"/>). El nuevo intento (R3) nace como un ítem que entra a la cola: <c>PENDIENTE</c>, sin
    /// reintentos ni error. Desde cualquier otro estado (en vuelo, exitoso, <c>NO_APLICA</c>…), <see cref="CodigoNoRelanzable"/>.
    /// </summary>
    private static ResultadoItem Relanzar(SituacionItem actual) =>
        EsRelanzable(actual)
            ? new ResultadoItem(new SituacionItem(EstadoItem.Pendiente), NuevoIntento: true)
            : throw TransicionInvalida.Con(CodigoNoRelanzable, "Este ítem no se puede relanzar.");

    private static bool EsRelanzable(SituacionItem actual) => actual.Estado switch
    {
        EstadoItem.InciertoRequiereDecision or EstadoItem.DatosIncompletos => true,
        EstadoItem.Error => actual.ClaseError == ClaseError.Reintentable
            || ClasificacionErrores.EsPlacaNoInscrita(actual.ErrorCodigo, actual.ErrorMensaje),
        _ => false,
    };

    private static ResultadoItem Hacia(SituacionItem actual, EstadoItem estado) => new(actual with { Estado = estado });

    private static ResultadoItem ErrorCon(SituacionItem actual, string codigo, ClaseError clase) =>
        new(actual with { Estado = EstadoItem.Error, ClaseError = clase }, ErrorCodigo: codigo);
}
