namespace Flit.Tramites.Domain.Entities.ConsolidadoLotes;

/// <summary>
/// HU #13420 (épica #13216) — rango de un parámetro del motor que la base acota con un CHECK de una sola columna en
/// <c>tramites.consolidado_export_settings</c> (DDL 133). <see cref="Maximo"/> es el máximo del tipo de la columna
/// (<c>int.MaxValue</c>) cuando el CHECK solo pone mínimo; desde Security L1 todos los CHECK de la tabla tienen tope.
/// </summary>
/// <param name="Campo">Nombre del campo en el contrato HTTP (camelCase), el que lleva <c>errors</c> en el 400.</param>
/// <param name="Columna">Columna de la tabla.</param>
/// <param name="Restriccion">Nombre del CHECK que lo impone en la base.</param>
public sealed record ConsolidadoExportSettingsRango(string Campo, string Columna, string Restriccion, int Minimo, int Maximo)
{
    /// <summary><c>true</c> si el CHECK no pone máximo (el máximo es el del tipo de la columna).</summary>
    public bool SinMaximo => Maximo == int.MaxValue;
}

/// <summary>
/// HU #13420 — regla «el lease es mayor que su tiempo máximo» (CHECK de dos columnas del DDL 133). Security L1:
/// el mismo CHECK pone el tope <see cref="Maximo"/> (inclusivo) al lease.
/// </summary>
public sealed record ConsolidadoExportSettingsReglaLease(
    string CampoLease, string ColumnaLease, string CampoTimeout, string ColumnaTimeout, string Restriccion, int Maximo);

/// <summary>HU #13420 — valores editables del motor tal como llegan del Super Admin, ya sin nulos.</summary>
public sealed record ConsolidadoExportSettingsValores(
    int MaxItemsPerBatch,
    int MaxPdfsPerPart,
    int MaxMbPerPart,
    int ItemSlots,
    int ItemTimeoutSeconds,
    int ItemLeaseSeconds,
    int MaxItemAttempts,
    int RetryDelaySeconds,
    int PartTimeoutSeconds,
    int PartLeaseSeconds,
    int MaxPartAttempts,
    int RetentionHours,
    bool IsActive);

/// <summary>HU #13420 — error de validación de un campo (<see cref="Campo"/> en camelCase, como en el contrato HTTP).</summary>
public sealed record ConsolidadoExportSettingsError(string Campo, string Mensaje);

/// <summary>
/// HU #13420 (AC3) — rangos de los parámetros del motor, <b>idénticos</b> a los CHECK de
/// <c>tramites.consolidado_export_settings</c> (DDL 133). Es la única fuente de los literales en C#: el endpoint del
/// Super Admin valida con <see cref="Validar"/> y el contrato los publica en <c>limites</c>. La paridad con el DDL la
/// fija <c>ConsolidadoExportSettingsRangosParidadTests</c> (texto del DDL) y
/// <c>ParametrosMotorLoteIntegrationTests</c> (extremos contra PostgreSQL real).
/// <para>Uso de ejemplo:
/// <code>
/// var errores = ConsolidadoExportSettingsRangos.Validar(valores);
/// if (errores.Count == 0) ConsolidadoExportSettingsRangos.Aplicar(fila, valores);
/// </code></para>
/// </summary>
public static class ConsolidadoExportSettingsRangos
{
    public const int MaxItemsPerBatchMinimo = 1;
    public const int MaxPdfsPerPartMinimo = 1;
    public const int MaxPdfsPerPartMaximo = 5000;
    public const int MaxMbPerPartMinimo = 10;
    public const int MaxMbPerPartMaximo = 2048;
    public const int ItemSlotsMinimo = 1;
    public const int ItemSlotsMaximo = 6;
    public const int ItemTimeoutSecondsMinimo = 1;
    /// <summary>Security L1 — tope de <c>item_timeout_seconds</c> (1 h).</summary>
    public const int ItemTimeoutSecondsMaximo = 3600;
    /// <summary>Security L1 — tope de <c>item_lease_seconds</c> (2 h); además debe superar <c>item_timeout_seconds</c>.</summary>
    public const int ItemLeaseSecondsMaximo = 7200;
    public const int MaxItemAttemptsMinimo = 1;
    public const int MaxItemAttemptsMaximo = 10;
    public const int RetryDelaySecondsMinimo = 5;
    /// <summary>Security L1 — tope de <c>retry_delay_seconds</c> (1 h).</summary>
    public const int RetryDelaySecondsMaximo = 3600;
    public const int PartTimeoutSecondsMinimo = 1;
    /// <summary>Security L1 — tope de <c>part_timeout_seconds</c> (2 h).</summary>
    public const int PartTimeoutSecondsMaximo = 7200;
    /// <summary>Security L1 — tope de <c>part_lease_seconds</c> (4 h); además debe superar <c>part_timeout_seconds</c>.</summary>
    public const int PartLeaseSecondsMaximo = 14_400;
    public const int MaxPartAttemptsMinimo = 1;
    public const int MaxPartAttemptsMaximo = 10;
    public const int RetentionHoursMinimo = 1;
    public const int RetentionHoursMaximo = 168;

    /// <summary>Rangos de una sola columna, en el orden de la pantalla.</summary>
    public static IReadOnlyList<ConsolidadoExportSettingsRango> Todos { get; } =
    [
        new("maxItemsPerBatch", "max_items_per_batch", "ck_consolidado_export_settings_max_items",
            MaxItemsPerBatchMinimo, ConsolidadoExportSettings.MaxItemsPerBatchMaximo),
        new("maxPdfsPerPart", "max_pdfs_per_part", "ck_consolidado_export_settings_max_pdfs",
            MaxPdfsPerPartMinimo, MaxPdfsPerPartMaximo),
        new("maxMbPerPart", "max_mb_per_part", "ck_consolidado_export_settings_max_mb",
            MaxMbPerPartMinimo, MaxMbPerPartMaximo),
        new("itemSlots", "item_slots", "ck_consolidado_export_settings_item_slots", ItemSlotsMinimo, ItemSlotsMaximo),
        new("itemTimeoutSeconds", "item_timeout_seconds", "ck_consolidado_export_settings_item_timeout",
            ItemTimeoutSecondsMinimo, ItemTimeoutSecondsMaximo),
        new("maxItemAttempts", "max_item_attempts", "ck_consolidado_export_settings_max_item_attempts",
            MaxItemAttemptsMinimo, MaxItemAttemptsMaximo),
        new("retryDelaySeconds", "retry_delay_seconds", "ck_consolidado_export_settings_retry_delay",
            RetryDelaySecondsMinimo, RetryDelaySecondsMaximo),
        new("partTimeoutSeconds", "part_timeout_seconds", "ck_consolidado_export_settings_part_timeout",
            PartTimeoutSecondsMinimo, PartTimeoutSecondsMaximo),
        new("maxPartAttempts", "max_part_attempts", "ck_consolidado_export_settings_max_part_attempts",
            MaxPartAttemptsMinimo, MaxPartAttemptsMaximo),
        new("retentionHours", "retention_hours", "ck_consolidado_export_settings_retention",
            RetentionHoursMinimo, RetentionHoursMaximo),
    ];

    /// <summary>Las dos reglas lease &gt; timeout, con el tope del lease en el mismo CHECK (Security L1).</summary>
    public static IReadOnlyList<ConsolidadoExportSettingsReglaLease> ReglasLease { get; } =
    [
        new("itemLeaseSeconds", "item_lease_seconds", "itemTimeoutSeconds", "item_timeout_seconds",
            "ck_consolidado_export_settings_item_lease", ItemLeaseSecondsMaximo),
        new("partLeaseSeconds", "part_lease_seconds", "partTimeoutSeconds", "part_timeout_seconds",
            "ck_consolidado_export_settings_part_lease", PartLeaseSecondsMaximo),
    ];

    /// <summary>Errores por campo; vacío si los valores cumplen todos los CHECK del DDL 133.</summary>
    public static IReadOnlyList<ConsolidadoExportSettingsError> Validar(ConsolidadoExportSettingsValores valores)
    {
        ArgumentNullException.ThrowIfNull(valores);
        var errores = new List<ConsolidadoExportSettingsError>();

        foreach (var rango in Todos)
        {
            var valor = Valor(valores, rango.Campo);
            if (valor < rango.Minimo || valor > rango.Maximo)
                errores.Add(new(rango.Campo, rango.SinMaximo
                    ? $"Debe ser mayor o igual que {rango.Minimo}."
                    : $"Debe estar entre {rango.Minimo} y {rango.Maximo}."));
        }

        foreach (var regla in ReglasLease)
        {
            var lease = Valor(valores, regla.CampoLease);
            if (lease <= Valor(valores, regla.CampoTimeout))
                errores.Add(new(regla.CampoLease, $"Debe ser mayor que {regla.CampoTimeout}."));
            if (lease > regla.Maximo)
                errores.Add(new(regla.CampoLease, $"Debe ser menor o igual que {regla.Maximo}."));
        }

        return errores;
    }

    /// <summary>Campo del contrato que corresponde a un CHECK del DDL 133, o <c>null</c> si no es de esta tabla.</summary>
    public static string? CampoDeRestriccion(string? restriccion) =>
        Todos.FirstOrDefault(r => r.Restriccion == restriccion)?.Campo
        ?? ReglasLease.FirstOrDefault(r => r.Restriccion == restriccion)?.CampoLease;

    /// <summary>Copia los valores (ya validados) a la fila; los <c>smallint</c> caben porque sus rangos son de 1 a 10.</summary>
    public static void Aplicar(ConsolidadoExportSettings destino, ConsolidadoExportSettingsValores valores)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(valores);
        destino.MaxItemsPerBatch = valores.MaxItemsPerBatch;
        destino.MaxPdfsPerPart = valores.MaxPdfsPerPart;
        destino.MaxMbPerPart = valores.MaxMbPerPart;
        destino.ItemSlots = checked((short)valores.ItemSlots);
        destino.ItemTimeoutSeconds = valores.ItemTimeoutSeconds;
        destino.ItemLeaseSeconds = valores.ItemLeaseSeconds;
        destino.MaxItemAttempts = checked((short)valores.MaxItemAttempts);
        destino.RetryDelaySeconds = valores.RetryDelaySeconds;
        destino.PartTimeoutSeconds = valores.PartTimeoutSeconds;
        destino.PartLeaseSeconds = valores.PartLeaseSeconds;
        destino.MaxPartAttempts = checked((short)valores.MaxPartAttempts);
        destino.RetentionHours = valores.RetentionHours;
        destino.IsActive = valores.IsActive;
    }

    private static int Valor(ConsolidadoExportSettingsValores v, string campo) => campo switch
    {
        "maxItemsPerBatch" => v.MaxItemsPerBatch,
        "maxPdfsPerPart" => v.MaxPdfsPerPart,
        "maxMbPerPart" => v.MaxMbPerPart,
        "itemSlots" => v.ItemSlots,
        "itemTimeoutSeconds" => v.ItemTimeoutSeconds,
        "itemLeaseSeconds" => v.ItemLeaseSeconds,
        "maxItemAttempts" => v.MaxItemAttempts,
        "retryDelaySeconds" => v.RetryDelaySeconds,
        "partTimeoutSeconds" => v.PartTimeoutSeconds,
        "partLeaseSeconds" => v.PartLeaseSeconds,
        "maxPartAttempts" => v.MaxPartAttempts,
        "retentionHours" => v.RetentionHours,
        _ => throw new ArgumentOutOfRangeException(nameof(campo), campo, "Campo de parámetros desconocido."),
    };
}
