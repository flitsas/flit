using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Orchestration;
using Flit.DataMigration.V1.Storage;
using Microsoft.Extensions.Configuration;

namespace Flit.DataMigration.V1.Configuration;

/// <summary>
/// Toda la configuración del migrador, ya leída y validada.
/// <para>
/// <see cref="Bind"/> es estático y es el ÚNICO camino de lectura, a propósito: la consola y el
/// host HTTP lo comparten. Si cada host bindeara por su cuenta, el día que alguien añadiera una
/// clave se acordaría de un sitio y no del otro, y el comando y el endpoint migrarían distinto
/// sin que nadie se enterara hasta ver los datos.
/// </para>
/// <para>
/// Por eso tampoco es un <c>IOptions&lt;T&gt;</c> al uso: el enlace vive aquí, no en el arranque
/// de cada host.
/// </para>
/// </summary>
public sealed record MigrationSettings
{
    public required string V1Connection { get; init; }

    public required string V2Connection { get; init; }

    public string BatchId { get; init; } = "sin-lote";

    public string SystemUserEmail { get; init; } = "migracion.v1@flitsas.io";

    /// <summary>
    /// SOLO laboratorio. Un NIT sin tenant es cuarentena, no una empresa que se cree sola:
    /// <c>tenant_id</c> es NOT NULL pero SIN foreign key, así que la base no frena un id
    /// inventado y RLS escondería los trámites de su dueño real.
    /// </summary>
    public bool CreateTenantIfMissing { get; init; }

    public string AttachmentsMode { get; init; } = "Copy";

    public FileManagerEndpoint SourceFileManager { get; init; } = new();

    public FileManagerEndpoint TargetFileManager { get; init; } = new();

    /// <summary>
    /// El <c>Path</c> puede venir vacío: lo decide el tipo de trámite. Se resuelve en
    /// <see cref="ValidateForDocuments"/>, que es quien conoce el <see cref="V1ProcedureKind"/>.
    /// </summary>
    public V1SnapshotEndpoint Snapshot { get; init; } = new();

    /// <summary>
    /// Lee la configuración con las MISMAS claves de siempre. El prefijo de entorno
    /// (<c>FLITMIG_</c>) lo aplica cada host al construir su <see cref="IConfiguration"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si falta alguna cadena de conexión.</exception>
    public static MigrationSettings Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new MigrationSettings
        {
            V1Connection = Require(configuration, "ConnectionStrings:V1Source"),
            V2Connection = Require(configuration, "ConnectionStrings:V2Target"),
            BatchId = configuration["Migration:BatchId"] ?? "sin-lote",
            SystemUserEmail = configuration["Migration:SystemUserEmail"] ?? "migracion.v1@flitsas.io",
            CreateTenantIfMissing = configuration.GetValue("Migration:CreateTenantIfMissing", false),
            AttachmentsMode = configuration["Attachments:Mode"] ?? "Copy",
            SourceFileManager = ReadEndpoint(configuration, "SourceFileManager"),
            TargetFileManager = ReadEndpoint(configuration, "TargetFileManager"),
            Snapshot = new V1SnapshotEndpoint
            {
                BaseUrl = configuration["V1Snapshot:BaseUrl"] ?? string.Empty,
                Path = Blank(configuration["V1Snapshot:Path"]) ?? string.Empty,
                AuthToken = configuration["V1Snapshot:AuthToken"] ?? string.Empty,
                Include = configuration["V1Snapshot:Include"] ?? "generated",
                Consolidated = configuration["V1Snapshot:Consolidated"] ?? "auto",
            },
        };
    }

    /// <summary>
    /// Comprueba lo que exige la instancia 2. Los mensajes son los que el operador lleva años
    /// leyendo: cambiarlos rompería los runbooks, así que están fijados por test.
    /// </summary>
    public (CopyMode Mode, MigrationFailure? Failure) ValidateForAttachments()
    {
        if (!Enum.TryParse<CopyMode>(AttachmentsMode, ignoreCase: true, out var mode))
        {
            return (default, new MigrationFailure(
                MigrationFailureKind.BadConfiguration,
                "migracion.config.modo_adjuntos_invalido",
                $"Attachments:Mode '{AttachmentsMode}' inválido. Use 'Copy' o 'Reference'."));
        }

        if (!SourceFileManager.IsConfigured)
        {
            return (mode, new MigrationFailure(
                MigrationFailureKind.EnvironmentNotReady,
                "migracion.config.falta_file_manager_origen",
                "Falta SourceFileManager:BaseUrl (file-manager ORIGEN, donde viven los binarios de V1)."));
        }

        if (mode == CopyMode.Copy && !TargetFileManager.IsConfigured)
        {
            return (mode, new MigrationFailure(
                MigrationFailureKind.EnvironmentNotReady,
                "migracion.config.falta_file_manager_destino",
                "El modo Copy exige TargetFileManager:BaseUrl (file-manager DESTINO de V2)."));
        }

        return (mode, null);
    }

    /// <summary>
    /// Comprueba lo que exige la instancia 3 y resuelve la ruta del snapshot.
    /// <para>
    /// La ruta por defecto la fija el tipo de trámite: traspaso y matrícula tienen endpoints
    /// distintos en V1. Solo se sobrescribe si la config trae un valor real.
    /// </para>
    /// </summary>
    public (V1SnapshotEndpoint Endpoint, MigrationFailure? Failure) ValidateForDocuments(V1ProcedureKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        var endpoint = new V1SnapshotEndpoint
        {
            BaseUrl = Snapshot.BaseUrl,
            Path = Blank(Snapshot.Path) ?? kind.SnapshotPath,
            AuthToken = Snapshot.AuthToken,
            Include = Snapshot.Include,
            Consolidated = Snapshot.Consolidated,
        };

        if (!endpoint.IsConfigured)
        {
            return (endpoint, new MigrationFailure(
                MigrationFailureKind.EnvironmentNotReady,
                "migracion.config.falta_snapshot_v1",
                $"Falta V1Snapshot:BaseUrl (API de V1 que materializa los documentos generados de {kind.Nombre})."));
        }

        if (!TargetFileManager.IsConfigured)
        {
            return (endpoint, new MigrationFailure(
                MigrationFailureKind.EnvironmentNotReady,
                "migracion.config.falta_file_manager_destino",
                "Falta TargetFileManager:BaseUrl (file-manager DESTINO de V2)."));
        }

        return (endpoint, null);
    }

    private static FileManagerEndpoint ReadEndpoint(IConfiguration configuration, string section) =>
        new()
        {
            BaseUrl = configuration[$"{section}:BaseUrl"] ?? string.Empty,
            FilesPath = configuration[$"{section}:FilesPath"] ?? "api/v1/files",
            AuthToken = configuration[$"{section}:AuthToken"] ?? string.Empty,
        };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Require(IConfiguration configuration, string key) =>
        configuration[key] ?? throw new InvalidOperationException(
            $"Falta la configuración '{key}' (appsettings.json o variable FLITMIG_{key.Replace(':', '_')}).");
}
