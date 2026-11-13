using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Flit.Diagnostico.Api.Infraestructura.Excel;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Celda = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;
using Columna = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Column;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>Lo que va encima de la tabla del índice: nombre del lote, instante de generación y cuántos vehículos traen su PDF.</summary>
internal sealed record CabeceraIndiceZip(string NombreDiagnostico, DateTimeOffset GeneradoEn, int ConPdf, int Total);

/// <summary>
/// Una fila del índice antes de pintarla. <see cref="Archivo"/> es el nombre del PDF dentro del ZIP (null si no va);
/// <see cref="Veredicto"/> y <see cref="Alertamientos"/> son los del dictamen vigente (null sin dictamen emitido);
/// <see cref="Tramite"/> es el número del borrador creado desde el diagnóstico (punto 4; null si no hay).
/// </summary>
internal sealed record FilaIndiceZip(int Orden, string Identificador, string Estado, string? Archivo, string? Veredicto, string? Alertamientos, string? Tramite, bool Ajustado = false);

/// <summary>Lo que el índice lee del dictamen vigente de un vehículo: el veredicto y el JSON de alertamientos (código, efecto, fuente).</summary>
internal sealed record DictamenDelIndice(string Veredicto, string Alertamientos, bool Ajustado = false);

/// <summary>
/// Índice del ZIP masivo en Excel (ajustes 2026-11-11, punto 8, decisiones 8.1–8.3): <c>indice.xlsx</c>, una sola hoja. Arriba, el
/// nombre del diagnóstico, la fecha y hora de generación en Colombia y «N de M vehículos con PDF»; debajo, la tabla con encabezado,
/// autofiltro y panel congelado: Orden, Placa/VIN (texto), Estado (rótulo del punto 5), En el ZIP (Sí/No), Archivo (vínculo relativo
/// al PDF dentro del ZIP), Dictamen (Viable/Bloqueado; Pendiente sin dictamen emitido), Motivos de bloqueo (rótulos de las reglas que
/// BLOQUEAN, solo con Bloqueado, separados por «; ») y Trámite. Sin documentos de personas, nombres, origen ni causas de error.
/// <para>
/// Streaming (AC5): <see cref="EscribirAsync"/> escribe cada fila al llegar (<see cref="TabularWorkbookWriter.HojaEnStreaming"/>) y
/// <see cref="FilasAsync"/> lee los dictámenes por páginas de <see cref="TamanoPagina"/> vehículos, solo a medida que se consumen.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var filas = IndiceZip.FilasAsync(plan.Vehiculos, archivos, LeerDictamenesAsync, IndiceZip.TamanoPagina, ct);
/// await IndiceZip.EscribirAsync(destino, new CabeceraIndiceZip(cabecera.Nombre, ahora, archivos.Count, plan.Vehiculos.Count), filas, ct);
/// </code>
/// </example>
internal static class IndiceZip
{
    public const string NombreArchivo = "indice.xlsx";

    public const string NombreHoja = "Índice";

    /// <summary>Fila (base 1) del encabezado de la tabla: tres de cabecera, una en blanco y el encabezado.</summary>
    public const int FilaEncabezado = 5;

    /// <summary>Vehículos por página al leer los dictámenes.</summary>
    public const int TamanoPagina = 500;

    /// <summary>Dictamen de un vehículo sin dictamen emitido.</summary>
    public const string DictamenPendiente = "Pendiente";

    public const string EnElZip = "Sí";

    public const string FueraDelZip = "No";

    public const string SeparadorMotivos = "; ";

    private const string Bloqueado = "BLOQUEADO";

    private const string Bloquea = "BLOQUEA";

    /// <summary>Columna «Archivo» (base 0): la que lleva el vínculo.</summary>
    private const int ColumnaArchivo = 4;

    /// <summary>Encabezados de la tabla, en orden.</summary>
    public static IReadOnlyList<Columna> Columnas { get; } =
    [
        new("Orden", 8), new("Placa/VIN", 20), new("Estado", 14), new("En el ZIP", 10), new("Archivo", 40), new("Dictamen", 12),
        new("Motivos de bloqueo", 60), new("Trámite", 18),
    ];

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Rótulo del veredicto (Viable/Bloqueado); <see cref="DictamenPendiente"/> sin dictamen emitido. Con ajuste FLIT (HU #14563,
    /// D6) suma «(ajustado por FLIT)».
    /// </summary>
    public static string Dictamen(string? veredicto, bool ajustado = false) =>
        veredicto is null
            ? DictamenPendiente
            : ajustado ? $"{RotulosDiagnostico.DeVeredicto(veredicto)} (ajustado por FLIT)" : RotulosDiagnostico.DeVeredicto(veredicto);

    /// <summary>
    /// Rótulos (punto 5) de las reglas con efecto BLOQUEA, en el orden del dictamen y sin repetir, separados por «; »; vacío si el
    /// veredicto no es BLOQUEADO o no hay alertamientos.
    /// </summary>
    public static string Motivos(string? veredicto, string? alertamientos)
    {
        if (veredicto != Bloqueado || string.IsNullOrWhiteSpace(alertamientos))
            return string.Empty;
        var alertas = JsonSerializer.Deserialize<List<AlertaLeida>>(alertamientos, Json) ?? [];
        return string.Join(SeparadorMotivos, alertas
            .Where(a => a.Efecto == Bloquea)
            .Select(a => RotulosDiagnostico.DeAlerta(a.Codigo, a.Fuente))
            .Distinct(StringComparer.Ordinal));
    }

    /// <summary>Escribe el índice en <paramref name="destino"/> (no lo cierra) fila a fila, a medida que <paramref name="filas"/> las entrega.</summary>
    public static async Task EscribirAsync(Stream destino, CabeceraIndiceZip cabecera, IAsyncEnumerable<FilaIndiceZip> filas, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(cabecera);
        ArgumentNullException.ThrowIfNull(filas);
        var generado = cabecera.GeneradoEn.ToOffset(VigenciaDelDia.OffsetColombia);
        IReadOnlyList<IReadOnlyList<Celda>> previas =
        [
            [Celda.Of(cabecera.NombreDiagnostico)],
            [Celda.Of(string.Create(CultureInfo.InvariantCulture, $"Generado el {generado:yyyy-MM-dd HH:mm} (hora Colombia)"))],
            [Celda.Of(string.Create(CultureInfo.InvariantCulture, $"{cabecera.ConPdf} de {cabecera.Total} vehículos con PDF"))],
            [],
        ];
        using var hoja = TabularWorkbookWriter.HojaEnStreaming.Abrir(destino, NombreHoja, previas, Columnas);
        await foreach (var fila in filas.WithCancellation(ct).ConfigureAwait(false))
        {
            hoja.Fila(
            [
                Celda.Of(fila.Orden),
                // Placa o VIN siempre como texto: «0012345» no se convierte en número.
                new Celda.Str(fila.Identificador),
                Celda.Of(fila.Estado),
                Celda.Of(fila.Archivo is null ? FueraDelZip : EnElZip),
                Celda.Of(fila.Archivo),
                Celda.Of(Dictamen(fila.Veredicto, fila.Ajustado)),
                Celda.Of(Motivos(fila.Veredicto, fila.Alertamientos)),
                Celda.Of(fila.Tramite),
            ], (ColumnaArchivo, fila.Archivo));
        }
    }

    /// <summary>
    /// Las filas del índice en el orden de <paramref name="vehiculos"/>: el archivo que quedó en el ZIP (<paramref name="archivos"/>), el
    /// dictamen vigente leído con <paramref name="leerPagina"/> de a <paramref name="tamanoPagina"/> vehículos (la página siguiente
    /// solo cuando se consumió la anterior) y el trámite del vehículo (<see cref="VehiculoDelZip.Tramite"/>).
    /// </summary>
    public static async IAsyncEnumerable<FilaIndiceZip> FilasAsync(
        IReadOnlyList<VehiculoDelZip> vehiculos, IReadOnlyDictionary<Guid, string> archivos,
        Func<IReadOnlyList<Guid>, CancellationToken, Task<IReadOnlyDictionary<Guid, DictamenDelIndice>>> leerPagina, int tamanoPagina,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(vehiculos);
        ArgumentNullException.ThrowIfNull(archivos);
        ArgumentNullException.ThrowIfNull(leerPagina);
        ArgumentOutOfRangeException.ThrowIfLessThan(tamanoPagina, 1);
        for (var desde = 0; desde < vehiculos.Count; desde += tamanoPagina)
        {
            var pagina = vehiculos.Skip(desde).Take(tamanoPagina).ToList();
            var dictamenes = await leerPagina([.. pagina.Select(v => v.Id)], ct).ConfigureAwait(false);
            foreach (var v in pagina)
            {
                var dictamen = dictamenes.GetValueOrDefault(v.Id);
                yield return new FilaIndiceZip(
                    v.Orden, v.Identificador, v.Estado, archivos.GetValueOrDefault(v.Id), dictamen?.Veredicto, dictamen?.Alertamientos, v.Tramite,
                    dictamen?.Ajustado ?? false);
            }
        }
    }

    private sealed record AlertaLeida(string? Codigo, string? Efecto, string? Fuente);
}
