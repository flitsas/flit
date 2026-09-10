using System.Globalization;

namespace Flit.Tramites.Domain.RuntConfirmation;

/// <summary>
/// Configuración GLOBAL del proceso de Confirmación RUNT (Epic #12234, Feature #12276, HU #12277).
/// Una sola fila (<c>tramites.runt_confirmation_settings</c>, índice único sobre una constante,
/// el patrón de <c>admin.notification_test_settings</c>): es una consulta aislada al trámite y no
/// hereda el proveedor de la empresa —decisión del PO—, así que no hay override por tenant y
/// ninguna pantalla de compañía la toca.
/// </summary>
public sealed class RuntConfirmationSettings
{
    public const string DefaultRunAtLocal = "02:00";
    public const int DefaultGraceDays = 0;
    public const int DefaultDiscrepancyAfterRuns = 3;
    public const int DefaultMaxAttempts = 10;

    /// <summary>uuid asignado por la base (uuidv7). <see cref="Guid.Empty"/> mientras la fila no exista.</summary>
    public Guid Id { get; set; }

    /// <summary>Token de concurrencia optimista; lo incrementa el trigger de la tabla.</summary>
    public long RowVersion { get; set; }

    /// <summary>Interruptor. Apagado, la corrida diaria no consulta nada y lo deja anotado.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hora local (America/Bogota) de la corrida, <c>HH:mm</c>.</summary>
    public string RunAtLocal { get; set; } = DefaultRunAtLocal;

    /// <summary>Proveedor con el que consulta la corrida (<see cref="RuntConfirmationProviderKeys"/>).</summary>
    public string ProviderKey { get; set; } = RuntConfirmationProviderKeys.Kyverum;

    /// <summary>Días tras la aprobación antes de la primera consulta. 0 = de inmediato (no se hereda el 7 de v1).</summary>
    public int GraceDays { get; set; } = DefaultGraceDays;

    /// <summary>Intentos en Pendiente tras los cuales el trámite se marca en discrepancia.</summary>
    public int DiscrepancyAfterRuns { get; set; } = DefaultDiscrepancyAfterRuns;

    /// <summary>Tope de intentos; alcanzado, el trámite sale del universo de la corrida.</summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static RuntConfirmationSettings Defaults() => new();

    /// <summary>
    /// Valida los rangos (HU #12277 AC2). Devuelve una entrada por campo inválido, con el nombre del
    /// campo tal como viaja en el contrato de la API (camelCase) para que el 400 lo identifique.
    /// </summary>
    public IReadOnlyList<RuntConfirmationSettingsError> Validate()
    {
        var errors = new List<RuntConfirmationSettingsError>();

        if (!TryParseRunAt(RunAtLocal, out _))
            errors.Add(new("runAtLocal", "La hora debe tener el formato HH:mm (00:00 a 23:59)."));

        if (!RuntConfirmationProviderKeys.IsValid(ProviderKey))
            errors.Add(new("providerKey", $"El proveedor debe ser uno de: {string.Join(", ", RuntConfirmationProviderKeys.All)}."));

        if (GraceDays < 0)
            errors.Add(new("graceDays", "Los días de gracia no pueden ser negativos."));

        if (DiscrepancyAfterRuns < 1)
            errors.Add(new("discrepancyAfterRuns", "Las corridas antes de discrepancia deben ser al menos 1."));

        if (MaxAttempts < 1)
            errors.Add(new("maxAttempts", "El tope de reintentos debe ser al menos 1."));
        else if (DiscrepancyAfterRuns >= 1 && MaxAttempts < DiscrepancyAfterRuns)
            errors.Add(new("maxAttempts", "El tope de reintentos no puede ser menor que las corridas antes de discrepancia."));

        return errors;
    }

    public static bool TryParseRunAt(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

public sealed record RuntConfirmationSettingsError(string Field, string Message);

/// <summary>Proveedores admitidos para la corrida. Mismas keys que los proveedores de consulta del wizard.</summary>
public static class RuntConfirmationProviderKeys
{
    public const string Kyverum = "kyverum_runt";
    public const string Verifik = "verifik";

    public static readonly IReadOnlyList<string> All = [Kyverum, Verifik];

    public static bool IsValid(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}
