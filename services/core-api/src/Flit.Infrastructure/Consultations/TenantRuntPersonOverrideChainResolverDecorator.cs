using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Decorador de <see cref="IConsultationProviderChainResolver"/> que cortocircuita la cadena
/// remota de conductor (Kyverum/Verifik) cuando existe un override manual activo para el tenant
/// y documento consultados (HU #14642).
///
/// <para>Flujo de decisión:</para>
/// <list type="number">
///   <item>Solo actúa sobre <see cref="ConsultationKind.Conductor"/>; los demás tipos se delegan sin cambios.</item>
///   <item>Lee el documento del <see cref="ConsultationContext.FieldValues"/> (claves <c>document_type</c>
///   y <c>document_number</c>).</item>
///   <item>Si existe un override activo y no expirado → construye el <see cref="ConsultationResult"/>
///   con provider <c>tenant_manual_conductor</c> sin llamar a Kyverum ni Verifik.</item>
///   <item>Si no existe override → delega en la cadena remota interna.</item>
/// </list>
/// </summary>
internal sealed class TenantRuntPersonOverrideChainResolverDecorator(
    IConsultationProviderChainResolver inner,
    ITenantRuntPersonOverrideReader overrideReader,
    ILogger<TenantRuntPersonOverrideChainResolverDecorator>? logger = null)
    : IConsultationProviderChainResolver
{
    internal const string ProviderKey = "tenant_manual_conductor";

    // ── IConsultationProviderChainResolver ────────────────────────────────────

    public IReadOnlyList<string> ResolveChain(
        ConsultationKind kind,
        ConsultationTenantOverride? tenantOverride = null)
        => inner.ResolveChain(kind, tenantOverride);

    public async Task<ConsultationResult> ConsultAsync(
        ConsultationKind kind,
        ConsultationContext ctx,
        ConsultationTenantOverride? tenantOverride,
        CancellationToken ct)
    {
        // Solo cortocircuitar consultas de conductor.
        if (kind != ConsultationKind.Conductor)
            return await inner.ConsultAsync(kind, ctx, tenantOverride, ct).ConfigureAwait(false);

        var documentType = GetFieldValue(ctx, "document_type");
        var documentNumber = GetFieldValue(ctx, "document_number");

        if (documentType is null || documentNumber is null)
            return await inner.ConsultAsync(kind, ctx, tenantOverride, ct).ConfigureAwait(false);

        // Buscar override activo para este tenant + documento.
        // document_type puede venir ya remapeado a código de proveedor (p. ej. PAS→PA);
        // el reader canoniza alias a CC|CE|PAS|TI|PPT (bug #14721).
        var @override = await overrideReader
            .FindActiveAsync(ctx.TenantId, documentType, documentNumber, ct)
            .ConfigureAwait(false);

        if (@override is null)
        {
            if (logger is not null)
                OverrideLog.SinOverride(logger, ctx.TenantId, documentType, documentNumber);
            return await inner.ConsultAsync(kind, ctx, tenantOverride, ct).ConfigureAwait(false);
        }

        if (logger is not null)
            OverrideLog.OverrideAplicado(logger, ctx.TenantId, documentType, documentNumber, @override.Id);

        return BuildResult(@override);
    }

    // ── Construcción del resultado de cortocircuito ───────────────────────────

    /// <summary>
    /// Construye un <see cref="ConsultationResult"/> con el mismo shape que produce
    /// <c>VerifikConductorResultMapper</c> o <c>KyverumRuntConductorResultMapper</c>:
    /// mismos <c>FieldKey</c> de <c>HydratedField</c> y mismos <c>Key</c> de <c>ConsultationCheck</c>.
    /// </summary>
    internal static ConsultationResult BuildResult(TenantRuntPersonOverride ov)
    {
        // Nombre resuelto con la misma lógica que DriverNameResolver.
        var names = DriverNameResolver.FromParts(
            ov.FirstName, ov.SecondName, ov.FirstLastName, ov.SecondLastName);
        var fullName = names.FullName;

        var hydrated = new List<HydratedField>();
        DriverNameResolver.AddHydratedNames(hydrated, names, fullName);

        // Mismos FieldKey que VerifikConductorResultMapper / KyverumRuntConductorResultMapper.
        // person_license_status = estado del conductor (ACTIVO); person_citizen_status = ACTIVA.
        Add(hydrated, "person_license_status", ov.DriverStatus);
        Add(hydrated, "person_citizen_status", ov.CitizenStatus);
        Add(hydrated, "person_has_active_license", ov.HasActiveLicense ? "true" : "false");
        if (!string.IsNullOrWhiteSpace(ov.LicenseCategories))
            Add(hydrated, "person_license_categories", ov.LicenseCategories);
        Add(hydrated, "person_has_pending_fines", ov.HasPendingFines ? "true" : "false");
        if (!string.IsNullOrWhiteSpace(ov.NroPazYSalvo))
            Add(hydrated, "person_paz_y_salvo", ov.NroPazYSalvo);

        var checks = new List<ConsultationCheck>
        {
            new("conductor_identidad", "Persona en RUNT", "ok", ProviderKey, null),
        };

        var driverOk = string.Equals(ov.DriverStatus, "ACTIVO", StringComparison.OrdinalIgnoreCase);
        var citizenOk = string.Equals(ov.CitizenStatus, "ACTIVA", StringComparison.OrdinalIgnoreCase);
        checks.Add(new ConsultationCheck(
            "conductor_estado",
            "Estado conductor",
            driverOk && citizenOk ? "ok" : "warn",
            ProviderKey,
            null));

        checks.Add(new ConsultationCheck(
            "conductor_licencia",
            "Licencia conductor",
            ov.HasActiveLicense ? "ok" : "warn",
            ProviderKey,
            null));

        checks.Add(new ConsultationCheck(
            "conductor_multas",
            "Multas conductor",
            ov.HasPendingFines ? "fail" : "ok",
            ProviderKey,
            null));

        // Overall alineado a los mappers RUNT: green solo si hallado + licencia activa + sin multas.
        var overall = (!ov.HasPendingFines && ov.HasActiveLicense) ? "green" : "yellow";

        return new ConsultationResult(ProviderKey, overall, checks, hydrated);
    }

    private static void Add(List<HydratedField> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields.Add(new HydratedField(key, value, null));
    }

    private static string? GetFieldValue(ConsultationContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}

/// <summary>Trazas del decorador de override de RUNT.</summary>
internal static partial class OverrideLog
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Sin override activo de RUNT para tenant {TenantId} / {DocumentType} {DocumentNumber}; delegando a cadena remota.")]
    public static partial void SinOverride(
        ILogger logger, Guid tenantId, string documentType, string documentNumber);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Override manual RUNT #{OverrideId} aplicado para tenant {TenantId} / {DocumentType} {DocumentNumber}.")]
    public static partial void OverrideAplicado(
        ILogger logger, Guid tenantId, string documentType, string documentNumber, Guid overrideId);
}
