using System.Net;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Concurrencia;
using FluentAssertions;
using Npgsql;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;

namespace Flit.Impuestos.Tests.Liquidaciones;

/// <summary>
/// HU #14694 (revisión previa al PR: DB, orden de bloqueos) — quien libera crédito toma el candado global del crédito ANTES de
/// bloquear la cabecera de un lote (orden: candado del crédito → cabecera → ítems). El cierre manual que espera el candado no
/// retiene todavía su cabecera, así que una liberación que tiene el candado y quiere esa cabecera no forma un ciclo (40P01).
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~OrdenDeBloqueosTests
/// </code>
/// </example>
public sealed class OrdenDeBloqueosTests
{
    private static readonly string[] Admin = [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear, Permisos.LiquidacionesCerrar];

    [Fact]
    public async Task El_cierre_toma_el_candado_del_credito_antes_que_la_cabecera()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, _) = await EvaluadorTerminacionTests.SembrarMasivaAsync(api, ct, EstadoItem.Enviado, EstadoItem.Pendiente);
        var (conexion, candado) = await Bloqueos.RetenerAsync(api.BaseDeDatos, Bloqueos.SqlCandadoCredito, ct);
        await using var _ = conexion;

        var cierre = api.Cliente(api.Token(empresa: empresa, permisos: Admin)).PostAsync($"{M.Liquidaciones}/{lote}/cerrar", null, ct);
        (await Bloqueos.EsperarSesionesBloqueadasAsync(api.BaseDeDatos, 1, TimeSpan.FromSeconds(15), ct)).Should().BeTrue("el cierre espera el candado");

        string? error = null;
        await using (var otra = new NpgsqlConnection(api.BaseDeDatos))
        {
            await otra.OpenAsync(ct);
            await using var tx = await otra.BeginTransactionAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT 1 FROM impuestos.liquidaciones WHERE id = $1 FOR UPDATE NOWAIT", otra, tx);
            cmd.Parameters.Add(new NpgsqlParameter { Value = lote });
            try
            {
                await cmd.ExecuteScalarAsync(ct);
            }
            catch (PostgresException ex)
            {
                error = ex.SqlState;
            }

            await tx.RollbackAsync(ct);
        }

        await candado.CommitAsync(ct);
        using var r = await cierre.WaitAsync(TimeSpan.FromSeconds(30), ct);

        error.Should().BeNull("la cabecera no está bloqueada mientras el cierre espera el candado (55P03 = la retenía)");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        (await M.ItemsAsync(api, empresa, lote, ct)).Should().OnlyContain(i => i.Estado == EstadoItem.SinResultadoLoteCerrado);
    }
}
