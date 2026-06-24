using System.Text.Json;
using Flit.Admin.Application.OtWebhooks.ProcessOtWebhookCallback;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>Endpoints públicos de integración OT (callbacks inbound RF04).</summary>
public static class OtIntegrationEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOtIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/integrations/ot")
            .WithTags("OT Integrations")
            .AllowAnonymous();

        group.MapPost("/webhooks/{subscriptionId:guid}/callback", ProcessWebhookCallbackAsync)
            .WithName("OtWebhookInboundCallback")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ProcessWebhookCallbackAsync(
        Guid subscriptionId,
        HttpContext httpContext,
        ProcessOtWebhookCallbackHandler handler,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(httpContext.Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        ProcessOtWebhookCallbackRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ProcessOtWebhookCallbackRequest>(rawBody, JsonOptions);
        }
        catch (JsonException)
        {
            return Results.Json(new { error = "INVALID_PAYLOAD" }, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (request is null)
        {
            return Results.Json(new { error = "INVALID_PAYLOAD" }, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var signature = httpContext.Request.Headers["X-Flit-Signature"].FirstOrDefault()
            ?? httpContext.Request.Headers["X-Signature"].FirstOrDefault();

        var result = await handler.HandleAsync(
            subscriptionId,
            rawBody,
            signature,
            request,
            cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            ProcessOtWebhookCallbackStatus.NotFound => Results.NotFound(new { error = "SUBSCRIPTION_NOT_FOUND" }),
            ProcessOtWebhookCallbackStatus.Inactive => Results.Json(
                new { error = "SUBSCRIPTION_INACTIVE" },
                statusCode: StatusCodes.Status403Forbidden),
            ProcessOtWebhookCallbackStatus.InvalidSignature => Results.Json(
                new { error = "INVALID_SIGNATURE" },
                statusCode: StatusCodes.Status401Unauthorized),
            ProcessOtWebhookCallbackStatus.InvalidPayload => Results.Json(
                new { error = "INVALID_PAYLOAD" },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            ProcessOtWebhookCallbackStatus.InvalidState => Results.Conflict(new { error = "INVALID_STATE" }),
            _ => Results.Ok(new { status = "processed" }),
        };
    }
}
