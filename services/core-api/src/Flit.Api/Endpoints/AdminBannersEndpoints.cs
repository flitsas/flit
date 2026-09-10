using System.Security.Claims;
using Flit.Admin.Application.Banners;
using Flit.Admin.Application.Banners.CreateBanner;
using Flit.Admin.Application.Banners.DeleteBanner;
using Flit.Admin.Application.Banners.ListBanners;
using Flit.Admin.Application.Banners.SetBannerActive;
using Flit.Admin.Application.Banners.UpdateBanner;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

public static class AdminBannersEndpoints
{
    public static IEndpointRouteBuilder MapAdminBannersEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/banners")
            .WithTags("Admin Banners");

        group.MapPost("/", CreateAsync)
            .RequirePermission("banners.manage")
            .WithName("AdminBannerCreate")
            .DisableAntiforgery();

        group.MapGet("/", ListAsync)
            .RequirePermission("banners.manage")
            .WithName("AdminBannerList");

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission("banners.manage")
            .WithName("AdminBannerUpdate")
            .DisableAntiforgery();

        group.MapPatch("/{id:guid}/active", SetActiveAsync)
            .RequirePermission("banners.manage")
            .WithName("AdminBannerSetActive");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission("banners.manage")
            .WithName("AdminBannerDelete");

        return app;
    }

    private static async Task<IResult> CreateAsync(
        [FromForm] string? name,
        [FromForm] string? linkUrl,
        [FromForm] DateTimeOffset? validFrom,
        [FromForm] DateTimeOffset? validUntil,
        IFormFile? file,
        HttpContext httpContext,
        [FromServices] CreateBannerHandler handler,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta la imagen.");
        }

        await using var stream = file.OpenReadStream();
        var command = new CreateBannerCommand
        {
            Name = name ?? string.Empty,
            LinkUrl = linkUrl,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            ImageFilename = file.FileName,
            ImageContentType = file.ContentType,
            ImageSizeBytes = file.Length,
            ImageContent = stream,
            CreatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(BuildLocation(result.Banner!.Id), result.Banner)
            : Results.Json(new ErrorResponse(result.Error!), statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListAsync(
        [FromServices] ListBannersHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] bool? includeDeleted = null)
    {
        var query = new ListBannersQuery
        {
            Page = page,
            PageSize = pageSize,
            IncludeDeleted = includeDeleted,
        };

        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromForm] string? name,
        [FromForm] string? linkUrl,
        [FromForm] DateTimeOffset? validFrom,
        [FromForm] DateTimeOffset? validUntil,
        IFormFile? file,
        HttpContext httpContext,
        [FromServices] UpdateBannerHandler handler,
        CancellationToken cancellationToken)
    {
        Stream? stream = null;
        try
        {
            if (file is not null && file.Length > 0)
            {
                stream = file.OpenReadStream();
            }

            var command = new UpdateBannerCommand
            {
                Id = id,
                Name = name ?? string.Empty,
                LinkUrl = linkUrl,
                ValidFrom = validFrom,
                ValidUntil = validUntil,
                ImageFilename = file?.FileName,
                ImageContentType = file?.ContentType,
                ImageSizeBytes = file?.Length,
                ImageContent = stream,
                UpdatedBy = ResolveUserId(httpContext.User),
            };

            var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

            return result.Outcome switch
            {
                UpdateBannerOutcome.Updated => Results.Ok(result.Banner),
                UpdateBannerOutcome.ValidationFailed => Results.Json(
                    new ErrorResponse(result.Error!), statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.NotFound(new ErrorResponse(NotFoundMessage(id))),
            };
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<IResult> SetActiveAsync(
        Guid id,
        [FromBody] SetBannerActiveRequest? body,
        HttpContext httpContext,
        [FromServices] SetBannerActiveHandler handler,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el cuerpo.");
        }

        var command = new SetBannerActiveCommand
        {
            Id = id,
            IsActive = body.IsActive,
            UpdatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome == SetBannerActiveOutcome.Updated
            ? Results.NoContent()
            : Results.NotFound(new ErrorResponse(NotFoundMessage(id)));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext httpContext,
        [FromServices] DeleteBannerHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] bool confirm = false)
    {
        var command = new DeleteBannerCommand
        {
            Id = id,
            Confirm = confirm,
            DeletedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            DeleteBannerOutcome.Deleted => Results.NoContent(),
            DeleteBannerOutcome.ConfirmationRequired => Results.Problem(
                statusCode: 400, title: "Bad Request",
                detail: "Se requiere confirmacion explicita antes de eliminar el banner."),
            _ => Results.NotFound(new ErrorResponse(NotFoundMessage(id))),
        };
    }

    private static string BuildLocation(Guid id) =>
        string.Concat("/api/v1/admin/banners/", id.ToString());

    private static string NotFoundMessage(Guid id) =>
        string.Concat("No existe el banner ", id.ToString());

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private sealed record ErrorResponse(string Error);

    private sealed record SetBannerActiveRequest(bool IsActive);
}
