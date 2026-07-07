using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.TransitOffices.CreateTransitOffice;
using Flit.Admin.Tests.TestDoubles;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitOffices;

/// <summary>
/// Tests del alta de tenants Organismo de Tránsito (refactor adminOT). Ejercitan el
/// handler real sobre <see cref="TransitOfficeTenantWriteRepository"/> con proveedor
/// InMemory: validación 422 sin persistir, la regla de negocio nueva "una oficina =
/// un solo tenant OT", el alta compuesta (tenant + perfil OT) y el TenantType fijo en
/// RENTING. HU #10505: "ot_admin" es ahora un rol del catálogo GLOBAL
/// (security.roles sin tenant_id) — el alta de un OT ya NO crea una fila de rol por
/// tenant (violaría UNIQUE(code, target_entity_type)); esa fila global se siembra por
/// migración/seed y se resuelve por Code en la invitación del primer admin del OT.
/// </summary>
public sealed class CreateTransitOfficeHandlerTests
{
    private static readonly Guid Operator = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Oficinas del catálogo estático de prueba (StaticTransitOfficeCatalog).
    private static readonly Guid BogotaOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
    private static readonly Guid MedellinOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000002");

    [Fact]
    public async Task ValidInput_CreatesTenantAndProfile_WithoutCreatingPerTenantRole()
    {
        var db = NewDbName();

        await using (var act = NewContext(db))
        {
            var handler = new CreateTransitOfficeHandler(
                new TransitOfficeTenantWriteRepository(act, NullAuditContextAccessor.Instance), new StaticTransitOfficeCatalog());

            var result = await handler.HandleAsync(new CreateTransitOfficeCommand
            {
                CreatedBy = Operator,
                Request = new CreateTransitOfficeRequest(
                    TransitOfficeId: MedellinOfficeId,
                    LegalName: "Secretaría de Movilidad Medellín OT",
                    TaxId: "900123456-1",
                    Code: "OT-MEDELLIN",
                    OperationMode: null),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
            result.TransitOffice.Should().NotBeNull();
            result.TransitOffice!.TenantType.Should().Be("RENTING");
            result.TransitOffice.EstadoActivo.Should().BeTrue();
            result.TransitOffice.TransitOfficeId.Should().Be(MedellinOfficeId);
            result.TransitOffice.OperationMode.Should().Be("dashboard"); // default cuando se omite
        }

        await using var verify = NewContext(db);
        var tenant = await verify.Tenants.SingleAsync(
            t => t.Code == "OT-MEDELLIN", cancellationToken: TestContext.Current.CancellationToken);
        tenant.TenantType.Should().Be("RENTING");
        tenant.CreatedBy.Should().Be(Operator);

        // HU #10505: el catálogo de roles es GLOBAL — el alta de un OT ya no crea ninguna fila
        // en security.roles (la fila "ot_admin" es global, sembrada por migración/seed).
        (await verify.Roles.CountAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Should().Be(0, "el rol ot_admin es global (HU #10505) — el alta de un OT no crea filas en security.roles");

        var profile = await verify.TransitOfficeProfiles.SingleAsync(
            p => p.TenantId == tenant.Id, cancellationToken: TestContext.Current.CancellationToken);
        profile.TransitOfficeId.Should().Be(MedellinOfficeId);
        profile.OperationMode.Should().Be("dashboard");
    }

    [Fact]
    public async Task OfficeAlreadyHasProfile_Returns422_AndPersistsNothing()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            var existingTenantId = Guid.NewGuid();
            seed.Tenants.Add(new Tenant
            {
                Id = existingTenantId,
                Code = "OT-BOGOTA-EXISTENTE",
                LegalName = "OT Bogotá existente",
                TaxId = "900000000-0",
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.TransitOfficeProfiles.Add(new TransitOfficeProfile
            {
                Id = Guid.NewGuid(),
                TenantId = existingTenantId,
                TransitOfficeId = BogotaOfficeId,
                OperationMode = "dashboard",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var act = NewContext(db))
        {
            var handler = new CreateTransitOfficeHandler(
                new TransitOfficeTenantWriteRepository(act, NullAuditContextAccessor.Instance), new StaticTransitOfficeCatalog());

            var result = await handler.HandleAsync(new CreateTransitOfficeCommand
            {
                Request = new CreateTransitOfficeRequest(
                    TransitOfficeId: BogotaOfficeId,
                    LegalName: "Otro OT Bogotá",
                    TaxId: "900111111-1",
                    Code: "OT-BOGOTA-DUP",
                    OperationMode: "dashboard"),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.Field == "transitOfficeId");
        }

        await using var verify = NewContext(db);
        (await verify.Tenants.CountAsync(t => t.Code == "OT-BOGOTA-DUP", cancellationToken: TestContext.Current.CancellationToken))
            .Should().Be(0);
    }

    [Fact]
    public async Task TransitOfficeNotInCatalog_Returns422()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateTransitOfficeHandler(
            new TransitOfficeTenantWriteRepository(ctx, NullAuditContextAccessor.Instance), new StaticTransitOfficeCatalog());

        var result = await handler.HandleAsync(new CreateTransitOfficeCommand
        {
            Request = new CreateTransitOfficeRequest(
                TransitOfficeId: Guid.NewGuid(),
                LegalName: "OT Inexistente",
                TaxId: "900222222-2",
                Code: "OT-INEXISTENTE",
                OperationMode: "dashboard"),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "transitOfficeId");
    }

    [Fact]
    public async Task MissingTransitOfficeId_Returns422()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateTransitOfficeHandler(
            new TransitOfficeTenantWriteRepository(ctx, NullAuditContextAccessor.Instance), new StaticTransitOfficeCatalog());

        var result = await handler.HandleAsync(new CreateTransitOfficeCommand
        {
            Request = new CreateTransitOfficeRequest(
                TransitOfficeId: null,
                LegalName: "OT Sin Oficina",
                TaxId: "900333333-3",
                Code: "OT-SINOFICINA",
                OperationMode: "dashboard"),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "transitOfficeId");
    }

    [Fact]
    public async Task InvalidOperationMode_Returns422()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateTransitOfficeHandler(
            new TransitOfficeTenantWriteRepository(ctx, NullAuditContextAccessor.Instance), new StaticTransitOfficeCatalog());

        var result = await handler.HandleAsync(new CreateTransitOfficeCommand
        {
            Request = new CreateTransitOfficeRequest(
                TransitOfficeId: MedellinOfficeId,
                LegalName: "OT Modo Inválido",
                TaxId: "900444444-4",
                Code: "OT-MODOINVALIDO",
                OperationMode: "invalid-mode"),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "operationMode");
    }

    [Fact]
    public async Task MissingRequiredFields_Returns422_AndPersistsNothing()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var handler = new CreateTransitOfficeHandler(
            new TransitOfficeTenantWriteRepository(ctx, NullAuditContextAccessor.Instance), new StaticTransitOfficeCatalog());

        var result = await handler.HandleAsync(new CreateTransitOfficeCommand
        {
            Request = new CreateTransitOfficeRequest(
                TransitOfficeId: null,
                LegalName: "  ",
                TaxId: "",
                Code: null,
                OperationMode: null),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.Field).Should()
            .Contain(["legalName", "taxId", "code", "transitOfficeId"]);
        (await ctx.Tenants.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task DuplicateCode_Returns422_WithoutDuplicating()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            seed.Tenants.Add(new Tenant
            {
                Id = Guid.NewGuid(),
                Code = "OT-DUPLICADO",
                LegalName = "Existente S.A.S.",
                TaxId = "900555555-5",
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var act = NewContext(db))
        {
            var handler = new CreateTransitOfficeHandler(
                new TransitOfficeTenantWriteRepository(act, NullAuditContextAccessor.Instance), new StaticTransitOfficeCatalog());

            var result = await handler.HandleAsync(new CreateTransitOfficeCommand
            {
                Request = new CreateTransitOfficeRequest(
                    TransitOfficeId: MedellinOfficeId,
                    LegalName: "Nuevo OT",
                    TaxId: "900666666-6",
                    Code: "OT-DUPLICADO",
                    OperationMode: "dashboard"),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.Field == "code");
        }

        await using var verify = NewContext(db);
        (await verify.Tenants.CountAsync(t => t.Code == "OT-DUPLICADO", cancellationToken: TestContext.Current.CancellationToken))
            .Should().Be(1);
    }

    // ---------- Helpers ----------

    private static string NewDbName() => $"flit-create-transit-office-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
