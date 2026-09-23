using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12777 AC1 / HU #12779 — la obligatoriedad se resuelve sobre los actores que el gestor tiene en
/// pantalla, no sobre los guardados: los actores solo se persisten con «Continuar y guardar».
/// </summary>
public sealed class CamaraComercioRequirementsPreviewTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private sealed class FakeVault(params string[] documentosConFirma) : ISignatureVaultPolicy
    {
        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default) =>
            Task.FromResult<SignatureVaultMatch?>(documentosConFirma.Contains(documentNumber)
                ? new SignatureVaultMatch(
                    Guid.NewGuid(), "Rep Legal", "hash", "path", "sha",
                    new DateOnly(2026, 1, 1), new DateOnly(2030, 1, 1), documentNumber)
                : null);
    }

    private static ProcedureInstance Instance(Guid id, params ProcedureInstanceActor[] guardados)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = id,
            TenantId = Tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FT2-0000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var a in guardados)
            instance.Actors.Add(a);
        return instance;
    }

    private static ActorInput Input(string rol, string tipoDoc, string numero, string? rlDoc = null) =>
        new(
            Rol: rol,
            TipoDocumento: tipoDoc,
            NumeroDocumento: numero,
            NombreCompleto: "PARTE",
            Email: "parte@flit.local",
            Telefono: null,
            RepresentanteLegal: rlDoc is null
                ? null
                : new ActorRepresentanteLegal("CC", rlDoc, "Rep Legal", "rl@flit.local", null));

    /// <summary>Escrituras vigentes de prueba, por rol de actor.</summary>
    private sealed class FakeDeeds(params string[] rolesConEscritura) : IProcedureDeedResolver
    {
        public Task<IReadOnlyList<ResolvedDeedDocument>> ResolveForActorsAsync(
            Guid tenantId, IEnumerable<ProcedureInstanceActor> actors, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ResolvedDeedDocument>>([]);

        public Task<IReadOnlyList<ActorDeedPresence>> ResolvePresenceForActorsAsync(
            Guid tenantId, IEnumerable<ProcedureInstanceActor> actors, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ActorDeedPresence>>(
                [.. rolesConEscritura.Select(r => new ActorDeedPresence($"escritura_{r}", "900123456", r, Guid.NewGuid()))]);
    }

    private GetCamaraComercioRequirementsHandler Handler(
        ISignatureVaultPolicy? vault = null,
        IProcedureDeedResolver? deeds = null) =>
        new(_repo, new CamaraComercioRequirementResolver(
            vault ?? new FakeVault(), deeds ?? NullProcedureDeedResolver.Instance));

    [Fact]
    public async Task ActorMarcadoNitSinGuardar_GeneraElRequisito()
    {
        var id = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        // Nada guardado todavía: el GET de siempre no devolvería requisito alguno.
        _repo.GetByIdWithDetailsAsync(id, Tenant, ct).Returns(Instance(id));

        var (result, error) = await Handler().HandlePreviewAsync(
            id, Tenant, [Input("comprador", "NIT", "890903938")], ct);

        error.Should().BeNull();
        result!.Requirements.Should().ContainSingle();
        result.Requirements[0].Rol.Should().Be("comprador");
        result.Requirements[0].Tipo.Should().Be(CamaraComercioAttachmentTipo.Comprador);
        result.Requirements[0].EsObligatorio.Should().BeTrue();
    }

    [Fact]
    public async Task ActorGuardadoJuridicoQueEnPantallaEsNatural_NoGeneraRequisito()
    {
        var id = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        var guardado = new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            PersonType = ActorPersonTypes.Juridical,
            DocumentType = "NIT",
            DocumentNumber = "890903938",
            FullName = "SOCIEDAD",
            Metadata = "{}",
        };
        _repo.GetByIdWithDetailsAsync(id, Tenant, ct).Returns(Instance(id, guardado));

        var (result, _) = await Handler().HandlePreviewAsync(
            id, Tenant, [Input("comprador", "CC", "1037669356")], ct);

        result!.Requirements.Should().BeEmpty();
    }

    [Fact]
    public async Task FirmaDelRepresentanteEnPantallaYEscritura_DejaElRequisitoOpcional()
    {
        var id = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(id, Tenant, ct).Returns(Instance(id));

        var (result, _) = await Handler(new FakeVault("555"), new FakeDeeds("vendedor")).HandlePreviewAsync(
            id, Tenant, [Input("vendedor", "NIT", "900111222", rlDoc: "555")], ct);

        result!.Requirements.Should().ContainSingle();
        result.Requirements[0].EsObligatorio.Should().BeFalse();
        result.Requirements[0].Exencion.Should().Be("firma_y_escritura");
    }

    [Fact]
    public async Task TramiteInexistente_DevuelveNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await Handler().HandlePreviewAsync(
            Guid.NewGuid(), Tenant, [Input("comprador", "NIT", "890903938")], ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }
}
