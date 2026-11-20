using System.Collections.Concurrent;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Diagnostico.Tests.Catalogos;

/// <summary>
/// HU #13861 (F09-H2), AC1, AC2 y AC3 (F09-C2) — réplica de <c>diagnostico.departamentos_impuesto</c> desde el rpc de capacidad
/// <c>ListarDepartamentosImpuesto</c> de core-consultas: crea los que llegan con la hora del <see cref="TimeProvider"/>, deja en
/// <c>soportado=false</c> los que dejan de llegar sin borrarlos (Q3=a), deja todos en false con una lista vacía OK (Q2=a) y no
/// cambia nada si el rpc falla (UNIMPLEMENTED, Unavailable, timeout) o el adaptador no está conectado. Ninguna prueba llama a
/// core-consultas: capacidad falsa (<see cref="CapacidadFalsa"/>) o el adaptador gRPC real sobre un cliente falso
/// (<see cref="CapacidadGrpcFalsa"/>, sin <c>x-flit-tenant-id</c>: capacidad global, Q4=a). Departamentos DIVIPOLA públicos.
/// PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~SincronizacionDepartamentosImpuestoTests
/// </code>
/// </example>
public sealed class SincronizacionDepartamentosImpuestoTests
{
    // AC1 — réplica vacía y el rpc responde 05 y 25: dos filas soportadas con la hora del reloj; HaySoportados verdadero.
    [Fact]
    public async Task Crea_los_soportados_con_la_fecha_del_reloj()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var reloj = new RelojManual();
        reloj.Avanzar(TimeSpan.FromDays(3));

        var resultado = await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa("05", "25"), reloj, ct);

        resultado.Should().Be(new ResultadoSincronizacionImpuesto(2, 2, 2, 0, 0));
        var filas = await ReplicaImpuesto.FilasAsync(api, ct);
        filas.Select(f => (f.DepartamentoCodigo, f.Soportado)).Should().Equal(("05", true), ("25", true));
        filas.Should().OnlyContain(f => f.ActualizadoEn == ReplicaImpuesto.EnMicrosegundos(reloj.GetUtcNow()), "la hora sale del TimeProvider inyectado");
        api.App.Services.GetRequiredService<EstadoCapacidadImpuesto>().HaySoportados.Should().BeTrue();
    }

    // AC2 — con 05 y 25 soportados, el rpc responde solo 05: 25 pasa a soportado=false y su fila sigue existiendo.
    [Fact]
    public async Task Departamento_que_deja_de_venir_pasa_a_no_soportado_sin_borrarse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var reloj = new RelojManual();
        await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa("05", "25"), reloj, ct);
        reloj.Avanzar(TimeSpan.FromDays(1));

        var resultado = await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa("05"), reloj, ct);

        resultado.Should().Be(new ResultadoSincronizacionImpuesto(1, 1, 0, 1, 0));
        var filas = await ReplicaImpuesto.FilasAsync(api, ct);
        filas.Select(f => (f.DepartamentoCodigo, f.Soportado)).Should().Equal(("05", true), ("25", false));
        filas.Should().OnlyContain(f => f.ActualizadoEn == ReplicaImpuesto.EnMicrosegundos(reloj.GetUtcNow()));
        api.App.Services.GetRequiredService<EstadoCapacidadImpuesto>().HaySoportados.Should().BeTrue("05 sigue soportado");
    }

    // AC3 (negativo) — UNIMPLEMENTED, Unavailable, DeadlineExceeded o adaptador no conectado: la réplica no cambia, la causa
    // queda en el log y la siguiente vuelta lo vuelve a intentar (una respuesta OK posterior sí se aplica).
    [Fact]
    public async Task Fallo_UNIMPLEMENTED_o_no_conectado_no_cambian_la_replica()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var reloj = new RelojManual();
        await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa("05"), reloj, ct);
        var antes = await ReplicaImpuesto.FilasAsync(api, ct);
        var logs = new LogsConEvento();

        foreach (var codigo in new[] { StatusCode.Unimplemented, StatusCode.Unavailable, StatusCode.DeadlineExceeded })
        {
            reloj.Avanzar(TimeSpan.FromHours(1));
            var grpc = new CapacidadGrpcFalsa { Falla = codigo };
            (await ReplicaImpuesto.SincronizarAsync(api, ReplicaImpuesto.Grpc(grpc, logs), reloj, ct)).Should().BeNull($"{codigo} no es una respuesta OK");
            grpc.Llamadas.Should().Be(1);
            logs.Eventos.Should().Contain(e => e.Mensaje.Contains(codigo.ToString(), StringComparison.Ordinal), "se registra la causa");
        }

        reloj.Avanzar(TimeSpan.FromHours(1));
        (await ReplicaImpuesto.SincronizarAsync(api, new CapacidadImpuestoNoConectada(), reloj, ct)).Should().BeNull();
        (await ReplicaImpuesto.FilasAsync(api, ct)).Should().BeEquivalentTo(antes, "un fallo nunca borra ni vacía la réplica");
        api.App.Services.GetRequiredService<EstadoCapacidadImpuesto>().HaySoportados.Should().BeTrue();

        (await ReplicaImpuesto.SincronizarAsync(api, ReplicaImpuesto.Grpc(new CapacidadGrpcFalsa("05", "11"), logs), reloj, ct))
            .Should().Be(new ResultadoSincronizacionImpuesto(2, 2, 1, 0, 0), "la siguiente vuelta con respuesta OK sí se aplica");
    }

    // Q2=a — una lista vacía con respuesta OK deja todos como no soportados (cierre seguro), sin borrar filas.
    [Fact]
    public async Task Lista_vacia_deja_todos_como_no_soportados()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var reloj = new RelojManual();
        await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa("05", "25"), reloj, ct);

        var resultado = await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa(), reloj, ct);

        resultado.Should().Be(new ResultadoSincronizacionImpuesto(0, 0, 0, 2, 0));
        (await ReplicaImpuesto.FilasAsync(api, ct)).Select(f => (f.DepartamentoCodigo, f.Soportado)).Should().Equal(("05", false), ("25", false));
        api.App.Services.GetRequiredService<EstadoCapacidadImpuesto>().HaySoportados.Should().BeFalse();
    }

    // Normalización como SincronizacionOrganismos.CodigoDepartamento: recorta, antepone 0 a un dígito, descarta vacíos, de más
    // de 8 caracteres y repetidos. Por el adaptador gRPC real, que no envía x-flit-tenant-id (capacidad global, Q4=a).
    [Fact]
    public async Task Normaliza_codigos_de_un_digito_y_descarta_invalidos_y_repetidos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var grpc = new CapacidadGrpcFalsa(" 5 ", "05", "", "   ", "123456789", "25", "25", "8");

        var resultado = await ReplicaImpuesto.SincronizarAsync(api, ReplicaImpuesto.Grpc(grpc, new LogsConEvento()), new RelojManual(), ct);

        resultado.Should().Be(new ResultadoSincronizacionImpuesto(8, 3, 3, 0, 5));
        (await ReplicaImpuesto.FilasAsync(api, ct)).Select(f => (f.DepartamentoCodigo, f.Soportado)).Should().Equal(("05", true), ("08", true), ("25", true));
        grpc.Tenants.Should().ContainSingle().Which.Should().BeNull("ListarDepartamentosImpuesto es capacidad global, sin empresa");
    }

    // EstadoCapacidadImpuesto: cargado desde la base (también sin adaptador conectado) y actualizado tras cada sincronización.
    [Fact]
    public async Task Estado_de_capacidad_refleja_si_hay_al_menos_un_soportado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var estado = api.App.Services.GetRequiredService<EstadoCapacidadImpuesto>();
        var reloj = new RelojManual();
        estado.HaySoportados.Should().BeFalse("sin réplica no hay departamento soportado");

        await Motor.EjecutarAsync(api,
            "INSERT INTO diagnostico.departamentos_impuesto (departamento_codigo, soportado, actualizado_en) VALUES ('25', true, now())", ct);
        (await ReplicaImpuesto.SincronizarAsync(api, new CapacidadImpuestoNoConectada(), reloj, ct)).Should().BeNull();
        estado.Cargado.Should().BeTrue();
        estado.HaySoportados.Should().BeTrue("sin adaptador conectado el estado se carga desde la base");

        await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa(), reloj, ct);
        estado.HaySoportados.Should().BeFalse();
        await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa("25"), reloj, ct);
        estado.HaySoportados.Should().BeTrue();
        await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa("05"), reloj, ct);
        estado.HaySoportados.Should().BeTrue("basta con uno soportado");
    }
}

/// <summary>Ayudas de la réplica de departamentos con impuesto (HU #13861).</summary>
internal static class ReplicaImpuesto
{
    /// <summary>Sincroniza con la capacidad y el reloj dados; base, estado y logs son los de la API.</summary>
    public static async Task<ResultadoSincronizacionImpuesto?> SincronizarAsync(
        ApiDePrueba api, ICapacidadImpuestoPlataforma capacidad, TimeProvider reloj, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<SincronizacionDepartamentosImpuesto>(scope.ServiceProvider, capacidad, reloj).SincronizarAsync(ct);
    }

    /// <summary>El adaptador gRPC real sobre el cliente falso, con sus logs en <paramref name="logs"/>.</summary>
    public static CapacidadImpuestoGrpc Grpc(CapacidadGrpcFalsa grpc, LogsConEvento logs) =>
        new(new ClienteConsultas(grpc, NullLogger<ClienteConsultas>.Instance), logs.Logger<CapacidadImpuestoGrpc>());

    public static async Task<List<DepartamentoImpuesto>> FilasAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        return await db.Set<DepartamentoImpuesto>().AsNoTracking().OrderBy(d => d.DepartamentoCodigo).ToListAsync(ct);
    }

    /// <summary>PostgreSQL guarda timestamptz en microsegundos.</summary>
    public static DateTimeOffset EnMicrosegundos(DateTimeOffset t) => new(t.UtcTicks - (t.UtcTicks % 10), TimeSpan.Zero);
}

/// <summary>Capacidad falsa (HU #13861): responde la lista dada (vacía si no se da ninguna) y cuenta las llamadas.</summary>
public sealed class CapacidadFalsa(params string[] departamentos) : ICapacidadImpuestoPlataforma
{
    private int _llamadas;

    public int Llamadas => Volatile.Read(ref _llamadas);

    public Task<IReadOnlyList<string>?> ListarAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _llamadas);
        return Task.FromResult<IReadOnlyList<string>?>(departamentos);
    }
}

/// <summary>
/// Cliente gRPC falso de core-consultas para <c>ListarDepartamentosImpuesto</c> (HU #13861, patrón <c>AvaluosGrpcFalso</c>):
/// responde los códigos dados o lanza <see cref="RpcException"/> con <see cref="Falla"/>; anota la empresa de la metadata.
/// </summary>
public sealed class CapacidadGrpcFalsa(params string[] departamentos) : ConsultasService.ConsultasServiceClient
{
    private readonly ConcurrentQueue<string?> _tenants = new();

    public StatusCode? Falla { get; init; }

    public int Llamadas => _tenants.Count;

    public IReadOnlyCollection<string?> Tenants => _tenants;

    public override AsyncUnaryCall<ListarDepartamentosImpuestoResponse> ListarDepartamentosImpuestoAsync(ListarDepartamentosImpuestoRequest request, CallOptions options)
    {
        _tenants.Enqueue(options.Headers?.GetValue(PlatformServiceCallInterceptor.TenantMetadata));
        if (Falla is { } codigo)
            throw new RpcException(new Status(codigo, "capacidad de prueba"));
        var respuesta = new ListarDepartamentosImpuestoResponse();
        respuesta.DepartamentoCodigos.Add(departamentos);
        return new AsyncUnaryCall<ListarDepartamentosImpuestoResponse>(
            Task.FromResult(respuesta), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }
}

/// <summary>Logs en memoria con su EventId y nivel (HU #13861).</summary>
public sealed class LogsConEvento
{
    private readonly ConcurrentQueue<(int EventId, LogLevel Nivel, string Mensaje)> _eventos = new();

    public IReadOnlyCollection<(int EventId, LogLevel Nivel, string Mensaje)> Eventos => _eventos;

    public ILogger<T> Logger<T>() => new Registro<T>(_eventos);

    private sealed class Registro<T>(ConcurrentQueue<(int, LogLevel, string)> eventos) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            eventos.Enqueue((eventId.Id, logLevel, formatter(state, exception)));
    }
}
