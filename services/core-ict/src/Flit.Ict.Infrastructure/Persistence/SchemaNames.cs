namespace Flit.Ict.Infrastructure.Persistence;

/// <summary>Nombres de schema de Postgres usados por core-ict.</summary>
public static class SchemaNames
{
    /// <summary>Schema propio del servicio de integración con terceros.</summary>
    public const string Ict = "ict";

    /// <summary>Schema de identidad (propiedad de core-api); solo lectura desde ICT.</summary>
    public const string Identity = "identity";
}
