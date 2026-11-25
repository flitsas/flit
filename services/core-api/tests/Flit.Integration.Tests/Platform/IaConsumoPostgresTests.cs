using Flit.Integration.Tests.Postgres;
using Flit.Platform.Sdk.IaConsumo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// ADR-0090 — el almacén del SDK escribe en <c>admin.ia_consumo_log</c>/<c>ia_consumo_detalle</c> tal como las crea el DDL
/// 202, lee la semilla de tarifas y purga el detalle viejo (el borrado en cascada sigue a la fila).
/// </summary>
public sealed class IaConsumoPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static IaConsumoPendiente Pendiente(DateTimeOffset inicio, bool conDetalle)
    {
        var fila = new IaConsumoLog
        {
            Id = Guid.CreateVersion7(),
            CorrelacionId = Guid.CreateVersion7(),
            FechaInicio = inicio,
            FechaFin = inicio.AddSeconds(2),
            LatenciaMs = 2000,
            Aplicativo = "flit",
            Servicio = "core-api",
            Ambiente = "local",
            Origen = IaOrigenes.Usuario,
            Modulo = IaProcesosFlit.ModuloTramites,
            Proceso = IaProcesosFlit.OcrDocumento,
            TipoTarea = IaTiposTarea.Ocr,
            Placa = "ABC123",
            Proveedor = IaProveedores.Anthropic,
            ModeloSolicitado = "claude-haiku-4-5",
            HttpStatus = 200,
            Resultado = IaResultados.Exito,
            TokensEntrada = 1000,
            TokensSalida = 50,
            CostoUsdEstimado = 0.00125m,
            Metadatos = """{"origen":"prueba"}""",
        };
        return new IaConsumoPendiente(fila, conDetalle
            ? new IaConsumoDetalle { ConsumoId = fila.Id, FechaInicio = inicio, PromptTexto = "p", RespuestaTexto = "r" }
            : null);
    }

    [PostgresFact]
    public async Task GuardaElLoteConSuDetalleYLeeLasTarifasSembradas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = NewContext())
        {
            var almacen = new EfIaConsumoAlmacen<Flit.Infrastructure.Persistence.FlitDbContext>(db);
            await almacen.GuardarAsync(
                [Pendiente(DateTimeOffset.UtcNow.AddDays(-40), conDetalle: true), Pendiente(DateTimeOffset.UtcNow, conDetalle: true)], ct);

            var tarifas = await almacen.TarifasAsync(ct);
            tarifas.Should().Contain(t => t.Proveedor == "anthropic" && t.Modelo == "claude-haiku-4-5" && t.PrecioEntradaMtok == 1m);
        }

        await using var lectura = NewContext();
        (await lectura.Set<IaConsumoLog>().CountAsync(ct)).Should().Be(2);
        (await lectura.Set<IaConsumoDetalle>().CountAsync(ct)).Should().Be(2, "el detalle no se purga, ni el de hace 40 días");
        var fila = await lectura.Set<IaConsumoLog>().OrderBy(f => f.FechaInicio).FirstAsync(ct);
        fila.Intento.Should().Be((short)1, "el DEFAULT del contrato");
        fila.CostoUsdEstimado.Should().Be(0.00125m);
    }
}
