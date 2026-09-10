using Flit.Admin.Application.Banners.GetBannerImage;
using Flit.Admin.Application.Banners.ListActiveBanners;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Public;

/// <summary>
/// Endpoints PUBLICOS de banners promocionales (HU #12240, Feature #12236). Sin auth ni tenant
/// header: el set es global (ADR-0058) y el contenido no es sensible (ADR-0057). Nunca se
/// expone <c>image_storage_path</c> ni una URL de S3/presigned — solo el <c>id</c> del banner,
/// con el que el frontend arma <c>/api/v1/public/banners/{id}/image</c>.
/// </summary>
internal static class PublicBannersEndpoints
{
    internal static IEndpointRouteBuilder MapPublicBannersEndpoints(this IEndpointRouteBuilder app)
    {
        // GET banners activos y vigentes (AC1) -> 200 { data: [...] }, lista vacia si no hay (AC3).
        app.MapGet("/api/v1/public/banners/active", async Task<IResult> (
            ListActiveBannersHandler handler,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(timeProvider.GetUtcNow(), ct);
            return Results.Ok(result);
        }).WithName("GetActivePublicBanners").AllowAnonymous();

        // GET imagen del banner por streaming (AC2): ETag = SHA-256, Cache-Control 24h, 304 sin
        // releer el binario si If-None-Match coincide. 404 si no existe o fue eliminado (AC3).
        app.MapGet("/api/v1/public/banners/{id:guid}/image", async Task<IResult> (
            Guid id,
            HttpRequest request,
            HttpResponse response,
            GetBannerImageHandler handler,
            CancellationToken ct) =>
        {
            var ifNoneMatchHeader = request.Headers.IfNoneMatch.ToString();
            var ifNoneMatch = string.IsNullOrWhiteSpace(ifNoneMatchHeader) ? null : ifNoneMatchHeader;

            var result = await handler.HandleAsync(id, ifNoneMatch, ct);
            if (!result.Found)
            {
                return Results.Problem(
                    statusCode: 404, title: "Not Found", detail: "Banner no encontrado.");
            }

            response.Headers.CacheControl = "public, max-age=86400";
            response.Headers.ETag = $"\"{result.Sha256}\"";

            return result.IsNotModified
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Stream(result.Content!, result.ContentType);
        }).WithName("GetPublicBannerImage").AllowAnonymous();

        return app;
    }
}
