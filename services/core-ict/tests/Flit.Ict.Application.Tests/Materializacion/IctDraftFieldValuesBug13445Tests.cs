using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.ExternalClients;
using Flit.Ict.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Materializacion;

/// <summary>
/// Bug #13445 — las transformaciones (<c>master.Transformations</c>, catálogo Tipo Trámite ICT 5/6/7/9) y la prenda
/// (<c>master.Limitations*</c>) viajan a core-api en <c>CreateDraftFromIctRequest.field_values</c>
/// según el contrato de carriles (05-contrato-carriles.md). Antes solo viajaban <c>vin</c> y <c>plate</c>.
/// <para>Uso de ejemplo:
/// <code>
/// var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(master, tipo, resolver, log: null, ct);
/// request.FieldValues // contiene cambio_color="true" si master.Transformations trae el código 7
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

    // ---------- Transformaciones (catálogo Tipo Trámite ICT: 5/6/7/9 — D6) ----------

    [Fact]
    public async Task Codigo5_EsBlindaje()
    {
        var fv = await FieldValuesAsync(ConTransformacion(5));

        fv.Should().ContainKey("blindaje").WhoseValue.Should().Be("true");
        fv.Keys.Should().NotContain(["cambio_color", "cambio_carroceria", "cambio_combustible", "blindaje_nivel"]);
    }

    [Theory]
    [InlineData((short)1, "NIVEL_1")]
    [InlineData((short)2, "NIVEL_2")]
    [InlineData((short)3, "NIVEL_3")]
    [InlineData((short)4, "DESMONTE")]
    public async Task Codigo5ConNivel_EnviaBlindajeYNivel(short nivelIct, string esperado)
    {
        var master = Master();
        master.ArmorLevelNumberId = nivelIct;
        var fv = await FieldValuesAsync(ConTransformacion(5, master));

        fv.Should().Contain("blindaje", "true").And.Contain("blindaje_nivel", esperado);
    }

    [Fact]
    public async Task Codigo6_EsCambioDeCarroceria()
    {
        var fv = await FieldValuesAsync(ConTransformacion(6));

        fv.Should().ContainKey("cambio_carroceria").WhoseValue.Should().Be("true");
        fv.Keys.Should().NotContain(["cambio_color", "cambio_combustible", "blindaje"]);
    }

    [Fact]
    public async Task Codigo7_EsCambioDeColor()
    {
        var fv = await FieldValuesAsync(ConTransformacion(7));

        fv.Should().ContainKey("cambio_color").WhoseValue.Should().Be("true");
        fv.Keys.Should().NotContain(["cambio_carroceria", "cambio_combustible", "blindaje"]);
    }

    [Theory]
    [InlineData((short)1, "GASOLINA")]
    [InlineData((short)2, "GAS NATURAL")]
    [InlineData((short)3, "DIESEL")]
    [InlineData((short)5, "ELECTRICO")]
    [InlineData((short)6, "HIDROGENO")]
    [InlineData((short)7, "ETANOL")]
    [InlineData((short)8, "BIODIESEL")]
    public async Task Codigo9ConCombustibleExacto_EnviaBanderaYCombustible(short combustibleIct, string esperado)
    {
        var master = Master();
        master.NewVehicleFuelType = combustibleIct;
        var fv = await FieldValuesAsync(ConTransformacion(9, master));

        fv.Should().Contain("cambio_combustible", "true").And.Contain("vehicle_fuel", esperado);
        fv.Keys.Should().NotContain(["blindaje", "ict_transformacion_sin_subtipo"]);
    }

    [Theory]
    [InlineData((short)4)]   // GAS GASOL
    [InlineData((short)9)]   // GLP
    [InlineData((short)10)]  // GASO ELEC
    [InlineData((short)11)]  // DIES ELEC
    [InlineData((short)12)]  // AGUA CON GAS
    [InlineData((short)13)]  // fuera del catálogo
    public async Task Codigo9ConCombustibleSinEquivalenciaExacta_EnviaSoloLaBandera(short combustibleIct)
    {
        var master = Master();
        master.NewVehicleFuelType = combustibleIct;
        var fv = await FieldValuesAsync(ConTransformacion(9, master));

        fv.Should().Contain("cambio_combustible", "true");
        fv.Keys.Should().NotContain("vehicle_fuel");
    }

    [Fact]
    public async Task Codigo9SinCombustible_EnviaSoloLaBandera_YYaNoHayMarcadorSinSubtipo()
    {
        var fv = await FieldValuesAsync(ConTransformacion(9));

        fv.Should().Contain("cambio_combustible", "true");
        fv.Keys.Should().NotContain(["ict_transformacion_sin_subtipo", "vehicle_fuel", "blindaje", "blindaje_nivel"]);
    }

    [Theory]
    [InlineData(17)] // carrocería RUNT del catálogo viejo: retirado
    [InlineData(8)]  // cambio de locatario
    [InlineData(10)] // duplicado de placa
    [InlineData(11)] // duplicado de tarjeta
    [InlineData(42)]
    public async Task CodigoQueNoEsTransformacion_NoEnviaNada(int codigo)
    {
        var fv = await FieldValuesAsync(ConTransformacion(codigo));

        fv.Keys.Should().NotContain(ClavesDelBug);
        fv.Keys.Should().BeEquivalentTo(["vin", "plate"]);
    }

    // ---------- El valor implica su bandera (D9 + preflight core-api) ----------
    // core-api (PreflightCommand.UpsertTransformationAwareField) pisa vehicle_fuel con el RUNT salvo que
    // cambio_combustible="true": el valor declara la transformación y viaja siempre con su bandera.

    [Fact]
    public async Task CombustibleExactoSinCodigo9_EnviaValorYBandera()
    {
        var master = Master(transactionType: 9);
        master.NewVehicleFuelType = 7;
        var fv = await FieldValuesAsync(master);

        fv.Should().Contain("vehicle_fuel", "ETANOL").And.Contain("cambio_combustible", "true");
        fv.Keys.Should().NotContain(["blindaje", "blindaje_nivel", "cambio_color", "cambio_carroceria"]);
    }

    [Fact]
    public async Task NivelSinCodigo5_EnviaValorYBandera()
    {
        var master = Master(transactionType: 5);
        master.ArmorLevelNumberId = 3;
        var fv = await FieldValuesAsync(master);

        fv.Should().Contain("blindaje_nivel", "NIVEL_3").And.Contain("blindaje", "true");
        fv.Keys.Should().NotContain(["vehicle_fuel", "cambio_combustible", "cambio_color", "cambio_carroceria"]);
    }

    [Fact]
    public async Task Codigo9ConCombustibleExacto_UnaSolaBandera()
    {
        var master = Master();
        master.NewVehicleFuelType = 1;
        master.ArmorLevelNumberId = 2;
        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            ConTransformacion(5, ConTransformacion(9, master)), Tipo, SinAdjuntos(), log: null,
            TestContext.Current.CancellationToken);

        request.FieldValues.Count(f => f.FieldKey == "cambio_combustible").Should().Be(1);
        request.FieldValues.Count(f => f.FieldKey == "blindaje").Should().Be(1);
        request.FieldValues.Should().Contain(f => f.FieldKey == "vehicle_fuel" && f.ValueText == "GASOLINA");
    }

    [Theory]
    [InlineData((short)4)]
    [InlineData((short)9)]
    [InlineData((short)13)]
    public async Task CombustibleDudosoSinCodigo9_NoEnviaNada(short combustibleIct)
    {
        var master = Master(transactionType: 9);
        master.NewVehicleFuelType = combustibleIct;
        master.ArmorLevelNumberId = 9; // nivel fuera de catálogo: tampoco arrastra bandera
        var fv = await FieldValuesAsync(master);

        fv.Keys.Should().NotContain(["vehicle_fuel", "cambio_combustible", "blindaje", "blindaje_nivel"]);
        fv.Keys.Should().BeEquivalentTo(["vin", "plate"]);
    }

    [Fact]
    public async Task NivelFueraDeCatalogo_NoEnviaNivel()
    {
        var master = Master();
        master.ArmorLevelNumberId = 5;
        var fv = await FieldValuesAsync(ConTransformacion(5, master));

        fv.Should().Contain("blindaje", "true");
        fv.Keys.Should().NotContain("blindaje_nivel");
    }

    [Fact]
    public async Task VariasTransformaciones_SeSuman()
    {
        var master = Master();
        master.ArmorLevelNumberId = 1;
        master.NewVehicleFuelType = 1;
        master = ConTransformacion(9, ConTransformacion(7, ConTransformacion(6, ConTransformacion(5, master))));
        var fv = await FieldValuesAsync(master);

        fv.Should().Contain("blindaje", "true")
            .And.Contain("blindaje_nivel", "NIVEL_1")
            .And.Contain("cambio_carroceria", "true")
            .And.Contain("cambio_color", "true")
            .And.Contain("cambio_combustible", "true")
            .And.Contain("vehicle_fuel", "GASOLINA");
    }

    // ---------- Catálogo DDL (sin PostgreSQL: se lee el script embebido) ----------

    [Fact]
    public void Ddl27_SeEmbebeDespuesDel12YDel13_YCorrigeElCatalogoSinBorrarEl17()
    {
        var scripts = EmbeddedDdl.AllScriptsInOrder().ToList();
        const string script27 = "27-ICT-transformation-type-tipo-tramite.sql";
        scripts.Should().Contain(script27);
        scripts.IndexOf(script27).Should().BeGreaterThan(scripts.IndexOf("13-ICT-master-transformations.sql"));

        var sql = EmbeddedDdl.LoadUp(script27);
        sql.Should().Contain("(5, 'Blindaje')")
            .And.Contain("(6, 'Cambio de Carrocería')")
            .And.Contain("(7, 'Cambio de Color')")
            .And.Contain("(9, 'Conversiones de Combustible')")
            .And.Contain("SET is_active = false")
            .And.Contain("WHERE NOT EXISTS");
        // Revisión DB H2: dos arranques simultáneos no chocan por la PK calculada con MAX(id)+1.
        System.Text.RegularExpressions.Regex.Count(
                sql, "^ON CONFLICT DO NOTHING;", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Should().Be(2, "los dos INSERT llevan ON CONFLICT DO NOTHING (el comentario de cabecera no cuenta)");
        sql.Should().NotContainEquivalentOf("DELETE FROM");
    }

    // ---------- Prenda ----------

    [Theory]
    [InlineData((short)1)]
    [InlineData((short)2)]
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

    [Fact]
    public async Task PrendaOmitir_SoloEnviaLaOperacion_SinAcreedorNiFecha()
    {
        // Revisión SEC m2 / CR m2 (Ley 1581, minimización): con «omitir» el acreedor no se usa.
        var master = Master();
        master.LimitationsOperationType = 3;
        master.LimitationsCreditor = "BANCO DE PRUEBA S.A.";
        master.LimitationsCreditorDocumentType = "NIT";
        master.LimitationsCreditorDocumentNumber = "900000001";
        master.LimitationsInscriptionDate = "2025-01-31";

        var fv = await FieldValuesAsync(master);

        fv.Should().Contain("ict_prenda_operacion", "3");
        fv.Keys.Where(k => k.StartsWith("ict_prenda_", StringComparison.Ordinal))
            .Should().BeEquivalentTo(["ict_prenda_operacion"]);
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
