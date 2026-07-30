using Flit.DataMigration.V1.Mapping;

namespace Flit.DataMigration.V1.Orchestration;

/// <summary>
/// Orden de trabajo, agnóstica del host: qué trámites migrar, de qué tipo, en qué instancia y
/// con qué opciones. La consola la arma desde <c>MigrationArgs</c>; el API, desde la ruta y el
/// query string.
/// <para>
/// El migrador NO decide qué se migra: recibe esta orden y la ejecuta tal cual, sin filtrar por
/// estado ni por compañía. La selección es una decisión de negocio que vive fuera de aquí.
/// </para>
/// </summary>
/// <param name="Kind">Traspaso o matrícula. Emparejа tabla de origen, catálogo de estados y mapa de adjuntos.</param>
/// <param name="Instance">Data plana, adjuntos o documentos generados.</param>
/// <param name="Ids">Ids de V1, sin duplicados y en el orden en que se pidieron.</param>
/// <param name="DryRun">Simula y revierte: no escribe en V2 ni sube binarios.</param>
/// <param name="Force">Re-migra borrando lo anterior. NO se expone por HTTP.</param>
/// <param name="KeepIdentityImages">Migra las imágenes sueltas de identidad además de la carta selfie.</param>
/// <param name="BatchId">Etiqueta del lote: es la columna por la que se audita y se revierte una tanda.</param>
/// <param name="Tipo">
/// El <c>--tipo</c> tal cual lo escribió el operador («transfer-attachments»). Solo se usa para
/// el encabezado del reporte; el API lo sintetiza desde la ruta.
/// </param>
public sealed record MigrationRequest(
    V1ProcedureKind Kind,
    MigrationInstance Instance,
    IReadOnlyList<long> Ids,
    bool DryRun,
    bool Force,
    bool KeepIdentityImages,
    string BatchId,
    string Tipo);
