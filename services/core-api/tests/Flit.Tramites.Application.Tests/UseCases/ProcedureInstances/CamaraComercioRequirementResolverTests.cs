using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12775 — la escalera que decide si el certificado de Cámara de Comercio es obligatorio para un
/// actor persona jurídica, y cuál de las dos condiciones lo eximió.
/// </summary>
public sealed class CamaraComercioRequirementResolverTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    // ── Dobles ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Baúl de firmas de prueba. Devuelve la firma solo para los documentos configurados: el puerto
    /// real ya aplica activa + vigente + flag del tenant, así que "no vigente" y "revocada" llegan al
    /// resolutor de la misma forma — como ausencia.
    /// </summary>
    private sealed class FakeVault : ISignatureVaultPolicy
    {
        private readonly HashSet<string> _conFirma;

        public FakeVault(params string[] documentosConFirma) =>
            _conFirma = new HashSet<string>(documentosConFirma, StringComparer.OrdinalIgnoreCase);

        public int Calls { get; private set; }

        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default)
        {
            Calls++;
            if (!_conFirma.Contains(documentNumber))
                return Task.FromResult<SignatureVaultMatch?>(null);

            return Task.FromResult<SignatureVaultMatch?>(new SignatureVaultMatch(
                Guid.NewGuid(), "Rep Legal", "hash", "path", "sha",
                new DateOnly(2026, 1, 1), new DateOnly(2030, 1, 1), documentNumber));
        }
    }

    /// <summary>Escrituras vigentes de prueba, por rol de actor.</summary>
    private sealed class FakeDeeds(params string[] rolesConEscritura) : IProcedureDeedResolver
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ResolvedDeedDocument>> ResolveForActorsAsync(
            Guid tenantId, IEnumerable<ProcedureInstanceActor> actors, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ResolvedDeedDocument>>([]);

        public Task<IReadOnlyList<ActorDeedPresence>> ResolvePresenceForActorsAsync(
            Guid tenantId, IEnumerable<ProcedureInstanceActor> actors, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ActorDeedPresence>>(
                [.. rolesConEscritura.Select(r =>
                    new ActorDeedPresence($"escritura_{r}", "900123456", r, Guid.NewGuid()))]);
        }
    }

    // ── Datos ────────────────────────────────────────────────────────────────

    /// <summary>Actor persona jurídica con representante legal capturado (el sujeto de identidad).</summary>
    private static ProcedureInstanceActor Juridico(string rol, string nit, string? rlDoc = "1020304050") =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorType = rol,
            PersonType = ActorPersonTypes.Juridical,
            DocumentType = "NIT",
            DocumentNumber = nit,
            FullName = $"SOCIEDAD {rol.ToUpperInvariant()} S.A.S.",
            Metadata = rlDoc is null
                ? "{}"
                : JsonSerializer.Serialize(new
                {
                    representanteLegal = new { tipoDocumento = "CC", numeroDocumento = rlDoc },
                }),
        };

    private static ProcedureInstanceActor Natural(string rol) =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorType = rol,
            PersonType = ActorPersonTypes.Natural,
            DocumentType = "CC",
            DocumentNumber = "1122334455",
            FullName = "PERSONA NATURAL",
            Metadata = "{}",
        };

    private static Task<IReadOnlyList<CamaraComercioRequirement>> Resolve(
        IEnumerable<ProcedureInstanceActor> actors,
        ISignatureVaultPolicy? vault = null,
        IProcedureDeedResolver? deeds = null) =>
        new CamaraComercioRequirementResolver(vault ?? new FakeVault(), deeds ?? new FakeDeeds())
            .ResolveAsync(Tenant, actors, TestContext.Current.CancellationToken);

    // ── AC1 — firma precargada vigente ───────────────────────────────────────

    [Fact]
    public async Task AC1_FirmaPrecargadaVigente_DejaElRequisitoOpcional()
    {
        var actors = new[] { Juridico("vendedor", "900111222", rlDoc: "555") };

        var r = await Resolve(actors, vault: new FakeVault("555"));

        r.Should().ContainSingle();
        r[0].EsObligatorio.Should().BeFalse();
        r[0].Exencion.Should().Be(CamaraComercioExencion.FirmaPrecargada);
        r[0].Tipo.Should().Be(CamaraComercioAttachmentTipo.Vendedor);
    }

    // ── AC2 — escritura vigente ──────────────────────────────────────────────

    [Fact]
    public async Task AC2_EscrituraVigenteSinFirma_DejaElRequisitoOpcional()
    {
        var actors = new[] { Juridico("comprador", "900333444") };

        var r = await Resolve(actors, deeds: new FakeDeeds("comprador"));

        r.Should().ContainSingle();
        r[0].EsObligatorio.Should().BeFalse();
        r[0].Exencion.Should().Be(CamaraComercioExencion.EscrituraVigente);
        r[0].Tipo.Should().Be(CamaraComercioAttachmentTipo.Comprador);
    }

    /// <summary>
    /// La escritura empareja por ROL. Sin esto, la escritura del vendedor eximiría al comprador —que
    /// es la misma colisión que los códigos por rol vienen a evitar, un nivel más arriba.
    /// </summary>
    [Fact]
    public async Task AC2_LaEscrituraDeUnRolNoEximeAlOtro()
    {
        var actors = new[] { Juridico("vendedor", "900111222"), Juridico("comprador", "900333444") };

        var r = await Resolve(actors, deeds: new FakeDeeds("vendedor"));

        r.Should().HaveCount(2);
        r.Single(x => x.Rol == "vendedor").EsObligatorio.Should().BeFalse();
        r.Single(x => x.Rol == "comprador").EsObligatorio.Should().BeTrue();
    }

    // ── AC3 — sin nada, obligatorio ──────────────────────────────────────────

    [Fact]
    public async Task AC3_SinFirmaNiEscritura_ElRequisitoEsObligatorio()
    {
        var actors = new[] { Juridico("vendedor", "900111222") };

        var r = await Resolve(actors);

        r.Should().ContainSingle();
        r[0].EsObligatorio.Should().BeTrue();
        r[0].Exencion.Should().Be(CamaraComercioExencion.Ninguna);
    }

    // ── AC4 — firma que no está vigente no exime ─────────────────────────────

    /// <summary>
    /// El puerto del baúl ya filtra por activa + vigente + flag del tenant: una firma revocada o
    /// caducada llega como ausencia, y entonces el requisito tiene que volver a ser obligatorio.
    /// </summary>
    [Fact]
    public async Task AC4_FirmaRevocadaOFueraDeVigencia_NoExime()
    {
        var actors = new[] { Juridico("vendedor", "900111222", rlDoc: "555") };

        // El baúl no devuelve firma para el documento 555 (revocada o fuera de vigencia).
        var r = await Resolve(actors, vault: new FakeVault("otro-documento"));

        r[0].EsObligatorio.Should().BeTrue();
        r[0].Exencion.Should().Be(CamaraComercioExencion.Ninguna);
    }

    // ── AC5 — firma y escritura a la vez ─────────────────────────────────────

    [Fact]
    public async Task AC5_ConFirmaYEscritura_ReportaUnaSolaCondicionYEsLaFirma()
    {
        var actors = new[] { Juridico("vendedor", "900111222", rlDoc: "555") };

        var r = await Resolve(actors, vault: new FakeVault("555"), deeds: new FakeDeeds("vendedor"));

        r.Should().ContainSingle();
        r[0].EsObligatorio.Should().BeFalse();
        r[0].Exencion.Should().Be(CamaraComercioExencion.FirmaPrecargada,
            "la firma se nombra primero porque además cambia el modelo de firmado; el orden es fijo "
            + "para que dos renders del mismo paso no alternen el mensaje");
    }

    [Fact]
    public async Task AC5_ElOrdenEsDeterministaEntreLlamadas()
    {
        var actors = new[] { Juridico("vendedor", "900111222", rlDoc: "555") };

        var primera = await Resolve(actors, new FakeVault("555"), new FakeDeeds("vendedor"));
        var segunda = await Resolve(actors, new FakeVault("555"), new FakeDeeds("vendedor"));

        segunda[0].Exencion.Should().Be(primera[0].Exencion);
    }

    // ── Alcance ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PersonaNatural_NoGeneraRequisito()
    {
        var r = await Resolve([Natural("comprador"), Natural("vendedor")]);

        r.Should().BeEmpty("el certificado acredita a una sociedad; a una persona natural no le aplica");
    }

    [Fact]
    public async Task CadaActorJuridicoGeneraSuPropioRequisito()
    {
        var actors = new[] { Juridico("vendedor", "900111222"), Juridico("comprador", "900333444") };

        var r = await Resolve(actors);

        r.Should().HaveCount(2);
        r.Select(x => x.Tipo).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// «propietario» no es un rol del modelo. Si llegara uno, se omite en vez de caer a un código por
    /// defecto: darle el buzón de otro rol dejaría que un certificado satisficiera el requisito ajeno.
    /// </summary>
    [Fact]
    public async Task RolDesconocido_SeOmiteEnVezDeCaerAUnCodigoPorDefecto()
    {
        var r = await Resolve([Juridico("propietario", "900555666")]);

        r.Should().BeEmpty();
    }

    /// <summary>
    /// Sin representante legal capturado no hay a quién buscarle firma en el baúl: se consulta solo
    /// la escritura. Consultar el baúl con el NIT devolvería la firma de cualquier otro representante.
    /// </summary>
    [Fact]
    public async Task SinRepresentanteCapturado_NoSeConsultaElBaul()
    {
        var vault = new FakeVault();

        var r = await Resolve([Juridico("vendedor", "900111222", rlDoc: null)], vault);

        vault.Calls.Should().Be(0);
        r[0].EsObligatorio.Should().BeTrue();
    }

    /// <summary>
    /// Una sola lectura del directorio de escrituras para todo el trámite: esto se recalcula en cada
    /// render del paso del actor.
    /// </summary>
    [Fact]
    public async Task LasEscriturasSeLeenUnaSolaVezPorTramite()
    {
        var deeds = new FakeDeeds("vendedor");

        await Resolve([Juridico("vendedor", "900111222"), Juridico("comprador", "900333444")], deeds: deeds);

        deeds.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SinActores_NoConsultaNingunPuerto()
    {
        var vault = new FakeVault();
        var deeds = new FakeDeeds();

        var r = await Resolve([], vault, deeds);

        r.Should().BeEmpty();
        vault.Calls.Should().Be(0);
        deeds.Calls.Should().Be(0);
    }

    // ── Contrato de salida ───────────────────────────────────────────────────

    /// <summary>
    /// La exención viaja como texto estable: renombrar un valor del enum es refactor interno y no
    /// puede cambiar en silencio lo que el cliente ya interpreta.
    /// </summary>
    [Theory]
    [InlineData(CamaraComercioExencion.Ninguna, "ninguna")]
    [InlineData(CamaraComercioExencion.FirmaPrecargada, "firma_precargada")]
    [InlineData(CamaraComercioExencion.EscrituraVigente, "escritura_vigente")]
    public void ElContratoDeLaExencionEsTextoEstable(CamaraComercioExencion exencion, string esperado) =>
        GetCamaraComercioRequirementsHandler.ToWire(exencion).Should().Be(esperado);
}
