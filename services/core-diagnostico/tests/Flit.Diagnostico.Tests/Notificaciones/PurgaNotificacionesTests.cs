using Flit.Diagnostico.Api.Modulos.Notificaciones;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Persistencia;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Diagnostico.Tests.Notificaciones;

/// <summary>
/// HU #14563 B8 (AC40; schema §3.5, ADR-0086 excepción 1 y 5) — <see cref="PurgaNotificaciones"/> con reloj simulado: borra en
/// bloques las de más de 90 días de ambas compañías, deja las de 89 días y no toca otras tablas.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~PurgaNotificacionesTests
/// </code>
/// </example>
public sealed class PurgaNotificacionesTests
{
    [Fact]
    public async Task Purga_en_bloques_las_de_91_dias_de_dos_companias_y_deja_las_de_89()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await EsquemaR8Tests.AbrirAsync(api, ct);
        var ahora = new DateTimeOffset(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var (empresaA, loteA) = await EsquemaR8Tests.SembrarLoteAsync(conexion, ct);
        var (empresaB, loteB) = await EsquemaR8Tests.SembrarLoteAsync(conexion, ct);
        foreach (var (empresa, lote) in new[] { (empresaA, loteA), (empresaB, loteB) })
        {
            for (var i = 0; i < 3; i++)
                await EsquemaR8Tests.InsertarAsync(conexion, empresa, lote, Notificacion.LoteTerminado, "{}", ct, creadaEn: ahora.AddDays(-91));
            await EsquemaR8Tests.InsertarAsync(conexion, empresa, lote, Notificacion.ZipEvidenciasListo, "{}", ct, creadaEn: ahora.AddDays(-89));
        }

        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        var purga = new PurgaNotificaciones(db, Options.Create(new OpcionesNotificaciones { TamanoLote = 2 }), new RelojFijo(ahora));

        var borradas = await purga.PurgarAsync(ct);

        borradas.Should().Be(6);
        var quedan = await EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT tipo FROM diagnostico.notificaciones WHERE diagnostico_id IN ('{loteA}', '{loteB}')", ct);
        quedan.Should().Equal(Notificacion.ZipEvidenciasListo, Notificacion.ZipEvidenciasListo);
        (await EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT id::text FROM diagnostico.diagnosticos WHERE id IN ('{loteA}', '{loteB}')", ct)).Should().HaveCount(2);
    }

    [Fact]
    public void El_SQL_de_la_purga_es_constante_parametrizado_y_solo_toca_notificaciones()
    {
        PurgaNotificaciones.SqlBorrado.Should().Contain("@corte").And.Contain("@lote").And.StartWith("DELETE FROM diagnostico.notificaciones")
            .And.NotContain("RETURNING");
        new OpcionesNotificaciones().RetencionDias.Should().Be(90);
        new OpcionesNotificaciones().IntervaloPurga.Should().Be(TimeSpan.FromHours(24));
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
