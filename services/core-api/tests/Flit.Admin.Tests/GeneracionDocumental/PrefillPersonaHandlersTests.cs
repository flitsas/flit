using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Application.GeneracionDocumental.Prefill;
using Flit.Admin.Domain.GeneracionDocumental;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12206 — precedencia de fuentes por PARTE (CF-25), probada en backend y no en el navegador.
///
/// Uso de ejemplo:
/// <code>
/// var handler = new PrefillPersonaJuridicaHandler(directorio, rues);
/// var r = await handler.HandleAsync(new PrefillPersonaJuridicaCommand(tenant, "900123456"));
/// // r.Source == "DIRECTORIO" y el RUES no se consultó
/// </code>
/// </summary>
public sealed class PrefillPersonaJuridicaHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Nit = "900123456";

    private readonly FakeLegalRepresentativeReader _directorio = new();
    private readonly FakeStandaloneRuesCompanyLookup _rues = new();

    private PrefillPersonaJuridicaHandler Sut => new(_directorio, _rues);

    // ── AC: el directorio va primero ────────────────────────────────────────────────

    [Fact]
    public async Task NitEnElDirectorio_DevuelveElDirectorioYNoConsultaElRues()
    {
        _directorio.Match = FakeLegalRepresentativeReader.Representante(Nit);

        var result = await Sut.HandleAsync(
            new PrefillPersonaJuridicaCommand(Tenant, Nit), TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.Source.Should().Be(PrefillSources.Directorio);
        result.Fields.Should().Contain(f => f.Key == "razon_social" && f.Value == "TRANSPORTES DEL SUR SAS");
        result.Fields.Should().Contain(f => f.Key == "representante_legal" && f.Value == "CARLOS MEJIA OSORIO");
        result.Fields.Should().Contain(f => f.Key == "cc_rl" && f.Value == "71234567");

        _rues.Calls.Should().Be(0, "el RUES solo se consulta si el directorio no responde");
    }

    [Fact]
    public async Task CadaCampoDelDirectorioDeclaraEsaFuente()
    {
        _directorio.Match = FakeLegalRepresentativeReader.Representante(Nit);

        var result = await Sut.HandleAsync(
            new PrefillPersonaJuridicaCommand(Tenant, Nit), TestContext.Current.CancellationToken);

        result.Fields
            .Where(f => f.Key != "digito_verificacion")
            .Should().OnlyContain(f => f.Source == PrefillSources.Directorio);
    }

    // ── AC: el RUES solo como respaldo ──────────────────────────────────────────────

    [Fact]
    public async Task NitFueraDelDirectorio_CaeAlRuesYLoDeclara()
    {
        _directorio.Match = null;

        var result = await Sut.HandleAsync(
            new PrefillPersonaJuridicaCommand(Tenant, Nit), TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.Source.Should().Be(PrefillSources.Rues);
        result.Fields.Should().Contain(f => f.Key == "razon_social" && f.Value == "EMPRESA DE PRUEBA SAS");

        _directorio.Calls.Should().Be(1);
        _rues.Calls.Should().Be(1);
        result.Attempts.Select(a => a.Source).Should().ContainInOrder(PrefillSources.Directorio, PrefillSources.Rues);
    }

    [Fact]
    public async Task DirectorioCaido_NoTumbaElPrellenado_CaeAlRues()
    {
        _directorio.Caido = true;

        var result = await Sut.HandleAsync(
            new PrefillPersonaJuridicaCommand(Tenant, Nit), TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.Source.Should().Be(PrefillSources.Rues);
        result.Attempts.Should().Contain(a =>
            a.Source == PrefillSources.Directorio && a.Outcome == PrefillOutcomes.Error);
    }

    [Fact]
    public async Task ElRuesNoInventaRepresentanteLegal()
    {
        // El RUES devuelve la FACULTAD de representación, no la persona. Rellenar el representante
        // desde ahí sería inventar el dato.
        _directorio.Match = null;

        var result = await Sut.HandleAsync(
            new PrefillPersonaJuridicaCommand(Tenant, Nit), TestContext.Current.CancellationToken);

        result.Fields.Should().NotContain(f => f.Key == "representante_legal");
        result.Fields.Should().NotContain(f => f.Key == "cc_rl");
    }

    // ── AC: degradación ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinCoincidenciaEnNingunaFuente_200ConFoundFalse()
    {
        _directorio.Match = null;
        _rues.Result = new StandaloneRuesLookupResult(
            false, new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null);

        var result = await Sut.HandleAsync(
            new PrefillPersonaJuridicaCommand(Tenant, "900999999"), TestContext.Current.CancellationToken);

        result.Found.Should().BeFalse();
        result.Fields.Should().BeEmpty();
        result.Error.Should().BeNull("sin coincidencia no es 404 ni 502");
    }

    [Fact]
    public async Task DirectorioSinMatchYRuesCaido_SigueSiendo200()
    {
        // Una fuente contestó ("no está") y la otra se cayó: la respuesta degrada, no falla.
        _directorio.Match = null;
        _rues.Result = new StandaloneRuesLookupResult(
            false, new Dictionary<string, string?>(), DateTimeOffset.UtcNow, "provider_unavailable");

        var result = await Sut.HandleAsync(
            new PrefillPersonaJuridicaCommand(Tenant, Nit), TestContext.Current.CancellationToken);

        result.Error.Should().BeNull();
        result.Found.Should().BeFalse();
        result.Attempts.Should().Contain(a => a.Source == PrefillSources.Rues && a.ErrorCode == "provider_unavailable");
    }

    [Fact]
    public async Task TodasLasFuentesCaidas_EsFalloDeProveedor()
    {
        _directorio.Caido = true;
        _rues.Result = new StandaloneRuesLookupResult(
            false, new Dictionary<string, string?>(), DateTimeOffset.UtcNow, "provider_unavailable");

        var result = await Sut.HandleAsync(
            new PrefillPersonaJuridicaCommand(Tenant, Nit), TestContext.Current.CancellationToken);

        result.Error.Should().Be("provider_unavailable");
    }

    // ── AC: el DV se calcula ────────────────────────────────────────────────────────

    [Fact]
    public async Task ElDigitoDeVerificacionViajaCalculado()
    {
        _directorio.Match = FakeLegalRepresentativeReader.Representante(Nit);

        var result = await Sut.HandleAsync(
            new PrefillPersonaJuridicaCommand(Tenant, "900.123.456"), TestContext.Current.CancellationToken);

        var dv = result.Fields.Single(f => f.Key == "digito_verificacion");
        dv.Value.Should().Be("8");
        dv.Source.Should().Be(PrefillSources.Calculado, "el DV no se consulta ni se captura");

        // Y el NIT viaja limpio de puntos, como lo guarda el directorio.
        result.Fields.Should().Contain(f => f.Key == "no_doc" && f.Value == "900123456");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC")]
    public async Task NitInvalido_NoConsultaNingunaFuente(string? nit)
    {
        var result = await Sut.HandleAsync(
            new PrefillPersonaJuridicaCommand(Tenant, nit), TestContext.Current.CancellationToken);

        result.Error.Should().Be("invalid_request");
        _directorio.Calls.Should().Be(0);
        _rues.Calls.Should().Be(0);
    }

    // ── AC: el prellenado no persiste ───────────────────────────────────────────────

    [Fact]
    public void NoTienePorDondePersistir()
    {
        var dependencias = typeof(PrefillPersonaJuridicaHandler)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        dependencias.Should().NotContain(typeof(IStandaloneDocumentRepository));
        dependencias.Should().NotContain(typeof(IStandaloneDocumentStorage));
    }
}

/// <summary>
/// HU #12206 — precedencia de la parte NATURAL: cadena RUNT persona primero, contact-lookup después.
///
/// Uso de ejemplo:
/// <code>
/// var handler = new PrefillPersonaNaturalHandler(runt, contactos);
/// var r = await handler.HandleAsync(new PrefillPersonaNaturalCommand(tenant, "CC", "1020304050"));
/// </code>
/// </summary>
public sealed class PrefillPersonaNaturalHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakeStandaloneRuntPersonPrefill _runt = new();
    private readonly FakeStandaloneActorContactLookup _contactos = new();

    private PrefillPersonaNaturalHandler Sut => new(_runt, _contactos);

    [Fact]
    public async Task DocumentoConAntecedenteEnRunt_ElNombreSaleDelRunt()
    {
        var result = await Sut.HandleAsync(
            new PrefillPersonaNaturalCommand(Tenant, "cc", "1020304050"),
            TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.Source.Should().Be(PrefillSources.Runt);

        var nombre = result.Fields.Single(f => f.Key == "nombre_razon_social");
        nombre.Value.Should().Be("MARIA CAMILA GOMEZ RUIZ");
        nombre.Source.Should().Be(PrefillSources.Runt);

        _runt.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ElDomicilioSiempreDeclaraContactLookup()
    {
        // El RUNT no entrega domicilio: aunque resuelva la identidad, la ciudad viene del histórico
        // de actores y la respuesta lo dice.
        var result = await Sut.HandleAsync(
            new PrefillPersonaNaturalCommand(Tenant, "CC", "1020304050"),
            TestContext.Current.CancellationToken);

        var domicilio = result.Fields.Single(f => f.Key == "domicilio");
        domicilio.Value.Should().Be("MEDELLIN");
        domicilio.Source.Should().Be(PrefillSources.ContactLookup);
    }

    [Fact]
    public async Task RuntCaido_CaeAContactLookupYLoDeclara()
    {
        _runt.Result = new StandaloneRuntPersonResult(false, null, null, null, null, "provider_unavailable");

        var result = await Sut.HandleAsync(
            new PrefillPersonaNaturalCommand(Tenant, "CC", "1020304050"),
            TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.Source.Should().Be(PrefillSources.ContactLookup);
        result.Error.Should().BeNull("una fuente caída deja el resto utilizable");
        result.Attempts.Select(a => a.Source).Should().ContainInOrder(
            PrefillSources.Runt, PrefillSources.ContactLookup);
    }

    [Fact]
    public async Task ElRespaldoNoInventaElNombre()
    {
        // contact-lookup nunca devuelve nombre ni documento (contrato del wizard): el nombre queda
        // para captura manual.
        _runt.Result = new StandaloneRuntPersonResult(false, null, null, null, null, "provider_unavailable");

        var result = await Sut.HandleAsync(
            new PrefillPersonaNaturalCommand(Tenant, "CC", "1020304050"),
            TestContext.Current.CancellationToken);

        result.Fields.Should().NotContain(f => f.Key == "nombre_razon_social");
    }

    [Fact]
    public async Task SinCoincidenciaEnNingunaFuente_200ConFoundFalse()
    {
        _runt.Result = new StandaloneRuntPersonResult(false, null, null, null, "kyverum_runt_conductor", null);
        _contactos.Result = new StandaloneContactLookupResult(false, null, null, null);

        var result = await Sut.HandleAsync(
            new PrefillPersonaNaturalCommand(Tenant, "CC", "9999999999"),
            TestContext.Current.CancellationToken);

        result.Found.Should().BeFalse();
        result.Fields.Should().BeEmpty();
        result.Error.Should().BeNull("sin coincidencia no es 404 ni 502");
    }

    [Fact]
    public async Task TodasLasFuentesCaidas_EsFalloDeProveedor()
    {
        _runt.Result = new StandaloneRuntPersonResult(false, null, null, null, null, "provider_unavailable");
        _contactos.Result = new StandaloneContactLookupResult(false, null, null, "provider_unavailable");

        var result = await Sut.HandleAsync(
            new PrefillPersonaNaturalCommand(Tenant, "CC", "1020304050"),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be("provider_unavailable");
    }

    [Theory]
    [InlineData(null, "1020304050")]
    [InlineData("CC", null)]
    [InlineData("NIT", "900123456")]
    public async Task DocumentoInvalido_NoConsultaNingunaFuente(string? tipo, string? numero)
    {
        var result = await Sut.HandleAsync(
            new PrefillPersonaNaturalCommand(Tenant, tipo, numero),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be("invalid_request");
        _runt.Calls.Should().Be(0);
        _contactos.Calls.Should().Be(0);
    }

    [Fact]
    public void NoTienePorDondePersistir()
    {
        var dependencias = typeof(PrefillPersonaNaturalHandler)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        dependencias.Should().NotContain(typeof(IStandaloneDocumentRepository));
        dependencias.Should().NotContain(typeof(IStandaloneDocumentStorage));
    }

    [Fact]
    public void LaCacheDeLaConsultaSeGuardaConInstanciaNula()
    {
        // El AC lo pide literal: la cadena RUNT persona corre «con instancia nula en el guardado de
        // caché». Se verifica sobre la fuente del adaptador, que es donde vive esa llamada.
        var fuente = File.ReadAllText(PrefillVehiculoHandlerTests.SourcePath(
            "src", "Flit.Infrastructure", "Consultations", "StandalonePersonPrefillAdapter.cs"));

        fuente.Should().Contain("sourceProcedureInstanceId: null");
        fuente.Should().NotContain("RuntPersonLookupHandler(");
        fuente.Should().NotContain("IProcedureInstanceRepository");
    }
}
