using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Domain.Companies.VehicleOwnership;

/// <summary>
/// Contexto de evaluación del interceptor al iniciar un trámite con regla de vehículos propios
/// (HU #10191, RF04; extendido a familias MATRICULAS | TRASPASO | OTROS).
/// </summary>
/// <param name="TenantId">Tenant que radica.</param>
/// <param name="VehicleIdentifier">Identificador del vehículo evaluado (placa/VIN).</param>
/// <param name="UserEmail">Correo del usuario que radica (para la exención por whitelist, AC2).</param>
/// <param name="EffectivePolicy">
/// Política operativa <em>efectiva</em> del trámite. Para un trámite in-flight es el
/// snapshot capturado al crearlo; para una radicación nueva es la política live
/// (resuelta vía <see cref="ITenantPolicyResolver"/>, AC3).
/// </param>
/// <param name="ProcedureFamily">
/// Familia del tipo (<c>MATRICULAS</c> | <c>TRASPASO</c> | <c>OTROS</c>). Null ⇒ TRASPASO (legado).
/// </param>
public sealed record VehicleOwnershipCheckContext(
    Guid TenantId,
    string VehicleIdentifier,
    string UserEmail,
    TenantSettings EffectivePolicy,
    string? ProcedureFamily = null);
