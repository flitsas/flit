namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Configuración por tenant de los proveedores de avalúo comercial habilitados en el paso comercial
/// del traspaso (Feature #10707, HU de ajuste). Fasecolda es el proveedor base: siempre está
/// habilitado y es el sugerido por defecto; los demás (base gravable, Mercado Libre) se habilitan
/// explícitamente por compañía. Modelo de dominio puro: el mapeo a/desde el jsonb ocurre en el
/// repositorio.
/// </summary>
public sealed class AvaluoProviderConfig
{
    /// <summary>Proveedor base, siempre habilitado y sugerido por defecto.</summary>
    public const string BaseProvider = "fasecolda";

    /// <summary>Configuración por defecto: solo Fasecolda, sugerido Fasecolda.</summary>
    public static AvaluoProviderConfig Default { get; } =
        new(BaseProvider, [BaseProvider]);

    public AvaluoProviderConfig(string primary, IReadOnlyList<string> enabled)
    {
        // Fasecolda es el proveedor base: se fuerza a habilitado aunque no venga en la lista.
        var normalized = new List<string>();
        if (!enabled.Contains(BaseProvider, StringComparer.OrdinalIgnoreCase))
        {
            normalized.Add(BaseProvider);
        }

        foreach (var key in enabled)
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                !normalized.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(key);
            }
        }

        Enabled = normalized;
        // El primario debe estar habilitado; si no, cae al proveedor base.
        Primary = normalized.Contains(primary, StringComparer.OrdinalIgnoreCase)
            ? primary
            : BaseProvider;
    }

    /// <summary>Proveedor cuyo valor se sugiere por defecto (debe estar habilitado).</summary>
    public string Primary { get; }

    /// <summary>Proveedores habilitados para la agregación (incluye siempre a Fasecolda).</summary>
    public IReadOnlyList<string> Enabled { get; }
}
