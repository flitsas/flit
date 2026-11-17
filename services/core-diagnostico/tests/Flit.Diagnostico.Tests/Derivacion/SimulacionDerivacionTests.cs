using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Derivacion.Bus;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// core-api simulado para la prueba de concurrencia: idempotente por <c>external_ref</c> como el rpc real (la segunda llamada
/// devuelve el mismo trámite con <c>ya_existia</c>), con <see cref="Falla"/> por llamada (índice 1-based, referencia), una demora de
/// <see cref="Demora"/> por llamada y la cuenta de llamadas simultáneas y de borradores creados.
/// </summary>
internal sealed class CoreApiSimulado : DerivacionTramitesService.DerivacionTramitesServiceClient
{
    private int _enCurso;
    private int _llamadas;

    public Func<int, string, StatusCode?> Falla { get; set; } = (_, _) => null;

    public TimeSpan Demora { get; set; } = TimeSpan.FromMilliseconds(1);

    public ConcurrentDictionary<string, Guid> Borradores { get; } = new();

    public int Creaciones;

    public int MaxSimultaneas;

    public int Llamadas => Volatile.Read(ref _llamadas);

    public ConcurrentQueue<(int Indice, long Inicio, long Fin, bool Fallo)> Registro { get; } = new();

    public override AsyncUnaryCall<CrearBorradorDesdeDiagnosticoResponse> CrearBorradorDesdeDiagnosticoAsync(
        CrearBorradorDesdeDiagnosticoRequest request, CallOptions options) =>
        new(SimularAsync(request), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });

    private async Task<CrearBorradorDesdeDiagnosticoResponse> SimularAsync(CrearBorradorDesdeDiagnosticoRequest request)
    {
        var indice = Interlocked.Increment(ref _llamadas);
        var inicio = Stopwatch.GetTimestamp();
        var simultaneas = Interlocked.Increment(ref _enCurso);
        int previo;
        do
        {
            previo = Volatile.Read(ref MaxSimultaneas);
        }
        while (simultaneas > previo && Interlocked.CompareExchange(ref MaxSimultaneas, simultaneas, previo) != previo);

        try
        {
            await Task.Delay(Demora).ConfigureAwait(false);
            if (Falla(indice, request.ExternalRef) is { } estado)
            {
                Registro.Enqueue((indice, inicio, Stopwatch.GetTimestamp(), true));
                throw new RpcException(new Status(estado, "simulado"));
            }

            var nuevo = Guid.NewGuid();
            var id = Borradores.GetOrAdd(request.ExternalRef, nuevo);
            if (id == nuevo)
                Interlocked.Increment(ref Creaciones);
            Registro.Enqueue((indice, inicio, Stopwatch.GetTimestamp(), false));
            return DerivacionTramitesFalso.Creado(id, $"TR-{indice:000000}", yaExistia: id != nuevo);
        }
        finally
        {
            Interlocked.Decrement(ref _enCurso);
        }
    }
}

/// <summary>
/// El protocolo del masivo en memoria (diseño §5.3, ADR-0084), con las piezas REALES: <see cref="DespachadorCredito.Siguientes"/> y
/// <see cref="DespachadorCredito.Marcar"/> para liberar, <see cref="CreditoDerivacion.Libres"/>, <see cref="EjecutorItemDerivacion"/>
/// (freno + cliente gRPC real <see cref="ClienteDerivacionTramitesGrpc"/> sobre <see cref="CoreApiSimulado"/>) y
/// <see cref="CierreItemDerivacion"/>. Lo simulado es la base (listas bajo un candado, como el advisory lock por compañía) y el broker
/// (un canal FIFO; un reintento vuelve al final de la cola, como la cola <c>.retry.n</c>). <see cref="Concurrencia"/> trabajadores
/// atienden la cola como el consumidor del SDK.
/// </summary>
internal sealed class MundoDerivacion
{
    private readonly object _candado = new();
    private readonly Channel<(Guid Empresa, DerivacionSolicitada Mensaje)> _broker = Channel.CreateUnbounded<(Guid, DerivacionSolicitada)>();
    private readonly List<OperacionDerivacion> _operaciones = [];
    private readonly List<ItemDerivacion> _items = [];
    private readonly Dictionary<Guid, VehiculoDiagnostico> _vehiculos = [];
    private readonly Dictionary<Guid, int> _maxEnVuelo = [];
    private readonly OpcionesDerivacion _opciones;
    private DateTimeOffset _reloj = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);

    public MundoDerivacion(OpcionesDerivacion opciones, CoreApiSimulado coreApi)
    {
        _opciones = opciones;
        CoreApi = coreApi;
        var opcionesIo = Options.Create(opciones);
        Freno = new FrenoTramites(opciones, TimeProvider.System, autoReponer: true);
        Cliente = new ClienteDerivacionTramitesGrpc(coreApi, opcionesIo, TimeProvider.System, NullLogger<ClienteDerivacionTramitesGrpc>.Instance);
        Ejecutor = new EjecutorItemDerivacion(Cliente, Freno, opcionesIo, TimeProvider.System);
    }

    public CoreApiSimulado CoreApi { get; }

    public FrenoTramites Freno { get; }

    public ClienteDerivacionTramitesGrpc Cliente { get; }

    public EjecutorItemDerivacion Ejecutor { get; }

    public int Concurrencia => _opciones.Concurrencia;

    /// <summary>Orden en que cerraron los ítems (empresa).</summary>
    public List<Guid> Cierres { get; } = [];

    public int MaxEnVueloGlobal { get; private set; }

    public IReadOnlyDictionary<Guid, int> MaxEnVueloPorEmpresa => _maxEnVuelo;

    public IReadOnlyList<ItemDerivacion> Items => _items;

    /// <summary>Encola una selección de <paramref name="vehiculos"/> de la compañía y libera su crédito (paso 1 del masivo).</summary>
    public Guid Encolar(Guid empresa, int vehiculos)
    {
        lock (_candado)
        {
            _reloj = _reloj.AddMilliseconds(1);
            var operacion = OperacionDerivacion.Masiva(empresa, Guid.NewGuid(), "TRASPASO_STANDARD", Guid.NewGuid(), vehiculos, _reloj);
            _operaciones.Add(operacion);
            for (var i = 1; i <= vehiculos; i++)
            {
                var vehiculo = new VehiculoDiagnostico { TenantId = empresa, DiagnosticoId = operacion.DiagnosticoId, IdentificadorOriginal = "TST", IdentificadorNorm = "TST" };
                _vehiculos[vehiculo.Id] = vehiculo;
                _items.Add(ItemDerivacion.Pendiente(operacion, vehiculo.Id, i, _reloj));
            }

            Liberar(empresa);
            return operacion.Id;
        }
    }

    public async Task CorrerAsync(TimeSpan tope, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(tope);
        await Task.WhenAll(Enumerable.Range(0, Concurrencia).Select(_ => Task.Run(() => TrabajadorAsync(limite.Token), limite.Token))).ConfigureAwait(false);
    }

    private async Task TrabajadorAsync(CancellationToken ct)
    {
        await foreach (var (empresa, mensaje) in _broker.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            ItemDerivacion item;
            short previos;
            lock (_candado)
            {
                item = _items.Single(i => i.Id == mensaje.ItemId);
                if (item.Estado != EstadoItemDerivacion.Enviado)
                    continue; // re-entrega de un ítem ya cerrado: sin efecto
                previos = item.Intentos;
            }

            var solicitud = new CrearBorradorDesdeDiagnosticoRequest { ExternalRef = mensaje.VehiculoId.ToString(), IntentoId = mensaje.ItemId.ToString() };
            var paso = await Ejecutor.EjecutarAsync(empresa, solicitud, previos, ct).ConfigureAwait(false);
            lock (_candado)
            {
                switch (paso)
                {
                    case PasoDerivacion.Reintentar r:
                        // Conserva el crédito: el ítem sigue ENVIADO y el mensaje vuelve por la cola de reintentos.
                        item.Intentos = r.Intentos;
                        item.DisponibleDesde = r.DisponibleDesde;
                        _broker.Writer.TryWrite((empresa, mensaje));
                        break;
                    case PasoDerivacion.Cerrar c:
                        item.Intentos = c.Intentos;
                        var operacion = _operaciones.Single(o => o.Id == item.OperacionId);
                        CierreItemDerivacion.Cerrar(operacion, item, _vehiculos[item.VehiculoId], c.Resultado, _reloj);
                        Cierres.Add(empresa);
                        Liberar(empresa);
                        if (_operaciones.TrueForAll(o => o.Estado == EstadoOperacionDerivacion.Terminada))
                            _broker.Writer.TryComplete();
                        break;
                }
            }
        }
    }

    /// <summary>Paso 2 del masivo (bajo el candado, como el advisory lock): libera hasta el crédito y mide lo que queda en vuelo.</summary>
    private void Liberar(Guid empresa)
    {
        var enVuelo = _items.Count(i => i.TenantId == empresa && i.Estado == EstadoItemDerivacion.Enviado);
        var libres = CreditoDerivacion.Libres(_opciones.CreditoPorCompania, enVuelo);
        foreach (var item in DespachadorCredito.Siguientes(_items.AsQueryable(), _operaciones.AsQueryable(), empresa).Take(libres).ToList())
        {
            DespachadorCredito.Marcar(item, _reloj);
            _broker.Writer.TryWrite((empresa, new DerivacionSolicitada(item.OperacionId, item.Id, item.VehiculoId)));
        }

        _maxEnVuelo[empresa] = Math.Max(_maxEnVuelo.GetValueOrDefault(empresa), _items.Count(i => i.TenantId == empresa && i.Estado == EstadoItemDerivacion.Enviado));
        MaxEnVueloGlobal = Math.Max(MaxEnVueloGlobal, _items.Count(i => i.Estado == EstadoItemDerivacion.Enviado));
    }
}

/// <summary>
/// Punto 4, incremento 3 (diseño §5.3 y §9 «equidad»; ADR-0084, RNF de carga) — prueba de concurrencia y equidad del masivo con un
/// doble del cliente (la prueba de carga real contra core-api la corre QA: <c>docs/diagnosticos/prueba-carga-derivacion.md</c>):
/// <list type="bullet">
/// <item>1 compañía con 500 placas + 3 con 10: las pequeñas no esperan a la grande (turno rotativo por el crédito), nunca hay más de
/// crédito × compañías ítems en vuelo ni más de <c>Concurrencia</c> llamadas simultáneas a Trámites, y no se duplica ningún borrador.</item>
/// <item>RESOURCE_EXHAUSTED (el 429 de gRPC): el ítem reintenta conservando su crédito (la compañía no inunda) y termina creado una
/// sola vez.</item>
/// <item>RESOURCE_EXHAUSTED seguidos: el freno abre la pausa y casi no entran llamadas nuevas hasta que vence (baja el ritmo).</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~SimulacionDerivacionTests
/// </code>
/// </example>
public sealed class SimulacionDerivacionTests
{
    private static OpcionesDerivacion Opciones(int pausaTrasFallos = 1000, int pausaSegundos = 30) => new()
    {
        Concurrencia = 4,
        Prefetch = 8,
        CreditoPorCompania = 2,
        TasaPorSegundo = 10_000,
        PausaTrasFallos = pausaTrasFallos,
        PausaSegundos = pausaSegundos,
        MaxIntentos = 3,
    };

    [Fact]
    public async Task Una_compania_con_500_no_hace_esperar_a_tres_con_10_y_el_techo_es_credito_por_companias()
    {
        var ct = TestContext.Current.CancellationToken;
        var mundo = new MundoDerivacion(Opciones(), new CoreApiSimulado());
        var grande = Guid.NewGuid();
        Guid[] pequenas = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        mundo.Encolar(grande, 500);
        foreach (var p in pequenas)
            mundo.Encolar(p, 10);

        await mundo.CorrerAsync(TimeSpan.FromSeconds(60), ct);

        mundo.Items.Should().HaveCount(530).And.OnlyContain(i => i.Estado == EstadoItemDerivacion.Creado);
        mundo.CoreApi.Creaciones.Should().Be(530, "un borrador por vehículo, sin duplicados");
        mundo.MaxEnVueloPorEmpresa.Values.Should().OnlyContain(v => v <= 2, "crédito por compañía = 2");
        mundo.MaxEnVueloGlobal.Should().BeLessThanOrEqualTo(2 * 4, "techo = crédito × compañías activas");
        mundo.CoreApi.MaxSimultaneas.Should().BeLessThanOrEqualTo(4, "Concurrencia del consumidor");

        var ultimaPequena = mundo.Cierres.FindLastIndex(e => pequenas.Contains(e));
        var grandesAntes = mundo.Cierres.Take(ultimaPequena + 1).Count(e => e == grande);
        grandesAntes.Should().BeLessThanOrEqualTo(60,
            $"las 30 placas de las pequeñas cierran en turno rotativo (en FIFO puro esperarían las 500 de la grande); cerraron tras {grandesAntes} de la grande");
    }

    [Fact]
    public async Task Con_429_el_item_reintenta_conservando_su_credito_y_no_se_duplica()
    {
        var ct = TestContext.Current.CancellationToken;
        var vistos = new ConcurrentDictionary<string, int>();
        var coreApi = new CoreApiSimulado
        {
            // Cada referencia falla con RESOURCE_EXHAUSTED en su primera llamada.
            Falla = (_, referencia) => vistos.AddOrUpdate(referencia, 1, (_, n) => n + 1) == 1 ? StatusCode.ResourceExhausted : null,
        };
        var mundo = new MundoDerivacion(Opciones(), coreApi);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        mundo.Encolar(a, 20);
        mundo.Encolar(b, 20);

        await mundo.CorrerAsync(TimeSpan.FromSeconds(60), ct);

        mundo.Items.Should().OnlyContain(i => i.Estado == EstadoItemDerivacion.Creado && i.Intentos == 2);
        coreApi.Creaciones.Should().Be(40);
        mundo.MaxEnVueloPorEmpresa.Values.Should().OnlyContain(v => v <= 2, "el ítem en reintento conserva el crédito: no se libera otro en su lugar");
    }

    [Fact]
    public async Task Con_429_seguidos_el_freno_pausa_y_baja_el_ritmo()
    {
        var ct = TestContext.Current.CancellationToken;
        // core-api saturado los primeros 300 ms: todo responde RESOURCE_EXHAUSTED; luego se recupera.
        var caida = Stopwatch.StartNew();
        var coreApi = new CoreApiSimulado { Falla = (_, _) => caida.ElapsedMilliseconds < 300 ? StatusCode.ResourceExhausted : null };
        var mundo = new MundoDerivacion(Opciones(pausaTrasFallos: 5, pausaSegundos: 1), coreApi);
        mundo.Encolar(Guid.NewGuid(), 10);
        mundo.Encolar(Guid.NewGuid(), 10);

        await mundo.CorrerAsync(TimeSpan.FromSeconds(60), ct);

        var registro = coreApi.Registro.ToList();
        var quintoFallo = registro.Where(r => r.Fallo).OrderBy(r => r.Fin).ElementAt(4).Fin;
        var ventana = quintoFallo + (long)(0.8 * Stopwatch.Frequency);
        registro.Count(r => r.Inicio > quintoFallo && r.Inicio < ventana).Should().BeLessThanOrEqualTo(mundo.Concurrencia - 1,
            "con la pausa abierta solo terminan de entrar las llamadas que ya habían pasado el freno (sin el freno, el reintento inmediato las repetiría sin parar)");
        registro.Should().Contain(r => r.Inicio >= ventana && !r.Fallo, "al vencer la pausa se retoma sin intervención");
        mundo.Items.Should().OnlyContain(i => i.Estado == EstadoItemDerivacion.Creado || i.Estado == EstadoItemDerivacion.Fallido, "todos cierran");
        coreApi.Creaciones.Should().Be(mundo.Items.Count(i => i.Estado == EstadoItemDerivacion.Creado), "sin duplicados");
        mundo.Freno.EnPausa.Should().BeFalse("el primer éxito cerró la pausa");
    }
}
