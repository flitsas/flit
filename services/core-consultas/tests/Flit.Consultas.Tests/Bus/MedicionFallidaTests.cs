using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Grpc;
using Flit.Consultas.Api.Persistence;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// HU #13876 (ADR-0075, Q4) — guardar la medición de consumo nunca tumba la consulta, tampoco dentro de la transacción del
/// consumidor del bus: si Postgres rechaza la fila de <c>consultas.consumo</c>, el resultado del trabajo se publica igual (sin
/// consumo), la transacción del SDK sigue viva y lo que ya estaba en el <c>DbContext</c> antes de medir no se pierde. En el
/// camino gRPC en línea (sin transacción) la consulta responde y la medición siguiente del mismo scope se guarda.
/// La falla la provoca un trigger de la base efímera que rechaza las filas de <see cref="EmpresaSinMedicion"/>.
/// <para>Uso de ejemplo: <c>await bus.EjecutarAsync&lt;ConsumidorTrabajosLote&gt;(AnfitrionBus.Trabajo(MedicionFallidaTests.EmpresaSinMedicion, …))</c>.</para>
/// </summary>
public sealed class MedicionFallidaTests(MedicionFallidaTests.AnfitrionConMedicionRota bus) : IClassFixture<MedicionFallidaTests.AnfitrionConMedicionRota>
{
    /// <summary>Empresa cuya fila de consumo rechaza la base (datos de prueba).</summary>
    public static readonly Guid EmpresaSinMedicion = Guid.Parse("0199a000-0000-7000-8000-00000000d13e");

    [Fact]
    public async Task En_el_bus_la_medicion_que_falla_no_impide_publicar_el_resultado()
    {
        var sobre = AnfitrionBus.Trabajo(EmpresaSinMedicion, FuentesBus.Runt, RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = AnfitrionBus.PlacaEncontrada });

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        var datos = (await bus.ResultadosAsync(sobre)).Should().ContainSingle("el trabajo no va a reintento ni a la DLQ por la medición").Subject
            .DataAs<ResultadoConsultaDiagnostico>();
        datos.Estado.Should().Be(ResultadoConsultaDiagnostico.Ok);
        (await bus.ConsumosAsync(EmpresaSinMedicion)).Should().BeEmpty();
    }

    [Fact]
    public async Task En_una_transaccion_lo_encolado_antes_de_medir_se_conserva_y_se_confirma()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid previo = Guid.Empty;
        await using (var scope = bus.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
            var outbox = scope.ServiceProvider.GetRequiredService<IPlatformOutbox>();
            var medidor = scope.ServiceProvider.GetRequiredService<ConsumoRecorder>();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                previo = outbox.Enqueue("consultas.prueba.previo", 1, EmpresaSinMedicion, new { n = 1 }).EventId;
                var respuesta = await medidor.MedirAsync(
                    new PlatformServiceCaller("svc-diagnostico", EmpresaSinMedicion), "runt", () => Task.FromResult("ok"), _ => ("p", "con_datos", false), ct);
                respuesta.Should().Be("ok");
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }

        await using var lectura = bus.App.Services.CreateAsyncScope();
        (await lectura.ServiceProvider.GetRequiredService<ConsultasDb>().Set<OutboxMessage>().AsNoTracking().AnyAsync(m => m.Id == previo, ct))
            .Should().BeTrue("la medición no borra lo que el consumidor encoló antes");
        (await bus.ConsumosAsync(EmpresaSinMedicion)).Should().BeEmpty();
    }

    [Fact]
    public async Task En_linea_sin_transaccion_la_consulta_responde_y_la_siguiente_medicion_del_scope_se_guarda()
    {
        var ct = TestContext.Current.CancellationToken;
        var otra = Guid.NewGuid();
        await using (var scope = bus.App.Services.CreateAsyncScope())
        {
            var medidor = scope.ServiceProvider.GetRequiredService<ConsumoRecorder>();
            (await medidor.MedirAsync(new PlatformServiceCaller("svc-tramites", EmpresaSinMedicion), "runt", () => Task.FromResult(1), _ => ("p", "con_datos", false), ct))
                .Should().Be(1);
            (await medidor.MedirAsync(new PlatformServiceCaller("svc-tramites", otra), "runt", () => Task.FromResult(2), _ => ("p", "con_datos", false), ct))
                .Should().Be(2);
        }

        (await bus.ConsumosAsync(otra)).Should().ContainSingle().Which.Producto.Should().Be("tramites");
        (await bus.ConsumosAsync(EmpresaSinMedicion)).Should().BeEmpty();
    }

    /// <summary><see cref="AnfitrionBus"/> con el trigger que rechaza la medición de <see cref="EmpresaSinMedicion"/>.</summary>
    public sealed class AnfitrionConMedicionRota : AnfitrionBus
    {
        public override async ValueTask InitializeAsync()
        {
            await base.InitializeAsync();
            await using var scope = App.Services.CreateAsyncScope();
            var conexion = (NpgsqlConnection)scope.ServiceProvider.GetRequiredService<ConsultasDb>().Database.GetDbConnection();
            await conexion.OpenAsync();
            await using var cmd = new NpgsqlCommand($"""
                CREATE FUNCTION consultas.rechazar_medicion() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'medición rechazada en la prueba'; END $$;
                CREATE TRIGGER rechazar_medicion BEFORE INSERT ON consultas.consumo
                    FOR EACH ROW WHEN (NEW.tenant_id = '{EmpresaSinMedicion}') EXECUTE FUNCTION consultas.rechazar_medicion();
                """, conexion);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
