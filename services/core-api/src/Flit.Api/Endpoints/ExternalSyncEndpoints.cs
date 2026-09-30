using System.Globalization;
using Flit.Admin.Domain.Integrations;
using Flit.Api.Authorization;
using Flit.Queries.Domain.Time;
using Flit.Tramites.Domain.ExternalSync;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13081 (Feature #13066, Épica #12737) — <c>GET /api/v1/external/tramites/sync</c> (contrato v3.1 §3):
/// recorrido del feed de trámites de todas las compañías por cursor opaco. Solo el pase externo con
/// <c>external.tramites.read</c>; sin <c>external.tramites.pii.read</c> los datos personales de los
/// compradores llegan enmascarados. La ruta no pasa por el middleware de tenant (no está en su lista):
/// la lectura entre compañías la acota el ámbito exclusivo del repositorio (HU #13076).
/// </summary>
public static class ExternalSyncEndpoints
{
    public const int DefaultPageSize = 200;
    public const int MaxPageSize = 1000;

    /// <summary>
    /// ISO-8601 con zona explícita. Sin zona, la fecha se tomaría en la hora local del servidor y el
    /// recorrido arrancaría corrido sin aviso; un formato regional (<c>09/30/2026</c>) tampoco se acepta.
    /// </summary>
    private static readonly string[] FormatosSince =
        ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];

    public static IEndpointRouteBuilder MapExternalSyncEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet($"{ExternalClientAuthorization.RoutePrefix}/tramites/sync", SyncAsync)
            .RequireAuthorization(ExternalClientAuthorization.TramitesReadPolicy)
            .WithTags("External")
            .WithName("ExternalTramitesSync");

        return app;
    }

    private static async Task<IResult> SyncAsync(
        HttpContext context,
        IProcedureSyncReadRepository repository,
        IConfiguration configuration,
        TimeProvider timeProvider,
        string? cursor,
        string? since,
        string? pageSize,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(cursor) && !string.IsNullOrEmpty(since))
        {
            return await Problem(context, "cursor_and_since_exclusive", "cursor y since no pueden ir juntos.").ConfigureAwait(false);
        }

        var size = DefaultPageSize;
        if (pageSize is not null
            && (!int.TryParse(pageSize, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > MaxPageSize))
        {
            return await Problem(context, "invalid_page_size", $"pageSize debe estar entre 1 y {MaxPageSize}.").ConfigureAwait(false);
        }

        ProcedureSyncPosition? after = null;
        DateTimeOffset? desde = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!ExternalSyncCursor.TryDecode(cursor, out after, out desde))
            {
                return await Problem(context, "invalid_cursor", "El cursor no es válido.").ConfigureAwait(false);
            }
        }
        else if (!string.IsNullOrEmpty(since))
        {
            if (!DateTimeOffset.TryParseExact(since, FormatosSince, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var s))
            {
                return await Problem(context, "invalid_since", "since debe ser ISO-8601 con zona (Z o ±hh:mm).").ConfigureAwait(false);
            }

            desde = s;
        }

        var lag = TimeSpan.FromSeconds(configuration.GetValue("ExternalClients:StabilityLagSeconds", 5));
        var entries = await repository.ReadItemsAsync(
            new ProcedureSyncPageRequest(after, desde, size + 1, lag), cancellationToken).ConfigureAwait(false);

        var hasMore = entries.Count > size;
        var page = hasMore ? entries.Take(size).ToList() : entries;
        var conPii = context.User.HasClaim(ExternalClientAuthorization.ScopeClaim, ExternalScopes.TramitesPiiRead);

        var nextCursor = page.Count > 0
            ? ExternalSyncCursor.Encode(page[^1].Position)
            : !string.IsNullOrEmpty(cursor) ? cursor
            : desde is { } d ? ExternalSyncCursor.EncodeSince(d)
            : ExternalSyncCursor.Encode(ExternalSyncCursor.Start);

        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new ExternalSyncPage(
            page.Select(e => conPii ? e.Item : ProcedureSyncPiiMasker.Mask(e.Item)).ToList(),
            nextCursor,
            hasMore,
            size,
            ColombiaTime.From(timeProvider.GetUtcNow())));
    }

    private static async Task<IResult> Problem(HttpContext context, string code, string detail)
    {
        await ExternalProblem.WriteAsync(context, StatusCodes.Status400BadRequest, code, detail, context.RequestAborted)
            .ConfigureAwait(false);
        return Results.Empty;
    }
}

/// <summary>Respuesta de la sincronización (contrato v3.1 §3).</summary>
public sealed record ExternalSyncPage(
    IReadOnlyList<ProcedureSyncItem> Items,
    string NextCursor,
    bool HasMore,
    int PageSize,
    DateTimeOffset ServerTime);
