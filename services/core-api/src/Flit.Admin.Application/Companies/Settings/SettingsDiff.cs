using System.Globalization;
using System.Text;
using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.Settings;

/// <summary>
/// Calcula la lista de cambios campo a campo entre la configuración previa y la
/// nueva, para generar una entrada de auditoría por cada campo modificado (AC1).
/// Los nombres de campo son las columnas BD (snake_case); los valores se
/// codifican como JSON para la columna jsonb <c>old_value</c>/<c>new_value</c>.
///
/// La codificación JSON se hace a mano (sin <c>JsonSerializer</c>) para mantener
/// el ensamblado AOT-compatible (sin reflexión de serialización).
/// </summary>
internal static class SettingsDiff
{
    public static IReadOnlyList<TenantConfigChange> Compute(TenantSettings previous, TenantSettings updated)
    {
        var changes = new List<TenantConfigChange>();

        AddBool(changes, "allow_initial_registration", previous.AllowInitialRegistration, updated.AllowInitialRegistration);
        AddBool(changes, "allow_misc_new_vehicles", previous.AllowMiscNewVehicles, updated.AllowMiscNewVehicles);
        AddBool(changes, "only_own_vehicles", previous.OnlyOwnVehicles, updated.OnlyOwnVehicles);
        AddBool(changes, "only_own_vehicles_matriculas", previous.OnlyOwnVehiclesMatriculas, updated.OnlyOwnVehiclesMatriculas);
        AddBool(changes, "only_own_vehicles_otros", previous.OnlyOwnVehiclesOtros, updated.OnlyOwnVehiclesOtros);
        AddBool(changes, "block_procedure_family_traspaso", previous.BlockProcedureFamilyTraspaso, updated.BlockProcedureFamilyTraspaso);
        AddBool(changes, "block_procedure_family_otros", previous.BlockProcedureFamilyOtros, updated.BlockProcedureFamilyOtros);
        AddBool(changes, "signature_vault_enabled", previous.SignatureVaultEnabled, updated.SignatureVaultEnabled);
        AddBool(changes, "plate_preassign_enabled", previous.PlatePreassignEnabled, updated.PlatePreassignEnabled);
        AddBool(changes, "validate_soat_with_runt", previous.ValidateSoatWithRunt, updated.ValidateSoatWithRunt);
        AddBool(changes, "plate_flow_skip_to_terminado", previous.PlateFlowSkipToTerminado, updated.PlateFlowSkipToTerminado);
        // HU #11357/#11362 (ADR-0043) — elegibilidad de documentos personalizados, campo propio.
        AddBool(changes, "personalized_documents_enabled", previous.PersonalizedDocumentsEnabled, updated.PersonalizedDocumentsEnabled);

        // FEATURE 02 — fuente de comparendos (internal | external).
        AddString(changes, "fines_query_source", previous.FinesQuerySource, updated.FinesQuerySource);

        AddString(
            changes,
            "notification_channel",
            TenantSettingsCodes.ToDb(previous.NotificationChannel),
            TenantSettingsCodes.ToDb(updated.NotificationChannel));

        AddString(
            changes,
            "notification_target",
            TenantSettingsCodes.ToDb(previous.NotificationTarget),
            TenantSettingsCodes.ToDb(updated.NotificationTarget));

        if (!previous.PaymentMethods.SequenceEqual(updated.PaymentMethods, StringComparer.Ordinal))
        {
            changes.Add(new TenantConfigChange(
                "payment_methods",
                JsonArray(previous.PaymentMethods),
                JsonArray(updated.PaymentMethods)));
        }

        // HU #10478 — timeout de failover (número JSON) y override de proveedores (objeto jsonb).
        if (previous.RuntFailoverTimeoutMs != updated.RuntFailoverTimeoutMs)
        {
            changes.Add(new TenantConfigChange(
                "runt_failover_timeout_ms",
                previous.RuntFailoverTimeoutMs.ToString(CultureInfo.InvariantCulture),
                updated.RuntFailoverTimeoutMs.ToString(CultureInfo.InvariantCulture)));
        }

        var previousConfig = JsonConsultationConfig(previous.ConsultationProviderConfig);
        var updatedConfig = JsonConsultationConfig(updated.ConsultationProviderConfig);
        if (!string.Equals(previousConfig, updatedConfig, StringComparison.Ordinal))
        {
            changes.Add(new TenantConfigChange("consultation_provider_config", previousConfig, updatedConfig));
        }

        // Feature #10707 — proveedores de avalúo (objeto jsonb).
        var previousAvaluo = JsonAvaluoConfig(previous.AvaluoProviderConfig);
        var updatedAvaluo = JsonAvaluoConfig(updated.AvaluoProviderConfig);
        if (!string.Equals(previousAvaluo, updatedAvaluo, StringComparison.Ordinal))
        {
            changes.Add(new TenantConfigChange("avaluo_provider_config", previousAvaluo, updatedAvaluo));
        }

        return changes;
    }

    /// <summary>
    /// JSON canónico de la config de avalúo para auditoría (<c>primary</c> + <c>enabled</c>), sin
    /// <c>JsonSerializer</c> para preservar AOT.
    /// </summary>
    private static string JsonAvaluoConfig(AvaluoProviderConfig config)
    {
        var builder = new StringBuilder();
        builder.Append('{')
            .Append(JsonString("primary")).Append(':').Append(JsonString(config.Primary))
            .Append(',').Append(JsonString("enabled")).Append(':').Append(JsonArray(config.Enabled))
            .Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// JSON canónico del override de proveedores para auditoría (orden de tipos fijo, sin
    /// <c>JsonSerializer</c> para preservar AOT). Solo incluye los tipos presentes; <c>{}</c> si vacío.
    /// </summary>
    private static string JsonConsultationConfig(ConsultationProviderConfig config)
    {
        var builder = new StringBuilder();
        builder.Append('{');

        var first = true;
        foreach (var kind in new[] { "vehicle_vin", "vehicle_plate", "conductor" })
        {
            if (!config.ByKind.TryGetValue(kind, out var selection))
            {
                continue;
            }

            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append(JsonString(kind)).Append(":{")
                .Append(JsonString("primary")).Append(':').Append(JsonString(selection.Primary))
                .Append(',').Append(JsonString("fallback")).Append(':').Append(JsonArray(selection.Fallback))
                .Append('}');
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static void AddBool(List<TenantConfigChange> changes, string field, bool previous, bool updated)
    {
        if (previous != updated)
        {
            changes.Add(new TenantConfigChange(field, JsonBool(previous), JsonBool(updated)));
        }
    }

    private static void AddString(List<TenantConfigChange> changes, string field, string previous, string updated)
    {
        if (!string.Equals(previous, updated, StringComparison.Ordinal))
        {
            changes.Add(new TenantConfigChange(field, JsonString(previous), JsonString(updated)));
        }
    }

    private static string JsonBool(bool value) => value ? "true" : "false";

    private static string JsonArray(IReadOnlyList<string> items) =>
        $"[{string.Join(",", items.Select(JsonString))}]";

    private static string JsonString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (ch < ' ')
                    {
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
