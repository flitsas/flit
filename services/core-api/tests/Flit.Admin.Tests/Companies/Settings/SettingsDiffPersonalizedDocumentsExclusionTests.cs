using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.Settings;
using Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.Settings;

/// <summary>
/// HU #11320, AC1 (segunda mitad) — «cero entradas en el diff de configuración del tenant»: los
/// documentos personalizados por compañía (Feature #11309, ADR-0042) NO son campos escalares del
/// formulario de configuración (<c>UpdateTenantSettingsRequest</c>/<c>SettingsDiff</c>) y un archivo no
/// cabe ahí — su ciclo de vida vive por completo en <c>admin.company_personalized_documents</c> con su
/// propio trigger + <see cref="IAdminAuditWriter"/> (ver HU #11320, ítem 1).
///
/// Este test ejercita <see cref="UpdateTenantSettingsHandler"/> (que internamente llama a
/// <c>SettingsDiff.Compute</c>, <c>internal</c>) con un cambio que toca la mayor cantidad de campos
/// posible del formulario y verifica que NINGUNA fila de auditoría resultante en
/// <c>admin.tenant_config_audit_logs</c> menciona <c>personalized_document</c>, <c>mandato</c> ni
/// <c>tramite_virtual</c>: es un guardián de regresión — si alguien agrega un campo relacionado al
/// formulario de configuración del tenant, este test debe fallar.
/// </summary>
public sealed class SettingsDiffPersonalizedDocumentsExclusionTests
{
    private static readonly Guid ChangedBy = Guid.Parse("99999999-1111-4000-8000-000000000099");

    [Fact]
    public async Task UpdateTenantSettings_MaximalDiff_NeverIncludesPersonalizedDocumentKeys()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TenantOperationalPolicies.Add(new TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AllowInitialRegistration = true,
                AllowMiscNewVehicles = true,
                OnlyOwnVehicles = false,
                SignatureVaultEnabled = false,
                NotificationChannel = "flit_smtp",
                NotificationTarget = "RADICADOR",
                PaymentMethods = "[]",
                RuntProviderStrategy = "verifik",
                RuntFailoverTimeoutMs = 4000,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var act = NewContext(db))
        {
            var handler = new UpdateTenantSettingsHandler(new TenantSettingsRepository(act, NullAuditContextAccessor.Instance));

            // Toca todos los campos escalares del formulario que admiten un valor distinto del
            // sembrado — la superficie máxima de diff que hoy existe para la configuración del tenant.
            var result = await handler.HandleAsync(new UpdateTenantSettingsCommand
            {
                TenantId = tenantId,
                ChangedBy = ChangedBy,
                Request = new UpdateTenantSettingsRequest(
                    new SwitchesMatricula(false, true, true),
                    BaulFirmasActivo: true,
                    EnrutamientoSMTP: "TENANT_API",
                    NotificationTarget: "COMPRADOR",
                    MetodosRecaudo: ["pse", "efecty"],
                    RuntFailoverTimeoutMs: 8000,
                    PreasignacionPlacaActiva: true,
                    PlateFlowSkipToTerminado: true,
                    ValidarSoatConRunt: true,
                    FinesQuerySource: "internal"),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
        }

        await using var verify = NewContext(db);
        var audits = await verify.TenantConfigAuditLogs
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(TestContext.Current.CancellationToken);

        // El diff SÍ produjo filas (guardián de que el test no está vacío por accidente).
        audits.Should().NotBeEmpty();

        audits.Should().OnlyContain(a => a.EntityName == "tenant_operational_policies");
        audits.Should().NotContain(a =>
            a.FieldName.Contains("personalized", StringComparison.OrdinalIgnoreCase)
            || a.FieldName.Contains("mandato", StringComparison.OrdinalIgnoreCase)
            || a.FieldName.Contains("tramite_virtual", StringComparison.OrdinalIgnoreCase)
            || a.FieldName.Contains("documento", StringComparison.OrdinalIgnoreCase));

        // Ninguna fila lleva el valor nuevo/anterior de un documento personalizado (JSON o binario).
        audits.Should().NotContain(a =>
            (a.NewValue != null && a.NewValue.Contains("storage_path", StringComparison.OrdinalIgnoreCase))
            || (a.OldValue != null && a.OldValue.Contains("storage_path", StringComparison.OrdinalIgnoreCase)));
    }

    private static string NewDbName() => $"flit-settingsdiff-personalized-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
