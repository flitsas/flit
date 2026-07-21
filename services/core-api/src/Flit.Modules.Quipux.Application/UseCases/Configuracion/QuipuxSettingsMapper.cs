using Flit.Modules.Quipux.Domain.Configuracion;

namespace Flit.Modules.Quipux.Application.UseCases.Configuracion;

/// <summary>
/// Proyecta el modelo de dominio <see cref="QuipuxSettings"/> (con secretos en claro en memoria) a
/// la <see cref="QuipuxSettingsView"/> que sale hacia la API, redactando los secretos a un simple
/// "hay / no hay". El texto en claro nunca cruza esta frontera.
/// </summary>
internal static class QuipuxSettingsMapper
{
    public static QuipuxSettingsView ToView(QuipuxSettings s) => new(
        Enabled: s.Enabled,
        UrlLogin: s.UrlLogin,
        UrlRegisterDocument: s.UrlRegisterDocument,
        UrlValidateStatus: s.UrlValidateStatus,
        Username: s.Username,
        HasPassword: !string.IsNullOrWhiteSpace(s.Password),
        ConsumerCode: s.ConsumerCode,
        Bucket: s.Bucket,
        S3Prefix: s.S3Prefix,
        AwsRegion: s.AwsRegion,
        AwsAccessKeyId: s.AwsAccessKeyId,
        HasAwsSecretAccessKey: !string.IsNullOrWhiteSpace(s.AwsSecretAccessKey),
        OfficerDocumentType: s.OfficerDocumentType,
        OfficerDocumentNumber: s.OfficerDocumentNumber,
        RegisterIntervalMinutes: s.RegisterIntervalMinutes,
        PollIntervalMinutes: s.PollIntervalMinutes,
        BatchSize: s.BatchSize,
        MaxAttempts: s.MaxAttempts,
        MaxPolls: s.MaxPolls,
        TimeoutSeconds: s.TimeoutSeconds,
        EstaCompleta: s.EstaCompleta(),
        UpdatedAt: s.UpdatedAt);
}
