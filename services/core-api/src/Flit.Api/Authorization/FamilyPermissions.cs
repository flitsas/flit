using Flit.Modules.Security.Domain.Roles;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Microsoft.Extensions.Options;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #13664 — exigencia de los permisos Ver/Crear por familia de trámite, detrás de
/// <see cref="ModulePermissionEnforcementOptions"/>. Misma regla que <see cref="PermissionAuthorizationHandler.Grants"/>
/// (bypass SuperAdmin, slug en el claim <c>permissions</c>). Los endpoints gRPC (ICT) no llevan permisos de usuario: se eximen
/// por su metadata de endpoint, nunca por el Content-Type.
/// </summary>
public sealed class HttpProcedureFamilyPermissionGate(
    IHttpContextAccessor accessor, IOptionsMonitor<ModulePermissionEnforcementOptions> options) : IProcedureFamilyPermissionGate
{
    private static readonly ProcedureFamily[] All = [ProcedureFamily.Matriculas, ProcedureFamily.Traspaso, ProcedureFamily.Otros];

    public bool IsActive => Active(out _);

    public bool CanCreate(ProcedureFamily family) => Check(FamilyPermissionSlugs.Create(family));

    public bool CanRead(ProcedureFamily family) => Check(FamilyPermissionSlugs.Read(family));

    public IReadOnlyList<string>? ReadableFamilyCodes()
    {
        if (!Active(out _))
            return null;
        var readable = All.Where(CanRead).Select(ProcedureFamilyCodes.ToCode).ToList();
        return readable.Count == All.Length ? null : readable;
    }

    private bool Check(string slug) => !Active(out var user) || PermissionAuthorizationHandler.Grants(user!, slug);

    private bool Active(out System.Security.Claims.ClaimsPrincipal? user)
    {
        var http = accessor.HttpContext;
        user = http?.User;
        return options.CurrentValue.Enabled
            && http is not null
            && user?.Identity?.IsAuthenticated == true
            // Solo se eximen los endpoints gRPC registrados (ICT): el Content-Type lo controla el cliente y no sirve de prueba.
            && http.GetEndpoint()?.Metadata.GetMetadata<global::Grpc.AspNetCore.Server.GrpcMethodMetadata>() is null;
    }
}

