using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #14563 (B1, ADR-0086 D-1) — versión de fila del tablero: <see cref="EscritorCeldas.SubirVersionFilaAsync"/> sube la
/// cabecera y deja <c>vehiculos.version_tablero</c> con ese valor en la misma transacción, con el vehículo seguido o no, sin
/// chocar con el <c>xmin</c>, y se serializa por la cabecera. PostgreSQL real; placas TST###.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~VersionFilaTableroTests
/// </code>
/// </example>
public sealed class VersionFilaTableroTests
{
    // AC5/AC8 — el vehículo no seguido queda con la versión de la cabecera; invariante fila <= lote.
    [Fact]
    public async Task HU14563_AC5_Subir_version_fila_sube_cabecera_y_vehiculo_no_seguido()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);

        long v;
        await using (var db = empresa.Db())
        {
            v = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var nueva = await EscritorCeldas.SubirVersionFilaAsync(db, lote.Id, lote.Vehiculos[0].Id, ct);
                await tx.CommitAsync(ct);
                return nueva;
            });
        }

        v.Should().Be(8);
        var (lote1, filas) = await LeerAsync(empresa, lote.Id, ct);
        lote1.Should().Be(8);
        filas[lote.Vehiculos[0].Id].Should().Be(8);
        filas[lote.Vehiculos[1].Id].Should().Be(0, "la otra fila no cambió");
        filas.Values.Should().OnlyContain(f => f <= lote1);
    }

    // AC9 — con el vehículo seguido, el SaveChanges posterior no lanza DbUpdateConcurrencyException.
    [Fact]
    public async Task HU14563_AC9_Vehiculo_seguido_guarda_sin_conflicto_de_xmin()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);

        await using (var db = empresa.Db())
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var vehiculo = await db.Set<VehiculoDiagnostico>().SingleAsync(x => x.Id == lote.Vehiculos[1].Id, ct);
            var v = await EscritorCeldas.SubirVersionFilaAsync(db, lote.Id, vehiculo.Id, ct);
            vehiculo.VersionTablero.Should().Be(v);
            vehiculo.TramiteId = Guid.NewGuid();
            var guardar = () => db.SaveChangesAsync(ct);
            await guardar.Should().NotThrowAsync();
            await tx.CommitAsync(ct);
        });

        var (version, filas) = await LeerAsync(empresa, lote.Id, ct);
        filas[lote.Vehiculos[1].Id].Should().Be(version).And.Be(8);
    }

    // AC7 — MarcarFila reutiliza la versión de la celda: no sube la cabecera otra vez.
    [Fact]
    public void HU14563_AC7_Marcar_fila_asigna_la_version_dada()
    {
        var vehiculo = new VehiculoDiagnostico { IdentificadorOriginal = "TST301", IdentificadorNorm = "TST301" };

        EscritorCeldas.MarcarFila(vehiculo, 12);

        vehiculo.VersionTablero.Should().Be(12);
        var bajar = () => EscritorCeldas.MarcarFila(vehiculo, 11);
        bajar.Should().Throw<InvalidOperationException>("la versión de fila es monótona");
    }

    // AC9 — dos escritores sobre el mismo lote se serializan por la cabecera: versiones distintas y sin interbloqueo.
    [Fact]
    public async Task HU14563_AC9_Escritores_concurrentes_se_serializan_por_la_cabecera()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);

        async Task<long> EscribirAsync(Guid vehiculoId)
        {
            await using var db = empresa.Db();
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var v = await EscritorCeldas.SubirVersionFilaAsync(db, lote.Id, vehiculoId, ct);
                await Task.Delay(50, ct);
                await tx.CommitAsync(ct);
                return v;
            });
        }

        var versiones = await Task.WhenAll(EscribirAsync(lote.Vehiculos[0].Id), EscribirAsync(lote.Vehiculos[1].Id));

        versiones.Should().BeEquivalentTo([8L, 9L]);
        var (version, filas) = await LeerAsync(empresa, lote.Id, ct);
        version.Should().Be(9);
        filas.Values.Should().OnlyContain(f => f <= version);
    }

    private static Task<LoteSembrado> SembrarAsync(EmpresaDePrueba empresa, CancellationToken ct) =>
        Tablero.SembrarAsync(empresa, ct, version: 7, vehiculos:
        [
            new("TST301", [new(Fuente.Runt, EstadoCelda.Completado, 1)]),
            new("TST302", [new(Fuente.Runt, EstadoCelda.Completado, 2)]),
        ]);

    private static async Task<(long Lote, Dictionary<Guid, long> Filas)> LeerAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        var version = await db.Set<CabeceraDiagnostico>().Where(c => c.Id == lote).Select(c => c.VersionTablero).SingleAsync(ct);
        var filas = await db.Set<VehiculoDiagnostico>().Where(v => v.DiagnosticoId == lote).ToDictionaryAsync(v => v.Id, v => v.VersionTablero, ct);
        return (version, filas);
    }
}
