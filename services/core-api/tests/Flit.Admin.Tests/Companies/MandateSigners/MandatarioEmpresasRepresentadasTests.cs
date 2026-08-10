using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// El mandatario se asocia a EMPRESAS REPRESENTADAS por organismo, y el trámite ofrece solo los que
/// aplican a la empresa que otorga el mandato.
///
/// <para><b>La ausencia significa "aplica a todas"</b>, y es lo que más importa proteger: los
/// mandatarios registrados antes de esta acotación no tienen ninguna asociada, así que sin esa regla
/// desaparecerían de todos los trámites al desplegar.</para>
/// </summary>
public sealed class MandatarioEmpresasRepresentadasTests
{
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid Gestora = Guid.NewGuid();

    private const string NitAcme = "900111111";
    private const string NitBeta = "900222222";

    [Fact]
    public async Task AltaConEmpresasRepresentadas_PersisteElPuenteEnUnSoloSaveChanges()
    {
        // Reproduce el camino del configurador de compañía (officeCompanies en el alta).
        // En Postgres, sin HasOne al mandatario este SaveChanges fallaba con 23503.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var acme = SeedEmpresa(ctx, NitAcme, "ACME SAS");
        var repo = new MandateSignerRepository(ctx);

        var signerId = await repo.CreateAsync(
            new CreateMandateSignerData(
                TransitOfficeId: Ot,
                OtTenantId: Guid.NewGuid(),
                FullName: "Daniel Amado",
                DocumentNumber: "1193552679",
                IntegrityHash: new string('b', 64),
                RegisteredAt: DateTimeOffset.UtcNow,
                CompanyTenantIds: [Gestora],
                CreatedBy: null,
                CorrelationId: null,
                DocumentType: "CC",
                Email: "daniel@ejemplo.com",
                TransitOfficeIds: [Ot],
                OfficeCompanies:
                [
                    new MandateSignerOfficeCompanies(Ot, [acme]),
                ]),
            ct);

        var filas = await ctx.MandateSignerRepresentedCompanies
            .Where(x => x.MandateSignerId == signerId && x.IsActive)
            .ToListAsync(ct);

        filas.Should().ContainSingle();
        filas[0].RepresentedCompanyId.Should().Be(acme);
        filas[0].TransitOfficeId.Should().Be(Ot);
    }

    [Fact]
    public void ElModeloEfDeclaraFkAlMandatario_ParaOrdenarElInsertDelPuente()
    {
        // Sin HasOne(MandateSigner), EF puede insertar mandate_signer_represented_companies
        // antes que mandate_signers y Postgres responde 23503 fk_msrc_mandate_signer.
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(MandateSignerRepresentedCompany));
        entity.Should().NotBeNull();
        var fk = entity!.GetForeignKeys()
            .SingleOrDefault(f => f.PrincipalEntityType.ClrType == typeof(MandateSigner));
        fk.Should().NotBeNull();
        fk!.Properties.Select(p => p.Name).Should().Equal(nameof(MandateSignerRepresentedCompany.MandateSignerId));
    }

    [Fact]
    public async Task ElTramiteSoloOfreceLosMandatariosDeLaEmpresaQueOtorga()
    {
        await using var ctx = NewContext();
        var acme = SeedEmpresa(ctx, NitAcme, "ACME SAS");
        var beta = SeedEmpresa(ctx, NitBeta, "BETA SAS");
        var deAcme = await SeedMandatarioAsync(ctx, "Ana", "111", [acme]);
        var deBeta = await SeedMandatarioAsync(ctx, "Beto", "222", [beta]);

        var candidatos = await new MandateSignerDirectory(ctx)
            .GetCandidatesAsync(Ot, Gestora, NitAcme, TestContext.Current.CancellationToken);

        candidatos.Select(c => c.Id).Should().BeEquivalentTo([deAcme]);
        candidatos.Select(c => c.Id).Should().NotContain(deBeta);
    }

    [Fact]
    public async Task UnMandatarioSinEmpresasAsociadas_SigueApareciendoParaCualquiera()
    {
        // Es el caso de todos los mandatarios que ya existen. Sin esta regla, desplegar los borraría de
        // la lista de cada trámite hasta que alguien los reasociara uno por uno.
        await using var ctx = NewContext();
        var acme = SeedEmpresa(ctx, NitAcme, "ACME SAS");
        var universal = await SeedMandatarioAsync(ctx, "Universal", "999", []);
        var soloAcme = await SeedMandatarioAsync(ctx, "Ana", "111", [acme]);

        var paraOtra = await new MandateSignerDirectory(ctx)
            .GetCandidatesAsync(Ot, Gestora, NitBeta, TestContext.Current.CancellationToken);

        paraOtra.Select(c => c.Id).Should().BeEquivalentTo([universal]);
        paraOtra.Select(c => c.Id).Should().NotContain(soloAcme);
    }

    [Fact]
    public async Task SinNitDelMandante_NoSeAcotaNada()
    {
        // Durante el asistente la parte que otorga puede no estar capturada todavía: acotar sin saber
        // contra qué dejaría la elección de mandatario vacía justo cuando hace falta.
        await using var ctx = NewContext();
        var acme = SeedEmpresa(ctx, NitAcme, "ACME SAS");
        await SeedMandatarioAsync(ctx, "Ana", "111", [acme]);
        await SeedMandatarioAsync(ctx, "Universal", "999", []);

        var todos = await new MandateSignerDirectory(ctx)
            .GetCandidatesAsync(Ot, Gestora, null, TestContext.Current.CancellationToken);

        todos.Should().HaveCount(2);
    }

    [Fact]
    public async Task LaAsociacionEsPorORGANISMO_NoPorPersona()
    {
        // El mismo mandatario puede atender a una empresa en un organismo y no en otro. Si la
        // asociación fuera de la persona, acotar en uno lo acotaría en todos.
        await using var ctx = NewContext();
        var otroOt = Guid.NewGuid();
        var acme = SeedEmpresa(ctx, NitAcme, "ACME SAS");

        // La MISMA persona aplica en los dos organismos, pero solo en uno está acotada a ACME.
        var id = await SeedMandatarioAsync(ctx, "Ana", "111", [acme], otroOt);
        AgregarOrganismo(ctx, id, Ot, empresas: []);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Donde NO tiene asociación, aplica a cualquier empresa.
        var sinAcotar = await new MandateSignerDirectory(ctx)
            .GetCandidatesAsync(Ot, Gestora, NitBeta, TestContext.Current.CancellationToken);
        sinAcotar.Select(c => c.Id).Should().Contain(id);

        // Donde sí la tiene, solo a ACME: para otra empresa no se ofrece.
        var acotado = await new MandateSignerDirectory(ctx)
            .GetCandidatesAsync(otroOt, Gestora, NitBeta, TestContext.Current.CancellationToken);
        acotado.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Guid SeedEmpresa(FlitDbContext ctx, string nit, string nombre)
    {
        var id = Guid.NewGuid();
        ctx.RepresentedCompanies.Add(new RepresentedCompanyEntity
        {
            Id = id,
            TenantId = Gestora,
            DocumentType = "NIT",
            DocumentNumber = nit,
            Name = nombre,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return id;
    }

    /// <summary>Mandatario activo de la gestora en el organismo, con sus empresas representadas.</summary>
    private static async Task<Guid> SeedMandatarioAsync(
        FlitDbContext ctx, string nombre, string documento, Guid[] empresas, Guid? enOrganismo = null)
    {
        var officeId = enOrganismo ?? Ot;
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = id,
            TransitOfficeId = officeId,
            FullName = nombre,
            DocumentType = "CC",
            DocumentNumber = documento,
            IntegrityHash = new string('a', 64),
            RegisteredAt = now,
            IsActive = true,
            CreatedAt = now,
        });
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(),
            MandateSignerId = id,
            TransitOfficeId = officeId,
            CompanyTenantId = Gestora,
            IsActive = true,
            CreatedAt = now,
        });
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(),
            MandateSignerId = id,
            TransitOfficeId = officeId,
            IsActive = true,
            CreatedAt = now,
        });
        foreach (var empresaId in empresas)
        {
            ctx.MandateSignerRepresentedCompanies.Add(new MandateSignerRepresentedCompany
            {
                Id = Guid.NewGuid(),
                MandateSignerId = id,
                TransitOfficeId = officeId,
                RepresentedCompanyId = empresaId,
                IsActive = true,
                CreatedAt = now,
            });
        }

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>Añade el mandatario a otro organismo (mismo puente que usa el alta real).</summary>
    private static void AgregarOrganismo(FlitDbContext ctx, Guid signerId, Guid officeId, Guid[] empresas)
    {
        var now = DateTimeOffset.UtcNow;
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(),
            MandateSignerId = signerId,
            TransitOfficeId = officeId,
            CompanyTenantId = Gestora,
            IsActive = true,
            CreatedAt = now,
        });
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(),
            MandateSignerId = signerId,
            TransitOfficeId = officeId,
            IsActive = true,
            CreatedAt = now,
        });
        foreach (var empresaId in empresas)
        {
            ctx.MandateSignerRepresentedCompanies.Add(new MandateSignerRepresentedCompany
            {
                Id = Guid.NewGuid(),
                MandateSignerId = signerId,
                TransitOfficeId = officeId,
                RepresentedCompanyId = empresaId,
                IsActive = true,
                CreatedAt = now,
            });
        }
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-msrc-{Guid.NewGuid()}")
            .Options);
}
