using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using Flit.Queries.Domain.Time;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Export Excel del reporte detallado (HU #10816) — streaming OpenXml.
/// <para>
/// HU #12360 (Feature #12257): <see cref="ExportNetworkAsync"/> reutiliza el MISMO generador y las
/// mismas columnas precedidas por «Compañía» sobre el repositorio de red; la salida de
/// <see cref="ExportAsync"/> (cabeceras, celdas, hoja, comportamiento sin filas) no cambia (AC7).
/// </para>
/// </summary>
internal sealed class DetailedReportExcelExporter : IDetailedReportExcelExporter
{
    public const string ContentType = ProcedureExcelExporter.ContentType;

    /// <summary>Cabecera de la columna del cliente dueño en el reporte de red (HU #12360, AC1).</summary>
    public const string NetworkCompanyHeader = "Compañía";

    private static readonly string[] Headers =
    [
        "Referencia", "Tipo de trámite", "Categoría", "Estado", "Radicado por",
        "Persona documento", "Persona nombre", "Transformación", "Detalle transformación",
        "Leasing", "Tipo pago", "Tipo traspaso", "Enviado", "Completado",
        // HU #14319 — al final, para no mover las columnas que ya existían.
        "Canal", "OT del canal",
    ];

    /// <summary>Cabeceras del reporte de red: «Compañía» + las de siempre, en el mismo orden.</summary>
    public static readonly string[] NetworkHeaders = [NetworkCompanyHeader, .. Headers];

    private readonly IDetailedReportReadRepository _repo;
    private readonly INetworkDetailedReportReadRepository _networkRepo;

    public DetailedReportExcelExporter(IDetailedReportReadRepository repo, INetworkDetailedReportReadRepository networkRepo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _networkRepo = networkRepo ?? throw new ArgumentNullException(nameof(networkRepo));
    }

    public async Task ExportAsync(Stream output, DetailedReportFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(filter);

        // Paso previo: el máximo de actores por rol sobre TODO el filtro fija el ancho del bloque de validación de
        // identidad (igual para toda la hoja). Crece solo si hace falta.
        var max = await _repo.GetMaxActoresPorRolAsync(filter, ct).ConfigureAwait(false);

        var wroteRows = await WriteWorkbookAsync(
            output,
            DetalleHeaders(Headers, max),
            (writeRow, token) => _repo.ExportProceduresAsync(
                filter,
                (row, _) =>
                {
                    writeRow([.. ToCells(row), .. ActoresCells(row, max)]);
                    return Task.CompletedTask;
                },
                token),
            requireRows: true,
            ct,
            numericColumns: IntentosColumns(DetalleHeaders(Headers, max))).ConfigureAwait(false);

        if (!wroteRows)
            throw new InvalidOperationException("no_records");
    }

    public async Task<IReadOnlyList<Guid>> ExportNetworkAsync(Stream output, NetworkDetailedReportFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(filter);

        IReadOnlyList<Guid> reached = [];
        var max = await _networkRepo.GetNetworkMaxActoresPorRolAsync(filter, ct).ConfigureAwait(false);
        await WriteWorkbookAsync(
            output,
            DetalleHeaders(NetworkHeaders, max),
            async (writeRow, token) =>
            {
                // AC5 — la misma consulta y el mismo filtro resuelto que el listado; AC4 — un conjunto
                // vacío no llega a la base y deja un archivo solo con cabecera.
                reached = await _networkRepo.ExportNetworkProceduresAsync(
                    filter,
                    (row, _) =>
                    {
                        writeRow([.. ToNetworkCells(row), .. ActoresCells(row.ToRow(), max)]);
                        return Task.CompletedTask;
                    },
                    token).ConfigureAwait(false);
            },
            requireRows: false,
            ct,
            numericColumns: IntentosColumns(DetalleHeaders(NetworkHeaders, max))).ConfigureAwait(false);

        return reached;
    }

    /// <summary>
    /// Escribe el libro (una hoja «Reporte detallado» con <paramref name="headers"/> + filas) en un
    /// archivo temporal y lo copia a <paramref name="output"/>. Devuelve si hubo filas. Con
    /// <paramref name="requireRows"/> y sin filas no copia nada (el llamador decide el error).
    /// </summary>
    private static async Task<bool> WriteWorkbookAsync(
        Stream output,
        IReadOnlyList<string> headers,
        Func<Action<IReadOnlyList<string>>, CancellationToken, Task> writeRowsAsync,
        bool requireRows,
        CancellationToken ct,
        IReadOnlySet<int>? numericColumns = null)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"flit-detailed-report-{Guid.NewGuid():N}.xlsx");
        var wroteRows = false;
        try
        {
            using (var document = SpreadsheetDocument.Create(tempPath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

                using (var writer = OpenXmlWriter.Create(worksheetPart))
                {
                    writer.WriteStartElement(new Worksheet());
                    writer.WriteStartElement(new SheetData());
                    WriteRow(writer, headers, null);

                    await writeRowsAsync(
                        cells =>
                        {
                            wroteRows = true;
                            WriteRow(writer, cells, numericColumns);
                        },
                        ct).ConfigureAwait(false);

                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.Close();
                }

                if (requireRows && !wroteRows)
                    return false;

                workbookPart.Workbook = new Workbook(
                    new Sheets(new Sheet
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = 1U,
                        Name = "Reporte detallado",
                    }));
                workbookPart.Workbook.Save();
            }

            await using var file = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None,
                bufferSize: 81920, useAsync: true);
            await file.CopyToAsync(output, ct).ConfigureAwait(false);
            return wroteRows;
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Cabeceras de la hoja: las de siempre + (después de Canal / OT del canal) los dos grupos de validación de
    /// identidad, Comprador y Vendedor, cada uno agrupado por persona. Contrato v3.
    /// </summary>
    private static IReadOnlyList<string> DetalleHeaders(IReadOnlyList<string> baseHeaders, MaxActoresPorRolDto max) =>
    [
        .. baseHeaders,
        .. ActoresExcelColumns.Headers(max, ActoresExcelColumns.Grupo.Comprador),
        .. ActoresExcelColumns.Headers(max, ActoresExcelColumns.Grupo.Vendedor),
    ];

    private static IReadOnlyList<string> ActoresCells(DetailedProcedureRowDto row, MaxActoresPorRolDto max) =>
    [
        .. ActoresExcelColumns.Cells(row.Actores, max, ActoresExcelColumns.Grupo.Comprador),
        .. ActoresExcelColumns.Cells(row.Actores, max, ActoresExcelColumns.Grupo.Vendedor),
    ];

    /// <summary>Posiciones de las columnas «Intentos …»: son las únicas que se escriben como número.</summary>
    private static HashSet<int> IntentosColumns(IReadOnlyList<string> headers) =>
        headers.Select((h, i) => (h, i)).Where(x => x.h.StartsWith("Intentos ", StringComparison.Ordinal))
            .Select(x => x.i).ToHashSet();

    private static string[] ToCells(DetailedProcedureRowDto r) =>
    [
        r.ReferenceNumber,
        r.ProcedureTypeName,
        r.Category,
        r.Status,
        r.CreatedByDisplayName,
        r.PersonDocument,
        r.PersonFullName,
        r.HasTransformation ? "Sí" : "No",
        r.TransformationDetail ?? string.Empty,
        r.IsLeasing ? "Sí" : "No",
        r.PaymentType,
        r.TransferType ?? string.Empty,
        FormatoFecha.Instante(r.SubmittedAt),
        FormatoFecha.Instante(r.CompletedAt),
        // HU #14319 — nombre visible del catálogo; un código ajeno al catálogo se muestra tal cual en vez de ocultarse.
        TramiteCanal.NombreVisible(TramiteCanal.Efectivo(r.Channel)) ?? r.Channel,
        r.ChannelTransitOfficeName ?? string.Empty,
    ];

    /// <summary>«Compañía» + las mismas celdas de siempre (AC1/AC6: ningún enlace ni contenido de documentos).</summary>
    private static string[] ToNetworkCells(NetworkDetailedProcedureRowDto r) => [r.TenantName, .. ToCells(r.ToRow())];

    private static void WriteRow(OpenXmlWriter writer, IReadOnlyList<string> values, IReadOnlySet<int>? numericColumns)
    {
        writer.WriteStartElement(new Row());
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            // Solo las columnas marcadas (Intentos) van como número, para poder sumarlas y filtrarlas; el resto del
            // archivo conserva su convención de texto. Un valor vacío o no numérico se queda como texto.
            if (numericColumns is not null && numericColumns.Contains(i)
                && double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                writer.WriteElement(new Cell
                {
                    CellValue = new CellValue(number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                });
                continue;
            }

            var cell = new Cell { DataType = CellValues.InlineString };
            cell.AppendChild(new InlineString(new Text(value)));
            writer.WriteElement(cell);
        }

        writer.WriteEndElement();
    }
}
