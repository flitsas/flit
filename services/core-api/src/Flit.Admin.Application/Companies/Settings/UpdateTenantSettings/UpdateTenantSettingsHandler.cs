using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;

/// <summary>
/// Caso de uso de actualización atómica de la configuración operativa del tenant
/// (HU #10190, AC1/AC2).
///
/// Flujo: (1) valida el payload — si hay errores retorna 422 <em>sin tocar BD ni
/// auditoría</em>; (2) lee la configuración previa (o defaults del DDL); (3)
/// calcula el diff campo a campo; (4) delega al repositorio el upsert + audit log
/// en una única transacción (todo o nada).
///
/// Regla de snapshot (AC4): este guardado afecta únicamente a las <em>nuevas</em>
/// radicaciones. Los trámites in-flight conservan la política capturada al crearse
/// — ver <see cref="ITenantPolicyResolver"/>.
/// </summary>
public sealed class UpdateTenantSettingsHandler
{
    private readonly ITenantSettingsRepository _repository;

    public UpdateTenantSettingsHandler(ITenantSettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateTenantSettingsResult> HandleAsync(
        UpdateTenantSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var errors = new List<SettingsValidationError>();

        if (request.SwitchesMatricula is null)
        {
            errors.Add(new SettingsValidationError(
                "switchesMatricula", "switchesMatricula es obligatorio."));
        }

        if (!SettingsWire.TryParseChannel(request.EnrutamientoSMTP, out var channel))
        {
            errors.Add(new SettingsValidationError(
                "enrutamientoSMTP", $"Valor inválido. Valores permitidos: {SettingsWire.AllowedChannels}."));
        }

        NotificationTarget target = default;
        if (request.NotificationTarget is not null
            && !SettingsWire.TryParseTarget(request.NotificationTarget, out target))
        {
            errors.Add(new SettingsValidationError(
                "notificationTarget", $"Valor inválido. Valores permitidos: {SettingsWire.AllowedTargets}."));
        }

        var extraEmail = request.DestinatariosNotificacion?.ExtraEmail;
        if (!string.IsNullOrWhiteSpace(extraEmail))
        {
            var trimmed = extraEmail.Trim();
            if (trimmed.Contains(',') || trimmed.Length > 320 || !SettingsWire.IsSingleEmail(trimmed))
            {
                errors.Add(new SettingsValidationError(
                    "destinatariosNotificacion.extraEmail",
                    "Debe ser un único correo válido (máximo 320 caracteres)."));
            }
        }

        var methods = request.MetodosRecaudo ?? [];
        if (methods.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add(new SettingsValidationError(
                "metodosRecaudo", "No se permiten métodos de recaudo vacíos."));
        }

        // HU #10478 — timeout de failover (opcional): rango sano si viene.
        if (request.RuntFailoverTimeoutMs is { } timeout && (timeout < 500 || timeout > 60_000))
        {
            errors.Add(new SettingsValidationError(
                "runtFailoverTimeoutMs", "Debe estar entre 500 y 60000 ms."));
        }

        // HU #10478 — override de proveedores (opcional): valida kinds y provider keys por tipo.
        var consultationConfig = ConsultationConfigValidator.TryBuild(request.ConsultationProviderConfig, errors);

        // Feature #10707 — proveedores de avalúo (opcional): valida keys y el sugerido.
        var avaluoConfig = AvaluoConfigValidator.TryBuild(request.AvaluoProviderConfig, errors);

        // FEATURE 02 — fuente de comparendos (opcional): si viene, debe ser internal | external.
        string? finesQuerySource = null;
        if (request.FinesQuerySource is not null)
        {
            finesQuerySource = TenantSettingsCodes.ParseFinesSource(request.FinesQuerySource);
            if (finesQuerySource is null)
            {
                errors.Add(new SettingsValidationError(
                    "finesQuerySource",
                    $"Valor inválido. Valores permitidos: {TenantSettingsCodes.FinesSourceInternal}, {TenantSettingsCodes.FinesSourceExternal}."));
            }
        }

        // AC2: cualquier error de validación corta el flujo antes de persistir.
        if (errors.Count > 0)
        {
            return UpdateTenantSettingsResult.Invalid(errors);
        }

        var previous = await _repository.GetAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
            ?? TenantSettings.Default(command.TenantId);

        var switches = request.SwitchesMatricula!;
        var byFamily = switches.OnlyOwnVehiclesByFamily;
        // Nuevo contrato: onlyOwnVehiclesByFamily manda. Legado: onlyOwnVehicles solo actualiza TRASPASO.
        var onlyTraspaso = byFamily?.Traspaso ?? switches.OnlyOwnVehicles;
        var onlyMatriculas = byFamily?.Matriculas ?? previous.OnlyOwnVehiclesMatriculas;
        var onlyOtros = byFamily?.Otros ?? previous.OnlyOwnVehiclesOtros;
        if (byFamily is null)
        {
            // Cliente legado sin byFamily: el booleano único sigue siendo TRASPASO.
            onlyTraspaso = switches.OnlyOwnVehicles;
        }

        // Bloqueo por familia: blockProcedureFamily manda; si no viene, MATRICULAS sigue en allowInitial
        // (invertido) y TRASPASO/OTROS conservan el valor previo.
        var block = switches.BlockProcedureFamily;
        var allowInitial = block is null
            ? switches.AllowInitialRegistration
            : !block.Matriculas;
        var blockTraspaso = block?.Traspaso ?? previous.BlockProcedureFamilyTraspaso;
        var blockOtros = block?.Otros ?? previous.BlockProcedureFamilyOtros;

        var updated = new TenantSettings
        {
            TenantId = command.TenantId,
            AllowInitialRegistration = allowInitial,
            BlockProcedureFamilyTraspaso = blockTraspaso,
            BlockProcedureFamilyOtros = blockOtros,
            AllowMiscNewVehicles = switches.AllowMiscNewVehicles,
            OnlyOwnVehicles = onlyTraspaso,
            OnlyOwnVehiclesMatriculas = onlyMatriculas,
            OnlyOwnVehiclesOtros = onlyOtros,
            SignatureVaultEnabled = request.BaulFirmasActivo,
            PlatePreassignEnabled = request.PreasignacionPlacaActiva,
            ValidateSoatWithRunt = request.ValidarSoatConRunt,
            PlateFlowSkipToTerminado = request.PlateFlowSkipToTerminado,
            NotificationChannel = channel,
            // HU #11357/#11362 (ADR-0043) — campo propio, ya no derivado del canal. Opcional: si el
            // request no lo envía, se conserva el valor previo (ver UpdateTenantSettingsRequest).
            PersonalizedDocumentsEnabled = request.DocumentosPersonalizadosActivo ?? previous.PersonalizedDocumentsEnabled,
            TramiteApprovedEmailsEnabled = request.AvisosAprobacionActivos
                ?? request.AvisosCambioEstadoActivos
                ?? previous.TramiteApprovedEmailsEnabled,
            TramiteRejectedEmailsEnabled = request.AvisosRechazoActivos
                ?? request.AvisosCambioEstadoActivos
                ?? previous.TramiteRejectedEmailsEnabled,
            StateEmailRecipients = request.DestinatariosNotificacion is { } dest
                ? TramiteStateEmailRecipients.FromJson(
                    dest.Comprador, dest.VendedorOPropietario, dest.Radicador, dest.ExtraEmail)
                : previous.StateEmailRecipients,
            NotificationTarget = request.NotificationTarget is null
                ? previous.NotificationTarget
                : target,
            PaymentMethods = [.. methods],
            // Campos opcionales HU #10478: si el request los omite, se conserva el valor previo.
            RuntFailoverTimeoutMs = request.RuntFailoverTimeoutMs ?? previous.RuntFailoverTimeoutMs,
            ConsultationProviderConfig = consultationConfig ?? previous.ConsultationProviderConfig,
            AvaluoProviderConfig = avaluoConfig ?? previous.AvaluoProviderConfig,
            FinesQuerySource = finesQuerySource ?? previous.FinesQuerySource,
        };

        var changes = SettingsDiff.Compute(previous, updated);

        await _repository
            .SaveAsync(updated, changes, command.ChangedBy, command.CorrelationId, cancellationToken)
            .ConfigureAwait(false);

        return UpdateTenantSettingsResult.Success(SettingsMapper.ToResponse(updated));
    }
}
