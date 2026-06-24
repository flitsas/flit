namespace Flit.Infrastructure.Storage;

/// <summary>
/// Config del file-manager de la empresa (almacenamiento de adjuntos en S3 vía presigned URLs).
/// La API es de borde tras API Gateway; flit es solo cliente y NO maneja credenciales AWS ni bucket.
/// </summary>
public sealed class FileManagerOptions
{
    public const string SectionName = "FileManager";

    /// <summary>URL base del API Gateway del file-manager (debe terminar en '/').</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Path del recurso de archivos relativo a <see cref="BaseUrl"/> (sin '/' inicial).</summary>
    public string FilesPath { get; set; } = "api/v1/files";

    /// <summary>Categoría (prefijo de carpeta en S3) con la que se crean los adjuntos de trámites.</summary>
    public string Category { get; set; } = "tramites";

    /// <summary>Timeout HTTP en segundos para las llamadas al file-manager y a S3.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Token Bearer opcional. Hoy el file-manager es público (sin auth); si se reactiva,
    /// se setea aquí (o por env FILE_MANAGER_AUTH_TOKEN) y se envía SOLO a la API del
    /// file-manager, nunca a las presigned URLs de S3.
    /// </summary>
    public string AuthToken { get; set; } = string.Empty;
}
