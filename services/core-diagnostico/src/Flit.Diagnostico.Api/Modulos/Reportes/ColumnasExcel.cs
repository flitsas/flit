namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Catálogo de columnas de la hoja «Vehículos» del Excel del lote (HU #13849, F07-C5; documentado en <c>contracts/openapi/diagnostico.v1.yaml</c>,
/// operación <c>descargarExcelLote</c>). Columnas fijas (<see cref="Fijas"/>) y, por cada fuente del alcance en el orden de la grilla, una
/// columna de estado («{título} — Estado») seguida de sus columnas de dato («{título} — {dato}»). Una fuente fuera del alcance no tiene
/// columnas. Ninguna columna es de origen, usuario manual, proveedor ni transacción (R-20). Un cambio de este catálogo cambia los
/// encabezados, que entran en la huella del Excel por <see cref="ExcelLote.Firma"/> (solo invalida los lotes cuyo alcance tiene la
/// fuente tocada); sube <see cref="ExcelLote.VersionPlantilla"/> si cambias cómo se pinta una celda sin cambiar lo que muestra.
/// </summary>
/// <example>
/// <code>
/// ColumnasExcel.Encabezados(["RUNT", "SIMIT_VEHICULO"]); // Orden … Estado del vehículo, RUNT — Estado, RUNT — Servicio, …
/// </code>
/// </example>
internal static class ColumnasExcel
{
    public const string Estado = "Estado";

    public const string PersonasConsultadas = "Personas consultadas";

    public const string PersonasConError = "Personas con error";

    public const string Documentos = "Documentos";

    public const string Comparendos = "Comparendos";

    public const string ValorTotal = "Valor total";

    /// <summary>Impuesto departamental (HU #13864, Q6 = a): <c>monto_total</c> en pesos enteros.</summary>
    public const string TotalAdeudado = "Total adeudado";

    /// <summary>Impuesto departamental (HU #13864, Q6 = a): cantidad de vigencias con <c>pagada=false</c>.</summary>
    public const string VigenciasPendientes = "Vigencias pendientes";

    /// <summary>Columnas fijas, antes de las fuentes.</summary>
    public static readonly IReadOnlyList<string> Fijas = ["Orden", "Identificador", "Tipo de identificador", "Placa", "VIN", "Estado del vehículo"];

    /// <summary>Título de cada fuente (código de la base) en los encabezados y en la hoja «Consultas».</summary>
    public static readonly IReadOnlyDictionary<string, string> Titulos = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RUNT"] = "RUNT",
        ["RUNT_PROPIETARIO"] = "RUNT propietarios",
        ["RUNT_LOCATARIO"] = "RUNT locatarios",
        ["SIMIT_VEHICULO"] = "SIMIT vehículo",
        ["SIMIT_PROPIETARIO"] = "SIMIT propietarios",
        ["SIMIT_LOCATARIO"] = "SIMIT locatarios",
        ["AVALUO"] = "Avalúo",
        ["IMPUESTO_DEPARTAMENTAL"] = "Impuesto departamental",
    };

    private static readonly string[] DePersonaRunt = [PersonasConsultadas, PersonasConError, Documentos];

    private static readonly string[] DePersonaSimit = [PersonasConsultadas, PersonasConError, Documentos, Comparendos, ValorTotal];

    /// <summary>Columnas de dato de cada fuente (sin la de estado); una fuente sin entrada solo lleva su estado.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DeDato = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["RUNT"] = ["Servicio", "Clase", "Organismo de tránsito", "Marca", "Línea", "Modelo", "Propietarios", "Locatarios"],
        ["SIMIT_VEHICULO"] = ["Semáforo", Comparendos, ValorTotal],
        ["RUNT_PROPIETARIO"] = DePersonaRunt,
        ["RUNT_LOCATARIO"] = DePersonaRunt,
        ["SIMIT_PROPIETARIO"] = DePersonaSimit,
        ["SIMIT_LOCATARIO"] = DePersonaSimit,
        ["AVALUO"] = ["Valor sugerido", "Fuente principal", "Sin valor comercial"],
        ["IMPUESTO_DEPARTAMENTAL"] = [TotalAdeudado, VigenciasPendientes],
    };

    public static string Titulo(string fuente) => Titulos.GetValueOrDefault(fuente, fuente);

    /// <summary>Las columnas de dato de <paramref name="fuente"/> (vacía si solo lleva estado).</summary>
    public static IReadOnlyList<string> DatosDe(string fuente) => DeDato.GetValueOrDefault(fuente, []);

    /// <summary>Encabezados de la hoja «Vehículos» para las fuentes dadas (códigos, en el orden de la grilla).</summary>
    public static IReadOnlyList<string> Encabezados(IEnumerable<string> fuentes)
    {
        ArgumentNullException.ThrowIfNull(fuentes);
        var encabezados = new List<string>(Fijas);
        foreach (var fuente in fuentes)
        {
            var titulo = Titulo(fuente);
            encabezados.Add($"{titulo} — {Estado}");
            encabezados.AddRange(DatosDe(fuente).Select(dato => $"{titulo} — {dato}"));
        }

        return encabezados;
    }
}
