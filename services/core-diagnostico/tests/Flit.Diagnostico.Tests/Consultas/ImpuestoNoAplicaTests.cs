using System.Collections.Concurrent;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Catalogos;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13861 (F09-H2), AC4, AC5 y AC6 (CA 10 y 11; F09-C3, F09-C4) — con la réplica de departamentos con impuesto poblada por
/// la sincronización (capacidad falsa que responde <c>05</c>), al completar la RUNT la celda del impuesto queda en N/A con su
/// motivo y nunca en ERROR: primero el servicio público (<c>SERVICIO_PUBLICO</c>), después el departamento nulo o no soportado
/// (<c>DEPARTAMENTO_NO_SOPORTADO</c>). Una N/A no llama al adaptador ni deja intento. Fuente RUNT simulada de Intempo (escenario 1
/// = particular, 5 = servicio público, 6 = organismo fuera de la réplica de organismos), organismo ficticio. Mientras la fuente
/// no esté disponible por API (F09-H4), los lotes se crean con la bandera del impuesto encendida en la prueba (<see cref="MotorNa"/>).
/// PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ImpuestoNoAplicaTests
/// </code>
/// </example>
public sealed class ImpuestoNoAplicaTests
{
    /// <summary>Departamento soportado por la capacidad falsa (Antioquia, código DIVIPOLA público).</summary>
    private const string Soportado = "05";

    // AC4 (CA 11) — particular cuyo organismo deriva el departamento 11 (no soportado): N/A DEPARTAMENTO_NO_SOPORTADO, sin intento.
    [Fact]
    public async Task Departamento_no_soportado_queda_NA_sin_intento_ni_consumo()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await ArrancarAsync(orden, "11", ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], [Fuente.Runt, Fuente.ImpuestoDepartamental], null, null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, id, "TST001", ct);
        vehiculo.DepartamentoCodigo.Should().Be("11");
        await DebeQuedarNaAsync(api, empresa, id, vehiculo.Id, MotivoNa.DepartamentoNoSoportado, orden, ct);
    }

    // AC5 (CA 11, negativo) — organismo que no está en la réplica de organismos: departamento nulo, N/A y el vehículo sin error.
    [Fact]
    public async Task Organismo_sin_departamento_derivado_queda_NA_y_no_ERROR()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await ArrancarAsync(orden, Soportado, ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST006")], [Fuente.Runt, Fuente.ImpuestoDepartamental], null, null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, id, "TST006", ct);
        vehiculo.DepartamentoCodigo.Should().BeNull("el organismo del RUNT no está en la réplica");
        await DebeQuedarNaAsync(api, empresa, id, vehiculo.Id, MotivoNa.DepartamentoNoSoportado, orden, ct);
        (await MotorRunt.CeldasDelVehiculoAsync(api, vehiculo.Id, ct)).Should().NotContain(c => c.Contains(" ERROR ", StringComparison.Ordinal));
    }

    // AC6 (CA 10) — servicio público del departamento 05, que sí está soportado: N/A SERVICIO_PUBLICO (va primero), sin intento.
    [Fact]
    public async Task Servicio_publico_queda_NA_aunque_el_departamento_este_soportado()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await ArrancarAsync(orden, Soportado, ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST005")], [Fuente.Runt, Fuente.ImpuestoDepartamental], null, null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, id, "TST005", ct);
        vehiculo.Servicio.Should().Be("PUBLICO");
        vehiculo.DepartamentoCodigo.Should().Be(Soportado, "el departamento sí está soportado: manda el servicio público");
        await DebeQuedarNaAsync(api, empresa, id, vehiculo.Id, MotivoNa.ServicioPublico, orden, ct);
    }

    // Contraste — particular de un departamento soportado: al completar la RUNT la celda del impuesto se libera en PENDIENTE.
    [Fact]
    public async Task Departamento_soportado_y_servicio_particular_libera_la_celda_en_PENDIENTE()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await ArrancarAsync(orden, Soportado, ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], [Fuente.Runt, Fuente.ImpuestoDepartamental], null, null, ct);

        (await Motor.Procesador(api).ProcesarLoteAsync(ct)).Should().Be(1, "en la primera vuelta solo la RUNT es tomable");

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, id, "TST001", ct);
        vehiculo.DepartamentoCodigo.Should().Be(Soportado);
        var impuesto = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single(c => c.Fuente == Fuente.ImpuestoDepartamental);
        impuesto.Estado.Should().Be(EstadoCelda.Pendiente);
        impuesto.MotivoNa.Should().BeNull();
        impuesto.DisponibleDesde.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(1), "la RUNT completada la liberó (antes estaba diferida)");
        orden.Should().BeEmpty("la celda liberada espera a la siguiente vuelta del procesador");
    }

    /// <summary>
    /// API con RUNT simulada, el impuesto disponible y un espía de su ejecutor; réplica de organismos con el organismo de prueba en
    /// <paramref name="departamentoOrganismo"/> y réplica de departamentos con impuesto sincronizada con <c>05</c> soportado.
    /// </summary>
    private static async Task<ApiDePrueba> ArrancarAsync(ConcurrentQueue<string> orden, string departamentoOrganismo, CancellationToken ct)
    {
        var api = await MotorNa.ArrancarAsync(b =>
        {
            b.Services.AddScoped<ICatalogoOrganismosPlataforma, CatalogoOrganismosGrpc>();
            MotorNa.Espias(b, orden, Fuente.ImpuestoDepartamental);
        });
        if (api.Omitir is not null)
            return api;

        api.Catalogos.Organismos.Add(new OrganismoTransito
        {
            Id = Guid.NewGuid().ToString(),
            Codigo = "98001000",
            Nombre = "Secretaría de Tránsito de Prueba",
            DepartamentoCodigo = departamentoOrganismo,
            Departamento = "DEPARTAMENTO DE PRUEBA",
            Activo = true,
        });
        await using (var scope = api.App.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<SincronizacionOrganismos>().SincronizarAsync(ct))!.Nuevos.Should().Be(1);
        (await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa(Soportado), new RelojManual(), ct))!.Soportados.Should().Be(1);
        return api;
    }

    /// <summary>La celda del impuesto queda en N/A con el motivo, sin payload ni intento, y su ejecutor nunca se llamó.</summary>
    private static async Task DebeQuedarNaAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, Guid lote, Guid vehiculo, MotivoNa motivo, ConcurrentQueue<string> orden, CancellationToken ct)
    {
        (await MotorRunt.CeldasDelVehiculoAsync(api, vehiculo, ct)).Should().BeEquivalentTo(["IMPUESTO_DEPARTAMENTAL NA -", "RUNT COMPLETADO -"]);
        var impuesto = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single(c => c.Fuente == Fuente.ImpuestoDepartamental);
        CodigosDeEnum.Codigo(impuesto.MotivoNa!.Value).Should().Be(CodigosDeEnum.Codigo(motivo));
        impuesto.Payload.Should().BeNull();
        impuesto.Origen.Should().BeNull();
        impuesto.IntentosAuto.Should().Be(0);
        orden.Should().NotContain(o => o.StartsWith("IMPUESTO_DEPARTAMENTAL", StringComparison.Ordinal), "una N/A no llama al adaptador");
        (await Motor.EscalarAsync(api, $"SELECT count(*) FROM diagnostico.intentos_consulta WHERE vehiculo_id = '{vehiculo}' AND fuente = 'IMPUESTO_DEPARTAMENTAL'", ct))
            .Should().Be(0, "una N/A no deja intento ni consumo");
    }
}
