using Flit.Modules.Quipux.Domain.Configuracion;

namespace Flit.Modules.Quipux.Application.UseCases.Configuracion;

/// <summary>
/// Crea o actualiza la configuración Quipux (<c>admin.quipux_settings</c>, fila única). Delega el
/// cifrado de los secretos y la conservación del secreto existente cuando no se reenvía en
/// <see cref="IQuipuxSettingsRepository.SaveAsync"/>: aquí no se toca texto cifrado ni se decide UX,
/// solo se traduce el comando de la API al modelo de dominio.
/// </summary>
/// <remarks>
/// No valida URLs ni credenciales contra Quipux: una configuración a medio llenar es un estado
/// legítimo (el operador puede guardar las URLs hoy y las credenciales mañana). Es
/// <see cref="QuipuxSettings.EstaCompleta"/> —releída por los workers en cada ciclo— quien decide
/// que con esa fila todavía no se radica, de modo que guardar nunca rompe y encender la integración
/// a medias queda visible en la bitácora del job en vez de tumbar el ciclo.
/// </remarks>
public sealed class GuardarQuipuxSettingsHandler
{
    private readonly IQuipuxSettingsRepository _repository;

    public GuardarQuipuxSettingsHandler(IQuipuxSettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<QuipuxSettingsView> HandleAsync(
        GuardarQuipuxSettingsCommand command,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var settings = new QuipuxSettings
        {
            Enabled = command.Enabled,

            UrlLogin = command.UrlLogin.Trim(),
            UrlRegisterDocument = command.UrlRegisterDocument.Trim(),
            UrlValidateStatus = command.UrlValidateStatus.Trim(),
            Username = command.Username.Trim(),
            // Nulo/vacío ⇒ el repositorio conserva el secreto cifrado existente (no lo borra).
            Password = command.Password ?? string.Empty,
            ConsumerCode = command.ConsumerCode.Trim(),

            Bucket = command.Bucket.Trim(),
            S3Prefix = command.S3Prefix.Trim(),
            AwsRegion = command.AwsRegion.Trim(),
            AwsAccessKeyId = command.AwsAccessKeyId.Trim(),
            AwsSecretAccessKey = command.AwsSecretAccessKey ?? string.Empty,

            OfficerDocumentType = command.OfficerDocumentType,
            OfficerDocumentNumber = command.OfficerDocumentNumber.Trim(),

            RegisterIntervalMinutes = command.RegisterIntervalMinutes,
            PollIntervalMinutes = command.PollIntervalMinutes,
            BatchSize = command.BatchSize,
            MaxAttempts = command.MaxAttempts,
            MaxPolls = command.MaxPolls,
            TimeoutSeconds = command.TimeoutSeconds,
        };

        var saved = await _repository.SaveAsync(settings, updatedBy, cancellationToken).ConfigureAwait(false);
        return QuipuxSettingsMapper.ToView(saved);
    }
}
