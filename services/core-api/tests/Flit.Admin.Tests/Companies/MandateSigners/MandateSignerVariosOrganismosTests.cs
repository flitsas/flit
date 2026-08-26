using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #11201 (Feature #11190) — un mandatario para varios organismos de tránsito.
///
/// <para>Antes el organismo vivía EN el mandatario, así que la misma persona firmando en tres
/// organismos eran tres registros distintos: tres documentos que mantener al día, tres firmas del baúl
/// y tres validaciones de identidad que renovar por separado. Ahora la persona existe una sola vez y
/// sus organismos son filas de un puente.</para>
/// </summary>
public sealed class MandateSignerVariosOrganismosTests
{
    private static readonly Guid OtMedellin = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid OtEnvigado = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Guid OtBello = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000003");
    private static readonly Guid OtTenant = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Compania = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandatarios-{Guid.NewGuid()}")
            .Options);

    /// <summary>
    /// HU #11752 (ADR-0050) — MandateSignerDirectory ya no lee <c>admin.admin_identity_validations</c>:
    /// resuelve vigencia contra el módulo Identidad vía el tenant del OT. Esta suite no ejercita
    /// vigencia de identidad (solo organismos/compañías), así que el reader de estado operativo
    /// devuelve "sin tenant" y el resolver recibe un repositorio que nunca se invoca.
    /// </summary>
    private static MandateSignerDirectory Directorio(FlitDbContext ctx)
    {
        var otStatusReader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        otStatusReader
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TransitOfficeOperationalStatusItem?)null);
        var identityResolver = new IdentityVigenciaPorDocumentoResolver(Substitute.For<IProcedureInstanceRepository>());
        return new MandateSignerDirectory(ctx, otStatusReader, identityResolver);
    }

    private static CreateMandateSignerData Alta(IReadOnlyList<Guid>? organismos = null) =>
        new(
            TransitOfficeId: OtMedellin,
            OtTenantId: OtTenant,
            FullName: "Ana Restrepo",
            DocumentNumber: "1020304050",
            IntegrityHash: new string('a', 64),
            RegisteredAt: DateTimeOffset.UtcNow,
            CompanyTenantIds: [Compania],
            CreatedBy: null,
            CorrelationId: null,
            DocumentType: "CC",
            Email: "ana@ejemplo.com",
            UserId: null,
            TransitOfficeIds: organismos);

    // ── AC1 — un mandatario, varios organismos ────────────────────────────────

    [Fact]
    public async Task AC1_AltaConTresOrganismos_DejaUnaSolaPersonaConSusTresOrganismos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var repo = new MandateSignerRepository(ctx);

        var signerId = await repo.CreateAsync(Alta([OtMedellin, OtEnvigado, OtBello]), ct);

        // Una sola persona: es el punto de la HU. Antes habrían sido tres filas.
        (await ctx.MandateSigners.CountAsync(ct)).Should().Be(1);

        var organismos = await ctx.MandateSignerTransitOffices
            .Where(o => o.MandateSignerId == signerId && o.IsActive)
            .Select(o => o.TransitOfficeId)
            .ToListAsync(ct);

        organismos.Should().BeEquivalentTo([OtMedellin, OtEnvigado, OtBello]);
    }

    [Fact]
    public async Task AC1_LaCompaniaQuedaAsignadaEnCadaOrganismo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var repo = new MandateSignerRepository(ctx);

        var signerId = await repo.CreateAsync(Alta([OtMedellin, OtEnvigado]), ct);

        // La asignación a compañías lleva el organismo en su llave y es lo que consulta el trámite:
        // escribirla solo para el primario dejaría al mandatario invisible en el otro organismo.
        var asignaciones = await ctx.MandateSignerCompanies
            .Where(c => c.MandateSignerId == signerId && c.IsActive)
            .Select(c => c.TransitOfficeId)
            .ToListAsync(ct);

        asignaciones.Should().BeEquivalentTo([OtMedellin, OtEnvigado]);
    }

    [Fact]
    public async Task AC1_SinListaDeOrganismos_ElAltaConservaElComportamientoDeSiempre()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var repo = new MandateSignerRepository(ctx);

        // Es el alta desde el perfil del organismo, que no manda lista: su único organismo es el suyo.
        var signerId = await repo.CreateAsync(Alta(organismos: null), ct);

        var organismos = await ctx.MandateSignerTransitOffices
            .Where(o => o.MandateSignerId == signerId && o.IsActive)
            .Select(o => o.TransitOfficeId)
            .ToListAsync(ct);

        organismos.Should().BeEquivalentTo([OtMedellin]);
    }

    // ── AC2 — edición en un solo lugar ────────────────────────────────────────

    [Fact]
    public async Task AC2_CorregirLosDatosPersonales_AplicaEnTodosSusOrganismos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var repo = new MandateSignerRepository(ctx);
        var reader = new DbMandateSignerReader(ctx);

        var signerId = await repo.CreateAsync(Alta([OtMedellin, OtEnvigado]), ct);

        await repo.UpdateAsync(
            new UpdateMandateSignerData(
                signerId, OtTenant, "Ana María Restrepo", "1020304050", new string('b', 64),
                [Compania], null, null, "CC", "ana.maria@ejemplo.com"),
            ct);

        // Un solo registro corregido: el nombre nuevo se ve desde CUALQUIER organismo, sin repetir
        // la corrección en cada uno.
        foreach (var ot in new[] { OtMedellin, OtEnvigado })
        {
            var enElOt = await reader.ListByOtAsync(ot, ct);
            enElOt.Should().ContainSingle()
                .Which.FullName.Should().Be("Ana María Restrepo");
        }
    }

    // ── AC3 — quitar un organismo ─────────────────────────────────────────────

    [Fact]
    public async Task AC3_RetirarUnOrganismo_DejaDeEstarDisponibleAhiYSigueEnLosDemas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var repo = new MandateSignerRepository(ctx);
        var reader = new DbMandateSignerReader(ctx);

        var signerId = await repo.CreateAsync(Alta([OtMedellin, OtEnvigado, OtBello]), ct);

        await repo.UpdateAsync(
            new UpdateMandateSignerData(
                signerId, OtTenant, "Ana Restrepo", "1020304050", new string('a', 64),
                [Compania], null, null, "CC", "ana@ejemplo.com", null,
                TransitOfficeIds: [OtMedellin, OtBello]),
            ct);

        (await reader.ListByOtAsync(OtEnvigado, ct)).Should().BeEmpty();
        (await reader.ListByOtAsync(OtMedellin, ct)).Should().ContainSingle();
        (await reader.ListByOtAsync(OtBello, ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task AC3_AlRetirarUnOrganismo_ElTramiteYaNoLoOfreceAhi()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var repo = new MandateSignerRepository(ctx);
        var directorio = Directorio(ctx);

        var signerId = await repo.CreateAsync(Alta([OtMedellin, OtEnvigado]), ct);

        // Antes de retirarlo firma en los dos.
        (await directorio.GetCandidatesAsync(OtEnvigado, Compania, null, ct)).Should().ContainSingle();

        await repo.UpdateAsync(
            new UpdateMandateSignerData(
                signerId, OtTenant, "Ana Restrepo", "1020304050", new string('a', 64),
                [Compania], null, null, "CC", "ana@ejemplo.com", null,
                TransitOfficeIds: [OtMedellin]),
            ct);

        (await directorio.GetCandidatesAsync(OtEnvigado, Compania, null, ct)).Should().BeEmpty();
        (await directorio.GetCandidatesAsync(OtMedellin, Compania, null, ct)).Should().ContainSingle();
    }

    // ── AC4 — los mandatarios actuales siguen funcionando ─────────────────────

    /// <summary>
    /// AC4 — un mandatario dado de alta antes del cambio: el backfill del DDL le crea su vínculo con el
    /// organismo que tenía. Aquí se reproduce ese estado y se comprueba que conserva organismo y
    /// compañías, y que editarlo sin tocar organismos no se los quita.
    /// </summary>
    [Fact]
    public async Task AC4_MandatarioAnteriorAlCambio_ConservaOrganismoYCompanias()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var reader = new DbMandateSignerReader(ctx);
        var repo = new MandateSignerRepository(ctx);

        var signerId = Guid.NewGuid();
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = signerId,
            TransitOfficeId = OtMedellin,
            FullName = "Carlos Pérez",
            DocumentType = "CC",
            DocumentNumber = "9080706050",
            IntegrityHash = new string('c', 64),
            RegisteredAt = DateTimeOffset.UtcNow.AddMonths(-6),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6),
        });
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(),
            MandateSignerId = signerId,
            TransitOfficeId = OtMedellin,
            CompanyTenantId = Compania,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6),
        });
        // Lo que deja el backfill del DDL 48-HU11201.
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(),
            MandateSignerId = signerId,
            TransitOfficeId = OtMedellin,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6),
        });
        await ctx.SaveChangesAsync(ct);

        var antes = await reader.ListByOtAsync(OtMedellin, ct);
        antes.Should().ContainSingle();
        antes[0].TransitOfficeIds.Should().BeEquivalentTo([OtMedellin]);
        antes[0].CompanyTenantIds.Should().BeEquivalentTo([Compania]);

        // Edición que NO manda organismos (la consola del organismo solo cambia datos y compañías):
        // los organismos no se tocan.
        await repo.UpdateAsync(
            new UpdateMandateSignerData(
                signerId, OtTenant, "Carlos Andrés Pérez", "9080706050", new string('d', 64),
                [Compania], null, null),
            ct);

        var despues = await reader.ListByOtAsync(OtMedellin, ct);
        despues.Should().ContainSingle();
        despues[0].FullName.Should().Be("Carlos Andrés Pérez");
        despues[0].TransitOfficeIds.Should().BeEquivalentTo([OtMedellin]);
        despues[0].CompanyTenantIds.Should().BeEquivalentTo([Compania]);
    }

    // ── AC5 — la firma y los trámites siguen apuntando a la persona ───────────

    /// <summary>
    /// AC5 — el cambio de modelo no toca las referencias a la PERSONA: <c>signature_vault</c> y
    /// <c>procedure_instances</c> apuntan a <c>mandate_signers.id</c>, que sigue siendo el mismo. La
    /// prueba comprueba que la firma sigue resuelta y que el trámite sigue hallando al mandatario por
    /// su id, incluso después de reorganizarle los organismos.
    /// </summary>
    [Fact]
    public async Task AC5_TrasElCambioDeModelo_LaFirmaYElTramiteSiguenResolviendoALaPersona()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var repo = new MandateSignerRepository(ctx);
        var directorio = Directorio(ctx);

        var signerId = await repo.CreateAsync(Alta([OtMedellin, OtEnvigado]), ct);

        // La firma del baúl se ancla a la persona (ADR-0025 / ADR-0036 §D6).
        var firmaId = Guid.NewGuid();
        var signer = await ctx.MandateSigners.FirstAsync(s => s.Id == signerId, ct);
        signer.SignatureVaultId = firmaId;
        await ctx.SaveChangesAsync(ct);

        // Se le retira un organismo y se le agrega otro: la persona no cambia de identidad.
        await repo.UpdateAsync(
            new UpdateMandateSignerData(
                signerId, OtTenant, "Ana Restrepo", "1020304050", new string('a', 64),
                [Compania], null, null, "CC", "ana@ejemplo.com", null,
                TransitOfficeIds: [OtMedellin, OtBello]),
            ct);

        var porId = await directorio.GetByIdAsync(signerId, ct);
        porId.Should().NotBeNull();
        porId!.Id.Should().Be(signerId);
        porId.SignatureVaultId.Should().Be(firmaId);

        // Y en el organismo nuevo el trámite lo ofrece sin haber duplicado la persona.
        (await directorio.GetCandidatesAsync(OtBello, Compania, null, ct)).Should().ContainSingle()
            .Which.Id.Should().Be(signerId);
        (await ctx.MandateSigners.CountAsync(ct)).Should().Be(1);
    }

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Inactivar_RetiraAlMandatarioDeTodosSusOrganismos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var repo = new MandateSignerRepository(ctx);
        var directorio = Directorio(ctx);

        var signerId = await repo.CreateAsync(Alta([OtMedellin, OtEnvigado]), ct);

        await repo.InactivateAsync(new InactivateMandateSignerData(signerId, OtTenant, null, null), ct);

        (await directorio.GetCandidatesAsync(OtMedellin, Compania, null, ct)).Should().BeEmpty();
        (await directorio.GetCandidatesAsync(OtEnvigado, Compania, null, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Inactivar_LoDejaVisibleEnSuOrganismoParaPoderReactivarlo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var repo = new MandateSignerRepository(ctx);
        var reader = new DbMandateSignerReader(ctx);

        var signerId = await repo.CreateAsync(Alta([OtMedellin, OtEnvigado]), ct);
        await repo.InactivateAsync(new InactivateMandateSignerData(signerId, OtTenant, null, null), ct);

        // Un vínculo inactivo por baja de la PERSONA no es lo mismo que un organismo retirado: el
        // mandatario debe seguir a la vista para poder reactivarlo.
        var enElOt = await reader.ListByOtAsync(OtMedellin, ct);
        enElOt.Should().ContainSingle().Which.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Reactivar_DevuelveAlMandatarioSuOrganismoPrimario()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();
        var repo = new MandateSignerRepository(ctx);
        var reader = new DbMandateSignerReader(ctx);

        var signerId = await repo.CreateAsync(Alta([OtMedellin, OtEnvigado]), ct);
        await repo.InactivateAsync(new InactivateMandateSignerData(signerId, OtTenant, null, null), ct);
        await repo.ReactivateAsync(new ReactivateMandateSignerData(signerId, OtTenant, null, null), ct);

        // Sin devolverle al menos el primario quedaría activo y sin aparecer en ninguna consola: activo
        // e inalcanzable. Los demás se le vuelven a asignar desde la edición.
        var enElOt = await reader.ListByOtAsync(OtMedellin, ct);
        enElOt.Should().ContainSingle().Which.TransitOfficeIds.Should().Contain(OtMedellin);
    }
}
