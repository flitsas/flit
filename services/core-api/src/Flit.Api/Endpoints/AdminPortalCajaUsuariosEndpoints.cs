using System.Globalization;
using Flit.Api.Authorization;
using Flit.Api.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.PersonaNatural;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Auth.ResendInvitation;
using Flit.Modules.Security.Application.Products;
using Flit.Modules.Security.Application.UserManagement.SuspendUser;
using Flit.Modules.Security.Application.UserManagement.UnsuspendUser;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Modules.Security.Domain.UserRoles;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #14454 / #14455 (Feature #14453, Épica #14210, ADR-0082) — «Usuarios de caja» del organismo desde el hub del OT.
/// <c>/api/v1/admin/ot/portal-caja/users</c>: lista, invita (rol fijo <c>ot_caja</c>), desactiva, reactiva y reenvía la invitación.
/// Política <see cref="AdminAuthorization.OtAdminOrSuperAdminPolicy"/>; el <c>ot_admin</c> opera sobre el organismo de su token (ajeno
/// da 403) y el SuperAdmin envía <c>?transitOfficeId=</c>. Reutiliza los handlers de invitación y suspensión de usuarios OT. Errores
/// <c>{ code, message }</c>. Responde 404 mientras <c>PortalCaja:Habilitado</c> esté apagado.
/// </summary>
public static class AdminPortalCajaUsuariosEndpoints
{
    public const string BasePath = "/api/v1/admin/ot/portal-caja";

    private const string MotivoDesactivacion = "Desactivado desde Usuarios de caja.";

    public sealed record InvitarCajaRequest(string? Email, string? FullName);

    public static IEndpointRouteBuilder MapAdminPortalCajaUsuariosEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup(BasePath)
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .AddEndpointFilter(PortalCajaHabilitadoFilter.Instance)
            .WithTags("Admin · OT · Caja del portal");

        group.MapGet("/users", ListarAsync)
            .WithName("AdminOtPortalCajaUsersList")
            .WithSummary("Usuarios de caja del organismo: activos, desactivados e invitaciones pendientes")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound);

        group.MapPost("/users", InvitarAsync)
            .WithName("AdminOtPortalCajaUsersInvite")
            .WithSummary("Invita a un usuario de caja (rol ot_caja) por correo; la contraseña la define al activar")
            .Produces(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status409Conflict);

        group.MapPost("/users/{userId:guid}/suspend", DesactivarAsync)
            .WithName("AdminOtPortalCajaUsersSuspend")
            .WithSummary("Desactiva a un usuario de caja: pierde el acceso en su siguiente petición")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/users/{userId:guid}/suspend", ReactivarAsync)
            .WithName("AdminOtPortalCajaUsersUnsuspend")
            .WithSummary("Reactiva a un usuario de caja desactivado")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        group.MapPost("/invitations/{invitationId:guid}/resend", ReenviarAsync)
            .WithName("AdminOtPortalCajaInvitationResend")
            .WithSummary("Reenvía la invitación pendiente de un usuario de caja")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<IResult> ListarAsync(
        HttpContext http, FlitDbContext db, CancellationToken ct, [FromQuery] Guid? transitOfficeId = null)
    {
        var (alcance, error) = await AlcancePortalCaja.ResolverAsync(http.User, transitOfficeId, db, ct).ConfigureAwait(false);
        if (error is not null)
            return error;

        var filas = await CajaUsuarios.ListarAsync(db, alcance!.TenantId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return Results.Ok(filas
            .OrderBy(f => f.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(f => new
            {
                userId = f.UserId,
                invitationId = f.InvitationId,
                fullName = f.FullName,
                email = f.Email,
                status = f.Status,
                createdAt = f.CreatedAt,
            }));
    }

    private static async Task<IResult> InvitarAsync(
        InvitarCajaRequest? body, HttpContext http, FlitDbContext db, CreateInvitationHandler invitar, SetTenantProductEnabledHandler productos,
        CancellationToken ct, [FromQuery] Guid? transitOfficeId = null)
    {
        var (alcance, error) = await AlcancePortalCaja.ResolverAsync(http.User, transitOfficeId, db, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (AdminCompaniesDomainEndpoints.ResolveUserId(http.User) is not { } actor)
            return AlcancePortalCaja.Error(StatusCodes.Status401Unauthorized, "USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");

        var correo = body?.Email?.Trim();
        var nombre = body?.FullName?.Trim();
        if (string.IsNullOrEmpty(correo) || !correo.Contains('@', StringComparison.Ordinal))
            return AlcancePortalCaja.Error(StatusCodes.Status400BadRequest, "EMAIL_REQUIRED", "Escriba un correo válido.");
        if (string.IsNullOrEmpty(nombre))
            return AlcancePortalCaja.Error(StatusCodes.Status400BadRequest, "FULL_NAME_REQUIRED", "Escriba el nombre de la persona.");

        if (await CajaUsuarios.RolCajaIdAsync(db, ct).ConfigureAwait(false) is not { } rolId)
            return AlcancePortalCaja.Error(StatusCodes.Status409Conflict, "CAJA_NO_CONFIGURADA", "El rol de caja no está disponible en este ambiente.");

        try
        {
            // El producto «caja» tiene que estar encendido en el organismo para que el usuario pueda abrir sesión. Es idempotente.
            await productos.HandleAsync(new SetTenantProductEnabledCommand(alcance!.TenantId, ProductCodes.Caja, true, null), actor, ct).ConfigureAwait(false);
        }
        catch (TenantProductException ex)
        {
            return AlcancePortalCaja.Error(StatusCodes.Status409Conflict, ex.Code, "No se pudo habilitar la caja para este organismo: " + ex.Message);
        }

        try
        {
            var r = await invitar.HandleAsync(new CreateInvitationCommand(alcance.TenantId, correo, nombre, [rolId], actor), ct).ConfigureAwait(false);
            return Results.Created($"{BasePath}/users", new { invitationId = r.InvitationId, email = r.Email, emailSent = r.EmailSent });
        }
        catch (Exception ex) when (ex is InvitationAlreadyPendingException or UserAlreadyExistsException)
        {
            return AlcancePortalCaja.Error(StatusCodes.Status409Conflict, UserEmailConflictMessages.EmailAlreadyInUseCode, UserEmailConflictMessages.EmailAlreadyInUse);
        }
        catch (RoleNotFoundException)
        {
            return AlcancePortalCaja.Error(StatusCodes.Status409Conflict, "CAJA_NO_CONFIGURADA", "El rol de caja no está disponible en este ambiente.");
        }
    }

    private static async Task<IResult> DesactivarAsync(
        Guid userId, HttpContext http, FlitDbContext db, SuspendUserHandler handler, CancellationToken ct, [FromQuery] Guid? transitOfficeId = null)
    {
        var (alcance, error) = await AlcancePortalCaja.ResolverAsync(http.User, transitOfficeId, db, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (!await CajaUsuarios.EsDeCajaAsync(db, alcance!.TenantId, userId, ct).ConfigureAwait(false))
            return AlcancePortalCaja.Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "El usuario de caja no existe en este organismo.");

        var actor = AdminCompaniesDomainEndpoints.ResolveUserId(http.User) ?? Guid.Empty;
        try
        {
            await handler.HandleAsync(
                new SuspendUserCommand(alcance.TenantId, userId, MotivoDesactivacion, null, actor, http.User.IsInRole(AdminAuthorization.SuperAdminRole)), ct)
                .ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (TargetUserNotFoundException)
        {
            return AlcancePortalCaja.Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "El usuario de caja no existe en este organismo.");
        }
        catch (UserOutOfScopeException)
        {
            return AlcancePortalCaja.Error(StatusCodes.Status403Forbidden, "FORBIDDEN_SCOPE", "No tiene ámbito sobre este usuario.");
        }
        catch (SelfSuspensionException)
        {
            return AlcancePortalCaja.Error(StatusCodes.Status400BadRequest, "SELF_SUSPEND", "No puede desactivarse a sí mismo.");
        }
    }

    private static async Task<IResult> ReactivarAsync(
        Guid userId, HttpContext http, FlitDbContext db, UnsuspendUserHandler handler, CancellationToken ct, [FromQuery] Guid? transitOfficeId = null)
    {
        var (alcance, error) = await AlcancePortalCaja.ResolverAsync(http.User, transitOfficeId, db, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (!await CajaUsuarios.EsDeCajaAsync(db, alcance!.TenantId, userId, ct).ConfigureAwait(false))
            return AlcancePortalCaja.Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "El usuario de caja no existe en este organismo.");

        var actor = AdminCompaniesDomainEndpoints.ResolveUserId(http.User) ?? Guid.Empty;
        try
        {
            await handler.HandleAsync(
                new UnsuspendUserCommand(alcance.TenantId, userId, actor, http.User.IsInRole(AdminAuthorization.SuperAdminRole)), ct)
                .ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (TargetUserNotFoundException)
        {
            return AlcancePortalCaja.Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "El usuario de caja no existe en este organismo.");
        }
        catch (UserOutOfScopeException)
        {
            return AlcancePortalCaja.Error(StatusCodes.Status403Forbidden, "FORBIDDEN_SCOPE", "No tiene ámbito sobre este usuario.");
        }
        catch (NoActiveSuspensionException)
        {
            return AlcancePortalCaja.Error(StatusCodes.Status404NotFound, "NO_ACTIVE_SUSPENSION", "El usuario no está desactivado.");
        }
    }

    private static async Task<IResult> ReenviarAsync(
        Guid invitationId, HttpContext http, FlitDbContext db, ResendInvitationHandler handler, CancellationToken ct, [FromQuery] Guid? transitOfficeId = null)
    {
        var (alcance, error) = await AlcancePortalCaja.ResolverAsync(http.User, transitOfficeId, db, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (AdminCompaniesDomainEndpoints.ResolveUserId(http.User) is not { } actor)
            return AlcancePortalCaja.Error(StatusCodes.Status401Unauthorized, "USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");
        if (!await CajaUsuarios.InvitacionEsDeCajaAsync(db, alcance!.TenantId, invitationId, ct).ConfigureAwait(false))
            return AlcancePortalCaja.Error(StatusCodes.Status404NotFound, "INVITATION_NOT_FOUND", "La invitación no existe en este organismo.");

        try
        {
            await handler.HandleAsync(new ResendInvitationCommand(invitationId, alcance.TenantId, actor), ct).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (InvitationNotFoundException)
        {
            return AlcancePortalCaja.Error(StatusCodes.Status404NotFound, "INVITATION_NOT_FOUND", "La invitación no existe en este organismo.");
        }
        catch (InvitationNotPendingException)
        {
            return AlcancePortalCaja.Error(StatusCodes.Status409Conflict, "INVITATION_NOT_PENDING", "La invitación ya no está pendiente (fue aceptada o cancelada).");
        }
        catch (ResendCooldownActiveException ex)
        {
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(ex.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return AlcancePortalCaja.Error(StatusCodes.Status429TooManyRequests, "RESEND_COOLDOWN", "Espere un momento antes de reenviar de nuevo la invitación.");
        }
    }
}

/// <summary>Responde 404 a las rutas de la caja del portal mientras <c>PortalCaja:Habilitado</c> esté apagado (apagado por defecto).</summary>
internal sealed class PortalCajaHabilitadoFilter : IEndpointFilter
{
    public static readonly PortalCajaHabilitadoFilter Instance = new();

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        return CajaAdminExtensions.Habilitado(configuration)
            ? next(context)
            : ValueTask.FromResult<object?>(Results.NotFound());
    }
}
