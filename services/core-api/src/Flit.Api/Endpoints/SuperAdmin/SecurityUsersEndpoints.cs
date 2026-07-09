using System.Security.Claims;
using Flit.Modules.Security.Application.UserManagement.RestoreUser;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.SuperAdmin;

/// <summary>
/// HU #10623: gobernanza SuperAdmin sobre usuarios soft-deleted. Restaurar es EXCLUSIVO de
/// SuperAdmin (el grupo padre ya exige <c>SuperAdminPolicy</c> — ver
/// <c>SuperAdminEndpointExtensions</c>); eliminar vive en <c>SecurityEndpoints</c>/
/// <c>AdminOtEndpoints</c> junto al resto del ciclo de vida administrativo del usuario.
/// </summary>
internal static class SecurityUsersEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        // POST /api/v1/superadmin/users/{userId}/restore — deshace el soft-delete de un usuario
        // (HU #10623 AC3/AC5). No toca UserRoleAssignment ni UserTempSuspension: el usuario
        // recupera exactamente el mismo estado que tenía al momento de eliminarse.
        group.MapPost("/users/{userId:guid}/restore", async (
            Guid userId,
            ClaimsPrincipal caller,
            RestoreUserHandler handler,
            CancellationToken ct) =>
        {
            var subClaim = caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var callerId))
                return Results.Unauthorized();

            try
            {
                await handler.HandleAsync(new RestoreUserCommand(userId, callerId), ct);
                return Results.Ok();
            }
            catch (TargetUserNotFoundException)
            {
                return Results.NotFound(new { code = "USER_NOT_FOUND", message = "El usuario no existe." });
            }
            catch (UserNotDeletedException ex)
            {
                return Results.Json(
                    new { code = "USER_NOT_DELETED", message = ex.Message },
                    statusCode: StatusCodes.Status409Conflict);
            }
        }).WithName("RestoreUser");
    }
}
