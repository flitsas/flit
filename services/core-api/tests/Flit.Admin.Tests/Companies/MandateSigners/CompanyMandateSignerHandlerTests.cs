using Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListCompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #11202 — el alta de mandatarios pasa del perfil del organismo al configurador de la COMPAÑÍA.
/// La empresa captura a la persona una sola vez y marca en cuáles de SUS organismos aplica.
/// </summary>
public sealed class CompanyMandateSignerHandlerTests
{
    private static readonly Guid Compania = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
    private static readonly Guid OtMedellin = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");
    private static readonly Guid OtEnvigado = Guid.Parse("eeeeeeee-0000-4000-8000-000000000002");
    private static readonly Guid OtAjeno = Guid.Parse("eeeeeeee-0000-4000-8000-000000000009");
    private static readonly Guid OtTenantMedellin = Guid.Parse("ffffffff-0000-4000-8000-000000000001");

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandatarios-cia-{Guid.NewGuid()}")
            .Options);

    /// <summary>Compañía con dos organismos habilitados y uno ajeno que NO debería poder elegir.</summary>
    private static async Task SeedAsync(FlitDbContext ctx, CancellationToken ct)
    {
        ctx.TransitOffices.AddRange(
            NewOffice(OtMedellin, "05001000", "Secretaría de Movilidad de Medellín"),
            NewOffice(OtEnvigado, "05266000", "Tránsito de Envigado"),
            NewOffice(OtAjeno, "11001000", "Secretaría Distrital de Movilidad"));

        // La compañía tiene que existir como tenant: la validación RF33 exige que esté activa y con
        // grant habilitado en el organismo antes de dejarle registrar un mandatario.
        ctx.Tenants.Add(new Tenant
        {
            Id = Compania,
            Code = "CIA-1",
            LegalName = "Gestora de Prueba S.A.S.",
            TaxId = "900123456",
            TenantType = "company",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        ctx.TenantTransitOfficeGrants.AddRange(
            new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(),
                TenantId = Compania,
                TransitOfficeId = OtMedellin,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(),
                TenantId = Compania,
                TransitOfficeId = OtEnvigado,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        await ctx.SaveChangesAsync(ct);
    }

    private static TransitOffice NewOffice(Guid id, string code, string name) => new()
    {
        Id = id,
        Code = code,
        Name = name,
        DepartmentCode = "05",
        CityCode = "05001",
        IsActive = true,
    };

    private static ITransitOfficeOperationalStatusReader OtOperable()
    {
        var reader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        reader.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new TransitOfficeOperationalStatusItem
            {
                Id = OtMedellin,
                Code = "05001000",
                Name = "Secretaría de Movilidad de Medellín",
                HasTenant = true,
                TenantId = OtTenantMedellin,
                EstadoActivo = true,
            });
        return reader;
    }

    private static (CreateCompanyMandateSignerHandler Create, ListCompanyMandateSignersHandler List)
        Handlers(FlitDbContext ctx)
    {
        var reader = new DbMandateSignerReader(ctx);
        var repo = new MandateSignerRepository(ctx);
        var inner = new CreateMandateSignerHandler(OtOperable(), reader, repo);
        return (new CreateCompanyMandateSignerHandler(reader, inner), new ListCompanyMandateSignersHandler(reader));
    }

    private static UpdateCompanyMandateSignerHandler Editor(FlitDbContext ctx)
    {
        var reader = new DbMandateSignerReader(ctx);
        var repo = new MandateSignerRepository(ctx);
        return new UpdateCompanyMandateSignerHandler(
            reader, new UpdateMandateSignerHandler(OtOperable(), reader, repo));
    }

    /// <summary>Id del único mandatario de la compañía, ya guardado.</summary>
    private static async Task<Guid> IdDelUnicoAsync(
        ListCompanyMandateSignersHandler list, CancellationToken ct) =>
        (await list.HandleAsync(Compania, ct)).Single().Id;

    private static CompanyMandateSignerRequest Alta(params Guid[] organismos) =>
        new("Ana Restrepo", "1020304050", organismos, "CC", null);

    // ── AC1 — alta desde la compañía ──────────────────────────────────────────

    [Fact]
    public async Task AC1_LaCompaniaRegistraUnMandatario_YQuedaGuardado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);

        var result = await create.HandleAsync(Compania, Alta(OtMedellin, OtEnvigado), null, ct);

        result.IsValid.Should().BeTrue();

        var mandatarios = await list.HandleAsync(Compania, ct);
        mandatarios.Should().ContainSingle();
        mandatarios[0].FullName.Should().Be("Ana Restrepo");
        mandatarios[0].TransitOfficeIds.Should().BeEquivalentTo([OtMedellin, OtEnvigado]);
    }

    // ── AC2 — solo organismos de esa compañía ─────────────────────────────────

    [Fact]
    public async Task AC2_SoloSeOfrecenLosOrganismosAsignadosALaCompania()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var handler = new ListCompanyTransitOfficesHandler(new DbMandateSignerReader(ctx));

        var opciones = await handler.HandleAsync(Compania, ct);

        opciones.Select(o => o.TransitOfficeId).Should().BeEquivalentTo([OtMedellin, OtEnvigado]);
        opciones.Select(o => o.Name).Should().Contain("Tránsito de Envigado");
    }

    [Fact]
    public async Task AC2_UnOrganismoAjenoSeRechazaAunqueVengaEnLaPeticion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);

        // El servidor no puede fiarse de la lista que pintó el navegador: el grant pudo revocarse, o
        // la petición puede venir de otro sitio.
        var result = await create.HandleAsync(Compania, Alta(OtMedellin, OtAjeno), null, ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Field.Should().Be("transitOfficeIds");
        (await list.HandleAsync(Compania, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task AC2_SinOrganismos_NoSeRegistra()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, _) = Handlers(ctx);

        var result = await create.HandleAsync(Compania, Alta(), null, ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be("transitOfficeIds");
    }

    // ── AC3 — consulta y edición ──────────────────────────────────────────────

    [Fact]
    public async Task AC3_ElListadoDeLaCompaniaTraeSusMandatariosConSusOrganismos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);

        await create.HandleAsync(Compania, Alta(OtMedellin), null, ct);
        await create.HandleAsync(
            Compania,
            new CompanyMandateSignerRequest("Carlos Pérez", "9080706050", [OtEnvigado], "CC", null),
            null,
            ct);

        var mandatarios = await list.HandleAsync(Compania, ct);

        mandatarios.Should().HaveCount(2);
        mandatarios.Single(m => m.FullName == "Ana Restrepo").TransitOfficeIds
            .Should().BeEquivalentTo([OtMedellin]);
        mandatarios.Single(m => m.FullName == "Carlos Pérez").TransitOfficeIds
            .Should().BeEquivalentTo([OtEnvigado]);
    }

    [Fact]
    public async Task AC3_LaCompaniaNoVeLosMandatariosDeOtraCompania()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);

        await create.HandleAsync(Compania, Alta(OtMedellin), null, ct);

        // Comparten organismo, pero cada compañía solo ve a los suyos.
        var otraCompania = Guid.NewGuid();
        (await list.HandleAsync(otraCompania, ct)).Should().BeEmpty();
    }

    // ── Edición (hallado en validación manual: la edición respondía 404) ──────

    [Fact]
    public async Task Editar_AgregandoUnOrganismo_NoResponde404_AunqueElPrimeroDeLaListaNoSeaElPrimario()
    {
        // El caso reportado. Al editar, el formulario manda la lista COMPLETA de organismos; el primero
        // de esa lista no tiene por qué ser el primario guardado. Usarlo como identidad hacía que el
        // mandatario "no existiera" justo al añadirle un organismo.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);
        await create.HandleAsync(Compania, Alta(OtMedellin), null, ct);
        var id = await IdDelUnicoAsync(list, ct);

        // Envigado primero: distinto del primario (Medellín), que es como lo manda el multiselect.
        var result = await Editor(ctx).HandleAsync(
            Compania, id, new CompanyMandateSignerRequest(
                "Ana Restrepo", "1020304050", [OtEnvigado, OtMedellin], "CC", "ana@x.com"),
            null, ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        var actualizado = (await list.HandleAsync(Compania, ct)).Single();
        actualizado.TransitOfficeIds.Should().BeEquivalentTo([OtMedellin, OtEnvigado]);
        actualizado.Email.Should().Be("ana@x.com");
    }

    [Fact]
    public async Task Editar_ConservaElPrimarioMientrasSigaEnLaLista()
    {
        // Reordenar el multiselect no debe mover el organismo primario: es el que la reactivación
        // restaura, así que moverlo por accidente cambia a dónde vuelve el mandatario.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);
        await create.HandleAsync(Compania, Alta(OtMedellin, OtEnvigado), null, ct);
        var id = await IdDelUnicoAsync(list, ct);

        await Editor(ctx).HandleAsync(
            Compania, id, new CompanyMandateSignerRequest(
                "Ana Restrepo", "1020304050", [OtEnvigado, OtMedellin], "CC", null),
            null, ct);

        ctx.ChangeTracker.Clear();
        var fila = await ctx.MandateSigners.FirstAsync(m => m.Id == id, ct);
        fila.TransitOfficeId.Should().Be(OtMedellin);
    }

    [Fact]
    public async Task Editar_RetirandoElPrimario_LoRepuntaAUnoQueSiQueda()
    {
        // Sin repuntarlo, la fila quedaría apuntando a un organismo donde el mandatario ya no aplica, y
        // la reactivación —que restaura el primario— lo resucitaría.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);
        await create.HandleAsync(Compania, Alta(OtMedellin, OtEnvigado), null, ct);
        var id = await IdDelUnicoAsync(list, ct);

        var result = await Editor(ctx).HandleAsync(
            Compania, id, new CompanyMandateSignerRequest(
                "Ana Restrepo", "1020304050", [OtEnvigado], "CC", null),
            null, ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        ctx.ChangeTracker.Clear();
        var fila = await ctx.MandateSigners.FirstAsync(m => m.Id == id, ct);
        fila.TransitOfficeId.Should().Be(OtEnvigado);
        (await list.HandleAsync(Compania, ct)).Single()
            .TransitOfficeIds.Should().BeEquivalentTo([OtEnvigado]);
    }

    [Fact]
    public async Task Editar_ElMandatarioDeOtraCompania_Responde404()
    {
        // El ámbito de la ruta es la compañía. Antes bastaba acertar el id y compartir organismo con su
        // dueño para poder editar el mandatario de otra empresa.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);
        await create.HandleAsync(Compania, Alta(OtMedellin), null, ct);
        var id = await IdDelUnicoAsync(list, ct);

        var result = await Editor(ctx).HandleAsync(
            Guid.NewGuid(), id, new CompanyMandateSignerRequest(
                "Ana Restrepo", "1020304050", [OtMedellin], "CC", null),
            null, ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.NotFound);
    }
}
