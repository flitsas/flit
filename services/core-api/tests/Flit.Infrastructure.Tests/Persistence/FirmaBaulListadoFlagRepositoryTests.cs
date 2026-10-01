using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Bug #13194 (P4, D1) — el interruptor «Baúl de firmas activo» de la compañía MANDA también en el
/// listado: <see cref="ProcedureInstanceRepository.ListFirmaBaulVigenciaKeysAsync"/> solo devuelve firmas
/// de tenants con <c>signature_vault_enabled</c> activo. Antes las devolvía todas y la columna «Firmado»
/// pintaba «Firmado» mientras el paso de identidad y el gate (que sí miran el flag) decían «no iniciada».
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var claves = await new ProcedureInstanceRepository(db).ListFirmaBaulVigenciaKeysAsync([tenant], hoy, ct);
/// // claves[BiometricRules.IdentidadKey(tenant, "CC", doc)] == true solo con el baúl de la compañía activo.
/// </code>
/// Datos ficticios: documentos 9000000xxx.
/// </remarks>
public sealed class FirmaBaulListadoFlagRepositoryTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 1);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static SignatureVaultEntity FirmaVigente(Guid tenantId, string documento) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        DocumentType = "CC",
        DocumentNumber = documento,
        FullName = "Rep Legal",
        SignatureHash = "sig-hash",
        StoragePath = "vault/firma.png",
        StorageSha256 = "art-sha",
        Estado = "activa",
        VigenciaDesde = Hoy.AddDays(-10),
        VigenciaHasta = Hoy.AddDays(10),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static TenantOperationalPolicy Politica(Guid tenantId, bool baulActivo) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        SignatureVaultEnabled = baulActivo,
    };

    [Fact]
    public async Task BaulVigenteConFlagApagado_NoAparece_YElListadoNoPintaFirmado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        await using var db = NewContext($"baul-flag-off-{Guid.NewGuid()}");
        db.SignatureVault.Add(FirmaVigente(tenant, "9000000401"));
        db.TenantOperationalPolicies.Add(Politica(tenant, baulActivo: false));
        await db.SaveChangesAsync(ct);

        var claves = await new ProcedureInstanceRepository(db).ListFirmaBaulVigenciaKeysAsync([tenant], Hoy, ct);

        claves.Should().NotContainKey(BiometricRules.IdentidadKey(tenant, "CC", "9000000401"));
    }

    [Fact]
    public async Task BaulVigenteSinFilaDeConfiguracion_SeTrataComoApagado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        await using var db = NewContext($"baul-sin-config-{Guid.NewGuid()}");
        db.SignatureVault.Add(FirmaVigente(tenant, "9000000402"));
        await db.SaveChangesAsync(ct);

        var claves = await new ProcedureInstanceRepository(db).ListFirmaBaulVigenciaKeysAsync([tenant], Hoy, ct);

        claves.Should().BeEmpty();
    }

    [Fact]
    public async Task BaulVigenteConFlagActivo_ApareceVigente()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        await using var db = NewContext($"baul-flag-on-{Guid.NewGuid()}");
        db.SignatureVault.Add(FirmaVigente(tenant, "9000000403"));
        db.TenantOperationalPolicies.Add(Politica(tenant, baulActivo: true));
        await db.SaveChangesAsync(ct);

        var claves = await new ProcedureInstanceRepository(db).ListFirmaBaulVigenciaKeysAsync([tenant], Hoy, ct);

        claves.Should().ContainKey(BiometricRules.IdentidadKey(tenant, "CC", "9000000403"))
            .WhoseValue.Should().BeTrue();
    }

    [Fact]
    public async Task VariosTenants_SoloCuentanLosDeFlagActivo()
    {
        // Una sola consulta para todo el listado: el filtro es por tenant, no global.
        var ct = TestContext.Current.CancellationToken;
        var conBaul = Guid.NewGuid();
        var sinBaul = Guid.NewGuid();
        await using var db = NewContext($"baul-multi-{Guid.NewGuid()}");
        db.SignatureVault.Add(FirmaVigente(conBaul, "9000000404"));
        db.SignatureVault.Add(FirmaVigente(sinBaul, "9000000404"));
        db.TenantOperationalPolicies.Add(Politica(conBaul, baulActivo: true));
        db.TenantOperationalPolicies.Add(Politica(sinBaul, baulActivo: false));
        await db.SaveChangesAsync(ct);

        var claves = await new ProcedureInstanceRepository(db)
            .ListFirmaBaulVigenciaKeysAsync([conBaul, sinBaul], Hoy, ct);

        claves.Keys.Should().BeEquivalentTo([BiometricRules.IdentidadKey(conBaul, "CC", "9000000404")]);
    }
}
