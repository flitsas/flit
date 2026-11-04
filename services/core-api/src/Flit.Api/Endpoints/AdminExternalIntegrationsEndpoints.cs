using Flit.Admin.Application.Integrations.Clients;
using Flit.Admin.Domain.Integrations;
using Flit.Api.Authorization;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #14067 (Feature H, Épica #14021, ADR-0075) — integraciones externas ligadas a una compañía
/// (<c>/api/v1/admin/companies/{companyId}/external-integrations</c>), exclusivas del SuperAdmin (403 al resto). Reutiliza el
/// módulo de clientes externos (ADR-0067) con el tipo <c>persona_natural</c>: el alta devuelve el secreto UNA sola vez, la
/// rotación mantiene el anterior 24 h salvo <c>revocarAnterior</c>, y bloquear/desactivar corta la emisión de pases y queda
/// auditado. El alcance del pase es fijo por tipo (solo la fachada de Persona Natural). Ninguna respuesta lleva hashes.
/// </summary>
public static class AdminExternalIntegrationsEndpoints
{
    public sealed record CreateRequest(string? Tipo, string? DisplayName, string? Purpose);

    public sealed record RotateRequest(bool? RevocarAnterior);

    public static IEndpointRouteBuilder MapAdminExternalIntegrationsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{companyId:guid}/external-integrations")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Integraciones externas de la compañía");

        group.MapGet("", ListAsync)
            .WithName("AdminExternalIntegrationList")
            .Produces<IReadOnlyList<ExternalClientView>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("", CreateAsync)
            .WithName("AdminExternalIntegrationCreate")
            .Produces<AdminExternalClientsEndpoints.SecretResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/rotate-secret", RotateAsync)
            .WithName("AdminExternalIntegrationRotateSecret")
            .Produces<AdminExternalClientsEndpoints.SecretResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/block", BlockAsync)
            .WithName("AdminExternalIntegrationBlock")
            .Produces<ExternalClientView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/unblock", UnblockAsync)
            .WithName("AdminExternalIntegrationUnblock")
            .Produces<ExternalClientView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("AdminExternalIntegrationDeactivate")
            .Produces<ExternalClientView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(Guid companyId, ListExternalIntegrationsHandler handler, CancellationToken cancellationToken)
    {
        var list = await handler.HandleAsync(companyId, cancellationToken).ConfigureAwait(false);
        return list is null ? MapError(ExternalIntegrationAdminErrors.CompanyNotFound) : Results.Ok(list);
    }

    private static async Task<IResult> CreateAsync(
        Guid companyId, CreateRequest body, CreateExternalIntegrationHandler handler, HttpContext context,
        CancellationToken cancellationToken)
    {
        var (result, error) = await handler.HandleAsync(
            new CreateExternalIntegrationCommand(
                companyId, body.Tipo, body.DisplayName, body.Purpose, AdminCompaniesDomainEndpoints.ResolveUserId(context.User)),
            cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return MapError(error);
        }

        context.Response.Headers.CacheControl = "no-store";
        return Results.Created(
            $"/api/v1/admin/companies/{companyId}/external-integrations/{result!.Client.Id}",
            new AdminExternalClientsEndpoints.SecretResponse(result.Client, result.ClientSecret));
    }

    private static async Task<IResult> RotateAsync(
        Guid companyId, Guid id, RotateRequest? body, RotateExternalIntegrationSecretHandler handler, HttpContext context,
        CancellationToken cancellationToken)
    {
        var (result, error) = await handler.HandleAsync(
            companyId, id, body?.RevocarAnterior ?? false, AdminCompaniesDomainEndpoints.ResolveUserId(context.User), cancellationToken)
            .ConfigureAwait(false);

        if (error is not null)
        {
            return MapError(error);
        }

        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new AdminExternalClientsEndpoints.SecretResponse(result!.Client, result.ClientSecret));
    }

    private static Task<IResult> BlockAsync(
        Guid companyId, Guid id, ChangeExternalIntegrationStateHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        ChangeAsync(companyId, id, ExternalIntegrationAction.Block, handler, context, cancellationToken);

    private static Task<IResult> UnblockAsync(
        Guid companyId, Guid id, ChangeExternalIntegrationStateHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        ChangeAsync(companyId, id, ExternalIntegrationAction.Unblock, handler, context, cancellationToken);

    private static Task<IResult> DeactivateAsync(
        Guid companyId, Guid id, ChangeExternalIntegrationStateHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        ChangeAsync(companyId, id, ExternalIntegrationAction.Deactivate, handler, context, cancellationToken);

    private static async Task<IResult> ChangeAsync(
        Guid companyId, Guid id, ExternalIntegrationAction action, ChangeExternalIntegrationStateHandler handler,
        HttpContext context, CancellationToken cancellationToken)
    {
        var (result, error) = await handler.HandleAsync(
            companyId, id, action, AdminCompaniesDomainEndpoints.ResolveUserId(context.User), cancellationToken).ConfigureAwait(false);
        return error is null ? Results.Ok(result) : MapError(error);
    }

    private static IResult MapError(string error) => error switch
    {
        ExternalIntegrationAdminErrors.CompanyNotFound => Results.Json(
            new { error, message = "La compañía no existe." }, statusCode: StatusCodes.Status404NotFound),
        ExternalIntegrationAdminErrors.NotFound => Results.Json(
            new { error, message = "La integración no existe en esta compañía." }, statusCode: StatusCodes.Status404NotFound),
        ExternalIntegrationAdminErrors.AlreadyActive => Results.Json(
            new { error, message = "La compañía ya tiene una integración activa de este tipo. Desactívela para crear otra." },
            statusCode: StatusCodes.Status409Conflict),
        ExternalIntegrationAdminErrors.Inactive => Results.Json(
            new { error, message = "La integración está desactivada; cree una nueva." }, statusCode: StatusCodes.Status409Conflict),
        ExternalIntegrationAdminErrors.Blocked => Results.Json(
            new { error, message = "La integración está bloqueada; desbloquéela antes de rotar el secreto." },
            statusCode: StatusCodes.Status409Conflict),
        ExternalIntegrationAdminErrors.InvalidType => Results.Json(
            new { error, message = $"Tipo inválido: use uno de {string.Join(", ", ExternalIntegrationTypes.PorCompania)}." },
            statusCode: StatusCodes.Status400BadRequest),
        ExternalClientAdminErrors.ClientIdTaken => Results.Json(
            new { error, message = "No se pudo generar un identificador único; intente de nuevo." },
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(new { error, message = "Nombre o finalidad vacíos o demasiado largos." },
            statusCode: StatusCodes.Status400BadRequest),
    };
}
