namespace Flit.Tramites.Domain.RuntConfirmation;

/// <summary>
/// Qué dice el historial de solicitudes del RUNT sobre la matrícula de un vehículo consultado por VIN
/// (Epic #12550, HU #12648, ADR-0059 §Ruta Corta).
/// </summary>
public enum MatriculaPreviaRuntVeredicto
{
    /// <summary>La respuesta no trae historial de solicitudes: no se puede afirmar nada por esta vía.</summary>
    SinHistorial,

    /// <summary>Hay historial y NO contiene una «MATRÍCULA INICIAL» autorizada/aprobada.</summary>
    NoMatriculado,

    /// <summary>Hay una «MATRÍCULA INICIAL» AUTORIZADA o APROBADA: el vehículo ya está matriculado.</summary>
    Matriculado,
}

/// <summary>
/// Resultado de leer el historial: veredicto más los datos que lo respaldan, para el check y el
/// mensaje al gestor. <see cref="Organismo"/> es la entidad de la solicitud decisiva (la de matrícula si
/// está matriculado; la de preasignación de placa si no).
/// </summary>
public sealed record MatriculaPreviaRunt(
    MatriculaPreviaRuntVeredicto Veredicto,
    string? Organismo,
    DateOnly? Fecha)
{
    public static readonly MatriculaPreviaRunt SinHistorial = new(MatriculaPreviaRuntVeredicto.SinHistorial, null, null);
}

/// <summary>
/// Regla de «ya matriculado» y de organismo de la Ruta Corta a partir de la respuesta del RUNT por VIN.
///
/// <para>Verificado el 2026-09-17 con siete VINes reales de FLIT 1 (capturas en
/// <c>context/estados-preasignado-asignado/capturas-runt-vin/</c>): un vehículo con placa preasignada
/// llega <c>REGISTRADO</c>, con placa, SIN <c>organismoTransito</c> y con una única solicitud
/// «TRÁMITE PREASIGNACIÓN PLACA CONTINGENCIA» APROBADA cuya <c>entidad</c> es el organismo; uno ya
/// matriculado llega <c>ACTIVO</c>, con organismo y con «TRÁMITE MATRÍCULA INICIAL» AUTORIZADA. Por eso
/// «matriculado» se lee del historial y no del estado del automotor: el estado describe el registro, el
/// historial describe el trámite, y es el trámite lo que decide si cabe otra matrícula o toca un
/// traspaso. El estado queda como respaldo cuando no hay historial
/// (<see cref="MatriculaPreviaRuntVeredicto.SinHistorial"/>), decisión que vive en el orquestador del
/// preflight, no aquí.</para>
/// </summary>
public static class RuntMatriculaPolicy
{
    /// <summary>Rótulo RUNT de la matrícula inicial (mismo que usa la Confirmación RUNT, #12276).</summary>
    public const string TramiteMatriculaInicial = "MATRICULA INICIAL";

    /// <summary>Rótulo RUNT de la preasignación de placa: es la solicitud que deja al vehículo con placa antes de matricularse.</summary>
    public const string TramitePreasignacionPlaca = "PREASIGNACION PLACA CONTINGENCIA";

    /// <summary>
    /// Lee el historial. Lista vacía o nula ⇒ <see cref="MatriculaPreviaRunt.SinHistorial"/>; también si
    /// ninguna solicitud dice QUÉ trámite fue (hay respuestas con solo número y estado): un historial
    /// que no se puede leer no descarta la matrícula, así que se deja decidir al respaldo por estado.
    /// </summary>
    public static MatriculaPreviaRunt EvaluarMatriculaPrevia(IReadOnlyList<RuntSolicitud>? solicitudes)
    {
        if (solicitudes is null || solicitudes.All(s => s.Tramites.Count == 0))
            return MatriculaPreviaRunt.SinHistorial;

        // El RUNT no actualiza filas, crea otras: manda la más reciente del tipo buscado. Sin fecha
        // (Verifik a veces no la trae) se conserva el orden de llegada.
        var matricula = MasReciente(solicitudes.Where(s =>
            s.Contiene(TramiteMatriculaInicial) && RuntSolicitudEstados.EsPositivo(s.EstadoNormalizado)));
        if (matricula is not null)
            return new MatriculaPreviaRunt(MatriculaPreviaRuntVeredicto.Matriculado, Trim(matricula.Entidad), matricula.Fecha);

        var preasignacion = MasReciente(solicitudes.Where(s =>
            s.Contiene(TramitePreasignacionPlaca) && RuntSolicitudEstados.EsPositivo(s.EstadoNormalizado)));
        return new MatriculaPreviaRunt(
            MatriculaPreviaRuntVeredicto.NoMatriculado,
            Trim(preasignacion?.Entidad),
            preasignacion?.Fecha);
    }

    /// <summary>
    /// Organismo del vehículo para la Ruta Corta: el del bloque del vehículo si viene; si no, el de la
    /// solicitud decisiva del historial (en un preasignado, la entidad que preasignó la placa).
    /// <c>null</c> cuando el RUNT no dice nada: el orquestador decide si el gestor lo elige.
    /// </summary>
    public static string? OrganismoDelVehiculo(string? organismoTransito, IReadOnlyList<RuntSolicitud>? solicitudes) =>
        Trim(organismoTransito) ?? EvaluarMatriculaPrevia(solicitudes).Organismo;

    private static RuntSolicitud? MasReciente(IEnumerable<RuntSolicitud> candidatas)
    {
        RuntSolicitud? mejor = null;
        foreach (var s in candidatas)
        {
            if (mejor is null || (s.Fecha is { } f && (mejor.Fecha is null || f > mejor.Fecha)))
                mejor = s;
        }

        return mejor;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
