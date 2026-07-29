using Flit.DataMigration.V1.Source;

namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// TODO lo que cambia entre un tipo de trámite de V1 y otro, en un solo objeto.
///
/// <para>
/// Existe porque la lista de cosas que divergen entre traspaso y matrícula resultó ser larga y
/// —esto es lo importante— <b>silenciosa</b>: la tabla de origen, el catálogo de estados, el mapa de
/// adjuntos, las partes con validación de identidad, el tipo de trámite en V2 y el endpoint de
/// snapshot. Cada una de ellas, si se toma la del trámite equivocado, produce una migración que
/// termina en verde y con datos falsos. Ya pasó una vez: reusar el mapa de estados de traspaso
/// habría migrado los 12.251 aprobados de matrícula como RECHAZADOS.
/// </para>
///
/// <para>
/// Con el descriptor, elegir el tipo de trámite es un único <c>switch</c> sobre <c>--tipo</c> y
/// todo lo demás viaja junto. No hay forma de emparejar la tabla de matrícula con el mapa de
/// traspaso sin editar este archivo a propósito.
/// </para>
/// </summary>
public sealed class V1ProcedureKind
{
    /// <summary>Nombre para el operador: "traspaso", "matrícula inicial".</summary>
    public required string Nombre { get; init; }

    /// <summary>Prefijo en la línea de comandos: <c>transfer</c>, <c>registration</c>.</summary>
    public required string CliName { get; init; }

    /// <summary>Master + historial en V1.</summary>
    public required V1SourceTables Tables { get; init; }

    /// <summary>Código del tipo de trámite en el catálogo de V2.</summary>
    public required string ProcedureTypeCode { get; init; }

    /// <summary>Catálogo de estados de V1 → estados de V2.</summary>
    public required IV1StateMap StateMap { get; init; }

    /// <summary>Columnas <c>id_attach*</c> → <c>tipo</c> de adjunto de V2.</summary>
    public required V1AttachmentMap AttachmentMap { get; init; }

    /// <summary>Qué imágenes de identidad absorbe la carta selfie, y de qué partes.</summary>
    public required IdentityAttachmentPolicy IdentityPolicy { get; init; }

    /// <summary>Ruta por defecto del endpoint de snapshot de V1 (instancia 3).</summary>
    public required string SnapshotPath { get; init; }

    /// <summary>Mapea un registro de V1 al grafo de entidades de V2.</summary>
    public required Func<V1SourceRecord, MappingContext, MappedProcedure> Map { get; init; }

    public static readonly V1ProcedureKind Transfer = new()
    {
        Nombre = "traspaso",
        CliName = "transfer",
        Tables = V1SourceTables.Transfer,
        ProcedureTypeCode = "TRASPASO_STANDARD",
        StateMap = TransferStateMap.Instance,
        AttachmentMap = TransferAttachmentMap.Instance,
        IdentityPolicy = IdentityAttachmentPolicy.Transfer,
        SnapshotPath = "api/v1/vehicle-transfer-migration",
        Map = TransferMapper.Map,
    };

    /// <summary>
    /// Matrícula inicial. El tipo destino <c>MATRICULA_INICIAL</c> corresponde a la tipología
    /// <c>matricula_inicial</c> del catálogo de V2 (5 pasos, un solo adquirente), que es la que
    /// resuelve el wizard para <c>modalidad_entrada = matricula_inicial</c>.
    /// </summary>
    public static readonly V1ProcedureKind Registration = new()
    {
        Nombre = "matrícula inicial",
        CliName = "registration",
        Tables = V1SourceTables.Registration,
        ProcedureTypeCode = "MATRICULA_INICIAL",
        StateMap = RegistrationStateMap.Instance,
        AttachmentMap = RegistrationAttachmentMap.Instance,
        IdentityPolicy = IdentityAttachmentPolicy.Registration,
        SnapshotPath = "api/v1/vehicle-registration-migration",
        Map = RegistrationMapper.Map,
    };

    public static readonly IReadOnlyList<V1ProcedureKind> All = [Transfer, Registration];

    /// <summary>
    /// ¿La columna es una referencia a un adjunto del File Manager de V1? La convención de nombres
    /// es la misma en las dos tablas.
    /// </summary>
    public static bool IsAttachmentColumn(string column) =>
        column.StartsWith("id_attach", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Qué instancia del migrador se está corriendo.</summary>
public enum MigrationInstance
{
    /// <summary>Instancia 1: data plana (campos, actores, historial, estado).</summary>
    Data = 1,

    /// <summary>Instancia 2: adjuntos que el cliente cargó y V1 guardó.</summary>
    Attachments = 2,

    /// <summary>Instancia 3: documentos que V1 genera en caliente y nunca guarda.</summary>
    Documents = 3,
}
