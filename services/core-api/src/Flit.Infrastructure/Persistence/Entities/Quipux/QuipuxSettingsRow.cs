namespace Flit.Infrastructure.Persistence.Entities.Quipux;

/// <summary>
/// Fila de <c>admin.quipux_settings</c>. Es una entidad de persistencia, no el modelo de dominio:
/// existe porque en BD los secretos viven cifrados (<c>password_enc</c>,
/// <c>aws_secret_access_key_enc</c>) y el dominio
/// (<c>Flit.Modules.Quipux.Domain.Configuracion.QuipuxSettings</c>) los expone en claro. El
/// repositorio traduce entre ambos, cifrando al escribir y descifrando al leer, de modo que el
/// texto cifrado nunca escapa de Infrastructure y el claro nunca toca la BD.
/// </summary>
internal sealed class QuipuxSettingsRow
{
    public Guid Id { get; set; }

    public bool Enabled { get; set; }

    public string UrlLogin { get; set; } = string.Empty;

    public string UrlRegisterDocument { get; set; } = string.Empty;

    public string UrlValidateStatus { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    /// <summary>Contraseña Quipux cifrada con Data Protection.</summary>
    public string PasswordEnc { get; set; } = string.Empty;

    public string ConsumerCode { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string S3Prefix { get; set; } = "FLIT/";

    public string AwsRegion { get; set; } = "us-east-1";

    public string AwsAccessKeyId { get; set; } = string.Empty;

    /// <summary>Secret key AWS cifrada con Data Protection.</summary>
    public string AwsSecretAccessKeyEnc { get; set; } = string.Empty;

    public short OfficerDocumentType { get; set; } = 3;

    public string OfficerDocumentNumber { get; set; } = string.Empty;

    public int RegisterIntervalMinutes { get; set; } = 15;

    public int PollIntervalMinutes { get; set; } = 15;

    public int BatchSize { get; set; } = 20;

    public int MaxAttempts { get; set; } = 5;

    public int MaxPolls { get; set; } = 500;

    public int TimeoutSeconds { get; set; } = 60;

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
