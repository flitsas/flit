using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.Auditing;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Infrastructure.Tests.Auditing;

/// <summary>
/// HU #10678 (RNF01, ADR-0024 extendido): <see cref="AdminAuditWriter"/> es el rastro único de
/// auditoría administrativa/seguridad sobre <c>admin.tenant_config_audit_logs</c>. Cubre AC1
/// (registro de éxito), AC2 (registro de fallo con su propio scope), AC3 (actor y afectado
/// distintos) y AC5 (datos mínimos: usuario, fecha/hora, IP, operación, resultado).
/// </summary>
public sealed class AdminAuditWriterTests
{
    private static ServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FlitDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();
        return services.BuildServiceProvider();
    }

    // ── AC1 — registro de operación exitosa ─────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_SuccessEntry_PersistsRowWithExpectedFields()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider(nameof(WriteAsync_SuccessEntry_PersistsRowWithExpectedFields));
        var actorId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAdminAuditWriter>();
            await writer.WriteAsync(
                new AdminAuditEntry(
                    tenantId,
                    TenantType: "COMPANY",
                    AuditVocabulary.Modules.Authentication,
                    EntityName: "session",
                    AuditVocabulary.Operations.Login,
                    AuditVocabulary.Results.Success,
                    ErrorCode: null,
                    ActorUserId: actorId,
                    TargetEntityType: "USER",
                    TargetEntityId: actorId,
                    ClientIp: "203.0.113.10",
                    UserAgent: "xunit-agent"),
                ct);
        }

        await using var verifyContext = provider.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();
        var row = await verifyContext.TenantConfigAuditLogs.SingleAsync(ct);

        row.Module.Should().Be(AuditVocabulary.Modules.Authentication);
        row.Operation.Should().Be(AuditVocabulary.Operations.Login);
        row.Result.Should().Be(AuditVocabulary.Results.Success);
        row.ChangedBy.Should().Be(actorId);
        row.ClientIp.Should().Be("203.0.113.10");
        row.ChangedAt.Should().NotBe(default);
        row.ChangedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        row.ErrorCode.Should().BeNull();
    }

    // ── AC2 — registro de operación fallida, sobrevive rollback del cambio principal ────

    [Fact]
    public async Task WriteAsync_FailureEntry_PersistsInOwnScopeWithErrorCodeAndNoSensitiveData()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider(nameof(WriteAsync_FailureEntry_PersistsInOwnScopeWithErrorCodeAndNoSensitiveData));

        using (var mainScope = provider.CreateScope())
        {
            var mainContext = mainScope.ServiceProvider.GetRequiredService<FlitDbContext>();
            // Simula un cambio principal en vuelo que nunca se guarda (rollback).
            mainContext.TenantOperationalPolicies.Add(new Flit.Infrastructure.Persistence.Entities.Admin.TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
            });

            var writer = mainScope.ServiceProvider.GetRequiredService<IAdminAuditWriter>();
            await writer.WriteAsync(
                new AdminAuditEntry(
                    TenantId: null,
                    TenantType: null,
                    AuditVocabulary.Modules.Authentication,
                    EntityName: "session",
                    AuditVocabulary.Operations.LoginFailed,
                    AuditVocabulary.Results.Failure,
                    ErrorCode: "invalid_credentials",
                    ActorUserId: null,
                    TargetEntityType: null,
                    TargetEntityId: null,
                    ClientIp: "198.51.100.20",
                    UserAgent: null),
                ct);

            // mainContext se descarta sin SaveChanges → el cambio principal "hace rollback".
        }

        await using var verifyContext = provider.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

        (await verifyContext.TenantOperationalPolicies.AnyAsync(ct)).Should().BeFalse();

        var row = await verifyContext.TenantConfigAuditLogs.SingleAsync(ct);
        row.Result.Should().Be(AuditVocabulary.Results.Failure);
        row.Operation.Should().Be(AuditVocabulary.Operations.LoginFailed);
        row.ErrorCode.Should().Be("invalid_credentials");
        row.ErrorCode.Should().NotContain("@").And.NotContainEquivalentOf("password");
    }

    // ── AC3 — actor y afectado (target) distintos se persisten ambos ────────────────

    [Fact]
    public async Task WriteAsync_ActorDifferentFromTarget_PersistsBothIds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider(nameof(WriteAsync_ActorDifferentFromTarget_PersistsBothIds));
        var actorId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAdminAuditWriter>();
            await writer.WriteAsync(
                new AdminAuditEntry(
                    tenantId,
                    TenantType: "COMPANY",
                    AuditVocabulary.Modules.Roles,
                    EntityName: "user_role",
                    AuditVocabulary.Operations.AssignRole,
                    AuditVocabulary.Results.Success,
                    ErrorCode: null,
                    ActorUserId: actorId,
                    TargetEntityType: "USER",
                    TargetEntityId: targetUserId,
                    ClientIp: "203.0.113.15",
                    UserAgent: null),
                ct);
        }

        await using var verifyContext = provider.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();
        var row = await verifyContext.TenantConfigAuditLogs.SingleAsync(ct);

        row.ChangedBy.Should().Be(actorId);
        row.TargetEntityId.Should().Be(targetUserId);
        row.TargetEntityType.Should().Be("USER");
        (row.ChangedBy != row.TargetEntityId).Should().BeTrue();
    }

    // ── AC5 — datos mínimos: usuario, fecha/hora, IP, operación y resultado ─────────

    [Fact]
    public async Task WriteAsync_AnyEntry_AlwaysPersistsMinimumRequiredFields()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider(nameof(WriteAsync_AnyEntry_AlwaysPersistsMinimumRequiredFields));
        var actorId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAdminAuditWriter>();
            await writer.WriteAsync(
                new AdminAuditEntry(
                    Guid.NewGuid(),
                    TenantType: "COMPANY",
                    AuditVocabulary.Modules.Users,
                    EntityName: "user",
                    AuditVocabulary.Operations.Suspend,
                    AuditVocabulary.Results.Success,
                    ErrorCode: null,
                    ActorUserId: actorId,
                    TargetEntityType: "USER",
                    TargetEntityId: Guid.NewGuid(),
                    ClientIp: "192.0.2.55",
                    UserAgent: "unit-test"),
                ct);
        }

        await using var verifyContext = provider.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();
        var row = await verifyContext.TenantConfigAuditLogs.SingleAsync(ct);

        // Usuario (actor).
        row.ChangedBy.Should().NotBeNull().And.Be(actorId);
        // Fecha/hora.
        row.ChangedAt.Should().NotBe(default);
        // IP.
        row.ClientIp.Should().NotBeNullOrEmpty();
        // Operación.
        row.Operation.Should().NotBeNullOrEmpty();
        // Resultado.
        row.Result.Should().NotBeNullOrEmpty();
    }

    // ── Best-effort: la escritura nunca lanza, ni cuando el proveedor falla ─────────

    [Fact]
    public async Task WriteAsync_NullEntry_ThrowsArgumentNullException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider(nameof(WriteAsync_NullEntry_ThrowsArgumentNullException));
        using var scope = provider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAdminAuditWriter>();

        var act = () => writer.WriteAsync(null!, ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
