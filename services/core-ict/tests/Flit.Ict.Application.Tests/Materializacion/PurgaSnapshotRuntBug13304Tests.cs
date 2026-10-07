using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Domain.Validation;
using Flit.Ict.Infrastructure.Jobs;
using Flit.Ict.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Materializacion;

/// <summary>
/// Bug #13304 (H-1, L-1, re-consulta) — retención de <c>vehicle_snapshot</c> (@pii:high): se purga en todos los caminos
/// que dejan el master fuera del envío (ps=5, ps=4 del envío y del orquestador, ps=6), siempre DESPUÉS de
/// los registros del estado y sin romper el flujo; un barrido de <see cref="RetentionJob"/> vacía lo que
/// tenga más de 2× la vigencia. Una consulta con fecha en el futuro (&gt; 5 min) cuenta como vencida.
/// <para>Uso de ejemplo:</para>
/// <code>
/// await SendToCoreApiJob.ResolverEnvioAsync(master, result, novedadRunt,
///     registrarBorrador, persistirNovedad, snapshots, logger, ct);
/// // ps=5 o ps=4 ⇒ registros primero, luego snapshots.PurgeAsync(master.Id).
/// </code>
/// </summary>
public sealed class PurgaSnapshotRuntBug13304Tests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);

    private const string PlacaPii = "ABC123";

    private static ExternalIntegrationMaster Master() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        TransactionType = 3,
        Plate = PlacaPii,
        ProcessStatusId = 2,
        ExternalCommentsValidation = string.Empty,
    };

    /// <summary>Lector que anota la purga en la bitácora compartida (para verificar el orden).</summary>
    private static IIctVehicleSnapshotReader Snapshots(List<string> bitacora, Exception? falla = null)
    {
        var reader = Substitute.For<IIctVehicleSnapshotReader>();
        reader.PurgeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            bitacora.Add("purga");
            return falla is null ? Task.FromResult(1) : Task.FromException<int>(falla);
        });
        return reader;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- 1a — envío (SendToCoreApiJob)

    [Fact]
    public async Task ResolverEnvio_BorradorCreado_PurgaDespuesDeLosRegistrosPs5()
    {
        var master = Master();
        var bitacora = new List<string>();
        var snapshots = Snapshots(bitacora);
        var instanceId = Guid.NewGuid();

        await SendToCoreApiJob.ResolverEnvioAsync(
            master, new CreateDraftResult(instanceId, "REF", "borrador", null), novedadRunt: null, reencolado: false,
            (_, _, _) => { bitacora.Add("ps5"); return Task.CompletedTask; },
            (_, _) => { bitacora.Add("ps4"); return Task.CompletedTask; },
            snapshots, new LogCaptura(), Ct);

        bitacora.Should().Equal("ps5", "purga");
        master.ProcessStatusId.Should().Be(5);
        master.ProcedureInstanceId.Should().Be(instanceId);
        await snapshots.Received(1).PurgeAsync(master.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolverEnvio_SinConsultaRuntTrasReconsulta_PersistePs4YPurga()
    {
        var master = Master();
        SendToCoreApiJob.AplicarNovedad(master, SendToCoreApiJob.NovedadSinConsultaRunt);
        var bitacora = new List<string>();
        var snapshots = Snapshots(bitacora);

        await SendToCoreApiJob.ResolverEnvioAsync(
            master, sent: null, SendToCoreApiJob.NovedadSinConsultaRunt, reencolado: false,
            (_, _, _) => { bitacora.Add("ps5"); return Task.CompletedTask; },
            (msg, _) => { bitacora.Add("ps4:" + msg); return Task.CompletedTask; },
            snapshots, new LogCaptura(), Ct);

        bitacora.Should().Equal("ps4:" + SendToCoreApiJob.NovedadSinConsultaRunt, "purga");
        master.ProcessStatusId.Should().Be(4);
        await snapshots.Received(1).PurgeAsync(master.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolverEnvio_ErrorDeCoreApiSinId_PersistePs4YPurga()
    {
        var master = Master();
        var bitacora = new List<string>();
        var snapshots = Snapshots(bitacora);

        await SendToCoreApiJob.ResolverEnvioAsync(
            master, new CreateDraftResult(null, null, null, "procedure_type_not_found"), novedadRunt: null, reencolado: false,
            (_, _, _) => { bitacora.Add("ps5"); return Task.CompletedTask; },
            (msg, _) => { bitacora.Add("ps4:" + msg); return Task.CompletedTask; },
            snapshots, new LogCaptura(), Ct);

        bitacora.Should().Equal("ps4:procedure_type_not_found", "purga");
        master.ProcessStatusId.Should().Be(4);
        master.ExternalCommentsValidation.Should().Contain("procedure_type_not_found");
    }

    [Fact]
    public async Task ResolverEnvio_Reencolado_NoCambiaEstadoNiPurga()
    {
        var master = Master();
        var bitacora = new List<string>();
        var snapshots = Snapshots(bitacora);

        await SendToCoreApiJob.ResolverEnvioAsync(
            master, sent: null, novedadRunt: null, reencolado: true,
            (_, _, _) => { bitacora.Add("ps5"); return Task.CompletedTask; },
            (_, _) => { bitacora.Add("ps4"); return Task.CompletedTask; },
            snapshots, new LogCaptura(), Ct);

        bitacora.Should().BeEmpty("la re-consulta pendiente lo saca de la elegibilidad; el siguiente ciclo envía");
        master.ProcessStatusId.Should().Be(2);
    }

    [Fact]
    public async Task ResolverEnvio_GrpcNoDisponible_NoCambiaEstadoNiPurga()
    {
        var master = Master();
        var bitacora = new List<string>();
        var snapshots = Snapshots(bitacora);

        await SendToCoreApiJob.ResolverEnvioAsync(
            master, new CreateDraftResult(null, null, null, "grpc_unavailable"), novedadRunt: null, reencolado: false,
            (_, _, _) => { bitacora.Add("ps5"); return Task.CompletedTask; },
            (_, _) => { bitacora.Add("ps4"); return Task.CompletedTask; },
            snapshots, new LogCaptura(), Ct);

        bitacora.Should().BeEmpty("se reintenta el siguiente ciclo con el snapshot vigente");
        master.ProcessStatusId.Should().Be(2);
    }

    [Fact]
    public async Task ResolverEnvio_PurgaFallida_NoRompeElFlujoYLogueaSinPii()
    {
        var master = Master();
        var bitacora = new List<string>();
        var snapshots = Snapshots(bitacora, new InvalidOperationException("detalle con placa " + PlacaPii));
        var log = new LogCaptura();

        var act = () => SendToCoreApiJob.ResolverEnvioAsync(
            master, new CreateDraftResult(Guid.NewGuid(), null, null, null), novedadRunt: null, reencolado: false,
            (_, _, _) => { bitacora.Add("ps5"); return Task.CompletedTask; },
            (_, _) => Task.CompletedTask,
            snapshots, log, Ct);

        await act.Should().NotThrowAsync();
        bitacora.Should().Equal("ps5", "purga");
        master.ProcessStatusId.Should().Be(5);
        log.Entradas.Should().ContainSingle(e => e.Level == LogLevel.Warning);
        log.Entradas.Should().OnlyContain(e => !e.Mensaje.Contains(PlacaPii) && e.Excepcion == null,
            "el mensaje de la excepción puede traer PII: solo se registra su tipo");
        log.Entradas.Single().Mensaje.Should().Contain(VehicleSnapshotPurge.CaminoBorrador)
            .And.Contain(nameof(InvalidOperationException));
    }

    // ---------------------------------------------------------------- 1a — helper y orquestador / anulación

    [Fact]
    public async Task DespuesDe_RegistrosFallan_NoPurgaYPropagaElError()
    {
        var bitacora = new List<string>();
        var snapshots = Snapshots(bitacora);
        var masterId = Guid.NewGuid();

        var act = () => VehicleSnapshotPurge.DespuesDeAsync(
            _ => throw new InvalidOperationException("histórico caído"),
            c => snapshots.PurgeAsync(masterId, c), masterId, VehicleSnapshotPurge.CaminoAnulado,
            new LogCaptura(), Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        bitacora.Should().BeEmpty("sin estado registrado no se purga: lo recoge el barrido por antigüedad");
    }

    [Fact]
    public async Task Orquestador_NovedadBloqueante_PurgaDespuesDeMarcarPs4()
    {
        var bitacora = new List<string>();
        var snapshots = Snapshots(bitacora);
        var masterId = Guid.NewGuid();

        await OrchestratorJob.FlagNoveltyAndPurgeAsync(
            _ => { bitacora.Add("ps4"); return Task.CompletedTask; },
            snapshots, masterId, new LogCaptura(), Ct);

        bitacora.Should().Equal("ps4", "purga");
        await snapshots.Received(1).PurgeAsync(masterId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Orquestador_PurgaFallida_NoRompeElFlujo()
    {
        var bitacora = new List<string>();
        var snapshots = Snapshots(bitacora, new NpgsqlException("conexión perdida"));
        var log = new LogCaptura();

        var act = () => OrchestratorJob.FlagNoveltyAndPurgeAsync(
            _ => { bitacora.Add("ps4"); return Task.CompletedTask; },
            snapshots, Guid.NewGuid(), log, Ct);

        await act.Should().NotThrowAsync();
        log.Entradas.Should().ContainSingle(e => e.Mensaje.Contains(VehicleSnapshotPurge.CaminoNovedadOrquestador));
    }

    [Fact]
    public async Task BestEffort_Cancelado_PropagaLaCancelacion()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => VehicleSnapshotPurge.BestEffortAsync(
            c => Task.FromCanceled<int>(c), Guid.NewGuid(), VehicleSnapshotPurge.CaminoAnulado, new LogCaptura(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------------------------------------------------------------- 1b — barrido de RetentionJob

    [Fact]
    public void Retencion_BarridoDelSnapshot_PorLotesYPorAntiguedad()
    {
        var sql = RetentionJob.VehicleSnapshotSweepSql;

        sql.Should().Contain("UPDATE ict.external_integration_source_response")
            .And.Contain("SET vehicle_snapshot = NULL")
            .And.Contain("WHERE ctid IN (")
            .And.Contain("SELECT ctid FROM ict.external_integration_source_response")
            .And.Contain("vehicle_snapshot IS NOT NULL")
            .And.Contain("created_at < now() - make_interval(hours => @hours)")
            .And.Contain("LIMIT @batch");
        sql.Should().NotContain("DELETE", "se vacía la columna; la respuesta queda para la trazabilidad");
    }

    [Theory]
    [InlineData(24, 48)]
    [InlineData(36, 72)]
    [InlineData(0, 48)]
    [InlineData(-3, 48)]
    public void Retencion_BarridoDelSnapshot_ParametrosDobleDeLaVigencia(int vigenciaHoras, int horasEsperadas)
    {
        var options = new IctJobOptions { VehicleConsultationMaxAgeHours = vigenciaHoras, RetentionBatchSize = 1234 };

        RetentionJob.VehicleSnapshotRetentionHours(options).Should().Be(horasEsperadas);
        var parametros = RetentionJob.VehicleSnapshotSweepParameters(options).Cast<NpgsqlParameter>().ToList();
        parametros.Should().HaveCount(2);
        parametros.Single(p => p.ParameterName == "hours").Value.Should().Be(horasEsperadas);
        parametros.Single(p => p.ParameterName == "batch").Value.Should().Be(1234);
    }

    // ---------------------------------------------------------------- 1c — DDL 26

    [Fact]
    public void Ddl26_ComentarioDeclaraLaRetencionReal()
    {
        var ddl = EmbeddedDdl.LoadUp("26-ICT-source-response-vehicle-snapshot.sql");

        ddl.Should().Contain("@pii:high")
            .And.Contain("se vacía al materializar el borrador, al anular o al marcar novedad")
            .And.Contain("barrido de retención vacía las de más de 2 veces la vigencia");
    }

    [Fact]
    public void Ddl26_IndiceParcialDelBarridoDeRetencion_Idempotente()
    {
        var ddl = EmbeddedDdl.LoadUp("26-ICT-source-response-vehicle-snapshot.sql");

        ddl.Should().Contain("CREATE INDEX IF NOT EXISTS ix_eisr_vehicle_snapshot_created")
            .And.Contain("ON ict.external_integration_source_response (created_at)")
            .And.Contain("WHERE vehicle_snapshot IS NOT NULL;");
        RetentionJob.VehicleSnapshotSweepSql.Should().Contain("WHERE vehicle_snapshot IS NOT NULL")
            .And.Contain("AND created_at < now()", "el predicado del barrido es el que soporta el índice parcial");
    }

    // ---------------------------------------------------------------- 1d — advertencias sin duplicar

    /// <summary>
    /// Una consulta re-encolada vuelve a producir las mismas advertencias: solo se agregan las que no estén ya en
    /// external_comments_validation y el evento «advertencia» sale únicamente si se agregó alguna.
    /// </summary>
    [Fact]
    public void Advertencia_ReEncolada_NoSeDuplicaNiRepiteElEvento()
    {
        var sql = OrchestratorJob.RecordWarningSql;

        sql.Should().Contain("unnest(@warnings::text[]) WITH ORDINALITY")
            .And.Contain("position(u.w IN m.external_comments_validation) = 0", "una advertencia ya contenida no se agrega")
            .And.Contain("WHERE m.id = @id AND n.txt IS NOT NULL", "sin advertencias nuevas no hay UPDATE")
            .And.Contain("RETURNING m.id, m.tenant_id, n.txt")
            .And.Contain("'advertencia'")
            .And.Contain("FROM upd", "el evento solo se registra por la fila efectivamente actualizada");
        sql.Should().NotContain("|| @msg", "ya no se concatena a ciegas");
    }

    // ---------------------------------------------------------------- 2 — L-1 consulted_at en el futuro

    [Theory]
    [InlineData(6, true)]
    [InlineData(60 * 24, true)]
    [InlineData(5, false)]
    [InlineData(1, false)]
    public void Vigencia_ConsultaConFechaFutura_MasDe5Minutos_CuentaComoVencida(int minutosEnElFuturo, bool vencida)
    {
        var snapshot = new VehicleConsultationSnapshot(
            "{}", Ahora.AddMinutes(minutosEnElFuturo), "kyverum_runt", VehicleConsultationSnapshot.KindPlate, PlacaPii, string.Empty);

        SendToCoreApiJob.ConsultaRuntVigente(new VehicleSnapshotLookup(true, snapshot), Ahora, 24)
            .Should().Be(!vencida, "una fecha futura de más de 5 min es vencida; hasta 5 min de desfase se tolera");
    }

    /// <summary>Logger de prueba que captura nivel, mensaje formateado y excepción.</summary>
    private sealed class LogCaptura : ILogger
    {
        public List<(LogLevel Level, string Mensaje, Exception? Excepcion)> Entradas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entradas.Add((logLevel, formatter(state, exception), exception));
    }
}
