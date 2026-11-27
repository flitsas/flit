using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Catálogo de columnas de la hoja «Vehículos» del Excel del lote (HU #13849, F07-C5; documentado en <c>contracts/openapi/diagnostico.v1.yaml</c>,
/// operación <c>descargarExcelLote</c>). Columnas fijas (<see cref="Fijas"/>) y, por cada fuente del alcance en el orden de la grilla, una
/// columna de estado («{título} — Estado») seguida de sus columnas de dato («{título} — {dato}»). Una fuente fuera del alcance no tiene
/// columnas. Al final, la cola <see cref="SinFuente"/> (HU #14752, D2). Solo las columnas de <see cref="EnClaro"/> llevan datos personales
/// en claro (D3). Ninguna columna es de origen, usuario manual, proveedor ni transacción (R-20). Un cambio de este catálogo cambia los
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

    /// <summary>Propietario actual declarado por el cliente, enmascarado («TIPO ****1234»), o vacío (HU #14582, §1.3).</summary>
    public const string PropietarioDeclarado = "Propietario actual declarado";

    /// <summary>Sí / No / vacío según <c>cambio_propietario</c> (HU #14582, §1.3).</summary>
    public const string CambioPropietario = "Cambio de propietario";

    /// <summary>HU #14752 (D2): <c>VehiculoDiagnostico.Id</c>, el id interno del vehículo.</summary>
    public const string IdVehiculo = "ID del vehículo";

    // HU #14752 (Excel v4, contrato-excel §2.1): columnas nuevas del RUNT, después de «Locatarios».
    public const string PropietarioTipoDocumento = "Tipo de documento del propietario";

    public const string PropietarioNumeroDocumento = "Número de documento del propietario";

    public const string LocatarioTipoDocumento = "Tipo de documento del locatario";

    public const string LocatarioNumeroDocumento = "Número de documento del locatario";

    public const string LocatarioNombre = "Nombre del locatario";

    public const string GarantiasMobiliarias = "Garantías mobiliarias";

    public const string GarantiasAFavor = "Garantías a favor de";

    // HU #14752 (contrato-excel §2.3): SIMIT locatario.
    public const string DetalleComparendosLocatario = "Detalle de comparendos del locatario";

    public const string TotalComparendosLocatario = "Total de comparendos del locatario";

    /// <summary>HU #14752 (contrato-excel §2.4, D8): VIGENTE, CON DEUDA, NO DISPONIBLE o vacío.</summary>
    public const string EstadoImpuesto = "Estado del impuesto";

    /// <summary>Columnas fijas, antes de las fuentes («ID del vehículo» al final, HU #14752).</summary>
    public static readonly IReadOnlyList<string> Fijas =
        ["Orden", "Identificador", "Tipo de identificador", "Placa", "VIN", PropietarioDeclarado, CambioPropietario, "Estado del vehículo", IdVehiculo];

    /// <summary>
    /// HU #14752 (D2): columnas sin fuente, sin prefijo, al final de la hoja; siempre presentes y siempre vacías (orden de v1).
    /// </summary>
    public static readonly IReadOnlyList<string> SinFuente =
    [
        "Tipo de locatario", "Normalización / saneamiento", "N.º de serie", "Licencia de tránsito de importación", "Empresa transportadora",
        "Tomador póliza RC", "Análisis de saneamiento",
    ];

    /// <summary>
    /// HU #14752 (D3, SP-11; ADR-0076, enmienda 2026-11-27): lista CERRADA de las columnas de dato con datos personales en claro. Nada
    /// fuera de esta lista sale en claro, y <see cref="ExcelLote.FirmaHuella"/> sustituye estas celdas para que no entren a la huella (SP-2).
    /// </summary>
    public static readonly IReadOnlySet<string> EnClaro = new HashSet<string>(StringComparer.Ordinal)
    {
        PropietarioNumeroDocumento, LocatarioNumeroDocumento, LocatarioNombre, GarantiasAFavor, GarantiasMobiliarias, DetalleComparendosLocatario,
    };

    /// <summary>De <see cref="EnClaro"/>, las que son un número de documento: en la huella van enmascaradas («****» + 4).</summary>
    public static readonly IReadOnlySet<string> DocumentosEnClaro = new HashSet<string>(StringComparer.Ordinal)
    {
        PropietarioNumeroDocumento, LocatarioNumeroDocumento,
    };

    /// <summary>Título de cada fuente (código de la base) en los encabezados y en la hoja «Consultas».</summary>
    /// <remarks>Son los nombres de fuente de <see cref="RotulosDiagnostico.Fuentes"/> (punto 5): los mismos de la pantalla.</remarks>
    public static IReadOnlyDictionary<string, string> Titulos => RotulosDiagnostico.Fuentes;

    private static readonly string[] DePersonaRunt = [PersonasConsultadas, PersonasConError, Documentos];

    private static readonly string[] DePersonaSimit = [PersonasConsultadas, PersonasConError, Documentos, Comparendos, ValorTotal];

    private static readonly string[] DeLocatarioSimit = [.. DePersonaSimit, DetalleComparendosLocatario, TotalComparendosLocatario];

    /// <summary>Columnas de dato de cada fuente (sin la de estado); una fuente sin entrada solo lleva su estado.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DeDato = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["RUNT"] =
        [
            "Servicio", "Clase", "Organismo de tránsito", "Marca", "Línea", "Modelo", "Propietarios", "Locatarios",
            PropietarioTipoDocumento, PropietarioNumeroDocumento, LocatarioTipoDocumento, LocatarioNumeroDocumento, LocatarioNombre,
            "Estado SOAT", "Vencimiento SOAT", "Estado RTM", "Vencimiento RTM", GarantiasMobiliarias, GarantiasAFavor, "Limitaciones a la propiedad",
            "Accidentalidad", "Blindaje", "Nivel de blindaje", "Fecha de matrícula", "Cilindraje", "Clasificación", "Color",
            "Estado del vehículo en el RUNT", "N.º de chasis", "N.º de motor", "Tiene gravámenes", "Tipo de carrocería", "Tipo de combustible",
        ],
        ["SIMIT_VEHICULO"] =
        [
            "Semáforo", Comparendos, ValorTotal, "Cantidad de resoluciones", "Estado de la placa", "Números de comparendo", "Fechas de comparendo",
            "Valores a pagar", "Total de infracciones",
        ],
        ["RUNT_PROPIETARIO"] = DePersonaRunt,
        ["RUNT_LOCATARIO"] = DePersonaRunt,
        ["SIMIT_PROPIETARIO"] = DePersonaSimit,
        ["SIMIT_LOCATARIO"] = DeLocatarioSimit,
        ["AVALUO"] = ["Valor sugerido", "Fuente principal", "Sin valor comercial"],
        ["IMPUESTO_DEPARTAMENTAL"] = [TotalAdeudado, VigenciasPendientes, EstadoImpuesto],
    };

    public static string Titulo(string fuente) => RotulosDiagnostico.DeFuente(fuente);

    /// <summary>Las columnas de dato de <paramref name="fuente"/> (vacía si solo lleva estado).</summary>
    public static IReadOnlyList<string> DatosDe(string fuente) => DeDato.GetValueOrDefault(fuente, []);

    /// <summary>Encabezados de la hoja «Vehículos» para las fuentes dadas (códigos, en el orden de la grilla) y la cola <see cref="SinFuente"/>.</summary>
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

        encabezados.AddRange(SinFuente);
        return encabezados;
    }
}
