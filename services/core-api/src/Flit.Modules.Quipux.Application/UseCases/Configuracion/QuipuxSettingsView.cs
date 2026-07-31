namespace Flit.Modules.Quipux.Application.UseCases.Configuracion;

/// <summary>
/// Vista de solo lectura de <c>admin.quipux_settings</c> para la consola de administración. NUNCA
/// expone los secretos (<c>password</c>, <c>awsSecretAccessKey</c>): solo indica si HAY uno cargado
/// (<see cref="HasPassword"/> / <see cref="HasAwsSecretAccessKey"/>), igual que el patrón de UX de
/// cualquier formulario de credenciales — el front sabe si existe un secreto, no cuál es.
/// </summary>
public sealed record QuipuxSettingsView(
    bool Enabled,
    string UrlLogin,
    string UrlRegisterDocument,
    string UrlValidateStatus,
    string Username,
    bool HasPassword,
    string ConsumerCode,
    string Bucket,
    string S3Prefix,
    string AwsRegion,
    string AwsAccessKeyId,
    bool HasAwsSecretAccessKey,
    int OfficerDocumentType,
    string OfficerDocumentNumber,
    int RegisterIntervalMinutes,
    int PollIntervalMinutes,
    int BatchSize,
    int MaxAttempts,
    int MaxPolls,
    int TimeoutSeconds,
    bool EstaCompleta,
    DateTimeOffset? UpdatedAt);
