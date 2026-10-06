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
/// HU #12775 — la regla que decide si el certificado de Cámara de Comercio es obligatorio para un
/// actor persona jurídica: opcional solo con firma precargada Y escritura vigentes.
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

    // ── Regla: opcional solo con firma Y escritura vigentes (Épica #12754) ──

    [Fact]
    public async Task AC1_FirmaYEscrituraVigentes_DejaElRequisitoOpcional()
    {
        var actors = new[] { Juridico("vendedor", "900111222", rlDoc: "555") };

        var r = await Resolve(actors, vault: new FakeVault("555"), deeds: new FakeDeeds("vendedor"));

        r.Should().ContainSingle();
        r[0].EsObligatorio.Should().BeFalse();
        r[0].Exencion.Should().Be(CamaraComercioExencion.FirmaYEscritura);
        r[0].Tipo.Should().Be(CamaraComercioAttachmentTipo.Vendedor);
    }

    [Fact]
    public async Task AC2_SoloFirmaVigente_ElRequisitoEsObligatorio()
    {
        var actors = new[] { Juridico("vendedor", "900111222", rlDoc: "555") };

        var r = await Resolve(actors, vault: new FakeVault("555"));

        r[0].EsObligatorio.Should().BeTrue("la firma sola no exime: hacen falta las dos");
        r[0].Exencion.Should().Be(CamaraComercioExencion.Ninguna);
    }

    [Fact]
    public async Task AC2_SoloEscrituraVigente_ElRequisitoEsObligatorio()
    {
        var actors = new[] { Juridico("comprador", "900333444", rlDoc: "555") };

        var r = await Resolve(actors, deeds: new FakeDeeds("comprador"));

        r[0].EsObligatorio.Should().BeTrue("la escritura sola no exime: hacen falta las dos");
        r[0].Exencion.Should().Be(CamaraComercioExencion.Ninguna);
    }

    /// <summary>
    /// La escritura empareja por ROL. Sin esto, la escritura del vendedor eximiría al comprador —que
    /// es la misma colisión que los códigos por rol vienen a evitar, un nivel más arriba.
    /// </summary>
    [Fact]
    public async Task AC2_LaEscrituraDeUnRolNoEximeAlOtro()
    {
        var actors = new[]
        {
            Juridico("vendedor", "900111222", rlDoc: "555"),
            Juridico("comprador", "900333444", rlDoc: "555"),
        };

        var r = await Resolve(actors, vault: new FakeVault("555"), deeds: new FakeDeeds("vendedor"));

        r.Should().HaveCount(2);
        r.Single(x => x.Rol == "vendedor").EsObligatorio.Should().BeFalse();
        r.Single(x => x.Rol == "comprador").EsObligatorio.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_SinFirmaNiEscritura_ElRequisitoEsObligatorio()
    {
        var actors = new[] { Juridico("vendedor", "900111222") };

        var r = await Resolve(actors);

        r.Should().ContainSingle();
        r[0].EsObligatorio.Should().BeTrue();
        r[0].Exencion.Should().Be(CamaraComercioExencion.Ninguna);
    }

    /// <summary>
    /// El puerto del baúl ya filtra por activa + vigente + flag del tenant: una firma revocada,
    /// caducada o de un tenant con el baúl apagado llega como ausencia, y entonces ni con escritura
    /// vigente el requisito deja de ser obligatorio.
    /// </summary>
    [Fact]
    public async Task AC4_FirmaRevocadaOFueraDeVigencia_NoEximeAunqueHayaEscritura()
    {
        var actors = new[] { Juridico("vendedor", "900111222", rlDoc: "555") };

        var r = await Resolve(actors, vault: new FakeVault("otro-documento"), deeds: new FakeDeeds("vendedor"));

        r[0].EsObligatorio.Should().BeTrue();
        r[0].Exencion.Should().Be(CamaraComercioExencion.Ninguna);
    }

    /// <summary>Sin escritura no hace falta preguntarle al baúl: la respuesta ya es «obligatorio».</summary>
    [Fact]
    public async Task SinEscritura_NoSeConsultaElBaul()
    {
        var vault = new FakeVault("555");

        var r = await Resolve([Juridico("vendedor", "900111222", rlDoc: "555")], vault);

        vault.Calls.Should().Be(0);
        r[0].EsObligatorio.Should().BeTrue();
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
    /// Sin representante legal capturado no hay a quién buscarle firma en el baúl, así que ni con
    /// escritura vigente se exime. Consultar el baúl con el NIT devolvería la firma de cualquier otro
    /// representante.
    /// </summary>
    [Fact]
    public async Task SinRepresentanteCapturado_NoSeConsultaElBaul()
    {
        var vault = new FakeVault();

        var r = await Resolve([Juridico("vendedor", "900111222", rlDoc: null)], vault, new FakeDeeds("vendedor"));

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
    [InlineData(CamaraComercioExencion.FirmaYEscritura, "firma_y_escritura")]
    public void ElContratoDeLaExencionEsTextoEstable(CamaraComercioExencion exencion, string esperado) =>
        GetCamaraComercioRequirementsHandler.ToWire(exencion).Should().Be(esperado);

    // ── HU #12775 AC3 — el gate de radicación ────────────────────────────────

    private static ProcedureInstance ConActores(params ProcedureInstanceActor[] actores)
    {
        var instance = new ProcedureInstance { Id = Guid.NewGuid(), TenantId = Tenant };
        foreach (var a in actores)
            instance.Actors.Add(a);
        return instance;
    }

    private static void Adjuntar(ProcedureInstance instance, string tipo) =>
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            StoragePath = $"p/{tipo}",
            UploadedAt = DateTimeOffset.UtcNow,
        });

    [Fact]
    public async Task RolSinCertificado_ObligatorioSinCargar_DevuelveElRol()
    {
        var instance = ConActores(Juridico("vendedor", "900111222", rlDoc: null));

        var rol = await new CamaraComercioRequirementResolver(new FakeVault(), new FakeDeeds())
            .RolSinCertificadoAsync(Tenant, instance, TestContext.Current.CancellationToken);

        rol.Should().Be("vendedor");
    }

    [Fact]
    public async Task RolSinCertificado_CargadoOExento_DevuelveNull()
    {
        var instance = ConActores(
            Juridico("vendedor", "900111222", rlDoc: null),
            Juridico("comprador", "900333444", rlDoc: "555"));
        Adjuntar(instance, CamaraComercioAttachmentTipo.Vendedor);

        var rol = await new CamaraComercioRequirementResolver(new FakeVault("555"), new FakeDeeds("comprador"))
            .RolSinCertificadoAsync(Tenant, instance, TestContext.Current.CancellationToken);

        rol.Should().BeNull();
    }

    [Fact]
    public async Task RolSinCertificado_ElDelOtroRolNoSatisface()
    {
        var instance = ConActores(
            Juridico("vendedor", "900111222", rlDoc: null),
            Juridico("comprador", "900333444", rlDoc: null));
        Adjuntar(instance, CamaraComercioAttachmentTipo.Vendedor);

        var rol = await new CamaraComercioRequirementResolver(new FakeVault(), new FakeDeeds())
            .RolSinCertificadoAsync(Tenant, instance, TestContext.Current.CancellationToken);

        rol.Should().Be("comprador");
    }
}
