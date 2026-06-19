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
        AddBool(changes, "signature_vault_enabled", previous.SignatureVaultEnabled, updated.SignatureVaultEnabled);

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

        return changes;
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
