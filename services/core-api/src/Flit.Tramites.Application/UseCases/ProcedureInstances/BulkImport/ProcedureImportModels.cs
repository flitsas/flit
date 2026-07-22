namespace Flit.Tramites.Application.UseCases.ProcedureInstances.BulkImport;

/// <summary>
/// Una fila del archivo de importación masiva de trámites (Excel/CSV). Todas las columnas de
/// negocio son opcionales a nivel de parseo; la obligatoriedad real (indicar la modalidad o el
/// código del tipo) la valida el handler por fila, reutilizando <c>CreateProcedureInstanceHandler</c>.
/// La compañía (tenant) y el usuario NO viajan en el archivo: se resuelven del JWT del que sube.
/// </summary>
/// <param name="RowNumber">Número de fila de datos (1-based, sin contar el encabezado).</param>
public sealed record ProcedureImportRow(
    int RowNumber,
    string? Modalidad,
    string? TipoCodigo,
    string? TransitOfficeCode,
    string? Vin,
    string? Placa);

/// <summary>Resultado de importar una fila: borrador creado o motivo del error.</summary>
/// <param name="Row">Número de fila de datos (1-based).</param>
/// <param name="Input">Etiqueta identificable de la fila (modalidad o código del tipo).</param>
/// <param name="Status"><c>created</c> | <c>failed</c>.</param>
/// <param name="ReferenceNumber">Referencia del trámite creado (<c>TRM-YYYY-NNNNNN</c>), si aplica.</param>
/// <param name="InstanceId">Id del borrador creado, si aplica.</param>
/// <param name="Error">Código de error de la fila cuando <c>Status = failed</c> (o aviso de seed).</param>
public sealed record ProcedureImportRowResult(
    int Row,
    string Input,
    string Status,
    string? ReferenceNumber,
    Guid? InstanceId,
    string? Error);

/// <summary>Reporte agregado de una importación (partial success es normal en lote).</summary>
public sealed record ProcedureImportReport(
    int Total,
    int Created,
    int Failed,
    IReadOnlyList<ProcedureImportRowResult> Results);

/// <summary>
/// Puerto: ¿la compañía tiene habilitada la matrícula inicial? (toggle
/// <c>SwitchesMatricula.AllowInitialRegistration</c>). El adaptador vive en la capa API y envuelve
/// <c>GetTenantSettingsHandler</c>, para no acoplar Tramites.Application con el módulo Admin.
/// </summary>
public interface IMatriculaInicialGate
{
    Task<bool> IsAllowedAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Puerto: resuelve el código de un organismo de tránsito
/// (<c>catalogs.transit_offices.code</c>) a su id. Devuelve <c>null</c> si el código no existe.
/// El adaptador (capa API) envuelve <c>ITransitOfficeCatalog</c>.
/// </summary>
public interface ITransitOfficeCodeResolver
{
    Guid? ResolveId(string code);
}

/// <summary>
/// Puerto: lee un archivo binario de importación (Excel <c>.xlsx</c>) y lo mapea a filas con las
/// mismas reglas que el CSV. La implementación vive en Infrastructure (SDK OpenXML). El CSV NO pasa
/// por aquí (lo maneja <see cref="ProcedureImportCsvParser"/> directamente sobre texto).
/// </summary>
public interface IProcedureImportFileReader
{
    ProcedureImportParseOutcome Read(Stream stream, int maxRows = ProcedureImportRowMapper.DefaultMaxRows);
}

/// <summary>
/// Puerto: genera la plantilla Excel (<c>.xlsx</c>) de importación (encabezado + filas de ejemplo)
/// para que el usuario la descargue. Implementación en Infrastructure (SDK OpenXML).
/// </summary>
public interface IProcedureImportTemplateProvider
{
    byte[] BuildXlsx();
}
