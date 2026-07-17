using Flit.Infrastructure.Persistence.Entities.Quipux;
using Flit.Infrastructure.Quipux;
using Flit.Modules.Quipux.Domain.Configuracion;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de la configuración Quipux (<c>admin.quipux_settings</c>, fila única).
///
/// <para>Traduce entre la entidad de persistencia <see cref="QuipuxSettingsRow"/> —con los secretos
/// en columnas <c>*_enc</c>— y el modelo de dominio <see cref="QuipuxSettings"/> —con los secretos en
/// claro—: descifra al leer y cifra al escribir con <see cref="IQuipuxSecretProtector"/>. La regla es
/// direccional y no negociable: el texto CIFRADO nunca sale de Infrastructure (el dominio y la API no
/// lo ven jamás) y el texto en CLARO nunca toca la BD (un dump de la tabla no expone credenciales).</para>
///
/// <para>Sin tenant y sin RLS: <c>admin.quipux_settings</c> no lleva <c>tenant_id</c> porque el
/// contrato con Quipux es de FLIT como consumidor (usuario FLIT, consumidor 1003), no de cada
/// cliente. Por eso aquí no hay GUC ni scope de tenant que abrir.</para>
///
/// <para>La configuración vive en BD y no en <c>appsettings</c>/env vars por requisito explícito:
/// cambiar credenciales, URLs o cadencia debe ser un UPDATE, sin desplegar. Los workers releen en cada
/// ciclo, así que un cambio surte efecto sin reiniciar.</para>
/// </summary>
internal sealed class QuipuxSettingsRepository(
    FlitDbContext db,
    IQuipuxSecretProtector protector) : IQuipuxSettingsRepository
{
    /// <summary>
    /// Configuración vigente con los secretos ya DESCIFRADOS, o null si aún no hay fila (integración
    /// inerte: sin fila, <c>Enabled</c> no existe y el worker no hace nada).
    /// </summary>
    public async Task<QuipuxSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.QuipuxSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToDomain(row);
    }

    /// <summary>
    /// Crea o actualiza la fila única, CIFRANDO <c>Password</c> y <c>AwsSecretAccessKey</c>.
    ///
    /// <para>Si un secreto entrante viene vacío se CONSERVA el cifrado existente. Es el patrón de UX de
    /// los formularios de configuración: el front nunca recibe el secreto (solo sabe si hay uno), así
    /// que "no reenviar el secreto" significa "no lo cambies", no "bórralo". Pisarlo con vacío dejaría
    /// la integración muerta tras un guardado inocente de, p. ej., la cadencia.</para>
    /// </summary>
    public async Task<QuipuxSettings> SaveAsync(
        QuipuxSettings settings,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var now = DateTimeOffset.UtcNow;

        var row = await db.QuipuxSettings
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            // Primera vez: uq_quipux_settings_singleton ((true)) garantiza en BD que no haya una segunda.
            row = new QuipuxSettingsRow
            {
                // PK store-generated (uuidv7()) pero asignada en código, como el resto del repo.
                Id = settings.Id == Guid.Empty ? Guid.NewGuid() : settings.Id,
                CreatedAt = now,
            };
            db.Add(row);
        }

        Apply(row, settings);
        row.UpdatedAt = now;
        row.UpdatedBy = updatedBy;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Se devuelve lo REALMENTE persistido (descifrado): así el llamador ve el secreto conservado
        // cuando no lo reenvió, en vez del vacío que mandó.
        return ToDomain(row);
    }

    /// <summary>Persistencia → dominio: descifra los <c>*_enc</c>.</summary>
    private QuipuxSettings ToDomain(QuipuxSettingsRow row) => new()
    {
        Id = row.Id,
        Enabled = row.Enabled,

        UrlLogin = row.UrlLogin,
        UrlRegisterDocument = row.UrlRegisterDocument,
        UrlValidateStatus = row.UrlValidateStatus,
        Username = row.Username,
        Password = Unprotect(row.PasswordEnc),
        ConsumerCode = row.ConsumerCode,

        Bucket = row.Bucket,
        S3Prefix = row.S3Prefix,
        AwsRegion = row.AwsRegion,
        AwsAccessKeyId = row.AwsAccessKeyId,
        AwsSecretAccessKey = Unprotect(row.AwsSecretAccessKeyEnc),

        OfficerDocumentType = row.OfficerDocumentType,
        OfficerDocumentNumber = row.OfficerDocumentNumber,

        RegisterIntervalMinutes = row.RegisterIntervalMinutes,
        PollIntervalMinutes = row.PollIntervalMinutes,
        BatchSize = row.BatchSize,
        MaxAttempts = row.MaxAttempts,
        MaxPolls = row.MaxPolls,
        TimeoutSeconds = row.TimeoutSeconds,

        UpdatedAt = row.UpdatedAt,
        UpdatedBy = row.UpdatedBy,
    };

    /// <summary>Dominio → persistencia: cifra los secretos (y conserva el vigente si no vino uno nuevo).</summary>
    private void Apply(QuipuxSettingsRow row, QuipuxSettings settings)
    {
        row.Enabled = settings.Enabled;

        row.UrlLogin = settings.UrlLogin;
        row.UrlRegisterDocument = settings.UrlRegisterDocument;
        row.UrlValidateStatus = settings.UrlValidateStatus;
        row.Username = settings.Username;
        row.PasswordEnc = ProtectOrKeep(settings.Password, row.PasswordEnc);
        row.ConsumerCode = settings.ConsumerCode;

        row.Bucket = settings.Bucket;
        row.S3Prefix = settings.S3Prefix;
        row.AwsRegion = settings.AwsRegion;
        row.AwsAccessKeyId = settings.AwsAccessKeyId;
        row.AwsSecretAccessKeyEnc = ProtectOrKeep(settings.AwsSecretAccessKey, row.AwsSecretAccessKeyEnc);

        row.OfficerDocumentType = (short)settings.OfficerDocumentType;
        row.OfficerDocumentNumber = settings.OfficerDocumentNumber;

        row.RegisterIntervalMinutes = settings.RegisterIntervalMinutes;
        row.PollIntervalMinutes = settings.PollIntervalMinutes;
        row.BatchSize = settings.BatchSize;
        row.MaxAttempts = settings.MaxAttempts;
        row.MaxPolls = settings.MaxPolls;
        row.TimeoutSeconds = settings.TimeoutSeconds;
    }

    /// <summary>Cifra el secreto entrante; si viene vacío, conserva el cifrado ya almacenado.</summary>
    private string ProtectOrKeep(string? entrante, string cifradoActual) =>
        string.IsNullOrWhiteSpace(entrante) ? cifradoActual : protector.Protect(entrante);

    /// <summary>
    /// Descifra el <c>*_enc</c>. El vacío se propaga tal cual: es el estado legítimo de una fila a medio
    /// llenar (las columnas tienen DEFAULT ''), no un texto cifrado que haya que pasar por el protector.
    /// <c>QuipuxSettings.EstaCompleta()</c> es quien decide que con ese vacío no se opera.
    /// </summary>
    private string Unprotect(string? cifrado) =>
        string.IsNullOrEmpty(cifrado) ? string.Empty : protector.Unprotect(cifrado);
}
