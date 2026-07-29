using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10975 (Feature #10972) — el OCR del SOAT ya extraía número de póliza, aseguradora, fechas y
/// estado, pero el endpoint es stateless y el frontend descartaba el resultado: el certificado de
/// vigencia SOAT y RTM salía con esas celdas en blanco. Aquí se cubre la persistencia y, sobre todo,
/// la <b>regla de precedencia</b>: el RUNT es fuente oficial y el OCR solo respaldo.
/// </summary>
public sealed class PersistOcrFieldsHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly PersistOcrFieldsHandler _sut;

    private readonly Guid _id = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();

    public PersistOcrFieldsHandlerTests() => _sut = new PersistOcrFieldsHandler(_repo);

    private ProcedureInstance Instance(string status = TramiteEstado.Borrador)
    {
        var instance = new ProcedureInstance
        {
            Id = _id,
            TenantId = _tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithDetailsAsync(_id, _tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private static void Seed(ProcedureInstance instance, string fieldKey, string value, string source)
    {
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = fieldKey,
            ValueText = value,
            Source = source,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static string? ValueOf(ProcedureInstance instance, string key) =>
        instance.FieldValues.FirstOrDefault(f => f.FieldKey == key)?.ValueText;

    private static string? SourceOf(ProcedureInstance instance, string key) =>
        instance.FieldValues.FirstOrDefault(f => f.FieldKey == key)?.Source;

    private static PersistOcrFieldsRequest SoatOcr(params (string Key, string? Value)[] fields) =>
        new("soat", fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase));

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Persiste_LosCamposDelSoatQueElOcrExtrae()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();

        var (result, error) = await _sut.HandleAsync(_id, _tenantId, SoatOcr(
            ("numero_poliza", "12345678"),
            ("aseguradora", "LA PREVISORA S.A."),
            ("fecha_inicio", "2026-01-20")), ct);

        error.Should().BeNull();
        result!.Persistidos.Should().Be(3);
        ValueOf(instance, "soat_poliza").Should().Be("12345678");
        ValueOf(instance, "soat_aseguradora").Should().Be("LA PREVISORA S.A.");
        ValueOf(instance, "soat_vigencia").Should().Be("2026-01-20");
        SourceOf(instance, "soat_poliza").Should().Be(PersistOcrFieldsHandler.OcrSource);
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Persiste_LaFechaDeExpedicionDelPromptV2()
    {
        // HU #10976 — el prompt v1 no distinguía expedición de inicio de vigencia.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();

        await _sut.HandleAsync(_id, _tenantId, SoatOcr(
            ("fecha_expedicion", "2026-01-15"),
            ("fecha_inicio", "2026-01-20")), ct);

        ValueOf(instance, "soat_expedicion").Should().Be("2026-01-15");
        ValueOf(instance, "soat_vigencia").Should().Be("2026-01-20");
    }

    [Fact]
    public async Task Persiste_LosCamposDeLaRtm()
    {
        // HU #10977 — prompt de RTM nuevo.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();

        var request = new PersistOcrFieldsRequest("rtm", new Dictionary<string, string?>
        {
            ["numero_certificado"] = "RTM-998877",
            ["cda_expide"] = "CDA LA 80",
            ["fecha_expedicion"] = "2026-02-01",
            ["fecha_vigencia"] = "2026-02-01",
        });

        var (result, error) = await _sut.HandleAsync(_id, _tenantId, request, ct);

        error.Should().BeNull();
        result!.Persistidos.Should().Be(4);
        ValueOf(instance, "rtm_numero").Should().Be("RTM-998877");
        ValueOf(instance, "rtm_entidad").Should().Be("CDA LA 80");
        ValueOf(instance, "rtm_expedicion").Should().Be("2026-02-01");
        ValueOf(instance, "rtm_vigencia").Should().Be("2026-02-01");
    }

    // ── Precedencia: el RUNT manda sobre el PDF ──────────────────────────────

    [Fact]
    public async Task NoPisa_UnValorEscritoPorLaConsultaAlRunt()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();
        Seed(instance, "soat_vencimiento", "2026-12-31", "consultation");

        var (result, _) = await _sut.HandleAsync(_id, _tenantId, SoatOcr(
            ("fecha_vencimiento", "2025-12-31"),   // SOAT viejo cargado a mano
            ("numero_poliza", "12345678")), ct);   // esta sí, porque nadie más la entrega

        ValueOf(instance, "soat_vencimiento").Should().Be("2026-12-31");
        SourceOf(instance, "soat_vencimiento").Should().Be("consultation");
        ValueOf(instance, "soat_poliza").Should().Be("12345678");
        result!.OmitidosPorPrecedencia.Should().Contain("soat_vencimiento");
        result.Persistidos.Should().Be(1);
    }

    [Fact]
    public async Task NoPisa_UnValorDigitadoPorElUsuario()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();
        Seed(instance, "soat_poliza", "DIGITADA-A-MANO", "user");

        var (result, _) = await _sut.HandleAsync(_id, _tenantId, SoatOcr(("numero_poliza", "12345678")), ct);

        ValueOf(instance, "soat_poliza").Should().Be("DIGITADA-A-MANO");
        result!.OmitidosPorPrecedencia.Should().Contain("soat_poliza");
    }

    [Fact]
    public async Task SiPisa_LoQueElPropioOcrHabiaEscritoAntes()
    {
        // Recargar el documento debe poder corregir una lectura previa del OCR.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();
        Seed(instance, "soat_poliza", "LECTURA-VIEJA", PersistOcrFieldsHandler.OcrSource);

        await _sut.HandleAsync(_id, _tenantId, SoatOcr(("numero_poliza", "12345678")), ct);

        ValueOf(instance, "soat_poliza").Should().Be("12345678");
    }

    [Fact]
    public async Task SoatEstado_SeNormalizaAlVocabularioDelGate()
    {
        // Es el gate de aprobación del OT y el frontend compara estricto contra "vigente".
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();

        await _sut.HandleAsync(_id, _tenantId, SoatOcr(("estado_poliza", "VIGENTE")), ct);

        ValueOf(instance, SoatGate.FieldKey).Should().Be(SoatGate.Vigente);
        SoatGate.BlocksApproval(ValueOf(instance, SoatGate.FieldKey)).Should().BeFalse();
    }

    [Fact]
    public async Task SoatEstado_NoDesplazaAlGateYaResueltoPorElRunt()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();
        Seed(instance, SoatGate.FieldKey, SoatGate.Vigente, "consultation");

        await _sut.HandleAsync(_id, _tenantId, SoatOcr(("estado_poliza", "vencida")), ct);

        ValueOf(instance, SoatGate.FieldKey).Should().Be(SoatGate.Vigente);
        SoatGate.BlocksApproval(ValueOf(instance, SoatGate.FieldKey)).Should().BeFalse();
    }

    // ── Alcance y bordes ─────────────────────────────────────────────────────

    [Fact]
    public async Task IgnoraLasLlaves_FueraDeLaWhitelistDelTipo()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();

        var (result, error) = await _sut.HandleAsync(_id, _tenantId, SoatOcr(
            ("vehiculo_marca", "TESLA"),      // el OCR del SOAT lo trae, pero no es suyo
            ("numero_poliza", "12345678")), ct);

        error.Should().BeNull();
        instance.FieldValues.Should().NotContain(f => f.FieldKey == "vehicle_brand");
        instance.FieldValues.Should().NotContain(f => f.FieldKey == "vehiculo_marca");
        result!.IgnoradosFueraDeAlcance.Should().Contain("vehiculo_marca");
        result.Persistidos.Should().Be(1);
    }

    [Fact]
    public async Task ValorVacio_NoEscribeLaLlave()
    {
        // Ausente ⇒ celda EN BLANCO (regla HU #10856), nunca una cadena vacía persistida.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();

        var (result, _) = await _sut.HandleAsync(_id, _tenantId, SoatOcr(
            ("numero_poliza", "   "),
            ("aseguradora", null)), ct);

        instance.FieldValues.Should().BeEmpty();
        result!.Persistidos.Should().Be(0);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TipoNoSoportado_SeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        Instance();

        var request = new PersistOcrFieldsRequest("impronta", new Dictionary<string, string?> { ["x"] = "y" });
        var (result, error) = await _sut.HandleAsync(_id, _tenantId, request, ct);

        error.Should().Be("tipo_no_soportado");
        result.Should().BeNull();
    }

    [Fact]
    public async Task TramiteEntregado_NoAdmiteEscritura()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(TramiteEstado.Entregado);

        var (result, error) = await _sut.HandleAsync(_id, _tenantId, SoatOcr(("numero_poliza", "123")), ct);

        error.Should().Be("not_draft");
        result.Should().BeNull();
        instance.FieldValues.Should().BeEmpty();
    }

    [Fact]
    public async Task Subsanacion_SiAdmiteEscritura()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(TramiteEstado.Rechazado);
        instance.SubsanacionActiva = true;

        var (result, error) = await _sut.HandleAsync(_id, _tenantId, SoatOcr(("numero_poliza", "123")), ct);

        error.Should().BeNull();
        result!.Persistidos.Should().Be(1);
        SourceOf(instance, "soat_poliza").Should().Be(PersistOcrFieldsHandler.OcrSource);
    }

    [Fact]
    public async Task InstanciaInexistente_DevuelveNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(_id, _tenantId, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);

        var (_, error) = await _sut.HandleAsync(_id, _tenantId, SoatOcr(("numero_poliza", "123")), ct);

        error.Should().Be("not_found");
    }
}
