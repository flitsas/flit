using System.Security.Claims;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Api.Endpoints.Auditing;

/// <summary>
/// HU #12361 (Feature #12257) — desenlace de una ruta de red que el endpoint publica en
/// <see cref="HttpContext.Items"/> para que <see cref="NetworkAccessAuditFilter"/> escriba UN registro
/// por petición (AC8). Los endpoints no conocen al writer: solo dicen qué recurso, qué hijos alcanzó
/// el resultado y con qué filtros (sin PII).
/// </summary>
/// <param name="Resource">Uno de <see cref="NetworkAccessVocabulary.Resources"/>.</param>
/// <param name="Result">Uno de <see cref="NetworkAccessVocabulary.Results"/>.</param>
/// <param name="ReachedTenantIds">Clientes presentes en el resultado (el filtro descarta a la cabeza y a quien no sea hijo del alcance).</param>
/// <param name="FiltersJson">Filtros aplicados serializados (solo identificadores/valores de filtro), o <c>null</c>.</param>
/// <param name="ProcedureId">Trámite consultado (detalle) o <c>null</c>.</param>
/// <param name="ProcedureTenantId">Dueño del trámite consultado (detalle) o <c>null</c>.</param>
/// <param name="AttachmentId">Documento descargado (HU #12410) o <c>null</c>.</param>
internal sealed record NetworkAccessOutcome(
    string Resource,
    string Result,
    IReadOnlyCollection<Guid> ReachedTenantIds,
    string? FiltersJson = null,
    Guid? ProcedureId = null,
    Guid? ProcedureTenantId = null,
    Guid? AttachmentId = null);

/// <summary>
/// Puente entre los endpoints de red y <see cref="NetworkAccessAuditFilter"/> (HU #12361): publica el
/// <see cref="NetworkAccessOutcome"/> de la petición y serializa los filtros SIN datos personales.
/// </summary>
internal static class NetworkAccessAuditContext
{
    internal const string ItemKey = "tramites.networkAccessOutcome";

    private static readonly JsonSerializerOptions FiltersJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Publica el desenlace; la última publicación de la petición es la que se registra.</summary>
    public static void Publish(HttpContext http, NetworkAccessOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(outcome);
        http.Items[ItemKey] = outcome;
    }

    public static NetworkAccessOutcome? Get(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items.TryGetValue(ItemKey, out var raw) && raw is NetworkAccessOutcome outcome ? outcome : null;
    }

    /// <summary>
    /// AC8 — filtros aplicados como JSON con lista blanca: cliente hijo, estados, modalidad, organismo,
    /// tipo, firmado, prioritario, rangos de fechas, orden y paginación. Los filtros de texto libre
    /// (placa, VIN, vendedor, comprador, gestor, búsqueda) y los VALORES de las condiciones de la
    /// gramática se registran solo por NOMBRE (<c>textFilters</c> / <c>conditionFields</c>): nunca su
    /// contenido, que puede ser un documento, un nombre o una placa.
    /// </summary>
    public static string? FiltersJson(ProcedureInstanceListRequest request, Guid? childTenantId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var textFilters = new List<string>(6);
        if (!string.IsNullOrWhiteSpace(request.Placa)) textFilters.Add("placa");
        if (!string.IsNullOrWhiteSpace(request.Vin)) textFilters.Add("vin");
        if (!string.IsNullOrWhiteSpace(request.Vendedor)) textFilters.Add("vendedor");
        if (!string.IsNullOrWhiteSpace(request.Comprador)) textFilters.Add("comprador");
        if (!string.IsNullOrWhiteSpace(request.Gestor)) textFilters.Add("gestor");
        if (!string.IsNullOrWhiteSpace(request.Busqueda)) textFilters.Add("busqueda");

        var payload = new
        {
            childTenantId,
            estados = request.Estados is { Count: > 0 } ? request.Estados : null,
            modalidad = NullIfBlank(request.Modalidad),
            organismoTransito = NullIfBlank(request.OrganismoTransito),
            tipoCodigo = NullIfBlank(request.TipoCodigo),
            firmado = request.Firmado,
            prioritario = request.Prioritario,
            createdFrom = request.CreatedFrom,
            createdTo = request.CreatedTo,
            updatedFrom = request.UpdatedFrom,
            updatedTo = request.UpdatedTo,
            sortBy = NullIfBlank(request.SortBy),
            sortDir = request.SortDescending ? "desc" : "asc",
            skip = request.Skip,
            take = request.Take,
            textFilters = textFilters.Count > 0 ? textFilters : null,
            conditionFields = request.Condiciones is { Count: > 0 }
                ? request.Condiciones.Select(c => c.FieldId).Distinct(StringComparer.Ordinal).ToList()
                : null,
        };

        return JsonSerializer.Serialize(payload, FiltersJsonOptions);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// HU #12361 (Feature #12257) — endpoint filter del grupo <c>/api/v1/tramites/network/**</c>: tras la
/// ruta, lee el <see cref="NetworkAccessOutcome"/> publicado por el endpoint y escribe UN registro en
/// <c>tramites.network_access_audit</c> (AC1/AC2/AC8) mediante <see cref="INetworkAccessAuditWriter"/>.
/// <list type="bullet">
///   <item>Solo audita a una cabeza de grupo (<c>TenantScope.IsGroup</c>): un SuperAdmin o un cliente
///   sin red que llegue aquí recibe 403 de <see cref="GroupHeadReadFilter"/> y no deja rastro (AC6).</item>
///   <item><c>reached_tenant_ids</c> = hijos del alcance presentes en el resultado; la cabeza se
///   descarta. Sin hijos alcanzados (resultado solo con datos propios, vacío, o sin desenlace publicado)
///   no se escribe nada (AC5).</item>
///   <item>Un rechazo por <c>childTenantId</c> fuera del alcance lo publica el endpoint con
///   <c>result = forbidden</c> y el hijo pedido: queda el intento (AC7 para descargas; el mecanismo
///   es el mismo).</item>
///   <item>Best-effort: cualquier fallo al auditar se registra como advertencia y la respuesta sigue.</item>
/// </list>
/// </summary>
internal sealed partial class NetworkAccessAuditFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var result = await next(context).ConfigureAwait(false);

        try
        {
            await AuditAsync(context.HttpContext).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var logger = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger<NetworkAccessAuditFilter>();
            if (logger is not null)
                AuditFailed(logger, context.HttpContext.Request.Path.ToString(), ex);
        }

        return result;
    }

    [LoggerMessage(
        EventId = 12362,
        Level = LogLevel.Warning,
        Message = "Fallo al auditar el acceso consolidado de {Path}; la respuesta no se altera (best-effort).")]
    private static partial void AuditFailed(ILogger logger, string path, Exception ex);

    private static async Task AuditAsync(HttpContext http)
    {
        var scope = RequestTenantResolver.ScopeFromItems(http);
        if (scope is null || !scope.IsGroup || scope.WriteTenantId is not { } head)
            return; // no es una cabeza de grupo: nada que auditar (AC6)

        var outcome = NetworkAccessAuditContext.Get(http);
        if (outcome is null)
            return;

        var reached = NetworkAccessAuditPolicy.ReachedChildren(scope, outcome.ReachedTenantIds, outcome.Result);
        if (reached.Count == 0)
            return; // AC5 — solo datos propios (o nada): no infla el registro

        var writer = http.RequestServices.GetService<INetworkAccessAuditWriter>();
        if (writer is null)
            return;

        await writer.WriteAsync(
            new NetworkAccessAuditEntry(
                ActorUserId: ResolveUserId(http.User),
                ActorTenantId: head,
                ReachedTenantIds: reached,
                Resource: outcome.Resource,
                FiltersJson: outcome.FiltersJson,
                ProcedureId: outcome.ProcedureId,
                ProcedureTenantId: outcome.ProcedureTenantId,
                AttachmentId: outcome.AttachmentId,
                Result: outcome.Result),
            http.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>Actor (claim <c>sub</c>); no toca el tenant — eso es de <see cref="RequestTenantResolver"/>.</summary>
    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
