using Flit.Admin.Application.Companies.Whitelist.AddWhitelistEmails;
using Flit.Admin.Application.Companies.Whitelist.GetWhitelist;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.Whitelist;

/// <summary>
/// API de lista blanca (HU #10191) — AC4 (alta masiva + audit por correo), AC5
/// (correo inválido → 422 atómico sin persistir) y AC6 (GET de correos activos).
/// Ejercita los handlers reales sobre el repositorio EF real con proveedor InMemory
/// (la transacción/SET LOCAL aplican solo en proveedor relacional).
/// </summary>
public sealed class WhitelistHandlerTests
{
    private static readonly Guid AddedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---------- AC4: alta masiva + auditoría ----------

    [Fact]
    public async Task AC4_AddsEmails_AndWritesAuditPerInsertedEmail()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            var handler = new AddWhitelistEmailsHandler(new WhitelistRepository(act));
            var result = await handler.HandleAsync(new AddWhitelistEmailsCommand
            {
                TenantId = tenantId,
                AddedBy = AddedBy,
                Request = new AddWhitelistEmailsRequest(["A@co.com", "b@co.com"], Reason: "exención piloto"),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.AddedEmails.Should().BeEquivalentTo("a@co.com", "b@co.com"); // normalizados
            result.SkippedEmails.Should().BeEmpty();
        }

        await using var verify = NewContext(db);

        var rows = await verify.TenantWhitelistUsers.Where(w => w.TenantId == tenantId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(w => w.AddedBy == AddedBy);
        rows.Select(w => w.Email).Should().BeEquivalentTo("a@co.com", "b@co.com");

        var audits = await verify.TenantConfigAuditLogs.Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        audits.Should().HaveCount(2);
        audits.Should().OnlyContain(a => a.EntityName == "tenant_whitelist_users" && a.FieldName == "email");
        audits.Should().OnlyContain(a => a.ChangedBy == AddedBy);
        audits.Should().Contain(a => a.NewValue == "\"a@co.com\"");
        audits.Should().Contain(a => a.NewValue == "\"b@co.com\"");
    }

    [Fact]
    public async Task AC4_IsIdempotent_SkipsExistingEmails()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TenantWhitelistUsers.Add(new TenantWhitelistUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = "a@co.com",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.SaveChanges();
        }

        await using (var act = NewContext(db))
        {
            var handler = new AddWhitelistEmailsHandler(new WhitelistRepository(act));
            var result = await handler.HandleAsync(new AddWhitelistEmailsCommand
            {
                TenantId = tenantId,
                AddedBy = AddedBy,
                Request = new AddWhitelistEmailsRequest(["a@co.com", "c@co.com"], Reason: null),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.AddedEmails.Should().BeEquivalentTo("c@co.com");
            result.SkippedEmails.Should().BeEquivalentTo("a@co.com");
        }

        await using var verify = NewContext(db);
        (await verify.TenantWhitelistUsers.CountAsync(w => w.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(2);
        // Solo se audita el correo realmente insertado.
        var audits = await verify.TenantConfigAuditLogs.Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        audits.Should().ContainSingle().Which.NewValue.Should().Be("\"c@co.com\"");
    }

    // ---------- AC5: correo inválido → 422 atómico sin persistir ----------

    [Fact]
    public async Task AC5_InvalidEmail_Returns422_AndPersistsNothing()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var act = NewContext(db))
        {
            var handler = new AddWhitelistEmailsHandler(new WhitelistRepository(act));
            var result = await handler.HandleAsync(new AddWhitelistEmailsCommand
            {
                TenantId = tenantId,
                AddedBy = AddedBy,
                Request = new AddWhitelistEmailsRequest(["valido@co.com", "notanemail"], Reason: null),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeFalse();
            result.AddedEmails.Should().BeEmpty();
            result.Errors.Should().ContainSingle(e => e.Field == "emails" && e.Value == "notanemail");
        }

        // Atomicidad: ni el correo válido ni la auditoría se persisten.
        await using var verify = NewContext(db);
        (await verify.TenantWhitelistUsers.CountAsync(w => w.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
        (await verify.TenantConfigAuditLogs.CountAsync(a => a.TenantId == tenantId, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AC5_EmptyEmails_Returns422()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new AddWhitelistEmailsHandler(new WhitelistRepository(ctx));

        var result = await handler.HandleAsync(new AddWhitelistEmailsCommand
        {
            TenantId = Guid.NewGuid(),
            Request = new AddWhitelistEmailsRequest([], Reason: null),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "emails");
    }

    // ---------- AC6: GET de correos activos ----------

    [Fact]
    public async Task AC6_Get_ReturnsActiveWhitelist()
    {
        var db = NewDbName();
        var tenantId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            seed.TenantWhitelistUsers.Add(new TenantWhitelistUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = "a@co.com",
                AddedBy = AddedBy,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.TenantWhitelistUsers.Add(new TenantWhitelistUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = "b@co.com",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1),
            });
            seed.SaveChanges();
        }

        await using var ctx = NewContext(db);
        var handler = new GetWhitelistHandler(new WhitelistRepository(ctx));

        var result = await handler.HandleAsync(new GetWhitelistQuery { TenantId = tenantId }, TestContext.Current.CancellationToken);

        result.Should().HaveCount(2);
        result.Select(r => r.Email).Should().BeEquivalentTo("a@co.com", "b@co.com");
        result.Should().Contain(r => r.Email == "a@co.com" && r.AddedBy == AddedBy);
    }

    [Fact]
    public async Task AC6_Get_ReturnsEmpty_WhenNoWhitelist()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new GetWhitelistHandler(new WhitelistRepository(ctx));

        var result = await handler.HandleAsync(new GetWhitelistQuery { TenantId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    // ---------- Helpers ----------

    private static string NewDbName() => $"flit-whitelist-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
