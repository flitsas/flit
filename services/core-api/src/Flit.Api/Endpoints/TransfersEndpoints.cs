using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Domain.Companies.VehicleOwnership;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoint <em>stub</em> de radicación de traspaso (HU #10191, RF04). El módulo de
/// traspasos completo está fuera de alcance; este endpoint mínimo cablea el
/// interceptor <see cref="IVehicleOwnershipGuard"/> en el pipeline HTTP para validar
/// AC1 (bloqueo 422) y AC2 (exención por whitelist) de extremo a extremo.
///
/// A diferencia del Administrador de Compañías, exige solo usuario autenticado (rol
/// operador), no SuperAdmin: la radicación la ejecuta el usuario del tenant.
///
/// La política efectiva se recibe ya resuelta en el payload
/// (<c>onlyOwnVehicles</c> + <c>isInFlight</c>): para un trámite in-flight el llamador
/// debe pasar el snapshot capturado al crearlo (AC3); para uno nuevo, la política live.
/// La regla de snapshot vive en <see cref="ITenantPolicyResolver"/> y se prueba a nivel
/// de dominio/aplicación.
/// </summary>
public static class TransfersEndpoints
{
    public static IEndpointRouteBuilder MapTransfersEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/internal/transfers")
            .RequireAuthorization();

        group.MapPost("/start", StartTransferAsync)
            .WithName("InternalTransfersStart");

        return app;
    }

    private static async Task<IResult> StartTransferAsync(
        StartTransferRequest request,
        [FromServices] IVehicleOwnershipGuard guard,
        CancellationToken cancellationToken)
    {
        var effectivePolicy = new TenantSettings
        {
            TenantId = request.TenantId,
            AllowInitialRegistration = true,
            AllowMiscNewVehicles = true,
            OnlyOwnVehicles = request.OnlyOwnVehicles,
            OnlyOwnVehiclesMatriculas = request.OnlyOwnVehicles,
            OnlyOwnVehiclesOtros = request.OnlyOwnVehicles,
            SignatureVaultEnabled = false,
            NotificationChannel = NotificationChannel.FlitSmtp,
            NotificationTarget = NotificationTarget.Radicador,
            PaymentMethods = [],
        };

        var context = new VehicleOwnershipCheckContext(
            request.TenantId,
            request.VehicleIdentifier ?? string.Empty,
            request.UserEmail ?? string.Empty,
            effectivePolicy,
            request.ProcedureFamily);

        var result = await guard.ValidateTransferStartAsync(context, cancellationToken).ConfigureAwait(false);

        return result.IsAllowed
            ? Results.Ok(new TransferStartedResponse(request.TenantId, request.VehicleIdentifier))
            : Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    /// <summary>
    /// Payload del stub de radicación. <paramref name="OnlyOwnVehicles"/> es la
    /// política efectiva ya resuelta (snapshot si in-flight, live si nuevo, AC3).
    /// </summary>
    public sealed record StartTransferRequest(
        Guid TenantId,
        string? VehicleIdentifier,
        string? UserEmail,
        bool OnlyOwnVehicles,
        /// <summary>Familia del trámite (<c>MATRICULAS</c> | <c>TRASPASO</c> | <c>OTROS</c>). Null ⇒ TRASPASO.</summary>
        string? ProcedureFamily = null);

    private sealed record TransferStartedResponse(Guid TenantId, string? VehicleIdentifier);
}
