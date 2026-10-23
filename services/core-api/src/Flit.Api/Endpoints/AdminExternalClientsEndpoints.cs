using Flit.Admin.Application.Integrations.Clients;
using Flit.Admin.Domain.Integrations;
using Flit.Api.Authorization;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13088 (Feature #13065, Épica #12737, ADR-0067) — administración de los clientes de integración
/// externos (<c>/api/v1/admin/external-clients</c>), exclusiva del SuperAdmin y sin pantalla en esta fase.
/// Patrón de <c>IctClientAdminEndpoints</c> (core-ict): el secreto lo genera el sistema y se devuelve UNA
/// sola vez en el alta y al regenerarlo; ninguna otra respuesta lleva el secreto ni sus hashes. Un pase
/// externo no es credencial de la plataforma: aquí recibe 401.
/// </summary>
public static class AdminExternalClientsEndpoints
{
    public sealed record CreateRequest(string? ClientId, string? DisplayName, string? Purpose, IReadOnlyList<string>? Scopes);

    public sealed record UpdateRequest(string? DisplayName, string? Purpose, IReadOnlyList<string>? Scopes, bool? IsActive, bool? MustRotate);

    public sealed record RegenerateRequest(bool? RevocarAnterior);

    public sealed record SecretResponse(ExternalClientView Client, string ClientSecret);

    public static IEndpointRouteBuilder MapAdminExternalClientsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/external-clients")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Clientes de integración externos");

        group.MapGet("", ListAsync)
            .WithName("AdminExternalClientList")
            .Produces<IReadOnlyList<ExternalClientView>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("AdminExternalClientGet")
            .Produces<ExternalClientView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("", CreateAsync)
            .WithName("AdminExternalClientCreate")
            .Produces<SecretResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("AdminExternalClientUpdate")
            .Produces<ExternalClientView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/regenerate-secret", RegenerateSecretAsync)
            .WithName("AdminExternalClientRegenerateSecret")
            .Produces<SecretResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/unlock", UnlockAsync)
            .WithName("AdminExternalClientUnlock")
            .Produces<ExternalClientView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(ListExternalClientsHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetAsync(Guid id, GetExternalClientHandler handler, CancellationToken cancellationToken)
    {
        var client = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);
        return client is null ? MapError(ExternalClientAdminErrors.NotFound) : Results.Ok(client);
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest body, CreateExternalClientHandler handler, HttpContext context, CancellationToken cancellationToken)
    {
        var (result, error) = await handler.HandleAsync(
            new CreateExternalClientCommand(body.ClientId, body.DisplayName, body.Purpose, body.Scopes,
                AdminCompaniesDomainEndpoints.ResolveUserId(context.User)),
            cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return MapError(error);
        }

        context.Response.Headers.CacheControl = "no-store";
        return Results.Created(
            $"/api/v1/admin/external-clients/{result!.Client.Id}", new SecretResponse(result.Client, result.ClientSecret));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateRequest body, UpdateExternalClientHandler handler, HttpContext context, CancellationToken cancellationToken)
    {
        var (result, error) = await handler.HandleAsync(
            new UpdateExternalClientCommand(id, body.DisplayName, body.Purpose, body.Scopes, body.IsActive, body.MustRotate,
                AdminCompaniesDomainEndpoints.ResolveUserId(context.User)),
            cancellationToken).ConfigureAwait(false);
        return error is null ? Results.Ok(result) : MapError(error);
    }

    private static async Task<IResult> RegenerateSecretAsync(
        Guid id, RegenerateRequest? body, RegenerateExternalClientSecretHandler handler, HttpContext context,
        CancellationToken cancellationToken)
    {
        var (result, error) = await handler.HandleAsync(
            id, body?.RevocarAnterior ?? false, AdminCompaniesDomainEndpoints.ResolveUserId(context.User), cancellationToken)
            .ConfigureAwait(false);

        if (error is not null)
        {
            return MapError(error);
        }

        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new SecretResponse(result!.Client, result.ClientSecret));
    }

    private static async Task<IResult> UnlockAsync(
        Guid id, UnlockExternalClientHandler handler, HttpContext context, CancellationToken cancellationToken)
    {
        var (result, error) = await handler.HandleAsync(
            id, AdminCompaniesDomainEndpoints.ResolveUserId(context.User), cancellationToken).ConfigureAwait(false);
        return error is null ? Results.Ok(result) : MapError(error);
    }

    private static IResult MapError(string error) => error switch
    {
        ExternalClientAdminErrors.NotFound => Results.Json(
            new { error, message = "El cliente de integración no existe." }, statusCode: StatusCodes.Status404NotFound),
        ExternalClientAdminErrors.ClientIdTaken => Results.Json(
            new { error, message = "El identificador ya existe o existió; los identificadores no se reutilizan." },
            statusCode: StatusCodes.Status409Conflict),
        ExternalClientAdminErrors.InvalidClientId => Results.Json(
            new { error, message = "Identificador inválido: minúsculas, dígitos y guiones, de 3 a 64 caracteres." },
            statusCode: StatusCodes.Status400BadRequest),
        ExternalClientAdminErrors.InvalidScopes => Results.Json(
            new { error, message = $"Permisos inválidos: al menos uno de {string.Join(", ", ExternalScopes.Todos)}." },
            statusCode: StatusCodes.Status400BadRequest),
        _ => Results.Json(new { error, message = "Nombre o finalidad vacíos o demasiado largos." },
            statusCode: StatusCodes.Status400BadRequest),
    };
}
