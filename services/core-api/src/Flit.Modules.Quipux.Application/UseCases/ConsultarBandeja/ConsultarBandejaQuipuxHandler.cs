using Flit.Modules.Quipux.Domain.LogQx;

namespace Flit.Modules.Quipux.Application.UseCases.ConsultarBandeja;

/// <summary>
/// Entrada del filtro tal como llega del borde HTTP: texto libre, sin parsear. El handler tolera lo
/// que no case (un id que no es UUID, un estado inexistente) en vez de devolver 400 — es una pantalla
/// de diagnóstico y un filtro mal tecleado debe dar una lista vacía, no un error crudo.
/// </summary>
public sealed record ConsultarBandejaQuipuxQuery(
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta,
    string? Placa,
    string? InstanceId,
    string? Referencia,
    string? Documento,
    string? Estado,
    string? TransitOfficeId,
    string? TenantId,
    string? ProcedureTypeId,
    string? Familia,
    int? Page,
    int? PageSize);

/// <summary>
/// Bandeja del LOG QX (HU #11786): lista los trámites con integración Quipux, uno por fila, con
/// filtros combinables y contadores por estado. Es la entrada del módulo y responde SIN filtros —
/// no exige buscar nada para mostrar datos, que es el defecto central de la pantalla anterior.
/// </summary>
/// <remarks>
/// Caso de uso de SOLO LECTURA. Normaliza la paginación, aplica el periodo por defecto, traduce los
/// identificadores de texto a <see cref="Guid"/> y delega la consulta en el repositorio; el
/// enriquecimiento que añade es el cálculo de las horas de espera, que se hace en servidor para que
/// la interfaz no dependa del reloj ni de la zona horaria del navegador.
/// </remarks>
public sealed class ConsultarBandejaQuipuxHandler
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;

    /// <summary>
    /// Ventana por defecto cuando no se envía rango. Acota la consulta para que la carga inicial no
    /// barra el histórico completo, y cubre de sobra lo que soporte necesita ver de un vistazo.
    /// </summary>
    public const int DefaultDiasVentana = 30;

    private readonly IQuipuxBandejaRepository _repository;
    private readonly TimeProvider _clock;

    public ConsultarBandejaQuipuxHandler(
        IQuipuxBandejaRepository repository,
        TimeProvider? clock = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ConsultarBandejaQuipuxResult> HandleAsync(
        ConsultarBandejaQuipuxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);
        var now = _clock.GetUtcNow();

        // Un identificador que no parsea no puede casar con nada: se devuelve página vacía, mismo
        // criterio que el LOG QX vigente. NO se ignora el filtro — eso mostraría resultados que el
        // usuario no pidió y le haría creer que su búsqueda funcionó.
        if (!TryParseOptionalGuid(query.InstanceId, out var instanceId)
            || !TryParseOptionalGuid(query.TransitOfficeId, out var officeId)
            || !TryParseOptionalGuid(query.TenantId, out var tenantId)
            || !TryParseOptionalGuid(query.ProcedureTypeId, out var typeId))
        {
            return Vacia(page, pageSize);
        }

        // Un estado desconocido tampoco casa con ninguna fila.
        var estado = Trim(query.Estado);
        if (estado is not null && !QuipuxBandejaEstados.EsValido(estado))
        {
            return Vacia(page, pageSize);
        }

        var desde = query.Desde ?? (query.Hasta is null
            ? now.AddDays(-DefaultDiasVentana)
            : null);

        var domainQuery = new QuipuxBandejaQuery(
            desde,
            query.Hasta,
            Trim(query.Placa),
            instanceId,
            Trim(query.Referencia),
            Trim(query.Documento),
            estado,
            officeId,
            tenantId,
            typeId,
            // Una familia desconocida se descarta en vez de mandarse: filtrar por ella devolvería
            // cero filas, y una bandeja vacía se leería como «no hay trámites de esa familia»
            // cuando lo que pasó es que el valor no existe.
            QuipuxFamilias.Normalizar(query.Familia),
            page,
            pageSize);

        var result = await _repository.SearchAsync(domainQuery, cancellationToken).ConfigureAwait(false);

        var data = result.Entries.Select(e => MapEntry(e, now)).ToList();
        var contadores = result.Contadores
            .Select(c => new QuipuxBandejaContadorView(c.Estado, c.Total))
            .ToList();

        return new ConsultarBandejaQuipuxResult(data, result.TotalCount, page, pageSize, contadores);
    }

    private static QuipuxBandejaEntryView MapEntry(QuipuxBandejaEntry e, DateTimeOffset now)
    {
        // Solo los no terminales acumulan espera. En un aprobado o un rechazado la antigüedad no
        // significa nada: el trámite ya se resolvió.
        double? horas = e.EsperandoDesde is { } desde && desde <= now
            ? (now - desde).TotalHours
            : null;

        return new QuipuxBandejaEntryView(
            e.ProcedureInstanceId,
            e.ReferenceNumber,
            e.Plate,
            e.ProcedureTypeName,
            e.Estado,
            e.ClientTenantId,
            e.ClientTenantName,
            e.TransitOfficeName,
            e.DivipoCode,
            e.DocumentoQx,
            e.SubmissionId,
            e.Intentos,
            e.Attempts,
            e.PollCount,
            e.QxRegisterCode,
            e.QxProcedureCode,
            e.RejectionReason,
            e.UltimaActividad,
            e.EsperandoDesde,
            horas,
            e.SubmissionCreatedAt);
    }

    /// <summary>
    /// Página vacía con los contadores en cero — todos, no una lista vacía: la interfaz debe poder
    /// pintar los siete contadores en cero en vez de quedarse sin ellos (AC7).
    /// </summary>
    private static ConsultarBandejaQuipuxResult Vacia(int page, int pageSize) =>
        new(
            [],
            0,
            page,
            pageSize,
            QuipuxBandejaEstados.Todos.Select(e => new QuipuxBandejaContadorView(e, 0)).ToList());

    private static bool TryParseOptionalGuid(string? raw, out Guid? value)
    {
        value = null;
        var trimmed = Trim(raw);
        if (trimmed is null)
        {
            return true;
        }

        if (!Guid.TryParse(trimmed, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int NormalizePage(int? page) =>
        page is null or < 1 ? DefaultPage : page.Value;

    private static int NormalizePageSize(int? pageSize)
    {
        if (pageSize is null or < 1)
        {
            return DefaultPageSize;
        }

        return pageSize.Value > MaxPageSize ? MaxPageSize : pageSize.Value;
    }
}
