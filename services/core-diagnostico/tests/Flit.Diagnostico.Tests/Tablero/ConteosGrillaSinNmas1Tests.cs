using System.Collections;
using System.Collections.Concurrent;
using System.Data.Common;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Derivacion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #14563 (B1, AC3) — la grilla con veredicto, conteos y elegibilidad no hace N+1: un interceptor de EF cuenta los comandos
/// SQL de la petición que tocan el lote (por su id o por los de sus vehículos) y el número es el mismo con 1 y con 50 vehículos
/// con dictamen. Ninguna lectura de <c>dictamenes</c> es por vehículo. PostgreSQL real; placas TST###.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ConteosGrillaSinNmas1Tests
/// </code>
/// </example>
public sealed class ConteosGrillaSinNmas1Tests
{
    [Fact]
    public async Task HU14563_AC3_Mismo_numero_de_comandos_con_1_y_con_50_vehiculos()
    {
        var contador = new ContadorComandos();
        // Con la derivación encendida: la grilla también arma la columna «Crear trámite» con la misma elegibilidad.
        await using var api = await DerivacionDePrueba.ApiAsync(new DerivacionTramitesFalso(), ademas: b =>
            b.Services.ConfigureDbContext<DiagnosticoDb>(o => o.AddInterceptors(contador)));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var uno = await SembrarAsync(empresa, 1, ct);
        var cincuenta = await SembrarAsync(empresa, 50, ct);

        var conUno = await ContarAsync(empresa, contador, uno, ct);
        var conCincuenta = await ContarAsync(empresa, contador, cincuenta, ct);

        conUno.Total.Should().BeGreaterThan(0, "el interceptor ve la petición");
        conCincuenta.Total.Should().Be(conUno.Total, "el número de comandos no depende del número de vehículos");
        conCincuenta.Dictamenes.Should().Be(conUno.Dictamenes).And.BeLessThanOrEqualTo(1, "el veredicto vigente se lee una vez, por conjunto, para los conteos y la derivación");
    }

    private static async Task<(int Total, int Dictamenes)> ContarAsync(EmpresaDePrueba empresa, ContadorComandos contador, LoteSembrado lote, CancellationToken ct)
    {
        contador.Empezar([lote.Id, .. lote.Vehiculos.Select(v => v.Id)]);
        var respuesta = await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id, desde: 0), ct);
        respuesta.EnsureSuccessStatusCode();
        var comandos = contador.Terminar();
        return (comandos.Count, comandos.Count(c => c.Contains("dictamenes", StringComparison.Ordinal)));
    }

    private static async Task<LoteSembrado> SembrarAsync(EmpresaDePrueba empresa, int vehiculos, CancellationToken ct)
    {
        var lote = await Tablero.SembrarAsync(empresa, ct, version: 3, estado: EstadoDiagnostico.Completado, modo: ModoDiagnostico.ConAnalisis,
            tipoTramiteCodigo: "TRASPASO_STANDARD",
            vehiculos: [.. Enumerable.Range(1, vehiculos).Select(i => new VehiculoSembrado($"TST{i:000}", [new(Fuente.Runt, EstadoCelda.Completado, 1)]))]);
        await using var db = empresa.Db();
        foreach (var (vehiculo, i) in lote.Vehiculos.Select((v, i) => (v, i)))
        {
            db.Add(new DictamenVehiculo
            {
                VehiculoId = vehiculo.Id,
                Version = 1,
                Vigente = true,
                Veredicto = i % 2 == 0 ? "VIABLE" : "BLOQUEADO",
                Alertamientos = "[]",
                CatalogoVersion = 1,
                RedactadoPor = "PLANTILLA",
                EstadoRedaccion = "PENDIENTE",
                CreadoEn = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
        return lote;
    }

    /// <summary>
    /// Cuenta los comandos cuyos parámetros nombran el lote o alguno de sus vehículos (los trabajos en segundo plano de la API de
    /// prueba no los tocan, así que no meten ruido).
    /// </summary>
    private sealed class ContadorComandos : DbCommandInterceptor
    {
        private ConcurrentQueue<string>? _comandos;
        private HashSet<Guid> _ids = [];

        public void Empezar(IEnumerable<Guid> ids)
        {
            _ids = [.. ids];
            _comandos = new ConcurrentQueue<string>();
        }

        public List<string> Terminar()
        {
            var comandos = _comandos?.ToList() ?? [];
            _comandos = null;
            return comandos;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Registrar(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Registrar(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Registrar(DbCommand command)
        {
            if (_comandos is not { } comandos)
                return;
            if (command.Parameters.Cast<DbParameter>().Any(p => Nombra(p.Value)))
                comandos.Enqueue(command.CommandText);
        }

        private bool Nombra(object? valor) => valor switch
        {
            Guid id => _ids.Contains(id),
            IEnumerable lista and not string => lista.OfType<Guid>().Any(_ids.Contains),
            _ => false,
        };
    }
}
