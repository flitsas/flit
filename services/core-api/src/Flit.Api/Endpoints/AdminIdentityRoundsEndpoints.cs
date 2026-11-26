using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.IdentityRounds;
using Flit.Tramites.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Administración → Plataforma → Rondas de validación de identidad. Configuración GLOBAL (no por empresa): tope de rondas, disparador de
/// la alerta por correo y lista de destinatarios. Solo el Super Admin FLIT: el permiso <c>identity_rounds.manage</c> se siembra únicamente
/// para el rol SuperAdmin y es «solo SuperAdmin» (<c>PrivilegeCeiling.IsSuperAdminOnly</c>): ningún rol de empresa ni permiso por usuario
/// lo recibe. Las reglas (rango 1..10, disparador, hasta 20 correos válidos y sin repetir) viven en el dominio
/// (<see cref="IdentityRoundsConfig.Create"/>); aquí solo se traducen a 400.
/// </summary>
public static class AdminIdentityRoundsEndpoints
{
    public const string ManagePermission = "identity_rounds.manage";

    public static IEndpointRouteBuilder MapAdminIdentityRoundsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/platform/identity-rounds")
            .RequirePermission(ManagePermission)
            .WithTags("Admin · Plataforma · Rondas de identidad");

        group.MapGet("", GetAsync)
            .WithName("AdminIdentityRoundsGet")
            .WithSummary("Configuración global de rondas de validación de identidad (fila única)")
            .Produces<IdentityRoundsConfigResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("", PutAsync)
            .WithName("AdminIdentityRoundsPut")
            .WithSummary("Guarda la configuración global de rondas; audita quién, cuándo y los valores")
            .Produces<IdentityRoundsConfigResponse>(StatusCodes.Status200OK)
            .Produces<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetAsync([FromServices] GetIdentityRoundsConfigHandler handler, CancellationToken ct) =>
        Results.Ok(IdentityRoundsConfigResponse.From(await handler.HandleAsync(ct).ConfigureAwait(false)));

    private static async Task<IResult> PutAsync(
        [FromBody] SaveIdentityRoundsConfigRequest? request,
        ClaimsPrincipal user,
        [FromServices] UpdateIdentityRoundsConfigHandler handler,
        CancellationToken ct)
    {
        if (request is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["El cuerpo es obligatorio."] });

        try
        {
            var saved = await handler.HandleAsync(
                new UpdateIdentityRoundsConfigCommand(
                    request.RondasMaximas, request.DisparadorAlerta, request.CorreosAlerta, AdminRuntConfirmationEndpoints.ResolveUserId(user)),
                ct).ConfigureAwait(false);
            return Results.Ok(IdentityRoundsConfigResponse.From(saved));
        }
        catch (IdentityRoundsConfigException ex)
        {
            return Results.ValidationProblem(ex.Errors.ToDictionary(e => e.Key, e => e.Value));
        }
    }
}

/// <summary>Cuerpo del PUT. Todo se valida en el dominio; <c>correosAlerta</c> omitido equivale a lista vacía.</summary>
public sealed record SaveIdentityRoundsConfigRequest(int? RondasMaximas, string? DisparadorAlerta, string?[]? CorreosAlerta);

public sealed record IdentityRoundsConfigResponse(int RondasMaximas, string DisparadorAlerta, IReadOnlyList<string> CorreosAlerta)
{
    internal static IdentityRoundsConfigResponse From(IdentityRoundsConfigDto dto) =>
        new(dto.RondasMaximas, dto.DisparadorAlerta, dto.CorreosAlerta);
}
