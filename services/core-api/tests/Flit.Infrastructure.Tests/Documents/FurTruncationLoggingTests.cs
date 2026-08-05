using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// PR #230 — el truncado de observaciones tiene que dejar rastro. <c>FurTextFitterTests</c> ya cubre
/// que el algoritmo invoque su callback, pero nadie verificaba que el renderer lo enganche de verdad al
/// logger: si ese cable se desconecta, el truncado vuelve a ser silencioso y toda la suite sigue verde.
/// Este test es la evidencia de esa costura.
///
/// <para>Verifica además lo contrario, que es lo que importa para Habeas Data (Ley 1581): el registro
/// lleva la referencia del trámite y un contador, y <b>nunca</b> el texto de la observación — que es
/// donde va el nombre del acreedor prendario.</para>
/// </summary>
public sealed class FurTruncationLoggingTests
{
    private const string Referencia = "TRM-2026-000042";

    /// <summary>Texto identificable: si apareciera en el log, el aserto de PII lo caza.</summary>
    private const string AcreedorSecreto = "BANCO CONFIDENCIAL DE PRUEBA S.A.";

    private static FurDocumentData Sample(string observaciones) => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: Referencia,
        Modalidad: "traspaso",
        TipologiaCodigo: "traspaso_standard",
        Vehiculo: new VehiculoDatos(
            Marca: "MARCA", Linea: "LINEA", Modelo: "2024", Color: "ROJO",
            Clase: "AUTOMOVIL", Combustible: "GASOLINA", Cilindraje: "1600",
            Vin: "VIN123", Placa: "ABC123", NumeroMotor: "M1", NumeroSerie: "S1"),
        Organismo: new OrganismoTransito("05001", "OT", "CIUDAD"),
        Partes:
        [
            new DocumentParte("vendedor", "APELLIDO1 APELLIDO2 NOMBRE", "111", null, DocumentType: "CC"),
            new DocumentParte("comprador", "OTRO1 OTRO2 NOMBRE", "222", null, DocumentType: "CC"),
        ],
        ValorVenta: 1000m, Causal: null, SellosFirma: [],
        FechaTramite: new DateTime(2026, 8, 5),
        Observaciones: observaciones);

    [Fact]
    public void ObservacionDesmedida_DejaAdvertenciaConLaReferenciaDelTramite()
    {
        var logger = new CapturingLogger();
        // Muy por encima de lo que rinde el recuadro (~1.500 caracteres): el truncado es inevitable.
        var observaciones = $"GRAVAMEN A FAVOR DE: {AcreedorSecreto}. " + new string('X', 20_000);

        new FurOverlayDocumentGenerator(AppContext.BaseDirectory, logger).GenerateFur(Sample(observaciones));

        logger.Warnings.Should().ContainSingle(w => w.Contains(Referencia) && w.Contains("observations"));
    }

    [Fact]
    public void ElLogNoFiltraElTextoDeLaObservacion()
    {
        // Ley 1581: el recuadro de observaciones transporta el nombre del acreedor prendario. Puede
        // registrarse QUE se truncó y CUÁNTO, nunca QUÉ decía.
        var logger = new CapturingLogger();
        var observaciones = $"GRAVAMEN A FAVOR DE: {AcreedorSecreto}. " + new string('X', 20_000);

        new FurOverlayDocumentGenerator(AppContext.BaseDirectory, logger).GenerateFur(Sample(observaciones));

        logger.Warnings.Should().NotBeEmpty("el escenario tiene que truncar para que el aserto valga");
        logger.Warnings.Should().NotContain(w => w.Contains(AcreedorSecreto));
        logger.Warnings.Should().NotContain(w => w.Contains("XXXXXXXXXX"));
    }

    [Fact]
    public void ObservacionQueCabe_NoRegistraNada()
    {
        var logger = new CapturingLogger();

        new FurOverlayDocumentGenerator(AppContext.BaseDirectory, logger)
            .GenerateFur(Sample("Vehículo con platón adaptado."));

        logger.Warnings.Should().BeEmpty();
    }

    private sealed class CapturingLogger : ILogger<FurOverlayDocumentGenerator>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
