using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// Tests del alta de compañías (botón "Crear compañía", #10118). Ejercitan el
/// handler real sobre el repositorio EF real con proveedor InMemory: validación
/// 422 sin persistir, unicidad del code, default de estado y persistencia real.
/// </summary>
public sealed class CreateCompanyHandlerTests
{
    private static readonly Guid Operator = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ValidInput_CreatesTenant_AndReturnsProjection()
    {
        var db = NewDbName();

        await using (var act = NewContext(db))
        {
            var handler = new CreateCompanyHandler(new CompanyWriteRepository(act));
            var result = await handler.HandleAsync(new CreateCompanyCommand
            {
                CreatedBy = Operator,
                Request = new CreateCompanyRequest(
                    RazonSocial: "Renting Andino S.A.S.",
                    Nit: "900123456-1",
                    Code: "RENTANDINO",
                    TenantType: "RENTING",
                    EstadoActivo: true),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
            result.Company.Should().NotBeNull();
            result.Company!.RazonSocial.Should().Be("Renting Andino S.A.S.");
            result.Company.Nit.Should().Be("900123456-1");
            result.Company.EstadoActivo.Should().BeTrue();
            result.Company.Id.Should().NotBe(Guid.Empty);
        }

        await using var verify = NewContext(db);
        var tenant = await verify.Tenants.SingleAsync(t => t.Code == "RENTANDINO", cancellationToken: TestContext.Current.CancellationToken);
        tenant.LegalName.Should().Be("Renting Andino S.A.S.");
        tenant.TaxId.Should().Be("900123456-1");
        tenant.TenantType.Should().Be("RENTING");
        tenant.IsActive.Should().BeTrue();
        tenant.CreatedBy.Should().Be(Operator);
    }

    [Fact]
    public async Task EstadoActivo_DefaultsToTrue_WhenOmitted()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest("Compañía Sin Estado", "900000000-0", "SINESTADO", "FLIT", EstadoActivo: null),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Company!.EstadoActivo.Should().BeTrue();
    }

    [Fact]
    public async Task TenantType_IsNormalizedToUpper()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest("Concesionario X", "901000000-1", "CONCEX", "concesionario", true),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        (await ctx.Tenants.SingleAsync(t => t.Code == "CONCEX", cancellationToken: TestContext.Current.CancellationToken)).TenantType.Should().Be("CONCESIONARIO");
    }

    [Fact]
    public async Task MissingRequiredFields_Return422_AndPersistNothing()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest(RazonSocial: "  ", Nit: "", Code: null, TenantType: null, EstadoActivo: null),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Company.Should().BeNull();
        result.Errors.Select(e => e.Field).Should()
            .Contain(["razonSocial", "nit", "code", "tenantType"]);
        (await ctx.Tenants.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task InvalidTenantType_Returns422()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest("Compañía Y", "902000000-2", "COMPY", "BANCO", true),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "tenantType");
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
                Code = "DUPLICADO",
                LegalName = "Existente S.A.S.",
                TaxId = "900111111-1",
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var act = NewContext(db))
        {
            var handler = new CreateCompanyHandler(new CompanyWriteRepository(act));
            var result = await handler.HandleAsync(new CreateCompanyCommand
            {
                Request = new CreateCompanyRequest("Nueva Compañía", "900222222-2", "DUPLICADO", "RENTING", true),
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.Field == "code");
        }

        await using var verify = NewContext(db);
        (await verify.Tenants.CountAsync(t => t.Code == "DUPLICADO", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task CodeTooLong_Returns422()
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest("Compañía Z", "903000000-3", new string('X', 33), "RENTING", true),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "code");
    }

    [Theory]
    [InlineData("900ABC123")]   // letras en el NIT
    [InlineData("900 123")]      // espacio
    [InlineData("900/123")]      // caracter especial
    public async Task InvalidNitCharacters_Returns422(string nit)
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest("Compañía Válida", nit, "NITCHARS", "RENTING", true),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "nit");
        (await ctx.Tenants.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Theory]
    [InlineData("Empresa <script>")]   // ángulos
    [InlineData("Empresa @ Hogar")]     // arroba
    [InlineData("Empresa #1 {test}")]   // llaves / numeral
    public async Task InvalidRazonSocialCharacters_Returns422(string razonSocial)
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest(razonSocial, "900123456-1", "RAZONCHARS", "RENTING", true),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "razonSocial");
    }

    [Theory]
    [InlineData("BAD CODE")]    // espacio
    [InlineData("CODE!")]        // signo
    [InlineData("CÓDIGO")]       // tilde (el code solo admite ASCII alfanumérico + - _)
    public async Task InvalidCodeCharacters_Returns422(string code)
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest("Compañía Válida", "900123456-1", code, "RENTING", true),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "code");
    }

    [Theory]
    [InlineData("'--.,''''/-,.-'°&/()")]               // pura puntuación
    [InlineData(".")]
    [InlineData("---")]
    [InlineData("°&&//(/(()°&&/()1234567890'''''")]   // empieza con símbolo, sin letras, puntuación repetida
    [InlineData("1234567890")]                          // solo dígitos (sin letra)
    [InlineData("AB&&//CD")]                             // puntuación repetida (&&, //)
    [InlineData("(Hola)")]                               // empieza con símbolo
    public async Task RazonSocialWeakOrPunctuation_Returns422(string razonSocial)
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest(razonSocial, "900123456-1", "OKCODE", "RENTING", true),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "razonSocial");
    }

    [Theory]
    [InlineData(".-.")]    // NIT sin dígitos
    [InlineData("--")]
    public async Task NitWithoutDigits_Returns422(string nit)
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest("Compañía Válida", nit, "OKCODE", "RENTING", true),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "nit");
    }

    [Theory]
    [InlineData("Renting Andino S.A.S.")]
    [InlineData("Autos & Más Ltda. (Sede 1)")]
    [InlineData("Compañía N° 1 / Calle 100")]
    [InlineData("3M Colombia")]      // empieza con dígito, tiene letras
    [InlineData("A&W Foods")]         // & entre letras
    public async Task ValidNameWithBusinessPunctuation_IsAccepted(string razonSocial)
    {
        await using var ctx = NewContext(NewDbName());
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));

        var result = await handler.HandleAsync(new CreateCompanyCommand
        {
            Request = new CreateCompanyRequest(razonSocial, "900123456-1", "OKCODE", "RENTING", true),
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
    }

    // ---------- Helpers ----------

    [Fact]
    public async Task DosCompaniasConElMismoNit_NoSePermiten()
    {
        // El NIT identifica a la empresa ante el Estado: dos tenants con el mismo NIT son la misma
        // empresa duplicada y aparecían dos veces —con la misma razón social— en los listados que las
        // ofrecen, sin nada que permitiera distinguirlas.
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var handler = new CreateCompanyHandler(new CompanyWriteRepository(ctx));
        var ct = TestContext.Current.CancellationToken;

        var primera = await handler.HandleAsync(new CreateCompanyCommand
        {
            CreatedBy = Operator,
            Request = new CreateCompanyRequest("Renting Andino S.A.S.", "900123456-1", "RENT1", "RENTING", true),
        }, ct);
        primera.IsValid.Should().BeTrue();

        // Mismo NIT, código distinto: antes se creaba sin queja.
        var segunda = await handler.HandleAsync(new CreateCompanyCommand
        {
            CreatedBy = Operator,
            Request = new CreateCompanyRequest("Renting Andino S.A.S.", "900123456-1", "RENT2", "RENTING", true),
        }, ct);

        segunda.IsValid.Should().BeFalse();
        segunda.Errors.Should().Contain(e => e.Field == "nit");
    }

    private static string NewDbName() => $"flit-create-company-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
