using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Flit.Api.Usuarios;
using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Api.Grpc;

/// <summary>
/// Adaptador gRPC de <c>flit.platform.v1.UsuariosProductoService</c> (ADR-0089, HU #14587). Delgado: verifica el llamante
/// y el producto (<see cref="DiagnosticoUsuariosServicioAuthorization.VerificarLlamante"/>), reverifica al actor en la base
/// (<see cref="IActorUsuariosProducto"/>), delega en los casos de uso por producto, audita cada escritura en
/// <c>admin.audit</c> (actor, empresa, usuario objetivo, rol antes y después, resultado y causa; sin correo, SP-16) y traduce
/// los rechazos a estados gRPC con <c>ErrorInfo</c> (contrato §3.4). Logs: solo ids y códigos.
/// </summary>
internal sealed partial class UsuariosProductoGrpcService(
    IActorUsuariosProducto actores,
    ListProductUsersHandler listar,
    InviteProductUserHandler invitar,
    AssignProductRoleHandler cambiarRol,
    SuspendProductRoleHandler suspender,
    ReactivateProductRoleHandler reactivar,
    IAdminAuditWriter auditoria,
    ILogger<UsuariosProductoGrpcService> logger)
    : UsuariosProductoService.UsuariosProductoServiceBase
{
    internal const string EntidadAuditada = "user_product_role";

    public override async Task<ListarUsuariosProductoResponse> ListarUsuariosProducto(ListarUsuariosProductoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var (tenant, actor) = await AutorizarAsync(request.Producto, context).ConfigureAwait(false);
        // CR-7: un valor que el enum no conoce (cliente con otra versión del proto) es un filtro inválido, no «suspendido».
        if (request.Estados.Any(e => e == EstadoUsuarioProducto.Unspecified || !Enum.IsDefined(e)))
            throw Rechazo(ProductRoleErrorCodes.FiltroInvalido, ProductRoleErrorKind.InvalidArgument, "Estado desconocido en el filtro.");

        var query = new ListProductUsersQuery(tenant, actor, request.Producto, request.Busqueda, [.. request.Roles],
            [.. request.Estados.Select(ToState)], request.Pagina, request.Tamano);
        var page = await Ejecutar(() => listar.HandleAsync(query, context.CancellationToken)).ConfigureAwait(false);

        var response = new ListarUsuariosProductoResponse { Total = page.Total };
        response.Usuarios.AddRange(page.Users.Select(ToProto));
        response.RolesAsignables.AddRange(page.AssignableRoles);
        return response;
    }

    public override async Task<InvitarUsuarioProductoResponse> InvitarUsuarioProducto(InvitarUsuarioProductoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var (tenant, actor) = await AutorizarAsync(request.Producto, context, AuditVocabulary.Operations.Invite).ConfigureAwait(false);
        var nuevo = Json(request.Producto, request.RolCodigo);
        try
        {
            var result = await invitar.HandleAsync(
                new InviteProductUserCommand(tenant, actor, request.Producto, request.Email, request.Nombre, request.RolCodigo),
                context.CancellationToken).ConfigureAwait(false);
            var causa = Causa(result.Outcome);
            await AuditarAsync(tenant, actor.UserId, AuditVocabulary.Operations.Invite, AuditVocabulary.Results.Success, causa,
                result.Outcome == InviteOutcome.InvitationCreated ? "invitation" : "user", result.TargetId, null, nuevo, context).ConfigureAwait(false);
            UsuariosProductoLog.Invitacion(logger, tenant, actor.UserId, causa, result.EmailSent);
            return new InvitarUsuarioProductoResponse();
        }
        catch (ProductRoleRuleException ex)
        {
            await AuditarAsync(tenant, actor.UserId, AuditVocabulary.Operations.Invite, AuditVocabulary.Results.Failure, ex.Code, "user", null, null, nuevo, context).ConfigureAwait(false);
            throw Traducir(ex);
        }
    }

    public override async Task<CambiarRolProductoResponse> CambiarRolProducto(CambiarRolProductoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var (tenant, actor) = await AutorizarAsync(request.Producto, context, AuditVocabulary.Operations.AssignRole, Posible(request.UsuarioId)).ConfigureAwait(false);
        var usuario = Usuario(request.UsuarioId);
        var outcome = await EscribirAsync(tenant, actor, usuario, request.Producto, AuditVocabulary.Operations.AssignRole, request.RolCodigo, context,
            () => cambiarRol.HandleAsync(new ChangeProductRoleCommand(tenant, actor, request.Producto, usuario, request.RolCodigo), context.CancellationToken)).ConfigureAwait(false);
        return new CambiarRolProductoResponse { Usuario = ToProto(outcome.Row) };
    }

    public override async Task<SuspenderEnProductoResponse> SuspenderEnProducto(SuspenderEnProductoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var (tenant, actor) = await AutorizarAsync(request.Producto, context, AuditVocabulary.Operations.Suspend, Posible(request.UsuarioId)).ConfigureAwait(false);
        var usuario = Usuario(request.UsuarioId);
        var outcome = await EscribirAsync(tenant, actor, usuario, request.Producto, AuditVocabulary.Operations.Suspend, null, context,
            () => suspender.HandleAsync(new ProductUserCommand(tenant, actor, request.Producto, usuario), context.CancellationToken)).ConfigureAwait(false);
        return new SuspenderEnProductoResponse { Usuario = ToProto(outcome.Row) };
    }

    public override async Task<ReactivarEnProductoResponse> ReactivarEnProducto(ReactivarEnProductoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var (tenant, actor) = await AutorizarAsync(request.Producto, context, AuditVocabulary.Operations.Unsuspend, Posible(request.UsuarioId)).ConfigureAwait(false);
        var usuario = Usuario(request.UsuarioId);
        var outcome = await EscribirAsync(tenant, actor, usuario, request.Producto, AuditVocabulary.Operations.Unsuspend, null, context,
            () => reactivar.HandleAsync(new ProductUserCommand(tenant, actor, request.Producto, usuario), context.CancellationToken)).ConfigureAwait(false);
        return new ReactivarEnProductoResponse { Usuario = ToProto(outcome.Row) };
    }

    private async Task<ProductRoleOutcome> EscribirAsync(
        Guid tenant, ProductActor actor, Guid usuario, string producto, string operacion, string? rolPedido, ServerCallContext context,
        Func<Task<ProductRoleOutcome>> accion)
    {
        try
        {
            var outcome = await accion().ConfigureAwait(false);
            await AuditarAsync(tenant, actor.UserId, operacion, AuditVocabulary.Results.Success, outcome.Changed ? null : "SIN_CAMBIO", "user", usuario,
                Json(producto, outcome.RoleBefore), Json(producto, outcome.RoleAfter), context).ConfigureAwait(false);
            UsuariosProductoLog.Escritura(logger, operacion, tenant, actor.UserId, usuario, outcome.Changed ? "ok" : "sin_cambio");
            return outcome;
        }
        catch (ProductRoleRuleException ex)
        {
            await AuditarAsync(tenant, actor.UserId, operacion, AuditVocabulary.Results.Failure, ex.Code, "user", usuario, null,
                rolPedido is null ? null : Json(producto, rolPedido), context).ConfigureAwait(false);
            UsuariosProductoLog.Escritura(logger, operacion, tenant, actor.UserId, usuario, ex.Code);
            throw Traducir(ex);
        }
    }

    /// <summary>Llamante + producto + actor reverificado. Un actor sin permiso también deja rastro si la operación escribe.</summary>
    private async Task<(Guid Tenant, ProductActor Actor)> AutorizarAsync(string producto, ServerCallContext context, string? operacion = null, Guid? usuario = null)
    {
        var caller = PlatformServiceCaller.From(context);
        var actorId = DiagnosticoUsuariosServicioAuthorization.VerificarLlamante(caller, producto, context.RequestHeaders);
        var actor = await actores.VerificarAsync(caller.TenantId, actorId, producto, context.CancellationToken).ConfigureAwait(false);
        if (actor is { IsSuperAdmin: true }
            && !await actores.EmpresaHabilitadaAsync(caller.TenantId, producto, context.CancellationToken).ConfigureAwait(false))
        {
            // CR-2 / D-7: el SuperAdmin elige la empresa; si no existe o no tiene el producto, NOT_FOUND uniforme.
            throw Rechazo(ProductRoleErrorCodes.EmpresaNoEncontrada, ProductRoleErrorKind.NotFound, "Empresa no encontrada.");
        }

        if (actor is not null)
            return (caller.TenantId, actor);

        if (operacion is not null)
            await AuditarAsync(caller.TenantId, actorId, operacion, AuditVocabulary.Results.Failure, "ACTOR_NO_AUTORIZADO", "user", usuario, null, null, context).ConfigureAwait(false);
        UsuariosProductoLog.ActorRechazado(logger, caller.TenantId, actorId);
        throw new RpcException(new Status(StatusCode.PermissionDenied, "El usuario no puede gestionar los usuarios del producto en esta empresa."));
    }

    private Task AuditarAsync(Guid tenant, Guid actor, string operacion, string resultado, string? causa, string tipoObjetivo, Guid? objetivo,
        string? antes, string? despues, ServerCallContext context) =>
        auditoria.WriteAsync(new AdminAuditEntry(
            TenantId: tenant,
            TenantType: null,
            Module: AuditVocabulary.Modules.Users,
            EntityName: EntidadAuditada,
            Operation: operacion,
            Result: resultado,
            ErrorCode: causa,
            ActorUserId: actor,
            TargetEntityType: tipoObjetivo,
            TargetEntityId: objetivo,
            ClientIp: null,
            UserAgent: DiagnosticoUsuariosServicioAuthorization.ClientId,
            OldValue: antes,
            NewValue: despues), context.CancellationToken);

    private static async Task<T> Ejecutar<T>(Func<Task<T>> accion)
    {
        try
        {
            return await accion().ConfigureAwait(false);
        }
        catch (ProductRoleRuleException ex)
        {
            throw Traducir(ex);
        }
    }

    internal static RpcException Traducir(ProductRoleRuleException ex) => Rechazo(ex.Code, ex.Kind, ex.Message);

    private static RpcException Rechazo(string code, ProductRoleErrorKind kind, string message) =>
        PlatformRpcErrors.ToRpcException(code, message, kind switch
        {
            ProductRoleErrorKind.PermissionDenied => StatusCode.PermissionDenied,
            ProductRoleErrorKind.NotFound => StatusCode.NotFound,
            ProductRoleErrorKind.FailedPrecondition => StatusCode.FailedPrecondition,
            _ => StatusCode.InvalidArgument,
        });

    /// <summary>Un id que no es uuid no puede ser de la empresa: NOT_FOUND, igual que un id ajeno (SP-13).</summary>
    private static Guid Usuario(string? id) =>
        Guid.TryParse(id, out var usuario) && usuario != Guid.Empty
            ? usuario
            : throw Rechazo(ProductRoleErrorCodes.UsuarioNoEncontrado, ProductRoleErrorKind.NotFound, "Usuario no encontrado.");

    private static Guid? Posible(string? id) => Guid.TryParse(id, out var usuario) ? usuario : null;

    private static string? Json(string producto, string? rol) =>
        rol is null ? null : JsonSerializer.Serialize(new { producto, rol });

    internal static string Causa(InviteOutcome outcome) => outcome switch
    {
        InviteOutcome.InvitationCreated => "INVITACION_CREADA",
        InviteOutcome.RoleAssigned => "ROL_ASIGNADO",
        InviteOutcome.AlreadyWithRole => "YA_CON_ROL",
        InviteOutcome.PendingInvitation => "INVITACION_PENDIENTE",
        InviteOutcome.ProtectedTarget => ProductRoleErrorCodes.ObjetivoProtegido,
        _ => "OTRA_COMPANIA",
    };

    internal static UsuarioProducto ToProto(ProductUserRow row)
    {
        var proto = new UsuarioProducto
        {
            Id = row.Id.ToString(),
            Tipo = row.IsInvitation ? TipoFila.Invitacion : TipoFila.Usuario,
            Nombre = row.Name,
            Email = row.Email,
            RolCodigo = row.RoleCode,
            Estado = row.State switch
            {
                ProductUserState.Active => EstadoUsuarioProducto.Activo,
                ProductUserState.Invited => EstadoUsuarioProducto.Invitado,
                _ => EstadoUsuarioProducto.Suspendido,
            },
            MotivoSinAcciones = row.Reason switch
            {
                NoActionsReason.Self => MotivoSinAcciones.UstedMismo,
                NoActionsReason.AdminCompany => MotivoSinAcciones.AdminCompania,
                NoActionsReason.SuperAdminOnly => MotivoSinAcciones.SoloSuperadmin,
                NoActionsReason.Invitation => MotivoSinAcciones.Invitacion,
                NoActionsReason.AccountInactive => MotivoSinAcciones.CuentaInactiva,
                _ => MotivoSinAcciones.Unspecified,
            },
        };
        proto.Acciones.AddRange(row.Actions.Select(a => a switch
        {
            ProductUserAction.ChangeRole => AccionUsuario.CambiarRol,
            ProductUserAction.Suspend => AccionUsuario.Suspender,
            _ => AccionUsuario.Reactivar,
        }));
        return proto;
    }

    private static ProductUserState ToState(EstadoUsuarioProducto estado) => estado switch
    {
        EstadoUsuarioProducto.Activo => ProductUserState.Active,
        EstadoUsuarioProducto.Invitado => ProductUserState.Invited,
        _ => ProductUserState.Suspended,
    };
}

internal static partial class UsuariosProductoLog
{
    [LoggerMessage(EventId = 89001, Level = LogLevel.Information, Message = "Usuarios de producto: {Operacion} por {Actor} en la empresa {TenantId} sobre {Usuario}: {Resultado}.")]
    public static partial void Escritura(ILogger logger, string operacion, Guid tenantId, Guid actor, Guid usuario, string resultado);

    [LoggerMessage(EventId = 89002, Level = LogLevel.Information, Message = "Usuarios de producto: invitación de {Actor} en la empresa {TenantId} resuelta como {Causa} (correo enviado: {Enviado}).")]
    public static partial void Invitacion(ILogger logger, Guid tenantId, Guid actor, string causa, bool enviado);

    [LoggerMessage(EventId = 89003, Level = LogLevel.Warning, Message = "Usuarios de producto: actor {Actor} rechazado en la empresa {TenantId} (sin admin del producto con permiso, suspendido o inactivo).")]
    public static partial void ActorRechazado(ILogger logger, Guid tenantId, Guid actor);
}
