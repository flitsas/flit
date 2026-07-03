namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Override por tenant de la cadena de proveedores de consulta RUNT (HU #10478). Indexado por tipo de
/// consulta (<c>vehicle_vin</c>, <c>vehicle_plate</c>, <c>conductor</c>). Vacío = usar los defaults
/// globales (<c>Consultations:DefaultChains</c>). Modelo de dominio puro (sin serialización): el
/// mapeo a/desde el jsonb ocurre en el repositorio.
/// </summary>
public sealed class ConsultationProviderConfig
{
    public static ConsultationProviderConfig Empty { get; } =
        new(new Dictionary<string, ConsultationProviderSelection>(StringComparer.OrdinalIgnoreCase));

    public ConsultationProviderConfig(IReadOnlyDictionary<string, ConsultationProviderSelection> byKind)
    {
        ByKind = byKind;
    }

    /// <summary>Selección de proveedor por tipo de consulta. Clave: vehicle_vin | vehicle_plate | conductor.</summary>
    public IReadOnlyDictionary<string, ConsultationProviderSelection> ByKind { get; }

    public bool IsEmpty => ByKind.Count == 0;
}

/// <summary>Proveedor primario + orden de fallback para un tipo de consulta.</summary>
public sealed record ConsultationProviderSelection(string Primary, IReadOnlyList<string> Fallback);
