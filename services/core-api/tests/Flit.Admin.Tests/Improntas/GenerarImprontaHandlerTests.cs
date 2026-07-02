using Flit.Admin.Application.Improntas.GenerarImpronta;
using Flit.Admin.Domain.Improntas;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Improntas;

/// <summary>
/// Tests del caso de uso de generación de impronta (HU #10467) sobre el handler real y
/// <see cref="ImprontaRepository"/> con proveedor InMemory (mismo patrón que
/// <c>CreateTransitOfficeHandlerTests</c>). El cliente externo Kyverum RUNT se sustituye por
/// <see cref="FakeImprontaExternalClient"/> — su implementación real (<c>ImprontaRuntClient</c>,
/// HTTP) ya se cubre en <c>Flit.Infrastructure.Tests/Improntas/ImprontaRuntClientTests</c> (HU #10465).
///
/// AC1 usa el fixture real <c>Improntas_12.pdf</c> (raíz del repo, Res. 17145/2023) codificado a
/// base64 para simular la respuesta de Kyverum y verificar que el handler decodifica exactamente
/// los mismos bytes desde el Data URI, sin corrupción del binario.
/// </summary>
public sealed class GenerarImprontaHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FlitUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly byte[] FixturePdfBytes = File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "Improntas", "Fixtures", "Improntas_12.pdf"));

    // ── AC1 — Escenario positivo ────────────────────────────────────────────────────────
    [Fact]
    public async Task AC1_DatosValidos_LlamaClienteExternoDecodificaPdfYPersisteTrazabilidad()
    {
        var db = NewDbName();
        var dataUri = "data:application/pdf;base64," + Convert.ToBase64String(FixturePdfBytes);
        var client = FakeImprontaExternalClient.Success(new ImprontaExternalResult(
            PdfDataUri: dataUri,
            Hash: new string('a', 64),
            Radicado: "IMPR-AC1TEST1",
            FechaImpresa: "2026-07-02T10:00:00Z"));

        GenerarImprontaResult result;
        await using (var act = NewContext(db))
        {
            var handler = new GenerarImprontaHandler(client, new ImprontaRepository(act));

            result = await handler.HandleAsync(new GenerarImprontaCommand
            {
                TenantId = TenantId,
                FlitUserId = FlitUserId,
                Request = ValidRequest(),
            }, TestContext.Current.CancellationToken);
        }

        result.Status.Should().Be(GenerarImprontaStatus.Success);
        result.PdfBytes.Should().Equal(FixturePdfBytes, "el binario decodificado del Data URI debe ser idéntico al PDF original");
        result.Radicado.Should().Be("IMPR-AC1TEST1");
        result.Hash.Should().Be(new string('a', 64));
        client.LastRequest.Should().NotBeNull();
        client.LastRequest!.Placa.Should().Be("ABC123");

        await using var verify = NewContext(db);
        var persisted = await verify.ImprontaGenerations.SingleAsync(
            g => g.Radicado == "IMPR-AC1TEST1", TestContext.Current.CancellationToken);
        persisted.TenantId.Should().Be(TenantId);
        persisted.FlitUserId.Should().Be(FlitUserId);
        persisted.PdfContent.Should().Equal(FixturePdfBytes);
        persisted.PdfSizeBytes.Should().Be(FixturePdfBytes.Length);
        persisted.Placa.Should().Be("ABC123");
        persisted.OrgNombre.Should().Be("FLIT SAS");
    }

    [Fact]
    public async Task AC1_SoloNumSerieDiligenciado_PasaLaValidacionYGeneraLaImpronta()
    {
        var db = NewDbName();
        var dataUri = "data:application/pdf;base64," + Convert.ToBase64String(FixturePdfBytes);
        var client = FakeImprontaExternalClient.Success(new ImprontaExternalResult(
            dataUri, new string('b', 64), "IMPR-AC1TEST2", "2026-07-02T10:05:00Z"));

        await using var act = NewContext(db);
        var handler = new GenerarImprontaHandler(client, new ImprontaRepository(act));

        var result = await handler.HandleAsync(new GenerarImprontaCommand
        {
            TenantId = TenantId,
            FlitUserId = FlitUserId,
            Request = ValidRequest() with { NumMotor = null, NumChasis = null, NumSerie = "5HTSN3527D7T91789" },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(GenerarImprontaStatus.Success);
        client.LastRequest!.NumMotor.Should().BeNull();
        client.LastRequest.NumChasis.Should().BeNull();
        client.LastRequest.NumSerie.Should().Be("5HTSN3527D7T91789");
    }

    [Fact]
    public async Task ValidacionLocal_PlacaYTodosLosIdentificadoresVacios_Retorna422SinLlamarAlProveedorNiPersistir()
    {
        var db = NewDbName();
        var client = FakeImprontaExternalClient.Success(new ImprontaExternalResult(
            "data:application/pdf;base64,AAAA", "hash", "IMPR-NOPE", "2026-07-02T10:00:00Z"));

        await using var act = NewContext(db);
        var handler = new GenerarImprontaHandler(client, new ImprontaRepository(act));

        var result = await handler.HandleAsync(new GenerarImprontaCommand
        {
            TenantId = TenantId,
            FlitUserId = FlitUserId,
            Request = ValidRequest() with { Placa = "", NumMotor = null, NumChasis = null, NumSerie = null },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(GenerarImprontaStatus.ValidationFailed);
        result.Errors.Should().Contain(e => e.Field == "placa");
        result.Errors.Should().Contain(e => e.Field == "numMotor");
        client.LastRequest.Should().BeNull("no debe invocarse Kyverum RUNT si la validación local falla");
        (await act.ImprontaGenerations.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task ValidacionLocal_SoloIdentificadorSinPlaca_Retorna422ConSoloElErrorDePlaca()
    {
        var db = NewDbName();
        var client = FakeImprontaExternalClient.Success(new ImprontaExternalResult(
            "data:application/pdf;base64,AAAA", "hash", "IMPR-NOPE2", "2026-07-02T10:00:00Z"));

        await using var act = NewContext(db);
        var handler = new GenerarImprontaHandler(client, new ImprontaRepository(act));

        var result = await handler.HandleAsync(new GenerarImprontaCommand
        {
            TenantId = TenantId,
            FlitUserId = FlitUserId,
            Request = ValidRequest() with { Placa = "   " },
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(GenerarImprontaStatus.ValidationFailed);
        result.Errors.Should().ContainSingle().Which.Field.Should().Be("placa");
    }

    // ── AC3 — Escenario de borde ────────────────────────────────────────────────────────
    [Fact]
    public async Task AC3_KyverumRespondeUpstreamUnavailable_Retorna502SinPersistirRegistroIncompleto()
    {
        var db = NewDbName();
        var client = FakeImprontaExternalClient.Failure(
            new ImprontaRuntException("Kyverum RUNT no disponible temporalmente: Servicio RUNT saturado, reintente.", isTransient: true));

        await using var act = NewContext(db);
        var handler = new GenerarImprontaHandler(client, new ImprontaRepository(act));

        var result = await handler.HandleAsync(new GenerarImprontaCommand
        {
            TenantId = TenantId,
            FlitUserId = FlitUserId,
            Request = ValidRequest(),
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(GenerarImprontaStatus.ProviderUnavailable);
        result.ProviderErrorCode.Should().Be("upstream_unavailable");
        (await act.ImprontaGenerations.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AC3_KyverumRespondeValidationError_Retorna422ConCodigoValidationErrorSinPersistir()
    {
        var db = NewDbName();
        var client = FakeImprontaExternalClient.Failure(
            new ImprontaRuntException(
                "Datos de la impronta inválidos: Cuerpo inválido (numMotor: Se requiere al menos uno)",
                isTransient: false));

        await using var act = NewContext(db);
        var handler = new GenerarImprontaHandler(client, new ImprontaRepository(act));

        var result = await handler.HandleAsync(new GenerarImprontaCommand
        {
            TenantId = TenantId,
            FlitUserId = FlitUserId,
            Request = ValidRequest(),
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(GenerarImprontaStatus.ProviderValidationFailed);
        result.ProviderErrorCode.Should().Be("validation_error");
        result.ProviderMessage.Should().Contain("numMotor");
        (await act.ImprontaGenerations.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task AC3_KyverumRechazaAutenticacion_Retorna401SinPersistir()
    {
        var db = NewDbName();
        var client = FakeImprontaExternalClient.Failure(
            new ImprontaRuntException(
                "Kyverum RUNT rechazó la autenticación: Scope insuficiente para esta operación.",
                isTransient: false));

        await using var act = NewContext(db);
        var handler = new GenerarImprontaHandler(client, new ImprontaRepository(act));

        var result = await handler.HandleAsync(new GenerarImprontaCommand
        {
            TenantId = TenantId,
            FlitUserId = FlitUserId,
            Request = ValidRequest(),
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(GenerarImprontaStatus.ProviderUnauthorized);
        result.ProviderErrorCode.Should().Be("unauthorized");
        (await act.ImprontaGenerations.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    // ---------- Helpers ----------

    private static GenerarImprontaRequest ValidRequest() => new(
        Placa: "ABC123",
        NumMotor: "MTR-123",
        NumChasis: null,
        NumSerie: null,
        Marca: "Chevrolet",
        Linea: "Spark",
        Modelo: "2024",
        OrgNombre: "FLIT SAS",
        OrgNit: "900000000-1",
        OrgCiudad: "Bogotá",
        Operador: "Operador X");

    private static string NewDbName() => $"flit-generar-impronta-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private sealed class FakeImprontaExternalClient : IImprontaExternalClient
    {
        private readonly ImprontaExternalResult? _success;
        private readonly ImprontaRuntException? _failure;

        private FakeImprontaExternalClient(ImprontaExternalResult? success, ImprontaRuntException? failure)
        {
            _success = success;
            _failure = failure;
        }

        public static FakeImprontaExternalClient Success(ImprontaExternalResult result) => new(result, null);

        public static FakeImprontaExternalClient Failure(ImprontaRuntException exception) => new(null, exception);

        public ImprontaExternalRequest? LastRequest { get; private set; }

        public Task<ImprontaExternalResult> GenerarAsync(ImprontaExternalRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return _failure is not null
                ? throw _failure
                : Task.FromResult(_success!);
        }
    }
}
