namespace Flit.Tramites.Application.UseCases.Avaluos;

/// <summary>
/// Contexto que el proveedor de avalúo recibe. <see cref="FieldValues"/> mapea
/// field_key → value_text actual de la instancia (VIN, cilindraje, combustible, año, ...).
/// </summary>
public sealed record AvaluoContext(
    Guid InstanceId,
    Guid TenantId,
    IReadOnlyDictionary<string, string?> FieldValues);

/// <summary>
/// Resultado normalizado de un proveedor de avalúo. <see cref="Value"/> ya viene en pesos
/// colombianos reales (Fasecolda aplica ×1000 internamente). Status ∈ {ok, no_data, error}.
/// </summary>
public sealed record AvaluoResult(
    string Source,
    string Status,
    long? Value,
    string Currency,
    string? Message,
    int? Muestras)
{
    public static AvaluoResult Ok(string source, long value, int? muestras = null) =>
        new(source, "ok", value, "COP", null, muestras);

    public static AvaluoResult NoData(string source, string? message = null) =>
        new(source, "no_data", null, "COP", message ?? "Sin datos de avalúo para el vehículo", null);

    public static AvaluoResult Error(string source, string? message = null) =>
        new(source, "error", null, "COP", message ?? "La fuente de avalúo no respondió", null);
}

/// <summary>
/// Sugerencia compuesta que expone el endpoint: valor sugerido (fuente principal) + desglose
/// por fuente. Contrato consumido por el frontend (tarjeta "Avalúo comercial").
/// </summary>
public sealed record SuggestedCommercialValue(
    long? Sugerido,
    string? FuentePrincipal,
    IReadOnlyList<AvaluoResult> Sources);
