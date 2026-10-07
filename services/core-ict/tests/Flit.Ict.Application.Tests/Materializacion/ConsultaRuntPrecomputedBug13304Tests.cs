using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Domain.Validation;
using Flit.Ict.Grpc.Contracts;
using Flit.Ict.Infrastructure.ExternalClients;
using Flit.Ict.Infrastructure.Jobs;
using Flit.Ict.Infrastructure.Persistence.Repositories;
using Flit.Ict.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Materializacion;

/// <summary>
/// Bug #13304 (numeral 3, carril B) — core-ict guarda la consulta RUNT de su validación y la envía a
/// core-api en el campo 14 (<c>precomputed_vehicle</c>) para que el borrador no re-consulte el RUNT.
/// Vigencia de 24 h: vencida o ausente ⇒ core-ict re-encola su consulta de vehículo (una vez por ventana) sin
/// llamar a core-api; si sigue sin snapshot válido, el master queda CON NOVEDADES (ps=4).
/// <para>Uso de ejemplo:</para>
/// <code>
/// var (result, novedad, reencolado) = await SendToCoreApiJob.EnviarConConsultaRuntAsync(
///     master, tipo, draftClient, snapshots, DateTimeOffset.UtcNow, maxAgeHours: 24, ct);
/// // reencolado ⇒ estado intacto; novedad != null ⇒ ps=4; en ambos draftClient no fue llamado.
/// </code>
/// </summary>
public sealed class ConsultaRuntPrecomputedBug13304Tests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);

    private static readonly DraftProcedureType Traspaso = new("TRASPASO_STANDARD", "TRASPASO", true, true);

    private const string SnapshotJson = """{"Checks":[{"Key":"gravamenes","Status":"warn"}],"HydratedFields":[],"Providers":["kyverum_runt"]}""";

    private static ExternalIntegrationMaster Master() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        TransactionType = 3,
        Plate = "ABC123",
        ProcessStatusId = 2,
        ExternalCommentsValidation = string.Empty,
    };

    private static VehicleConsultationSnapshot Snapshot(DateTimeOffset consultedAt) =>
        new(SnapshotJson, consultedAt, "kyverum_runt", VehicleConsultationSnapshot.KindPlate, "ABC123", string.Empty);

    private static IIctVehicleSnapshotReader Reader(VehicleSnapshotLookup lookup)
    {
        var reader = Substitute.For<IIctVehicleSnapshotReader>();
        reader.GetLatestAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(lookup);
        return reader;
    }

    private static IAttachmentDocTypeResolver SinAdjuntos() => Substitute.For<IAttachmentDocTypeResolver>();

    // ---------------------------------------------------------------- (1) vigencia ⇒ ps=4 sin core-api

    /// <summary>Horas de antigüedad de la última consulta; null = sin resultado guardado (ausente).</summary>
    public static TheoryData<int?> CasosSinConsultaVigente => new((int?)25, (int?)(24 * 7), (int?)null);

    private static VehicleSnapshotLookup LookupSinConsultaVigente(int? horasDeAntiguedad) => new(
        RequiresVehicle: true,
        horasDeAntiguedad is { } h ? Snapshot(Ahora.AddHours(-h)) : null);

    // (a) vencida y (b) ausente: primera vez en la ventana ⇒ re-encola, sin core-api y sin cambiar el estado.
    [Theory]
    [MemberData(nameof(CasosSinConsultaVigente))]
    public async Task Envio_ConsultaRuntVencidaOAusente_ReencolaLaConsulta_SinLlamarACoreApiNiCambiarEstado(int? horasDeAntiguedad)
    {
        var master = Master();
        var draftClient = Substitute.For<IProcedureDraftClient>();
        var reader = Reader(LookupSinConsultaVigente(horasDeAntiguedad));
        reader.RequeueVehicleQueryAsync(master.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);

        var (result, novedad, reencolado) = await SendToCoreApiJob.EnviarConConsultaRuntAsync(
            master, Traspaso, draftClient, reader, Ahora, maxAgeHours: 24, TestContext.Current.CancellationToken);

        reencolado.Should().BeTrue();
        result.Should().BeNull();
        novedad.Should().BeNull();
        master.ProcessStatusId.Should().Be(2, "el master queda en su estado: la consulta pendiente lo saca de la elegibilidad");
        master.ExternalCommentsValidation.Should().BeEmpty();
        await reader.Received(1).RequeueVehicleQueryAsync(master.Id, 24, Arg.Any<CancellationToken>());
        await draftClient.DidNotReceive().CreateDraftAsync(
            Arg.Any<ExternalIntegrationMaster>(), Arg.Any<DraftProcedureType>(),
            Arg.Any<VehicleConsultationSnapshot?>(), Arg.Any<CancellationToken>());
    }

    // (c) ya se re-consultó dentro de la ventana y sigue sin snapshot válido ⇒ ps=4 con la novedad y purga.
    [Theory]
    [MemberData(nameof(CasosSinConsultaVigente))]
    public async Task Envio_SegundaVezSinSnapshotEnLaVentana_DejaPs4ConNovedadYPurga(int? horasDeAntiguedad)
    {
        var master = Master();
        var draftClient = Substitute.For<IProcedureDraftClient>();
        var reader = Reader(LookupSinConsultaVigente(horasDeAntiguedad));
        reader.RequeueVehicleQueryAsync(master.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(false);
        var bitacora = new List<string>();
        reader.PurgeAsync(master.Id, Arg.Any<CancellationToken>()).Returns(_ => { bitacora.Add("purga"); return 1; });

        var (result, novedad, reencolado) = await SendToCoreApiJob.EnviarConConsultaRuntAsync(
            master, Traspaso, draftClient, reader, Ahora, maxAgeHours: 24, TestContext.Current.CancellationToken);
        await SendToCoreApiJob.ResolverEnvioAsync(
            master, result, novedad, reencolado,
            (_, _, _) => { bitacora.Add("ps5"); return Task.CompletedTask; },
            (msg, _) => { bitacora.Add("ps4:" + msg); return Task.CompletedTask; },
            reader, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, TestContext.Current.CancellationToken);

        reencolado.Should().BeFalse();
        novedad.Should().Be(SendToCoreApiJob.NovedadSinConsultaRunt);
        SendToCoreApiJob.NovedadSinConsultaRunt.Should().Be("sin consulta RUNT; re-registre el pretrámite");
        master.ProcessStatusId.Should().Be(4);
        master.ExternalCommentsValidation.Should().Contain(SendToCoreApiJob.NovedadSinConsultaRunt);
        bitacora.Should().Equal("ps4:" + SendToCoreApiJob.NovedadSinConsultaRunt, "purga");
        await draftClient.DidNotReceive().CreateDraftAsync(
            Arg.Any<ExternalIntegrationMaster>(), Arg.Any<DraftProcedureType>(),
            Arg.Any<VehicleConsultationSnapshot?>(), Arg.Any<CancellationToken>());
    }

    // (d) la re-consulta quedó resuelta y vigente ⇒ envía con el campo 14 y no vuelve a encolar.
    [Fact]
    public async Task Envio_ReconsultaResueltaYVigente_EnviaConElCampo14()
    {
        var master = Master();
        var fresca = Snapshot(Ahora.AddMinutes(-3));
        var reader = Reader(new VehicleSnapshotLookup(true, fresca));
        var draftClient = Substitute.For<IProcedureDraftClient>();
        draftClient.CreateDraftAsync(master, Traspaso, fresca, Arg.Any<CancellationToken>())
            .Returns(new CreateDraftResult(Guid.NewGuid(), "REF", "borrador", null));

        var (result, novedad, reencolado) = await SendToCoreApiJob.EnviarConConsultaRuntAsync(
            master, Traspaso, draftClient, reader, Ahora, 24, TestContext.Current.CancellationToken);
        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            master, Traspaso, SinAdjuntos(), fresca, log: null, TestContext.Current.CancellationToken);

        reencolado.Should().BeFalse();
        novedad.Should().BeNull();
        result!.ProcedureInstanceId.Should().NotBeNull();
        await draftClient.Received(1).CreateDraftAsync(master, Traspaso, fresca, Arg.Any<CancellationToken>());
        await reader.DidNotReceive().RequeueVehicleQueryAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        request.PrecomputedVehicle.Should().NotBeNull();
        request.PrecomputedVehicle.ConsultedAt.Should().Be(Timestamp.FromDateTimeOffset(fresca.ConsultedAt));
    }

    [Fact]
    public void Reencolado_SqlCopiaLaUltimaConsultaDeVehiculo_UnaVezPorVentana()
    {
        var sql = DbVehicleSnapshotReader.RequeueSql;

        sql.Should().Contain("INSERT INTO ict.external_integration_source_query")
            .And.Contain("query_type IN ('VEHICLE', 'VIN')")
            .And.Contain("sq.eim_id = @master")
            .And.Contain("ORDER BY sq.created_at DESC, sq.id DESC")
            .And.Contain("NOT EXISTS")
            .And.Contain("r.created_at >= now() - make_interval(hours => @hours)")
            .And.Contain("r.id <> (", "la consulta original del SP no cuenta como re-consulta");
        sql.Should().NotContain("is_data_queried = true", "la nueva source_query nace pendiente (default false)");
    }

    [Fact]
    public async Task Envio_ConsultaRuntVigente_LlamaACoreApiConElSnapshot()
    {
        var master = Master();
        var vigente = Snapshot(Ahora.AddHours(-23));
        var draftClient = Substitute.For<IProcedureDraftClient>();
        draftClient.CreateDraftAsync(master, Traspaso, vigente, Arg.Any<CancellationToken>())
            .Returns(new CreateDraftResult(Guid.NewGuid(), "REF", "borrador", null));

        var (result, novedad, reencolado) = await SendToCoreApiJob.EnviarConConsultaRuntAsync(
            master, Traspaso, draftClient, Reader(new VehicleSnapshotLookup(true, vigente)), Ahora, 24,
            TestContext.Current.CancellationToken);

        reencolado.Should().BeFalse();
        novedad.Should().BeNull();
        result!.ProcedureInstanceId.Should().NotBeNull();
        master.ProcessStatusId.Should().Be(2, "el estado lo fija el job tras crear el borrador");
        await draftClient.Received(1).CreateDraftAsync(master, Traspaso, vigente, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Envio_TipoSinConsultaDeVehiculo_NoExigeConsultaRunt()
    {
        // Solo traspaso (VEHICLE) y matrícula (VIN) consultan vehículo; el resto no puede quedar en ps=4.
        var master = Master();
        var draftClient = Substitute.For<IProcedureDraftClient>();
        draftClient.CreateDraftAsync(master, Traspaso, null, Arg.Any<CancellationToken>())
            .Returns(new CreateDraftResult(Guid.NewGuid(), null, null, null));

        var (_, novedad, _) = await SendToCoreApiJob.EnviarConConsultaRuntAsync(
            master, Traspaso, draftClient, Reader(new VehicleSnapshotLookup(false, null)), Ahora, 24,
            TestContext.Current.CancellationToken);

        novedad.Should().BeNull();
        await draftClient.Received(1).CreateDraftAsync(master, Traspaso, null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Vigencia_NoConfiguradaOInvalida_Usa24Horas(int maxAgeHours)
    {
        SendToCoreApiJob.ConsultaRuntVigente(new VehicleSnapshotLookup(true, Snapshot(Ahora.AddHours(-23))), Ahora, maxAgeHours)
            .Should().BeTrue();
        SendToCoreApiJob.ConsultaRuntVigente(new VehicleSnapshotLookup(true, Snapshot(Ahora.AddHours(-25))), Ahora, maxAgeHours)
            .Should().BeFalse();
    }

    // ---------------------------------------------------------------- (2) request con el campo 14

    [Fact]
    public async Task BuildRequest_ConConsultaVigente_LlenaPrecomputedVehicle()
    {
        var consultedAt = Ahora.AddHours(-2);
        var vehicle = new VehicleConsultationSnapshot(
            SnapshotJson, consultedAt, "kyverum_runt", VehicleConsultationSnapshot.KindPlate, "ABC123", string.Empty);

        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            Master(), Traspaso, SinAdjuntos(), vehicle, log: null, TestContext.Current.CancellationToken);

        request.PrecomputedVehicle.Should().NotBeNull();
        request.PrecomputedVehicle.SnapshotJson.Should().Be(SnapshotJson, "el JSON viaja opaco, sin tocar");
        request.PrecomputedVehicle.ConsultedAt.Should().Be(Timestamp.FromDateTimeOffset(consultedAt));
        request.PrecomputedVehicle.Provider.Should().Be("kyverum_runt");
        request.PrecomputedVehicle.Kind.Should().Be("VehiclePlate");
        request.PrecomputedVehicle.QueriedPlate.Should().Be("ABC123");
        request.PrecomputedVehicle.QueriedVin.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildRequest_PorVin_LlenaQueriedVin()
    {
        var vehicle = new VehicleConsultationSnapshot(
            SnapshotJson, Ahora, "kyverum_runt", VehicleConsultationSnapshot.KindVin, string.Empty, "9BWZZZ377VT004251");

        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            Master(), Traspaso, SinAdjuntos(), vehicle, log: null, TestContext.Current.CancellationToken);

        request.PrecomputedVehicle.Kind.Should().Be("VehicleVin");
        request.PrecomputedVehicle.QueriedVin.Should().Be("9BWZZZ377VT004251");
        request.PrecomputedVehicle.QueriedPlate.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildRequest_SinConsulta_DejaElCampoAusente()
    {
        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            Master(), Traspaso, SinAdjuntos(), vehicle: null, log: null, TestContext.Current.CancellationToken);

        request.PrecomputedVehicle.Should().BeNull();
    }

    // ---------------------------------------------------------------- (3) novedad preflight_warning visible

    [Fact]
    public void Novedad_PreflightWarning_EsVisibleEnElMaster()
    {
        var master = new ExternalIntegrationMaster { ProcessStatusId = 5 };

        var mensaje = MaterializacionNovedades.Mensaje(
            "seed_warning:x;preflight_warning:vehicle_consultation_expired");

        mensaje.Should().NotBeNull();
        MaterializacionNovedades.Aplicar(master, mensaje!);
        master.BusinessCommentsValidation.Should().Contain("preflight_warning:vehicle_consultation_expired")
            .And.NotContain("seed_warning");
        master.ExternalCommentsValidation.Should().Contain("preflight_warning:vehicle_consultation_expired");
        master.ProcessStatusId.Should().Be(5);
    }

    // ---------------------------------------------------------------- (4) guardado y purga

    [Fact]
    public void Orquestador_GuardaElSnapshotEnSuColumna_YLoLeeElEnvio()
    {
        var consultedAt = Ahora.AddMinutes(-10);
        var result = new ConsultationResult(
            SoatStatus: "VIGENTE", VehicleModelYear: 2020,
            Vehicle: new VehicleConsultationSnapshot(SnapshotJson, consultedAt, "kyverum_runt", VehicleConsultationSnapshot.KindPlate));

        var columna = VehicleSnapshotColumn.ForQuery("VEHICLE", "ABC123", "9BWZZZ377VT004251", result.Vehicle);
        var leido = VehicleSnapshotColumn.Parse(columna);

        leido.Should().NotBeNull();
        leido!.SnapshotJson.Should().Be(SnapshotJson);
        leido.ConsultedAt.Should().Be(consultedAt);
        leido.Provider.Should().Be("kyverum_runt");
        leido.Kind.Should().Be("VehiclePlate");
        leido.Plate.Should().Be("ABC123");
        leido.Vin.Should().BeEmpty("se consultó por placa: el VIN no es el identificador consultado");
        OrchestratorJob.InsertResponseSql.Should().Contain("vehicle_snapshot").And.Contain("@vehicle::jsonb");
    }

    [Fact]
    public void Orquestador_QueryResponseNoLlevaElSnapshot()
    {
        var result = new ConsultationResult(
            SoatStatus: "VIGENTE",
            Vehicle: new VehicleConsultationSnapshot(SnapshotJson, Ahora, "kyverum_runt", VehicleConsultationSnapshot.KindPlate));

        var json = OrchestratorJob.QueryResponseJson(result);

        json.Should().Contain("VIGENTE");
        json.Should().NotContain("gravamenes").And.NotContain("kyverum_runt").And.NotContain("\"Vehicle\"");
    }

    [Theory]
    [InlineData("RNMC")]
    [InlineData("DRIVER")]
    public void Orquestador_ConsultaSinResultadoDeVehiculo_NoGuardaColumna(string queryType) =>
        VehicleSnapshotColumn.ForQuery(queryType, "ABC123", string.Empty, vehicle: null).Should().BeNull();

    [Fact]
    public void ClienteConsulta_MapeaLosCampos9a12()
    {
        var consultedAt = Ahora.AddMinutes(-1);
        var reply = new ConsultationReply
        {
            VehicleSnapshotJson = SnapshotJson,
            ConsultedAt = Timestamp.FromDateTimeOffset(consultedAt),
            Provider = "kyverum_runt",
            ConsultationKind = "VehicleVin",
        };

        var vehicle = IctGrpcConsultationClient.MapVehicle(reply);

        vehicle.Should().NotBeNull();
        vehicle!.SnapshotJson.Should().Be(SnapshotJson);
        vehicle.ConsultedAt.Should().Be(consultedAt);
        vehicle.Provider.Should().Be("kyverum_runt");
        vehicle.Kind.Should().Be("VehicleVin");
        IctGrpcConsultationClient.MapVehicle(new ConsultationReply()).Should().BeNull();
    }

    [Fact]
    public void Purga_VaciaElSnapshotDeTodasLasRespuestasDelMaster()
    {
        var sql = DbVehicleSnapshotReader.PurgeSql;

        sql.Should().Contain("UPDATE ict.external_integration_source_response");
        sql.Should().Contain("SET vehicle_snapshot = NULL");
        sql.Should().Contain("sq.eim_id = @master");
        DbVehicleSnapshotReader.LatestSql.Should().Contain("sr.vehicle_snapshot IS NOT NULL")
            .And.Contain("sq.is_data_queried = true")
            .And.Contain("ORDER BY sr.created_at DESC");
    }

    [Fact]
    public void Ddl26_AgregaLaColumnaConMarcaPii_DespuesDel25()
    {
        var ddl = EmbeddedDdl.LoadUp("26-ICT-source-response-vehicle-snapshot.sql");

        ddl.Should().Contain("ALTER TABLE ict.external_integration_source_response");
        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS vehicle_snapshot jsonb NULL");
        ddl.Should().Contain("COMMENT ON COLUMN ict.external_integration_source_response.vehicle_snapshot IS")
            .And.Contain("@pii");
        var orden = EmbeddedDdl.AllScriptsInOrder().ToList();
        orden.IndexOf("26-ICT-source-response-vehicle-snapshot.sql").Should()
            .BeGreaterThan(orden.IndexOf("25-ICT-master-transit-office-name.sql"));
    }
}
