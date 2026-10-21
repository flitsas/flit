using System.Collections.Concurrent;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13733 (F03-H5), AC2 (F03-C9, F03-C4) — réplica de organismos desde <c>CatalogosService.ListarOrganismosTransito</c> de
/// core-api (doble <see cref="Catalogos.CatalogosFalso"/>, organismo ficticio) y derivación organismo → departamento por nombre
/// normalizado al completar la RUNT. Con el departamento sin fila soportada en <c>departamentos_impuesto</c> (o sin
/// departamento) el impuesto queda NA <c>DEPARTAMENTO_NO_SOPORTADO</c>, nunca en ERROR. Fuente RUNT simulada de Intempo
/// (escenario 1 = organismo «SECRETARIA DE TRANSITO DE PRUEBA», 6 = organismo que no está en la réplica). PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~OrganismosTests
/// </code>
/// </example>
public sealed class OrganismosTests
{
    /// <summary>Departamento ficticio del organismo de prueba (98 no es código DANE de ningún departamento).</summary>
    private const string DepartamentoPrueba = "98";

    // AC2 — el organismo del RUNT deriva el departamento; sin fila soportada, el impuesto queda NA DEPARTAMENTO_NO_SOPORTADO.
    [Fact]
    public async Task Organismo_del_RUNT_deriva_departamento()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await ArrancarAsync(orden);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        (await SincronizarAsync(api, ct))!.Nuevos.Should().Be(1);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], [Fuente.Runt, Fuente.ImpuestoDepartamental], null, null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, id, "TST001", ct);
        vehiculo.DepartamentoCodigo.Should().Be(DepartamentoPrueba, "el organismo del RUNT está en la réplica");
        (await MotorRunt.CeldasDelVehiculoAsync(api, vehiculo.Id, ct)).Should().BeEquivalentTo(["IMPUESTO_DEPARTAMENTAL NA -", "RUNT COMPLETADO -"]);
        (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single(c => c.Fuente == Fuente.ImpuestoDepartamental)
            .MotivoNa.Should().Be(MotivoNa.DepartamentoNoSoportado, "sin C3 la tabla departamentos_impuesto está vacía");
        orden.Should().NotContain(o => o.StartsWith("IMPUESTO_DEPARTAMENTAL", StringComparison.Ordinal));
    }

    // AC2 — con el departamento soportado, el impuesto se libera y se consulta.
    [Fact]
    public async Task Departamento_soportado_libera_el_impuesto()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await ArrancarAsync(orden);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await SincronizarAsync(api, ct);
        await Motor.EjecutarAsync(api,
            $"INSERT INTO diagnostico.departamentos_impuesto (departamento_codigo, soportado, actualizado_en) VALUES ('{DepartamentoPrueba}', true, now())", ct);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], [Fuente.Runt, Fuente.ImpuestoDepartamental], null, null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, id, "TST001", ct);
        (await MotorRunt.CeldasDelVehiculoAsync(api, vehiculo.Id, ct)).Should().BeEquivalentTo(["IMPUESTO_DEPARTAMENTAL COMPLETADO -", "RUNT COMPLETADO -"]);
        orden.Should().ContainSingle(o => o == $"IMPUESTO_DEPARTAMENTAL {vehiculo.Id}");
    }

    // AC2 — organismo que no está en la réplica: departamento nulo e impuesto NA DEPARTAMENTO_NO_SOPORTADO, sin ERROR.
    [Fact]
    public async Task Organismo_desconocido_deja_departamento_nulo_e_impuesto_NA()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await ArrancarAsync(orden);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await SincronizarAsync(api, ct);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST006")], [Fuente.Runt, Fuente.ImpuestoDepartamental], null, null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, id, "TST006", ct);
        vehiculo.OrganismoNombre.Should().Be("SECRETARIA DE TRANSITO DE PRUEBA SIN DEPARTAMENTO");
        vehiculo.DepartamentoCodigo.Should().BeNull();
        var impuesto = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single(c => c.Fuente == Fuente.ImpuestoDepartamental);
        impuesto.Estado.Should().Be(EstadoCelda.Na, "un departamento desconocido no es un ERROR");
        impuesto.MotivoNa.Should().Be(MotivoNa.DepartamentoNoSoportado);
        orden.Should().NotContain(o => o.StartsWith("IMPUESTO_DEPARTAMENTAL", StringComparison.Ordinal));
    }

    // Réplica: crea por código con el nombre normalizado, actualiza, retira lo que ya no viene y no aplica una lista vacía.
    [Fact]
    public async Task Sincronizacion_replica_el_catalogo_de_la_plataforma()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ArrancarAsync(new ConcurrentQueue<string>());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Catalogos.Organismos.Add(new OrganismoTransito { Id = Guid.NewGuid().ToString(), Codigo = "98002000", Nombre = "Tránsito Ficticio Dos", DepartamentoCodigo = "", Departamento = "", Activo = true });

        var primera = await SincronizarAsync(api, ct);
        primera.Should().Be(new ResultadoSincronizacion(2, 1, 0, 0, 1), "el organismo sin departamento no entra a la réplica");
        var replicado = await OrganismoAsync(api, "98001000", ct);
        replicado.NombreNorm.Should().Be("SECRETARIA DE TRANSITO DE PRUEBA");
        replicado.DepartamentoNombre.Should().Be("DEPARTAMENTO FICTICIO");

        api.Catalogos.Organismos.Clear();
        (await SincronizarAsync(api, ct)).Should().BeNull("una lista vacía no se aplica: sería borrar el catálogo");
        (await OrganismoAsync(api, "98001000", ct)).Should().NotBeNull();

        api.Catalogos.Organismos.Add(new OrganismoTransito { Id = Guid.NewGuid().ToString(), Codigo = "98003000", Nombre = "Tránsito Ficticio Tres", DepartamentoCodigo = "98", Departamento = "DEPARTAMENTO FICTICIO", Activo = true });
        (await SincronizarAsync(api, ct)).Should().Be(new ResultadoSincronizacion(1, 1, 0, 1, 0));
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        (await db.Set<Organismo>().AsNoTracking().Select(o => o.Codigo).ToListAsync(ct)).Should().Equal("98003000");
    }

    // Sin dirección del gRPC de catálogos el adaptador queda no conectado: la sincronización no toca la réplica.
    [Fact]
    public async Task Sin_catalogo_conectado_no_sincroniza()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.App.Services.GetRequiredService<ICatalogoOrganismosPlataforma>().Should().BeOfType<CatalogoOrganismosNoConectado>();
        (await SincronizarAsync(api, ct)).Should().BeNull();
        (await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.organismos", ct)).Should().Be(0);
    }

    /// <summary>API con el adaptador gRPC sobre el doble de catálogos (un organismo ficticio con departamento) y un espía del impuesto.</summary>
    private static async Task<ApiDePrueba> ArrancarAsync(ConcurrentQueue<string> orden)
    {
        var api = await MotorNa.ArrancarAsync(b =>
        {
            b.Services.AddScoped<ICatalogoOrganismosPlataforma, CatalogoOrganismosGrpc>();
            MotorNa.Espias(b, orden, Fuente.ImpuestoDepartamental);
        });
        api.Catalogos.Organismos.Add(new OrganismoTransito
        {
            Id = Guid.NewGuid().ToString(),
            Codigo = "98001000",
            Nombre = "Secretaría de Tránsito de Prueba",
            DepartamentoCodigo = DepartamentoPrueba,
            MunicipioCodigo = "98001",
            Municipio = "MUNICIPIO FICTICIO",
            Departamento = "DEPARTAMENTO FICTICIO",
            Activo = true,
        });
        return api;
    }

    private static async Task<ResultadoSincronizacion?> SincronizarAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SincronizacionOrganismos>().SincronizarAsync(ct);
    }

    private static async Task<Organismo> OrganismoAsync(ApiDePrueba api, string codigo, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        return await db.Set<Organismo>().AsNoTracking().SingleAsync(o => o.Codigo == codigo, ct);
    }
}
