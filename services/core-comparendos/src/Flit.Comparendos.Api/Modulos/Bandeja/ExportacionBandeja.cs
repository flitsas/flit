using Flit.Comparendos.Api.Infraestructura.Excel;
using Flit.Comparendos.Domain.Tiempo;
using Cell = Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;

namespace Flit.Comparendos.Api.Modulos.Bandeja;

/// <summary>
/// Exportación de la bandeja (§6.20, RN-184; HU #13871): exactamente lo filtrado, con el estado legal calculado a la fecha
/// de generación, en archivos de hasta 5.000 filas. El navegador pide cada parte y la descarga en el mismo clic, como la
/// exportación de Consultas de Trámites: no hay cola ni correo. Cada archivo trae solo las columnas visibles de la tabla.
/// </summary>
public sealed class ExportacionBandeja(ConsultaBandeja consulta)
{
    public const int TamanoParte = 5000;

    /// <summary>
    /// Las columnas del Excel por clave de columna de la tabla (components/bandeja/columnas.tsx), en el orden de la tabla.
    /// Algunas columnas de la tabla son dos en el Excel (NIT y titular, estado legal y días, radicado y resultado del DP).
    /// </summary>
    private static readonly (string Clave, (string Titulo, int Ancho, Func<FilaBandeja, Cell> Valor)[] Celdas)[] Catalogo =
    [
        ("numero", [("Número", 22, f => Cell.Of(f.Numero))]),
        ("placa", [("Placa", 10, f => Cell.Of(f.Placa))]),
        ("titular", [("NIT consultado", 14, f => Cell.Of(ConDv(f.NitConsultado))), ("Titular", 30, f => Cell.Of(f.Titular))]),
        ("ot", [("Organismo de tránsito", 34, f => Cell.Of(f.Organismo ?? f.SecretariaTexto))]),
        ("imposicion", [("Fecha imposición", 13, f => Fecha(f.FechaImposicion))]),
        ("notificacion", [("Fecha notificación", 13, f => Fecha(f.FechaNotificacion))]),
        ("valor", [("Valor total", 14, f => f.ValorTotal is { } v ? Cell.Of((double)v) : Cell.Empty)]),
        ("fuente", [("Fuente", 11, f => Cell.Of(Texto(f.Fuente)))]),
        ("semaforo", [("Estado legal", 13, f => Cell.Of(Texto(f.EstadoLegal))), ("Días hábiles restantes", 10, f => f.DiasHabilesRestantes is { } d ? Cell.Of(d) : Cell.Empty)]),
        ("ventana", [("Ventana de descuento", 14, f => Cell.Of(Texto(f.VentanaDescuento)))]),
        ("finDescuento", [("Fin del descuento", 13, f => Fecha(f.FinDescuentoVigente))]),
        ("proceso", [("Estado del proceso", 18, f => Cell.Of(Texto(f.EstadoProceso)))]),
        ("contraventor", [("Contraventor", 28, f => Cell.Of(f.ContraventorNombre))]),
        ("aviso", [("Último hito de notificación", 30, f => Cell.Of(ConsultaBandeja.TextoHito(f.UltimoHito)))]),
        ("cambio", [("Cambio de contraventor", 16, f => Cell.Of(Texto(f.CambioContraventor)))]),
        ("dp", [("Radicado DP", 18, f => Cell.Of(f.DpRadicado)), ("Resultado DP", 14, f => Cell.Of(Texto(f.DpResultado)))]),
        ("pagado", [("Pagado", 8, f => Cell.Of(f.Pagado ? "Sí" : "No"))]),
        ("activo", [("Activo", 8, f => Cell.Of(f.Activo ? "Sí" : "No"))]),
        ("cobroCoactivo", [("Cobro coactivo", 10, f => Cell.Of(f.EnCobroCoactivo ? "Sí" : "No"))]),
        ("actividad", [("Última actividad", 17, f => Cell.Of(f.UltimoEventoEn.ToOffset(ColombiaTime.Offset).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)))]),
    ];

    /// <summary>
    /// Una parte de la exportación (desde 1). <paramref name="columnas"/> son las claves de la tabla en el orden en que se
    /// ven; las desconocidas se ignoran y sin ninguna conocida salen todas.
    /// </summary>
    public async Task<byte[]> GenerarAsync(Guid? tenant, FiltrosBandeja filtros, int parte, IReadOnlyList<string>? columnas, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filtros);
        var h = await consulta.HoyAsync(ct).ConfigureAwait(false);
        var q = ConsultaBandeja.Ordenar(consulta.Filtrar(tenant, filtros, h), filtros.Orden, filtros.Descendente, h.Dia)
            .Skip((Math.Max(parte, 1) - 1) * TamanoParte).Take(TamanoParte);
        var filas = await consulta.ProyectarAsync(q, h, ct).ConfigureAwait(false);
        var celdas = Elegidas(columnas).SelectMany(c => c.Celdas).ToList();
        var hoja = new TabularWorkbookWriter.Sheet($"Comparendos {h.Dia:yyyy-MM-dd}",
            celdas.Select(c => new TabularWorkbookWriter.Column(c.Titulo, c.Ancho)).ToList(),
            filas.Select(f => (IReadOnlyList<Cell>)celdas.Select(c => c.Valor(f)).ToList()).ToList());
        return TabularWorkbookWriter.Write([hoja]);
    }

    private static IEnumerable<(string Clave, (string Titulo, int Ancho, Func<FilaBandeja, Cell> Valor)[] Celdas)> Elegidas(IReadOnlyList<string>? columnas)
    {
        var elegidas = (columnas ?? []).Distinct(StringComparer.Ordinal).Select(c => Catalogo.FirstOrDefault(x => x.Clave == c)).Where(x => x.Clave is not null).ToList();
        return elegidas.Count > 0 ? elegidas : Catalogo;
    }

    private static Cell Fecha(DateOnly? d) => d is { } x ? Cell.Of(x) : Cell.Empty;

    /// <summary>
    /// Los valores internos como los ve el usuario en la bandeja (hallazgo B5-3; mismos textos que lib/etiquetas.ts del
    /// frontend). Lo que no está en la tabla sale tal cual.
    /// </summary>
    private static readonly Dictionary<string, string> Etiquetas = new(StringComparer.Ordinal)
    {
        ["Simit"] = "SIMIT", ["Municipal"] = "Municipal", ["Ocr"] = "OCR", ["MigracionV1"] = "V1",
        ["SinNotificar"] = "Sin notificación", ["EnTiempo"] = "En tiempo", ["Vencido"] = "Vencido", ["Caducado"] = "Caducado", ["Prescrito"] = "Prescrito",
        ["Descuento50"] = "Descuento 50%", ["Descuento25"] = "Descuento 25%", ["SinDescuento"] = "Sin descuento", ["SinFechaBase"] = "Sin fecha base",
        ["SinProcesar"] = "Sin procesar", ["DerechoPeticion"] = "Derecho de petición", ["RespuestaFavorable"] = "Respuesta OT: favorable",
        ["RespuestaDesfavorable"] = "Respuesta OT: desfavorable", ["NoEncontrado"] = "No encontrado en fuente", ["Inactivado"] = "Inactivado",
        ["NoAplica"] = "No aplica", ["SinSolicitar"] = "Sin solicitar", ["Solicitado"] = "En proceso", ["RechazadoOt"] = "Rechazado OT", ["AceptadoOt"] = "Exitoso",
    };

    internal static string? Texto(string? valor) => valor is null ? null : Etiquetas.GetValueOrDefault(valor, valor);

    /// <summary>El NIT consultado como en la pantalla: «811011779-8».</summary>
    internal static string? ConDv(string? nit) =>
        nit is { Length: > 0 } && nit.All(char.IsAsciiDigit) ? $"{nit}-{Flit.Comparendos.Domain.Identificadores.Nit.DigitoVerificacion(nit)}" : nit;
}
