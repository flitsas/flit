using Flit.Admin.Application.Companies.MandateSigners;
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
        Handlers(FlitDbContext ctx, Guid? conFirma = null)
    {
        var reader = new DbMandateSignerReader(ctx);
        var repo = new MandateSignerRepository(ctx);
        var inner = new CreateMandateSignerHandler(OtOperable(), reader, repo);
        var vault = conFirma is null ? null : new DbSignatureVaultReader(ctx);
        return (
            new CreateCompanyMandateSignerHandler(reader, inner, vault),
            new ListCompanyMandateSignersHandler(reader));
    }

    /// <summary>Firma activa y vigente en el baúl de la compañía, a nombre del documento indicado.</summary>
    private static async Task<Guid> SeedFirmaAsync(FlitDbContext ctx, string documento, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.AddHours(-5).Date);
        ctx.SignatureVault.Add(new SignatureVaultEntity
        {
            Id = id,
            TenantId = Compania,
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = "Ana Restrepo",
            SignatureHash = "sha",
            StoragePath = "vault/f.png",
            StorageSha256 = "sha",
            Estado = "activa",
            VigenciaDesde = hoy.AddDays(-1),
            VigenciaHasta = hoy.AddYears(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(ct);
        return id;
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

    /// <summary>
    /// El correo va siempre: desde la HU #11715 no se habilita en un organismo a quien no puede firmar
    /// ante él, y con correo la validación de identidad sale al registrarlo.
    /// </summary>
    private static CompanyMandateSignerRequest Alta(params Guid[] organismos) =>
        new("Ana Restrepo", "1020304050", organismos, "CC", "ana@x.com");

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
            new CompanyMandateSignerRequest("Carlos Pérez", "9080706050", [OtEnvigado], "CC", "carlos@x.com"),
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

    // ── Firma del baúl del mandatario ─────────────────────────────────────────

    [Fact]
    public async Task LaFirmaDelBaulElegida_SePersisteEnElMandatario()
    {
        // La columna existía desde la HU #10910 pero NADIE la escribía: el trámite resolvía la firma por
        // documento y esta referencia quedaba siempre nula.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var firmaId = await SeedFirmaAsync(ctx, "1020304050", ct);
        var (create, list) = Handlers(ctx, firmaId);

        var result = await create.HandleAsync(
            Compania, Alta(OtMedellin) with { SignatureVaultId = firmaId }, null, ct);

        result.IsValid.Should().BeTrue();
        ctx.ChangeTracker.Clear();
        var id = (await list.HandleAsync(Compania, ct)).Single().Id;
        (await ctx.MandateSigners.FirstAsync(m => m.Id == id, ct))
            .SignatureVaultId.Should().Be(firmaId);
    }

    [Fact]
    public async Task UnaFirmaDeOtraPersona_SeRechaza()
    {
        // Mismo criterio que el representante legal: la firma tiene que ser de ESA persona. Sin la
        // comprobación, el mandato estamparía la firma de alguien distinto de quien lo suscribe.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var firmaAjena = await SeedFirmaAsync(ctx, "99999999", ct);
        var (create, _) = Handlers(ctx, firmaAjena);

        var result = await create.HandleAsync(
            Compania, Alta(OtMedellin) with { SignatureVaultId = firmaAjena }, null, ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "signatureVaultId");
    }

    [Fact]
    public async Task UnaFirmaInexistente_SeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, _) = Handlers(ctx, Guid.NewGuid());

        var result = await create.HandleAsync(
            Compania, Alta(OtMedellin) with { SignatureVaultId = Guid.NewGuid() }, null, ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "signatureVaultId");
    }

    [Fact]
    public async Task EditarDesdeElOrganismo_NoBorraLaFirmaQueEligioLaCompania()
    {
        // Invariante de no-regresión: `Guid?` no distingue "no gestiono la firma" de "quítala", así que
        // sin una señal explícita cada guardado desde el perfil del organismo —que no maneja este
        // campo— borraría la firma que la compañía acababa de elegir. Es la misma clase de fallo que
        // hizo que editar un representante legal vaciara el contacto de sus compañías.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var firmaId = await SeedFirmaAsync(ctx, "1020304050", ct);
        var (create, list) = Handlers(ctx, firmaId);
        await create.HandleAsync(Compania, Alta(OtMedellin) with { SignatureVaultId = firmaId }, null, ct);
        var id = await IdDelUnicoAsync(list, ct);

        // Edición que NO gestiona la firma (ActualizaFirma queda en false por defecto).
        await new MandateSignerRepository(ctx).UpdateAsync(
            new UpdateMandateSignerData(
                id, OtTenantMedellin, "Ana Restrepo", "1020304050", new string('a', 64),
                [Compania], null, null, "CC", "ana@x.com", null,
                TransitOfficeIds: [OtMedellin]),
            ct);

        ctx.ChangeTracker.Clear();
        (await ctx.MandateSigners.FirstAsync(m => m.Id == id, ct))
            .SignatureVaultId.Should().Be(firmaId);
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

    // ── HU #11715 — no se habilita a quien no puede firmar ────────────────────

    [Fact]
    public async Task Alta_SinMedioDeFirma_SeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);

        var result = await create.HandleAsync(
            Compania,
            new CompanyMandateSignerRequest("Ana Restrepo", "1020304050", [OtMedellin], "CC", null),
            null,
            ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Be(MandateSignerSigningCapability.SinMedioDeFirmaMessage);
        (await list.HandleAsync(Compania, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Alta_SinMedioDeFirma_PeroConFirmaFisica_SeAcepta()
    {
        var ct = TestContext.Current.CancellationToken;
        // El gestor eligió que ante ese organismo se firme a mano: la línea en blanco es correcta.
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);

        var result = await create.HandleAsync(
            Compania,
            new CompanyMandateSignerRequest(
                "Ana Restrepo", "1020304050", [OtMedellin], "CC", null,
                PhysicalSignatureOfficeIds: [OtMedellin]),
            null,
            ct);

        result.IsValid.Should().BeTrue();
        (await list.HandleAsync(Compania, ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task Editar_AgregandoUnOrganismoSinMedioDeFirma_SeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);
        await create.HandleAsync(
            Compania,
            new CompanyMandateSignerRequest(
                "Ana Restrepo", "1020304050", [OtMedellin], "CC", null,
                PhysicalSignatureOfficeIds: [OtMedellin]),
            null,
            ct);
        var id = await IdDelUnicoAsync(list, ct);

        var result = await Editor(ctx).HandleAsync(
            Compania, id,
            new CompanyMandateSignerRequest(
                "Ana Restrepo", "1020304050", [OtMedellin, OtEnvigado], "CC", null,
                PhysicalSignatureOfficeIds: [OtMedellin]),
            null, ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.ValidationFailed);
    }

    [Fact]
    public async Task Editar_SinAgregarOrganismos_NoObligaAArreglarLosPrevios()
    {
        var ct = TestContext.Current.CancellationToken;
        // AC6 — los vínculos previos que hoy no cumplirían se señalan (HU #11717), no se inhabilitan,
        // y editar cualquier otro dato del mandatario no puede quedar bloqueado por ellos.
        await using var ctx = NewContext();
        await SeedAsync(ctx, ct);
        var (create, list) = Handlers(ctx);
        await create.HandleAsync(
            Compania,
            new CompanyMandateSignerRequest(
                "Ana Restrepo", "1020304050", [OtMedellin], "CC", null,
                PhysicalSignatureOfficeIds: [OtMedellin]),
            null,
            ct);
        var id = await IdDelUnicoAsync(list, ct);

        var result = await Editor(ctx).HandleAsync(
            Compania, id,
            new CompanyMandateSignerRequest("Ana Restrepo Gómez", "1020304050", [OtMedellin], "CC", null),
            null, ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
    }
}
