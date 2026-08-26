using Flit.Admin.Application.Companies.PersonalizedDocuments;
using Flit.Admin.Domain.Companies.Settings;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.PersonalizedDocuments;

/// <summary>
/// HU #11362 (Feature #11348, ADR-0043) — AC4/AC5: el interruptor de documentos personalizados deja
/// de leer el canal de notificaciones y pasa a leer <see cref="TenantSettings.PersonalizedDocumentsEnabled"/>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var enabled = await PersonalizedDocumentEligibilityGuard.IsWriteEnabledAsync(repo, tenantId, ct);
/// </code>
/// </remarks>
public sealed class PersonalizedDocumentEligibilityGuardTests
{
    private static readonly Guid TenantId = Guid.Parse("22222222-3333-4000-8000-000000000002");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TenantSettings Settings(bool personalizedDocumentsEnabled, NotificationChannel channel) => new()
    {
        TenantId = TenantId,
        AllowInitialRegistration = true,
        AllowMiscNewVehicles = true,
        OnlyOwnVehicles = false,
        SignatureVaultEnabled = false,
        NotificationChannel = channel,
        NotificationTarget = NotificationTarget.Radicador,
        PaymentMethods = [],
        PersonalizedDocumentsEnabled = personalizedDocumentsEnabled,
    };

    // ---------- AC4: el booleano propio en true habilita, con independencia del canal ----------

    [Fact]
    public async Task AC4_BooleanEnabled_WithFlitSmtpChannel_WriteIsEnabled()
    {
        var repo = Substitute.For<ITenantSettingsRepository>();
        repo.GetAsync(TenantId, Ct).Returns(Settings(personalizedDocumentsEnabled: true, NotificationChannel.FlitSmtp));

        var enabled = await PersonalizedDocumentEligibilityGuard.IsWriteEnabledAsync(repo, TenantId, Ct);

        enabled.Should().BeTrue();
    }

    [Fact]
    public async Task AC4_BooleanDisabled_WithTenantApiChannel_WriteIsDisabled()
    {
        // Antes de esta HU, canal TENANT_API por sí solo habilitaba la escritura. Ahora NO: el canal
        // dejó de ser la fuente.
        var repo = Substitute.For<ITenantSettingsRepository>();
        repo.GetAsync(TenantId, Ct).Returns(Settings(personalizedDocumentsEnabled: false, NotificationChannel.TenantApi));

        var enabled = await PersonalizedDocumentEligibilityGuard.IsWriteEnabledAsync(repo, TenantId, Ct);

        enabled.Should().BeFalse();
    }

    // ---------- AC5: cambiar el canal ya no altera la elegibilidad ----------

    [Fact]
    public async Task AC5_BooleanEnabled_ChannelChangesFromTenantApiToFlitSmtp_WriteStaysEnabled()
    {
        var repo = Substitute.For<ITenantSettingsRepository>();
        repo.GetAsync(TenantId, Ct).Returns(Settings(personalizedDocumentsEnabled: true, NotificationChannel.TenantApi));

        var beforeSwitch = await PersonalizedDocumentEligibilityGuard.IsWriteEnabledAsync(repo, TenantId, Ct);
        beforeSwitch.Should().BeTrue();

        // El tenant cambia el canal a FlitSmtp; el booleano propio NO se toca.
        repo.GetAsync(TenantId, Ct).Returns(Settings(personalizedDocumentsEnabled: true, NotificationChannel.FlitSmtp));

        var afterSwitch = await PersonalizedDocumentEligibilityGuard.IsWriteEnabledAsync(repo, TenantId, Ct);

        afterSwitch.Should().BeTrue();
    }

    // ---------- AC6 (heredado del guardián anterior): tenant sin política ⇒ deshabilitado ----------

    [Fact]
    public async Task AC6_TenantWithoutOperationalPolicy_WriteIsDisabled()
    {
        var repo = Substitute.For<ITenantSettingsRepository>();
        repo.GetAsync(TenantId, Ct).Returns((TenantSettings?)null);

        var enabled = await PersonalizedDocumentEligibilityGuard.IsWriteEnabledAsync(repo, TenantId, Ct);

        enabled.Should().BeFalse();
    }
}
