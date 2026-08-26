using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de la configuración operativa del tenant (HU #10190).
///
/// RLS: <c>admin.tenant_operational_policies</c> y <c>admin.tenant_config_audit_logs</c>
/// están aisladas por <c>app.current_tenant_id</c>. Para la operación cross-tenant
/// de SuperAdmin se fija ese GUC <em>dentro</em> de la transacción con
/// <c>set_config(..., is_local := true)</c> (parametrizado, sin concatenar SQL)
/// antes de leer/escribir, y se revierte al cerrar la transacción.
///
/// Atomicidad (AC1): el upsert de la política y la inserción de las filas de
/// auditoría ocurren en una única transacción (todo o nada). En proveedores no
/// relacionales (InMemory, tests) se omite la transacción explícita y el GUC,
/// preservando el comportamiento de un único <c>SaveChanges</c>.
/// </summary>
internal sealed class TenantSettingsRepository : ITenantSettingsRepository
{
    private const string EntityName = "tenant_operational_policies";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly FlitDbContext _context;
    private readonly IAuditContextAccessor _auditContext;

    public TenantSettingsRepository(FlitDbContext context, IAuditContextAccessor auditContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditContext = auditContext ?? throw new ArgumentNullException(nameof(auditContext));
    }

    public async Task<TenantSettings?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.TenantOperationalPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public async Task SaveAsync(
        TenantSettings settings,
        IReadOnlyList<TenantConfigChange> changes,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(changes);

        if (_context.Database.IsRelational())
        {
            // La transacción manual debe ejecutarse como unidad reintentables porque
            // el DbContext tiene EnableRetryOnFailure (NpgsqlRetryingExecutionStrategy).
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    // RLS: habilita el acceso cross-tenant solo para esta transacción.
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {settings.TenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    await PersistAsync(settings, changes, changedBy, correlationId, cancellationToken)
                        .ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            return;
        }

        // Proveedor no relacional (InMemory): un solo SaveChanges atómico.
        await PersistAsync(settings, changes, changedBy, correlationId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PersistAsync(
        TenantSettings settings,
        IReadOnlyList<TenantConfigChange> changes,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var policy = await _context.TenantOperationalPolicies
            .FirstOrDefaultAsync(p => p.TenantId == settings.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (policy is null)
        {
            policy = new TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = settings.TenantId,
                CreatedAt = now,
                CreatedBy = changedBy,
            };
            _context.TenantOperationalPolicies.Add(policy);
        }
        else
        {
            policy.UpdatedAt = now;
            policy.UpdatedBy = changedBy;
        }

        ApplyTo(policy, settings);

        var clientIp = _auditContext.ClientIp;

        foreach (var change in changes)
        {
            _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = settings.TenantId,
                EntityName = EntityName,
                FieldName = change.FieldName,
                OldValue = change.OldValueJson,
                NewValue = change.NewValueJson,
                ChangedAt = now,
                ChangedBy = changedBy,
                CorrelationId = correlationId,
                // RNF01 (ADR-0024): IP, operación y resultado del cambio exitoso.
                ClientIp = clientIp,
                Operation = AuditVocabulary.Operations.Update,
                Result = AuditVocabulary.Results.Success,
            });
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyTo(TenantOperationalPolicy policy, TenantSettings settings)
    {
        policy.AllowInitialRegistration = settings.AllowInitialRegistration;
        policy.BlockProcedureFamilyTraspaso = settings.BlockProcedureFamilyTraspaso;
        policy.BlockProcedureFamilyOtros = settings.BlockProcedureFamilyOtros;
        policy.AllowMiscNewVehicles = settings.AllowMiscNewVehicles;
        policy.OnlyOwnVehicles = settings.OnlyOwnVehicles;
        policy.OnlyOwnVehiclesMatriculas = settings.OnlyOwnVehiclesMatriculas;
        policy.OnlyOwnVehiclesOtros = settings.OnlyOwnVehiclesOtros;
        policy.SignatureVaultEnabled = settings.SignatureVaultEnabled;
        policy.PlatePreassignEnabled = settings.PlatePreassignEnabled;
        policy.PlateFlowSkipToTerminado = settings.PlateFlowSkipToTerminado;
        policy.ValidateSoatWithRunt = settings.ValidateSoatWithRunt;
        policy.NotificationChannel = TenantSettingsCodes.ToDb(settings.NotificationChannel);
        policy.PersonalizedDocumentsEnabled = settings.PersonalizedDocumentsEnabled;
        policy.TramiteApprovedEmailsEnabled = settings.TramiteApprovedEmailsEnabled;
        policy.TramiteRejectedEmailsEnabled = settings.TramiteRejectedEmailsEnabled;
        policy.TramiteStateEmailRecipients = SerializeStateRecipients(settings.StateEmailRecipients);
        policy.NotificationTarget = TenantSettingsCodes.ToDb(settings.NotificationTarget);
        policy.PaymentMethods = JsonSerializer.Serialize(settings.PaymentMethods);
        policy.RuntFailoverTimeoutMs = settings.RuntFailoverTimeoutMs;
        policy.ConsultationProviderConfig = SerializeConsultationConfig(settings.ConsultationProviderConfig);
        policy.AvaluoProviderConfig = SerializeAvaluoConfig(settings.AvaluoProviderConfig);
        policy.FinesQuerySource = settings.FinesQuerySource;
    }

    private static TenantSettings Map(TenantOperationalPolicy entity) => new()
    {
        TenantId = entity.TenantId,
        AllowInitialRegistration = entity.AllowInitialRegistration,
        BlockProcedureFamilyTraspaso = entity.BlockProcedureFamilyTraspaso,
        BlockProcedureFamilyOtros = entity.BlockProcedureFamilyOtros,
        AllowMiscNewVehicles = entity.AllowMiscNewVehicles,
        OnlyOwnVehicles = entity.OnlyOwnVehicles,
        OnlyOwnVehiclesMatriculas = entity.OnlyOwnVehiclesMatriculas,
        OnlyOwnVehiclesOtros = entity.OnlyOwnVehiclesOtros,
        SignatureVaultEnabled = entity.SignatureVaultEnabled,
        PlatePreassignEnabled = entity.PlatePreassignEnabled,
        ValidateSoatWithRunt = entity.ValidateSoatWithRunt,
        PlateFlowSkipToTerminado = entity.PlateFlowSkipToTerminado,
        NotificationChannel = TenantSettingsCodes.ParseChannelDb(entity.NotificationChannel),
        PersonalizedDocumentsEnabled = entity.PersonalizedDocumentsEnabled,
        TramiteApprovedEmailsEnabled = entity.TramiteApprovedEmailsEnabled,
        TramiteRejectedEmailsEnabled = entity.TramiteRejectedEmailsEnabled,
        StateEmailRecipients = DeserializeStateRecipients(entity.TramiteStateEmailRecipients),
        NotificationTarget = TenantSettingsCodes.ParseTargetDb(entity.NotificationTarget),
        PaymentMethods = DeserializePaymentMethods(entity.PaymentMethods),
        RuntFailoverTimeoutMs = entity.RuntFailoverTimeoutMs,
        ConsultationProviderConfig = DeserializeConsultationConfig(entity.ConsultationProviderConfig),
        AvaluoProviderConfig = DeserializeAvaluoConfig(entity.AvaluoProviderConfig),
        FinesQuerySource = TenantSettingsCodes.ParseFinesSource(entity.FinesQuerySource) ?? TenantSettingsCodes.FinesSourceExternal,
    };

    private static List<string> DeserializePaymentMethods(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    private static string SerializeConsultationConfig(ConsultationProviderConfig config) =>
        JsonSerializer.Serialize(config.ByKind, WebJson);

    private static ConsultationProviderConfig DeserializeConsultationConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ConsultationProviderConfig.Empty;
        }

        var raw = JsonSerializer.Deserialize<Dictionary<string, ConsultationProviderSelection>>(json, WebJson);
        if (raw is null || raw.Count == 0)
        {
            return ConsultationProviderConfig.Empty;
        }

        // Normaliza fallback null → [] (un objeto sin la clave "fallback" deserializa a null).
        var byKind = new Dictionary<string, ConsultationProviderSelection>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, selection) in raw)
        {
            byKind[kind] = selection with { Fallback = selection.Fallback ?? [] };
        }

        return new ConsultationProviderConfig(byKind);
    }

    // ── Avalúo (Feature #10707) ─────────────────────────────────────────────
    private sealed record AvaluoConfigJson(string? Primary, List<string>? Enabled);

    private static string SerializeAvaluoConfig(AvaluoProviderConfig config) =>
        JsonSerializer.Serialize(new AvaluoConfigJson(config.Primary, [.. config.Enabled]), WebJson);

    private static AvaluoProviderConfig DeserializeAvaluoConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AvaluoProviderConfig.Default;
        }

        var raw = JsonSerializer.Deserialize<AvaluoConfigJson>(json, WebJson);
        if (raw is null || raw.Enabled is null || raw.Enabled.Count == 0)
        {
            return AvaluoProviderConfig.Default;
        }

        return new AvaluoProviderConfig(
            raw.Primary ?? AvaluoProviderConfig.BaseProvider,
            raw.Enabled);
    }

    private sealed record StateRecipientsJson(
        bool Comprador,
        bool VendedorOPropietario,
        bool Radicador,
        string? ExtraEmail);

    private static string SerializeStateRecipients(TramiteStateEmailRecipients value) =>
        JsonSerializer.Serialize(
            new StateRecipientsJson(
                value.Comprador, value.VendedorOPropietario, value.Radicador, value.ExtraEmail),
            WebJson);

    private static TramiteStateEmailRecipients DeserializeStateRecipients(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return TramiteStateEmailRecipients.AllOn;

        var raw = JsonSerializer.Deserialize<StateRecipientsJson>(json, WebJson);
        if (raw is null)
            return TramiteStateEmailRecipients.AllOn;

        return TramiteStateEmailRecipients.FromJson(
            raw.Comprador, raw.VendedorOPropietario, raw.Radicador, raw.ExtraEmail);
    }
}
