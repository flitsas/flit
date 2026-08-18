using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #11613 — la regeneración documental que disparan la aprobación del OT y la asignación de placa
/// fallaba EN SILENCIO: el generador señala los errores de negocio por RETORNO (tupla) y los endpoints
/// descartaban ese retorno dentro de un try/catch, así que no había log, ni traza, ni cambio de código
/// HTTP. Estas pruebas fijan el contrato del envoltorio trazado.
///
/// <para>Uso de ejemplo:
/// <code>
/// var resultado = await handler.HandleAsync(
///     instanceId, tenantId, RegeneracionDocumentalOrigen.AsignacionPlaca, ct);
/// // resultado.Ok == false, resultado.Error == "organismo_requerido", traza persistida
/// </code>
/// </para>
/// </summary>
public sealed class RegeneracionDocumentalTrazaTests
{
    private readonly TrazaWriterEspia _traza = new();
    private readonly LoggerEspia _logger = new();

    private RegenerarDocumentosTrazadoHandler Handler(IExpedienteHotDocumentsRegenerator regenerador) =>
        new(regenerador, _logger, _traza);

    [Fact]
    public async Task ErrorDeNegocio_SeInspecciona_LogueaError_YPersisteTraza()
    {
        // AC4: organismo_requerido no lanza excepción; antes se perdía por completo.
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var handler = Handler(new RegeneradorFalla("organismo_requerido"));

        var resultado = await handler.HandleAsync(
            instanceId, tenantId, RegeneracionDocumentalOrigen.AsignacionPlaca, ct);

        resultado.Ok.Should().BeFalse();
        resultado.Error.Should().Be("organismo_requerido");
        resultado.TrazaPersistida.Should().BeTrue();

        _logger.Niveles.Should().Contain(LogLevel.Error);

        _traza.Escrituras.Should().ContainSingle();
        var escritura = _traza.Escrituras[0];
        escritura.TenantId.Should().Be(tenantId);
        escritura.InstanceId.Should().Be(instanceId);
        escritura.Origen.Should().Be(RegeneracionDocumentalOrigen.AsignacionPlaca);
        escritura.CodigoError.Should().Be("organismo_requerido");
    }

    [Fact]
    public async Task RegeneracionOk_NoLogueaErrorNiEscribeTraza()
    {
        // Happy path: el camino normal no debe dejar ruido en la bitácora del trámite.
        var ct = TestContext.Current.CancellationToken;
        var handler = Handler(new RegeneradorFalla(null));

        var resultado = await handler.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), RegeneracionDocumentalOrigen.AprobacionOt, ct);

        resultado.Ok.Should().BeTrue();
        resultado.Error.Should().BeNull();
        _traza.Escrituras.Should().BeEmpty();
        _logger.Niveles.Should().NotContain(LogLevel.Error);
    }

    [Fact]
    public async Task Excepcion_NoSePropaga_Y_QuedaTrazadaComoExcepcion()
    {
        // AC5: best-effort — la aprobación / asignación de placa ya persistida NO se revierte, pero el
        // fallo tiene que quedar diagnosticable sin leer los logs del servidor.
        var ct = TestContext.Current.CancellationToken;
        var handler = Handler(new RegeneradorRevienta(new InvalidOperationException("boom")));

        var resultado = await handler.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), RegeneracionDocumentalOrigen.AprobacionOt, ct);

        resultado.Ok.Should().BeFalse();
        resultado.Error.Should().Be(RegenerarDocumentosTrazadoHandler.ErrorExcepcion);
        _traza.Escrituras.Should().ContainSingle();
        _traza.Escrituras[0].Detalle.Should().Be(nameof(InvalidOperationException));
        _logger.Niveles.Should().Contain(LogLevel.Error);
    }

    [Fact]
    public async Task FalloAlEscribirLaTraza_NoTumbaLaOperacion()
    {
        // Edge case: la propia bitácora puede fallar (conexión, RLS). Sigue siendo best-effort.
        var ct = TestContext.Current.CancellationToken;
        _traza.Lanza = new InvalidOperationException("sin conexión");
        var handler = Handler(new RegeneradorFalla("not_found"));

        var resultado = await handler.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), RegeneracionDocumentalOrigen.AprobacionOt, ct);

        resultado.Ok.Should().BeFalse();
        resultado.Error.Should().Be("not_found");
        resultado.TrazaPersistida.Should().BeFalse();
    }

    [Fact]
    public void EventoFallo_CabeEnLaColumnaDeLaBitacora()
    {
        // Contrato: tramites.procedure_instance_events.tipo es varchar(60).
        RegenerarDocumentosTrazadoHandler.EventoFallo.Should().Be("regeneracion_documental_fallida");
        RegenerarDocumentosTrazadoHandler.EventoFallo.Length.Should().BeLessThanOrEqualTo(60);
    }

    private sealed class RegeneradorFalla(string? error) : IExpedienteHotDocumentsRegenerator
    {
        public Task<string?> RegenerateHotDocumentsAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(error);
    }

    private sealed class RegeneradorRevienta(Exception ex) : IExpedienteHotDocumentsRegenerator
    {
        public Task<string?> RegenerateHotDocumentsAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
            throw ex;
    }

    private sealed record Escritura(
        Guid TenantId, Guid InstanceId, string Origen, string CodigoError, string? Detalle);

    private sealed class TrazaWriterEspia : IRegeneracionDocumentalTrazaWriter
    {
        public List<Escritura> Escrituras { get; } = [];
        public Exception? Lanza { get; set; }

        public Task<bool> EscribirFalloAsync(
            Guid tenantId,
            Guid procedureInstanceId,
            string origen,
            string codigoError,
            string? detalle,
            CancellationToken cancellationToken = default)
        {
            if (Lanza is not null)
                throw Lanza;

            Escrituras.Add(new Escritura(tenantId, procedureInstanceId, origen, codigoError, detalle));
            return Task.FromResult(true);
        }
    }

    private sealed class LoggerEspia : ILogger<RegenerarDocumentosTrazadoHandler>
    {
        public List<LogLevel> Niveles { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Niveles.Add(logLevel);
    }
}
