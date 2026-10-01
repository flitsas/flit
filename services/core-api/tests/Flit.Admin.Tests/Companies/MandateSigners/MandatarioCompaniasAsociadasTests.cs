using Flit.Admin.Application.Companies.MandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.AssociableCompanies;
using Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListCompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Api.Authorization;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13179 (Feature #13119 F7, Épica #13090) — guardar y validar las compañías asociadas del mandatario (por
/// tenant) y retirar la lista por Representante Legal y la comparación por NIT del vendedor. Cubre los tres
/// caminos de escritura (compañía, cliente hijo — mismo handler de compañía — y OT).
/// <para>Uso de ejemplo: el Admin de Compañía guarda <c>OfficeCompanies = [(OtMedellin, [Hija])]</c>; queda una fila
/// activa en <c>mandate_signer_associated_companies</c>. Con una compañía que no es su hija responde 403 y no
/// guarda nada.</para>
/// </summary>
public sealed class MandatarioCompaniasAsociadasTests
{
    private static readonly Guid Ot = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");
    private static readonly Guid OtTenant = Guid.Parse("ffffffff-0000-4000-8000-000000000001");
    private static readonly Guid Gestora = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
    private static readonly Guid Hija1 = Guid.Parse("dddddddd-0000-4000-8000-000000000011");
    private static readonly Guid Hija2 = Guid.Parse("dddddddd-0000-4000-8000-000000000012");
    private static readonly Guid Hija3 = Guid.Parse("dddddddd-0000-4000-8000-000000000013");
    private static readonly Guid HijaInactiva = Guid.Parse("dddddddd-0000-4000-8000-000000000014");
    private static readonly Guid Ajena = Guid.Parse("dddddddd-0000-4000-8000-000000000021");
    private static readonly Guid AjenaSinOperar = Guid.Parse("dddddddd-0000-4000-8000-000000000022");
    private static readonly Guid Inactiva = Guid.Parse("dddddddd-0000-4000-8000-000000000023");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── AC1: se guardan por tenant, y al editar se desactivan las retiradas sin duplicar ───────────

    [Fact]
    public async Task AC1_LaCompaniaGuardaSusHijas_FilasActivasPorTenant()
    {
        await using var ctx = await NewSeededContextAsync();

        var result = await CompanyCreate(ctx).HandleAsync(
            Gestora, Alta(new MandateSignerOfficeCompanies(Ot, [Hija1, Hija2])), null, Ct);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        var filas = await ctx.MandateSignerAssociatedCompanies.AsNoTracking().ToListAsync(Ct);
        filas.Select(f => f.AssociatedCompanyTenantId).Should().BeEquivalentTo([Hija1, Hija2]);
        filas.Should().OnlyContain(f => f.IsActive && f.TransitOfficeId == Ot);

        var listado = await new ListCompanyMandateSignersHandler(new DbMandateSignerReader(ctx)).HandleAsync(Gestora, Ct);
        listado.Single().OfficeCompanies!.Single().AssociatedCompanyTenantIds.Should().BeEquivalentTo([Hija1, Hija2]);
    }

    [Fact]
    public async Task AC1_AlEditar_SeDesactivanLasRetiradasYSeCreanLasNuevasSinDuplicar()
    {
        await using var ctx = await NewSeededContextAsync();
        await CompanyCreate(ctx).HandleAsync(
            Gestora, Alta(new MandateSignerOfficeCompanies(Ot, [Hija1, Hija2])), null, Ct);
        var id = await SignerIdAsync(ctx);

        var result = await CompanyUpdate(ctx).HandleAsync(
            Gestora, id, Alta(new MandateSignerOfficeCompanies(Ot, [Hija2, Hija3])), null, Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        var filas = await ctx.MandateSignerAssociatedCompanies.AsNoTracking().ToListAsync(Ct);
        filas.Where(f => f.IsActive).Select(f => f.AssociatedCompanyTenantId).Should().BeEquivalentTo([Hija2, Hija3]);
        filas.Single(f => f.AssociatedCompanyTenantId == Hija1).IsActive.Should().BeFalse("baja lógica");
        filas.Count(f => f.AssociatedCompanyTenantId == Hija2).Should().Be(1, "no se duplica la que ya estaba");

        // Volver a asociar la retirada REACTIVA su fila en vez de duplicarla.
        await CompanyUpdate(ctx).HandleAsync(
            Gestora, id, Alta(new MandateSignerOfficeCompanies(Ot, [Hija1])), null, Ct);
        (await ctx.MandateSignerAssociatedCompanies.AsNoTracking().CountAsync(f => f.AssociatedCompanyTenantId == Hija1, Ct))
            .Should().Be(1);
    }

    [Fact]
    public async Task AC1_LaEdicionSinListaNoTocaLasAsociaciones_YUnaListaVaciaLasRetira()
    {
        await using var ctx = await NewSeededContextAsync();
        await CompanyCreate(ctx).HandleAsync(
            Gestora, Alta(new MandateSignerOfficeCompanies(Ot, [Hija1])), null, Ct);
        var id = await SignerIdAsync(ctx);

        await CompanyUpdate(ctx).HandleAsync(Gestora, id, Alta(), null, Ct);
        (await ctx.MandateSignerAssociatedCompanies.AsNoTracking().CountAsync(f => f.IsActive, Ct)).Should().Be(1);

        await CompanyUpdate(ctx).HandleAsync(
            Gestora, id, Alta(new MandateSignerOfficeCompanies(Ot, [])), null, Ct);
        (await ctx.MandateSignerAssociatedCompanies.AsNoTracking().CountAsync(f => f.IsActive, Ct)).Should().Be(0);
    }

    [Fact]
    public async Task AC1_DarDeBajaAlMandatario_DaDeBajaSusAsociaciones()
    {
        await using var ctx = await NewSeededContextAsync();
        await CompanyCreate(ctx).HandleAsync(
            Gestora, Alta(new MandateSignerOfficeCompanies(Ot, [Hija1])), null, Ct);
        var id = await SignerIdAsync(ctx);

        var inactivate = new InactivateMandateSignerHandler(OtOperable(), new DbMandateSignerReader(ctx), new MandateSignerRepository(ctx));
        await inactivate.HandleAsync(
            new InactivateMandateSignerCommand { TransitOfficeId = Ot, MandateSignerId = id }, Ct);

        (await ctx.MandateSignerAssociatedCompanies.AsNoTracking().CountAsync(f => f.IsActive, Ct)).Should().Be(0);
    }

    // ── AC2: fuera del alcance del perfil → 403 y no se guarda nada ─────────────────────────────────

    [Fact]
    public async Task AC2_ElAdminDeCompaniaQueEnviaUnaQueNoEsHija_RecibeFueraDeAlcance_YNoSeGuardaNada()
    {
        await using var ctx = await NewSeededContextAsync();

        var act = async () => await CompanyCreate(ctx).HandleAsync(
            Gestora, Alta(new MandateSignerOfficeCompanies(Ot, [Hija1, Ajena])), null, Ct);

        await act.Should().ThrowAsync<AssociatedCompanyOutOfScopeException>();
        (await ctx.MandateSigners.CountAsync(Ct)).Should().Be(0);
        (await ctx.MandateSignerAssociatedCompanies.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task AC2_EnLaEdicion_TambienSeRechazaYNoSeTocaNada()
    {
        await using var ctx = await NewSeededContextAsync();
        await CompanyCreate(ctx).HandleAsync(
            Gestora, Alta(new MandateSignerOfficeCompanies(Ot, [Hija1])), null, Ct);
        var id = await SignerIdAsync(ctx);

        var act = async () => await CompanyUpdate(ctx).HandleAsync(
            Gestora, id, Alta(new MandateSignerOfficeCompanies(Ot, [Hija2, Ajena])), null, Ct);

        await act.Should().ThrowAsync<AssociatedCompanyOutOfScopeException>();
        (await ctx.MandateSignerAssociatedCompanies.AsNoTracking().Where(f => f.IsActive)
                .Select(f => f.AssociatedCompanyTenantId).ToListAsync(Ct))
            .Should().Equal(Hija1);
    }

    [Fact]
    public async Task AC2_ElFiltroDeLaApiLoTraduceA403ConElCodigo()
    {
        var filter = new MandateSignerLinkConflictFilter();
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();
        var result = await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(http),
            _ => throw new AssociatedCompanyOutOfScopeException());

        await ((IResult)result!).ExecuteAsync(http);

        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        http.Response.Body.Position = 0;
        (await new StreamReader(http.Response.Body).ReadToEndAsync(Ct)).Should().Contain("compania_asociada_fuera_de_alcance");
    }

    // ── AC3: propia, inactiva o inexistente → 422 por elemento y no se guarda ninguna ──────────────

    [Fact]
    public async Task AC3_PropiaInactivaOInexistente_Dan422PorElemento_YNoSeGuardaNinguna()
    {
        await using var ctx = await NewSeededContextAsync();
        var fantasma = Guid.NewGuid();

        var result = await CompanyCreate(ctx).HandleAsync(
            Gestora,
            Alta(new MandateSignerOfficeCompanies(Ot, [Hija1, Gestora, HijaInactiva, fantasma])),
            null, Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Value == Gestora.ToString() && e.Message == MandateSignerAssociationRules.PropiaMessage);
        result.Errors.Should().Contain(e => e.Value == HijaInactiva.ToString() && e.Message == MandateSignerAssociationRules.InactivaMessage);
        result.Errors.Should().Contain(e => e.Value == fantasma.ToString() && e.Message == MandateSignerAssociationRules.InexistenteMessage);
        result.Errors.Should().NotContain(e => e.Value == Hija1.ToString(), "la válida no se marca");
        (await ctx.MandateSigners.CountAsync(Ct)).Should().Be(0);
        (await ctx.MandateSignerAssociatedCompanies.CountAsync(Ct)).Should().Be(0);
    }

    // ── AC4: el OT asocia sin trámites previos, con RF33 vigente ──────────────────────────────────

    [Fact]
    public async Task AC4_ElOtAsociaUnaCompaniaActivaQueOperaEnElOrganismo_SinTramitesPrevios()
    {
        await using var ctx = await NewSeededContextAsync();

        var result = await OtCreate(ctx, organismo: Ot, [new MandateSignerOfficeCompanies(Ot, [Ajena])], "organismo");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        (await ctx.MandateSignerAssociatedCompanies.AsNoTracking().SingleAsync(Ct))
            .AssociatedCompanyTenantId.Should().Be(Ajena);
    }

    [Fact]
    public async Task AC4_RF33SeConserva_ElOtNoAsociaUnaCompaniaQueNoOperaEnSuOrganismo()
    {
        await using var ctx = await NewSeededContextAsync();

        var result = await OtCreate(
            ctx, organismo: Ot, [new MandateSignerOfficeCompanies(Ot, [AjenaSinOperar, Inactiva])], "organismo");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Value == AjenaSinOperar.ToString()
            && e.Message == MandateSignerAssociationRules.NoOperaEnOrganismoMessage);
        result.Errors.Should().Contain(e => e.Value == Inactiva.ToString()
            && e.Message == MandateSignerAssociationRules.InactivaMessage);
        (await ctx.MandateSignerAssociatedCompanies.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task AC4_ElOtTampocoPuedeAsociarEnOrganismosQueNoSonDelMandatario()
    {
        await using var ctx = await NewSeededContextAsync();
        var otroOrganismo = Guid.NewGuid();

        var result = await OtCreate(ctx, organismo: Ot, [new MandateSignerOfficeCompanies(otroOrganismo, [Ajena])], "organismo");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message == MandateSignerAssociationRules.OrganismoAjenoMessage);
    }

    [Fact]
    public async Task AC4_ElOtAlEditar_ReemplazaSoloLosOrganismosQueEnvia()
    {
        await using var ctx = await NewSeededContextAsync();
        var created = await OtCreate(ctx, organismo: Ot, [new MandateSignerOfficeCompanies(Ot, [Ajena])], "organismo");
        var id = created.MandateSignerId!.Value;

        var reader = new DbMandateSignerReader(ctx);
        var update = new UpdateMandateSignerHandler(
            OtOperable(), reader, new MandateSignerRepository(ctx), Associable(ctx));
        var result = await update.HandleAsync(
            new UpdateMandateSignerCommand
            {
                TransitOfficeId = Ot,
                MandateSignerId = id,
                FullName = "Ana Restrepo",
                DocumentNumber = "1020304050",
                CompanyTenantIds = [Gestora],
                SignatureMethod = "biometria",
                OfficeCompanies = [new MandateSignerOfficeCompanies(Ot, [])],
                ConfiguredByScope = "organismo",
                CompanyVisibility = OtCompanyVisibility.WholeNetwork,
            },
            Ct);

        result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        (await ctx.MandateSignerAssociatedCompanies.AsNoTracking().CountAsync(f => f.IsActive, Ct)).Should().Be(0);
    }

    // ── AC5: la resolución deja de comparar el NIT; el candidato propio se incluye siempre ─────────

    [Fact]
    public async Task AC5_ElDirectorioYaNoConsultaLaTablaLegadaNiCompara_ElNitDelMandante()
    {
        await using var ctx = await NewSeededContextAsync();
        await CompanyCreate(ctx).HandleAsync(Gestora, Alta(), null, Ct);
        var id = await SignerIdAsync(ctx);

        // Fila legada que ANTES acotaba el mandatario a la empresa ACME por NIT del vendedor.
        var ficha = Guid.NewGuid();
        ctx.RepresentedCompanies.Add(new RepresentedCompanyEntity
        {
            Id = ficha, TenantId = Gestora, DocumentType = "NIT", DocumentNumber = "900111111", Name = "ACME SAS",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.MandateSignerRepresentedCompanies.Add(new MandateSignerRepresentedCompany
        {
            Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = Ot, RepresentedCompanyId = ficha,
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);

        var paraOtroNit = await Directorio(ctx).GetCandidatesAsync(Ot, Gestora, "900222222", Ct);
        var sinNit = await Directorio(ctx).GetCandidatesAsync(Ot, Gestora, null, Ct);

        paraOtroNit.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(id, "la acotación por NIT ya no existe: el propio entra siempre");
        sinNit.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(id);
    }

    [Fact]
    public async Task AC5_ElModeloEfDeclaraFkAlMandatario_ParaOrdenarElInsertDelPuente()
    {
        await using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(MandateSignerAssociatedCompany));
        entity.Should().NotBeNull();
        entity!.GetForeignKeys().Should().ContainSingle(f => f.PrincipalEntityType.ClrType == typeof(MandateSigner));
        await Task.CompletedTask;
    }

    // ── AC6: rutas retiradas y escrituras intactas ──────────────────────────────────────────────────

    [Fact]
    public void AC6_ListRepresentedCompaniesAsyncSeConserva_ParaElFlujoDeEscrituras()
    {
        typeof(Flit.Admin.Domain.Companies.LegalRepresentatives.ILegalRepresentativeReader)
            .GetMethod("ListRepresentedCompaniesAsync").Should().NotBeNull();
    }

    // ── AC7: sin compañías asociadas es válido ──────────────────────────────────────────────────────

    [Fact]
    public async Task AC7_SinCompaniasAsociadas_AplicaSoloASuCompania_YEsValido()
    {
        await using var ctx = await NewSeededContextAsync();

        var sinLista = await CompanyCreate(ctx).HandleAsync(Gestora, Alta(), null, Ct);

        sinLista.IsValid.Should().BeTrue();
        (await ctx.MandateSignerAssociatedCompanies.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task AC7_UnaListaVaciaPorOrganismo_EsValida_ParaElOt()
    {
        await using var ctx = await NewSeededContextAsync();

        var conVacia = await OtCreate(ctx, organismo: Ot, [new MandateSignerOfficeCompanies(Ot, [])], "organismo");

        conVacia.IsValid.Should().BeTrue();
        (await ctx.MandateSignerAssociatedCompanies.CountAsync(Ct)).Should().Be(0);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static CompanyMandateSignerRequest Alta(params MandateSignerOfficeCompanies[] officeCompanies) =>
        new("Ana Restrepo", "1020304050", [Ot], "CC", null,
            SignatureMethod: "biometria",
            OfficeCompanies: officeCompanies.Length == 0 ? null : officeCompanies);

    private static async Task<Guid> SignerIdAsync(FlitDbContext ctx) =>
        (await ctx.MandateSigners.AsNoTracking().SingleAsync(Ct)).Id;

    private static MandatarioAssociableCompanies Associable(FlitDbContext ctx) =>
        new(new ManagingCompanyDirectory(ctx), new CompanyHierarchyRepository(ctx));

    private static CreateCompanyMandateSignerHandler CompanyCreate(FlitDbContext ctx)
    {
        var reader = new DbMandateSignerReader(ctx);
        var inner = new CreateMandateSignerHandler(
            OtOperable(), reader, new MandateSignerRepository(ctx), associable: Associable(ctx));
        return new CreateCompanyMandateSignerHandler(reader, inner);
    }

    private static UpdateCompanyMandateSignerHandler CompanyUpdate(FlitDbContext ctx)
    {
        var reader = new DbMandateSignerReader(ctx);
        return new UpdateCompanyMandateSignerHandler(
            reader, new UpdateMandateSignerHandler(OtOperable(), reader, new MandateSignerRepository(ctx), Associable(ctx)));
    }

    private static Task<CreateMandateSignerResult> OtCreate(
        FlitDbContext ctx,
        Guid organismo,
        IReadOnlyList<MandateSignerOfficeCompanies> officeCompanies,
        string origen,
        string documento = "1020304050") =>
        new CreateMandateSignerHandler(
            OtOperable(), new DbMandateSignerReader(ctx), new MandateSignerRepository(ctx), associable: Associable(ctx))
            .HandleAsync(
                new CreateMandateSignerCommand
                {
                    TransitOfficeId = organismo,
                    FullName = "Ana Restrepo",
                    DocumentNumber = documento,
                    CompanyTenantIds = [Gestora],
                    SignatureMethod = "biometria",
                    OfficeCompanies = officeCompanies,
                    ConfiguredByScope = origen,
                    CompanyVisibility = OtCompanyVisibility.WholeNetwork,
                },
                Ct);

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-13179-{Guid.NewGuid()}")
            .Options);

    private static ITransitOfficeOperationalStatusReader OtOperable()
    {
        var reader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        reader.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new TransitOfficeOperationalStatusItem
            {
                Id = Ot, Code = "05001000", Name = "OT de prueba", HasTenant = true, TenantId = OtTenant, EstadoActivo = true,
            });
        return reader;
    }

    private static Tenant NewTenant(Guid id, string nit, bool active = true, Guid? parent = null) => new()
    {
        Id = id,
        Code = "T-" + id.ToString("N")[..8],
        LegalName = "Compañía " + id.ToString("N")[..4],
        TaxId = nit,
        TenantType = "CONCESIONARIO",
        IsActive = active,
        ParentTenantId = parent,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Gestora con tres hijas activas y una inactiva; dos compañías ajenas (una opera en el organismo, otra no) y una
    /// inactiva. Solo la gestora, las hijas y «Ajena» tienen grant habilitado en el organismo.
    /// </summary>
    private static async Task<FlitDbContext> NewSeededContextAsync()
    {
        var ctx = NewContext();
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = Ot, Code = "05001000", Name = "OT de prueba", DepartmentCode = "05", CityCode = "05001", IsActive = true,
        });
        ctx.Tenants.AddRange(
            NewTenant(Gestora, "900000001-1"),
            NewTenant(Hija1, "900000011-1", parent: Gestora),
            NewTenant(Hija2, "900000012-1", parent: Gestora),
            NewTenant(Hija3, "900000013-1", parent: Gestora),
            NewTenant(HijaInactiva, "900000014-1", active: false, parent: Gestora),
            NewTenant(Ajena, "900000021-1"),
            NewTenant(AjenaSinOperar, "900000022-1"),
            NewTenant(Inactiva, "900000023-1", active: false));

        foreach (var tenant in new[] { Gestora, Hija1, Hija2, Hija3, Ajena, Inactiva })
        {
            ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(), TenantId = tenant, TransitOfficeId = Ot, IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await ctx.SaveChangesAsync(Ct);
        return ctx;
    }

    private static MandateSignerDirectory Directorio(FlitDbContext ctx)
    {
        var otStatusReader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        otStatusReader.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TransitOfficeOperationalStatusItem?)null);
        var identityRepo = Substitute.For<IProcedureInstanceRepository>();
        identityRepo.ListBiometricValidationsByPersonAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((Array.Empty<Flit.Tramites.Domain.Entities.ProcedureInstanceBiometricValidation>(), 0, false));
        return new MandateSignerDirectory(ctx, otStatusReader, new IdentityVigenciaPorDocumentoResolver(identityRepo));
    }
}
