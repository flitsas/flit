using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtRequirements;

/// <summary>
/// Tests de la política de consumo del baúl (HU #10643/#10930/#10937, ADR-0025 §4): ACTIVA el flag
/// <c>SignatureVaultEnabled</c>. Resuelve la firma activa de la PERSONA por (tenant, tipo+número de
/// documento del representante) solo si el baúl está habilitado y la firma está vigente hoy (Colombia
/// UTC-5); nunca devuelve material de firma.
/// </summary>
public sealed class SignatureVaultPolicyTests
{
    private static readonly Guid Tenant = Guid.Parse("99999999-0000-4000-8000-000000000001");
    private const string Nit = "900000000-1";
    private const string DocType = "CC";
    private const string DocNumber = "123456789";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly TodayColombia =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)).Date);

    [Fact]
    public async Task VaultDisabled_ReturnsNull_EvenWithActiveSignature()
    {
        await using var ctx = NewContext();
        SeedActiveVault(ctx, vigenciaHasta: TodayColombia.AddDays(30));
        var policy = new SignatureVaultPolicy(new FakeSettings(enabled: false), new DbSignatureVaultReader(ctx));

        var match = await policy.ResolveAsync(Tenant, DocType, DocNumber, Ct);

        match.Should().BeNull();
    }

    [Fact]
    public async Task NoConfigRow_ReturnsNull()
    {
        await using var ctx = NewContext();
        SeedActiveVault(ctx, vigenciaHasta: TodayColombia.AddDays(30));
        var policy = new SignatureVaultPolicy(new FakeSettings(settings: null), new DbSignatureVaultReader(ctx));

        var match = await policy.ResolveAsync(Tenant, DocType, DocNumber, Ct);

        match.Should().BeNull();
    }

    [Fact]
    public async Task EnabledAndVigente_ReturnsMatch_WithoutMaterial()
    {
        await using var ctx = NewContext();
        SeedActiveVault(ctx, vigenciaHasta: TodayColombia.AddDays(30));
        var policy = new SignatureVaultPolicy(new FakeSettings(enabled: true), new DbSignatureVaultReader(ctx));

        var match = await policy.ResolveAsync(Tenant, DocType, DocNumber, Ct);

        match.Should().NotBeNull();
        match!.FullName.Should().Be("Apoderada Renting S.A.S.");
        match.StoragePath.Should().Be("fm-file-abc");
        match.SignatureHash.Should().Be("sha-256-hex");
    }

    [Fact]
    public async Task EnabledButExpired_ReturnsNull()
    {
        await using var ctx = NewContext();
        SeedActiveVault(ctx, vigenciaHasta: TodayColombia.AddDays(-1)); // venció ayer.
        var policy = new SignatureVaultPolicy(new FakeSettings(enabled: true), new DbSignatureVaultReader(ctx));

        var match = await policy.ResolveAsync(Tenant, DocType, DocNumber, Ct);

        match.Should().BeNull();
    }

    [Fact]
    public async Task EnabledButNoActiveSignature_ReturnsNull()
    {
        await using var ctx = NewContext(); // baúl vacío.
        var policy = new SignatureVaultPolicy(new FakeSettings(enabled: true), new DbSignatureVaultReader(ctx));

        var match = await policy.ResolveAsync(Tenant, DocType, DocNumber, Ct);

        match.Should().BeNull();
    }

    // ---------- Helpers ----------

    private static void SeedActiveVault(FlitDbContext ctx, DateOnly vigenciaHasta)
    {
        ctx.SignatureVault.Add(new SignatureVaultEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            DocumentType = "CC",
            DocumentNumber = "123456789",
            NitEmpresa = Nit,
            FullName = "Apoderada Renting S.A.S.",
            SignatureHash = "sha-256-hex",
            StoragePath = "fm-file-abc",
            StorageSha256 = "sha-256-hex",
            Estado = "activa",
            VigenciaDesde = TodayColombia.AddDays(-10),
            VigenciaHasta = vigenciaHasta,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-signature-vault-policy-{Guid.NewGuid()}")
            .Options);

    /// <summary>Resolver de settings en memoria: fija el flag SignatureVaultEnabled (o no hay fila).</summary>
    private sealed class FakeSettings : ITenantSettingsRepository
    {
        private readonly TenantSettings? _settings;

        public FakeSettings(bool enabled)
            : this(settings: new TenantSettings
            {
                TenantId = Tenant,
                AllowInitialRegistration = false,
                AllowMiscNewVehicles = true,
                OnlyOwnVehicles = false,
                SignatureVaultEnabled = enabled,
                NotificationChannel = NotificationChannel.FlitSmtp,
                NotificationTarget = NotificationTarget.Radicador,
                PaymentMethods = [],
            })
        {
        }

        public FakeSettings(TenantSettings? settings) => _settings = settings;

        public Task<TenantSettings?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);

        public Task SaveAsync(
            TenantSettings settings,
            IReadOnlyList<TenantConfigChange> changes,
            Guid? changedBy,
            Guid? correlationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
