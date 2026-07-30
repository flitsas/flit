using Flit.DataMigration.V1.Cli;
using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Orchestration;
using Flit.DataMigration.V1.Storage;

namespace Flit.DataMigration.Tests.Reporting;

/// <summary>
/// Datos de entrada de los reportes de consola. Cada fixture está construida para tocar TODAS las
/// ramas de su método: cada icono, cada bloque condicional y cada línea de reconciliación.
/// <para>
/// Los GUID son fijos a propósito: un <c>Guid.NewGuid()</c> haría el golden irreproducible.
/// </para>
/// </summary>
internal static class ReportFixtures
{
    private static Guid Id(string prefix) => Guid.Parse($"{prefix}-4000-8000-000000000000");

    /// <summary>Cadena de conexión CON contraseña: el golden debe demostrar que no se filtra.</summary>
    internal const string V1Connection =
        "Host=localhost;Port=5432;Database=pdn_copy_updated;Username=flit;Password=NO-DEBE-APARECER";

    internal const string V2Connection =
        "Host=localhost;Port=5432;Database=flitdev_backup_updated;Username=flit;Password=NO-DEBE-APARECER";

    // --------------------------------------------------------------------- origen

    /// <summary>
    /// Se construye desde <see cref="MigrationArgs"/> igual que lo hace la consola, para que el
    /// golden ejercite el camino real y no una copia paralela.
    /// </summary>
    private static MigrationOrigin Origen(MigrationArgs parsed, string batchId) =>
        MigrationOrigin.From(
            new MigrationRequest(
                parsed.Kind, parsed.Instance, parsed.Ids, parsed.DryRun, parsed.Force,
                parsed.KeepIdentityImages, batchId, parsed.Tipo),
            V1Connection,
            V2Connection);

    /// <summary>11 ids: uno más del umbral, para ejercitar el truncado con «, …».</summary>
    internal static MigrationOrigin OrigenAdjuntosConIdsDeSobra() => Origen(
        MigrationArgs.Parse(
        [
            "--tipo", "registration-attachments",
            "--ids", "101,102,103,104,105,106,107,108,109,110,111",
        ]),
        "dev-20260729-ola-1");

    internal static MigrationOrigin OrigenTraspasoReal() => Origen(
        MigrationArgs.Parse(["--tipo", "transfer", "--ids", "8617"]),
        "pdn-20260805-ola-3");

    private static MigrationOrigin OrigenDatos(bool dryRun) => Origen(
        MigrationArgs.Parse(dryRun
            ? ["--tipo", "registration", "--ids", "101,102,103,104", "--dry-run"]
            : ["--tipo", "registration", "--ids", "101,102,103,104"]),
        "dev-20260729-ola-1");

    private static MigrationOrigin OrigenAdjuntos(bool dryRun) => Origen(
        MigrationArgs.Parse(dryRun
            ? ["--tipo", "registration-attachments", "--ids", "201,202,203,204,205", "--dry-run"]
            : ["--tipo", "registration-attachments", "--ids", "201,202,203,204,205"]),
        "dev-20260729-ola-1");

    private static MigrationOrigin OrigenDocumentos(bool dryRun) => Origen(
        MigrationArgs.Parse(dryRun
            ? ["--tipo", "registration-documents", "--ids", "301,302,303,304,305", "--dry-run"]
            : ["--tipo", "registration-documents", "--ids", "301,302,303,304,305"]),
        "dev-20260729-ola-1");

    // ------------------------------------------------------ instancia 1: data plana

    internal static DataInstanceReport DataReport(bool dryRun, IReadOnlyList<string> provisioned) =>
        new(OrigenDatos(dryRun), DataResults(), provisioned);

    /// <summary>Los cuatro <see cref="LoadStatus"/>, con avisos, motivo y conteos.</summary>
    private static List<LoadResult> DataResults() =>
    [
        new()
        {
            V1Id = 101,
            Status = LoadStatus.Migrated,
            V2Id = Id("11111111-1111"),
            FinalStatus = "aprobado",
            FieldCount = 42,
            ActorCount = 3,
            HistoryCount = 7,
        },
        new()
        {
            V1Id = 102,
            Status = LoadStatus.Simulated,
            V2Id = Id("22222222-2222"),
            FinalStatus = "borrador",
            FieldCount = 18,
            ActorCount = 1,
            HistoryCount = 1,
            Warnings = ["Estado 4 de V1 es ambiguo: se asumió 'preparado'.", "Combustible '99' desconocido."],
        },
        new()
        {
            V1Id = 103,
            Status = LoadStatus.Skipped,
            V2Id = Id("33333333-3333"),
            Reason = "Ya migrado (está en migration_map). Use --force para re-migrar.",
        },
        new()
        {
            V1Id = 104,
            Status = LoadStatus.Quarantined,
            Reason = "NIT '900123456' no existe como tenant en V2.",
            Warnings = ["Multipropietario: V1 trae 2 propietarios y V2 solo modela uno."],
        },
    ];

    /// <summary>Prerrequisitos creados: activa el bloque de cabecera del reporte de datos.</summary>
    internal static List<string> Provisioned() =>
    [
        "usuario de sistema 'migracion.v1@flitsas.io' en identity.users",
        "tenant para el NIT 900123456 (modo laboratorio)",
    ];

    // -------------------------------------------------------- instancia 2: adjuntos

    internal static AttachmentsInstanceReport AttachmentsReport(
        CopyMode mode, bool dryRun, bool keepIdentityImages, IReadOnlyList<string> undeclared) =>
        new(OrigenAdjuntos(dryRun), AttachmentResults(), mode,
            SourceFileManager(), TargetFileManager(), keepIdentityImages, undeclared);

    /// <summary>Los cinco <see cref="AttachmentLoadStatus"/>, con redundantes y fallidos.</summary>
    private static List<AttachmentLoadResult> AttachmentResults() =>
    [
        new()
        {
            V1Id = 201,
            Status = AttachmentLoadStatus.Migrated,
            V2Id = Id("44444444-4444"),
            Copied = 8,
            Skipped = 1,
            Failed = 2,
            Excluded = 3,
            Redundant = 3,
        },
        new()
        {
            V1Id = 202,
            Status = AttachmentLoadStatus.Simulated,
            V2Id = Id("55555555-5555"),
            Copied = 5,
            Warnings = ["id_attach_cedula: el origen respondió 500."],
        },
        new() { V1Id = 203, Status = AttachmentLoadStatus.NoAttachments },
        new()
        {
            V1Id = 204,
            Status = AttachmentLoadStatus.Skipped,
            V2Id = Id("66666666-6666"),
            Reason = "Todos los adjuntos ya estaban migrados.",
        },
        new()
        {
            V1Id = 205,
            Status = AttachmentLoadStatus.NotMigrated,
            Reason = "No existe en la copia de V1.",
        },
    ];

    internal static IReadOnlyList<string> UndeclaredColumns() =>
        ["id_attach_certificado_tradicion", "id_attach_paz_y_salvo"];

    // ------------------------------------------------------ instancia 3: documentos

    internal static DocumentsInstanceReport DocumentsReport(
        bool dryRun, IReadOnlyList<SnapshotLoadResult> results) =>
        new(OrigenDocumentos(dryRun), results, Snapshot(), TargetFileManager());

    /// <summary>Los cinco <see cref="SnapshotLoadStatus"/>, con issues y un trámite vacío.</summary>
    internal static List<SnapshotLoadResult> DocumentResults() =>
    [
        new()
        {
            V1Id = 301,
            Status = SnapshotLoadStatus.Materialized,
            V2Id = Id("77777777-7777"),
            Materialized = 9,
            Skipped = 2,
            Failed = 1,
            Duplicated = 4,
            Issues = ["carta selfie del comprador: V1 no la pudo construir."],
        },
        new()
        {
            V1Id = 302,
            Status = SnapshotLoadStatus.Simulated,
            V2Id = Id("88888888-8888"),
            Materialized = 6,
            Warnings = ["El consolidado llegó degradado (faltan 2 piezas)."],
        },
        // Materializado pero sin una sola pieza: dispara el bloque de 'vacios'.
        new() { V1Id = 303, Status = SnapshotLoadStatus.Materialized, V2Id = Id("99999999-9999") },
        new()
        {
            V1Id = 304,
            Status = SnapshotLoadStatus.NotFoundInV1,
            Reason = "V1 respondió 404 para el snapshot.",
        },
        new()
        {
            V1Id = 305,
            Status = SnapshotLoadStatus.Failed,
            Reason = "V1 respondió 500 al construir el expediente.",
        },
    ];

    // ------------------------------------------------------------------- endpoints

    private static FileManagerEndpoint SourceFileManager() => new()
    {
        BaseUrl = "https://ny46zhyeti.execute-api.us-east-1.amazonaws.com/pdn/",
        FilesPath = "api/v1/files",
        // A propósito: el test del centinela comprueba que este valor NUNCA sale en el reporte.
        AuthToken = "TOKEN-CENTINELA-NO-DEBE-APARECER",
    };

    private static FileManagerEndpoint TargetFileManager() => new()
    {
        BaseUrl = "https://devfilemanager.flitsas.online/",
        FilesPath = "api/v1/files",
        AuthToken = "TOKEN-CENTINELA-NO-DEBE-APARECER",
    };

    private static V1SnapshotEndpoint Snapshot() => new()
    {
        BaseUrl = "https://3g5y4b08il.execute-api.us-east-1.amazonaws.com/dev/",
        Path = "api/v1/vehicle-registration-migration",
        AuthToken = "TOKEN-CENTINELA-NO-DEBE-APARECER",
        Include = "generated",
        Consolidated = "auto",
    };
}
