using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.ExternalClients;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Materializacion;

/// <summary>
/// Bug #13445 — las transformaciones (<c>master.Transformations</c>, códigos RUNT 5/9/17) y la prenda
/// (<c>master.Limitations*</c>) viajan a core-api en <c>CreateDraftFromIctRequest.field_values</c>
/// según el contrato de carriles (05-contrato-carriles.md). Antes solo viajaban <c>vin</c> y <c>plate</c>.
/// <para>Uso de ejemplo:
/// <code>
/// var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(master, tipo, resolver, log: null, ct);
/// request.FieldValues // contiene cambio_color="true" si master.Transformations trae el código 5
/// </code></para>
/// </summary>
public sealed class IctDraftFieldValuesBug13445Tests
{
    private static readonly DraftProcedureType Tipo = new(
        "TRASPASO", "TRASPASO", RequiresCommercialValue: true, ResolvesTransitOfficeFromRunt: true);

    private static ExternalIntegrationMaster Master(int transactionType = 3) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        TransactionType = transactionType,
        Plate = "ABC123",
        Vin = "9BWZZZ377VT004251",
        SellingPrice = 45_000_000m,
        SellingDate = "2026-08-20",
    };

    private static ExternalIntegrationMaster ConTransformacion(int codigo, ExternalIntegrationMaster? master = null)
    {
        master ??= Master();
        master.Transformations.Add(new ExternalIntegrationMasterTransformation
        {
            MasterId = master.Id,
            TenantId = master.TenantId,
            IdTransformationType = codigo,
            Description = "desc",
        });
        return master;
    }

    private static IAttachmentDocTypeResolver SinAdjuntos()
    {
        var resolver = Substitute.For<IAttachmentDocTypeResolver>();
        resolver.ResolveDocTypeAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("otro"));
        return resolver;
    }

    private static async Task<Dictionary<string, string>> FieldValuesAsync(
        ExternalIntegrationMaster master, ILogger? log = null)
    {
        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            master, Tipo, SinAdjuntos(), log, TestContext.Current.CancellationToken);

        // Ninguna clave duplicada: core-api persiste la lista tal cual.
        request.FieldValues.Select(f => f.FieldKey).Should().OnlyHaveUniqueItems();
        return request.FieldValues.ToDictionary(f => f.FieldKey, f => f.ValueText);
    }

    private static readonly string[] ClavesDelBug =
    [
        "cambio_color", "cambio_carroceria", "cambio_combustible", "vehicle_fuel", "blindaje",
        "blindaje_nivel", "ict_transformacion_sin_subtipo", "ict_prenda_operacion",
        "ict_prenda_acreedor_nombre", "ict_prenda_acreedor_documento_tipo",
        "ict_prenda_acreedor_documento", "ict_prenda_fecha_inscripcion",
    ];

    // ---------- Transformaciones ----------

    [Fact]
    public async Task Codigo5_EnviaCambioColor()
    {
        var fv = await FieldValuesAsync(ConTransformacion(5));

        fv.Should().ContainKey("cambio_color").WhoseValue.Should().Be("true");
        fv.Keys.Should().NotContain(["cambio_carroceria", "cambio_combustible", "blindaje", "ict_transformacion_sin_subtipo"]);
    }

    [Fact]
    public async Task Codigo17_EnviaCambioCarroceria()
    {
        var fv = await FieldValuesAsync(ConTransformacion(17));

        fv.Should().ContainKey("cambio_carroceria").WhoseValue.Should().Be("true");
        fv.Keys.Should().NotContain(["cambio_color", "cambio_combustible", "blindaje"]);
    }

    [Fact]
    public async Task Codigo9ConCombustible_EnviaSoloLaBandera_PorqueElCatalogoIctNoTieneEquivalenteWeb()
    {
        var master = Master();
        master.NewVehicleFuelType = 3;
        var fv = await FieldValuesAsync(ConTransformacion(9, master));

        fv.Should().ContainKey("cambio_combustible").WhoseValue.Should().Be("true");
        // ids 1-12 sin catálogo semántico en core-ict → el gestor completa vehicle_fuel en el wizard.
        fv.Keys.Should().NotContain(["vehicle_fuel", "blindaje", "ict_transformacion_sin_subtipo"]);
    }

    [Fact]
    public async Task Codigo9ConBlindaje_EnviaSoloLaBandera_PorqueElCatalogoIctNoTieneEquivalenteWeb()
    {
        var master = Master();
        master.ArmorLevelNumberId = 2;
        var fv = await FieldValuesAsync(ConTransformacion(9, master));

        fv.Should().ContainKey("blindaje").WhoseValue.Should().Be("true");
        fv.Keys.Should().NotContain(["blindaje_nivel", "cambio_combustible", "ict_transformacion_sin_subtipo"]);
    }

    [Fact]
    public async Task Codigo9ConAmbosSubtipos_EnviaAmbasBanderas()
    {
        var master = Master();
        master.NewVehicleFuelType = 1;
        master.ArmorLevelNumberId = 1;
        var fv = await FieldValuesAsync(ConTransformacion(9, master));

        fv.Should().Contain("cambio_combustible", "true").And.Contain("blindaje", "true");
        fv.Keys.Should().NotContain("ict_transformacion_sin_subtipo");
    }

    [Fact]
    public async Task Codigo9SinSubtipo_NoEnviaBandera_YMarcaSinSubtipo()
    {
        var fv = await FieldValuesAsync(ConTransformacion(9));

        fv.Should().ContainKey("ict_transformacion_sin_subtipo").WhoseValue.Should().Be("true");
        fv.Keys.Should().NotContain(["cambio_combustible", "vehicle_fuel", "blindaje", "blindaje_nivel"]);
    }

    [Fact]
    public async Task CodigoDesconocido_NoEnviaNada()
    {
        var fv = await FieldValuesAsync(ConTransformacion(42));

        fv.Keys.Should().NotContain(ClavesDelBug);
        fv.Keys.Should().BeEquivalentTo(["vin", "plate"]);
    }

    [Fact]
    public async Task SubtiposSinCodigo9_NoEnvianNada()
    {
        // Los subtipos solo cuentan cuando la transformación 9 viene declarada.
        var master = Master();
        master.NewVehicleFuelType = 4;
        master.ArmorLevelNumberId = 3;
        var fv = await FieldValuesAsync(master);

        fv.Keys.Should().NotContain(ClavesDelBug);
    }

    [Fact]
    public async Task TransactionTypePrincipal5SinTransformaciones_NoEsCambioDeColor()
    {
        // El validador trata transaction_type principal 5 como blindaje; aquí solo manda master.Transformations.
        var master = Master(transactionType: 5);
        master.ArmorLevelNumberId = 1;
        var fv = await FieldValuesAsync(master);

        fv.Keys.Should().NotContain(ClavesDelBug);
    }

    [Fact]
    public async Task VariasTransformaciones_SeSuman()
    {
        var master = ConTransformacion(17, ConTransformacion(5));
        var fv = await FieldValuesAsync(master);

        fv.Should().Contain("cambio_color", "true").And.Contain("cambio_carroceria", "true");
    }

    // ---------- Prenda ----------

    [Theory]
    [InlineData((short)1)]
    [InlineData((short)2)]
    [InlineData((short)3)]
    public async Task PrendaCompleta_EnviaLasCincoClaves(short operacion)
    {
        var master = Master();
        master.LimitationsOperationType = operacion;
        master.LimitationsCreditor = "BANCO DE PRUEBA S.A.";
        master.LimitationsCreditorDocumentType = "NIT";
        master.LimitationsCreditorDocumentNumber = "900000001";
        master.LimitationsInscriptionDate = "2025-01-31";

        var fv = await FieldValuesAsync(master);

        fv.Should().Contain("ict_prenda_operacion", operacion.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .And.Contain("ict_prenda_acreedor_nombre", "BANCO DE PRUEBA S.A.")
            .And.Contain("ict_prenda_acreedor_documento_tipo", "NIT")
            .And.Contain("ict_prenda_acreedor_documento", "900000001")
            .And.Contain("ict_prenda_fecha_inscripcion", "2025-01-31");
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)4)]
    [InlineData((short)-1)]
    public async Task PrendaConOperacionInvalida_NoEnviaLaOperacion(short operacion)
    {
        var master = Master();
        master.LimitationsOperationType = operacion;
        master.LimitationsCreditor = "BANCO DE PRUEBA S.A.";

        var fv = await FieldValuesAsync(master);

        fv.Keys.Should().NotContain("ict_prenda_operacion");
        fv.Should().Contain("ict_prenda_acreedor_nombre", "BANCO DE PRUEBA S.A.");
    }

    [Fact]
    public async Task PrendaVacia_NoEnviaNingunaClave()
    {
        var master = Master();
        master.LimitationsOperationType = null;
        master.LimitationsCreditor = "   ";
        master.LimitationsCreditorDocumentType = string.Empty;
        master.LimitationsCreditorDocumentNumber = null;
        master.LimitationsInscriptionDate = " ";

        var fv = await FieldValuesAsync(master);

        fv.Keys.Should().NotContain(k => k.StartsWith("ict_prenda_", StringComparison.Ordinal));
        fv.Keys.Should().BeEquivalentTo(["vin", "plate"]);
    }

    [Fact]
    public async Task Prenda_NuncaLlegaALosLogs()
    {
        var master = Master();
        master.LimitationsOperationType = 2;
        master.LimitationsCreditor = "ACREEDOR SECRETO LTDA";
        master.LimitationsCreditorDocumentNumber = "811222333";
        master.Transformations.Add(new ExternalIntegrationMasterTransformation { IdTransformationType = 9 });
        // Un adjunto sin storage_path fuerza al menos un log en el armado del request.
        master.Attachments.Add(new TransactionAttachment { IdAttachment = 7 });
        var log = new CapturingLogger();

        await FieldValuesAsync(master, log);

        log.Messages.Should().NotBeEmpty();
        log.Messages.Should().NotContain(m => m.Contains("ACREEDOR SECRETO", StringComparison.OrdinalIgnoreCase)
            || m.Contains("811222333", StringComparison.Ordinal));
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> props)
            {
                Messages.AddRange(props.Select(p => $"{p.Key}={p.Value}"));
            }
        }
    }
}
