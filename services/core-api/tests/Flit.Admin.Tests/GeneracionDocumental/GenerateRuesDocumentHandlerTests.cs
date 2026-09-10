using System.Text.Json;
using Flit.Admin.Application.GeneracionDocumental.GenerateRues;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12203 (Feature #12201, ADR-0056-generacion-documental-standalone) — emisión del Certificado
/// RUES SIN trámite.
///
/// Uso de ejemplo:
/// <code>
/// var handler = new GenerateRuesDocumentHandler(repo, lookup, renderer, storage);
/// var result = await handler.HandleAsync(new GenerateRuesDocumentCommand(tenant, user, "900123456"));
/// // result.Status == "generated"
/// </code>
/// </summary>
public sealed class GenerateRuesDocumentHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtroTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Usuario = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly FakeStandaloneDocumentRepository _repo = new();
    private readonly FakeStandaloneRuesCompanyLookup _lookup = new();
    private readonly FakeStandaloneRuesCertificateRenderer _renderer = new();
    private readonly FakeStandaloneDocumentStorage _storage = new();

    private GenerateRuesDocumentHandler Sut => new(_repo, _lookup, _renderer, _storage);

    private static GenerateRuesDocumentCommand Command(string? nit = "900123456", string? key = null)
        => new(Tenant, Usuario, nit, key);

    // ── CF-04 / CF-02 — generación exitosa sin ProcedureInstance ─────────────────────

    [Fact]
    public async Task Generacion_Exitosa_PersisteLaFilaDelTenantYDelAutorDelJwt()
    {
        var result = await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GenerateRuesDocumentOutcome.Generated);
        result.Status.Should().Be("generated");
        result.Id.Should().NotBeNull().And.NotBe(Guid.Empty);

        var row = _repo.Rows.Should().ContainSingle().Subject;
        row.TenantId.Should().Be(Tenant);
        row.CreatedByUserId.Should().Be(Usuario);
        row.DocumentType.Should().Be("certificado_rues");
        row.Scenario.Should().BeNull("el CHECK por tipo prohíbe escenario en el Certificado RUES");
        row.Status.Should().Be("generated");
    }

    [Fact]
    public async Task Generacion_Exitosa_NoTocaNingunaProcedureInstance()
    {
        await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);

        // El handler solo puede escribir lo que le dan sus puertos; ninguno de ellos conoce trámites.
        typeof(GenerateRuesDocumentHandler).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.Namespace ?? string.Empty)
            .Should().OnlyContain(ns => !ns.StartsWith("Flit.Tramites", StringComparison.Ordinal),
                "restricción C6: Admin.Application no puede nombrar tipos de Flit.Tramites.*");

        typeof(GenerateRuesDocumentHandler).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().NotContain("Flit.Tramites.Application");
    }

    [Fact]
    public async Task Generacion_Exitosa_GuardaElPdfConElTenantComoClaveDeAgrupacion()
    {
        await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);

        var saved = _storage.Saved.Should().ContainSingle().Subject;
        saved.TenantId.Should().Be(Tenant);
        saved.Tipo.Should().Be("generacion_documental");
        saved.Length.Should().BeGreaterThan(0);

        var generated = _repo.Generated.Should().ContainSingle().Subject;
        generated.File.StoragePath.Should().NotBeNullOrWhiteSpace();
        generated.File.StorageSha256.Should().HaveLength(64);
        generated.File.Filename.Should().EndWith(".pdf");
    }

    [Fact]
    public async Task Generacion_Exitosa_CongelaElSnapshotAntesDeRenderizar()
    {
        await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);

        var snapshot = _repo.Snapshots.Should().ContainSingle().Subject.Snapshot;
        using var doc = JsonDocument.Parse(snapshot);

        doc.RootElement.TryGetProperty("queriedAt", out _).Should().BeTrue();
        doc.RootElement.GetProperty("fields").GetProperty("rues_razon_social").GetString()
            .Should().Be("EMPRESA DE PRUEBA SAS");
    }

    [Fact]
    public async Task Generacion_Exitosa_ElResumenDelHistorialEsPobreEnPii()
    {
        await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);

        var inputSummary = _repo.Rows.Single().InputSummary;

        inputSummary.Should().Contain("900123456");
        // Ley 1581: el resumen alimenta el listado; la razón social vive en el snapshot, no aquí.
        inputSummary.Should().NotContain("EMPRESA DE PRUEBA SAS");
    }

    [Fact]
    public async Task Generacion_Exitosa_UsaUnaReferenciaSinteticaPorqueNoHayRadicado()
    {
        var result = await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);

        _renderer.LastReferenceNumber.Should().StartWith("GD-");
        _renderer.LastReferenceNumber.Should().Be(GenerateRuesDocumentHandler.ReferenceNumberFor(result.Id!.Value));
    }

    // ── Contrato de respuesta (CF-21) ───────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LaRespuestaSoloDevuelveGeneratedOError(bool nitExiste)
    {
        if (!nitExiste)
        {
            _lookup.Result = new StandaloneRuesLookupResult(
                false, new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null);
        }

        var result = await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.Status.Should().BeOneOf("generated", "error");
        result.Status.Should().NotBe("pending");
        result.Status.Should().NotBe("processing");
    }

    [Fact]
    public async Task LaRespuestaNuncaTransportaElBinario()
    {
        var result = await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);

        // El contrato es { id, status }: si alguien agregara el PDF, este test lo detecta.
        typeof(GenerateRuesDocumentResult).GetProperties()
            .Select(p => p.Name)
            .Should().BeEquivalentTo(["Outcome", "Id", "Status", "ErrorCode"]);
        typeof(GenerateRuesDocumentResult).GetProperties()
            .Should().NotContain(p => p.PropertyType == typeof(byte[]));

        result.Id.Should().NotBeNull();
    }

    // ── CF-16 / R4 — idempotencia ANTES de gastar la consulta ───────────────────────

    [Fact]
    public async Task ClaveRepetida_DevuelveElMismoIdSinConsultarNiEscribir()
    {
        var primera = await Sut.HandleAsync(Command(key: "K-1"), TestContext.Current.CancellationToken);

        var segunda = await Sut.HandleAsync(Command(key: "K-1"), TestContext.Current.CancellationToken);

        segunda.Id.Should().Be(primera.Id);
        segunda.Status.Should().Be("generated");

        _lookup.Calls.Should().Be(1, "el replay no puede gastar una consulta al proveedor RUES");
        _storage.Saved.Should().HaveCount(1, "el replay no puede escribir un archivo nuevo");
        _repo.Rows.Should().HaveCount(1);
        _renderer.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ClaveRepetida_ConEspaciosAlrededor_SigueSiendoLaMismaClave()
    {
        await Sut.HandleAsync(Command(key: "K-1"), TestContext.Current.CancellationToken);

        var segunda = await Sut.HandleAsync(Command(key: "  K-1  "), TestContext.Current.CancellationToken);

        segunda.Id.Should().Be(_repo.Rows.Single().Id);
        _lookup.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ClaveRepetida_EnOtroTenant_NoResuelveElReplayAjeno()
    {
        await Sut.HandleAsync(Command(key: "K-1"), TestContext.Current.CancellationToken);

        var ajena = await Sut.HandleAsync(
            new GenerateRuesDocumentCommand(OtroTenant, Usuario, "900123456", "K-1"),
            TestContext.Current.CancellationToken);

        ajena.Id.Should().NotBe(_repo.Rows[0].Id, "la idempotencia es por tenant, no global");
        _lookup.Calls.Should().Be(2);
        _repo.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task SinClaveDeIdempotencia_CadaPeticionGeneraSuPropioDocumento()
    {
        await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);
        await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);

        _repo.Rows.Should().HaveCount(2);
        _lookup.Calls.Should().Be(2);
    }

    // ── NIT sin coincidencia ────────────────────────────────────────────────────────

    [Fact]
    public async Task NitInexistenteEnRues_DejaFilaEnErrorYNingunArchivo()
    {
        _lookup.Result = new StandaloneRuesLookupResult(
            false, new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null);

        var result = await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GenerateRuesDocumentOutcome.RuesNotFound);
        result.Status.Should().Be("error");
        result.ErrorCode.Should().Be("rues_not_found");

        _repo.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be("rues_not_found");
        _repo.Rows.Single().Status.Should().Be("error");
        _storage.Saved.Should().BeEmpty("un NIT sin coincidencia no puede dejar un PDF en storage");
        _renderer.Calls.Should().Be(0);
        _repo.Snapshots.Should().BeEmpty();
    }

    [Theory]
    [InlineData("provider_unavailable", GenerateRuesDocumentOutcome.ProviderUnavailable)]
    [InlineData("provider_not_found", GenerateRuesDocumentOutcome.ProviderNotFound)]
    public async Task FalloDelProveedor_DejaRastroAuditableYNingunArchivo(
        string error, GenerateRuesDocumentOutcome esperado)
    {
        _lookup.Result = new StandaloneRuesLookupResult(
            false, new Dictionary<string, string?>(), DateTimeOffset.UtcNow, error);

        var result = await Sut.HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(esperado);
        result.ErrorCode.Should().Be(error);
        _repo.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be(error);
        _storage.Saved.Should().BeEmpty();
    }

    // ── Validación de entrada ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("no-es-un-nit")]
    [InlineData("900123456789012345678901")]
    public async Task NitInvalido_NoCreaFilaNiConsultaAlProveedor(string? nit)
    {
        var result = await Sut.HandleAsync(Command(nit), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GenerateRuesDocumentOutcome.InvalidRequest);
        result.ErrorCode.Should().Be("invalid_request");
        result.Id.Should().BeNull();

        _repo.Rows.Should().BeEmpty();
        _lookup.Calls.Should().Be(0);
        _storage.Saved.Should().BeEmpty();
    }
}
