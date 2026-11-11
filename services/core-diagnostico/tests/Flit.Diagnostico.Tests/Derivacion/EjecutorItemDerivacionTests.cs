using System.Collections.Concurrent;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Tests.Consultas;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>Doble de <see cref="IClienteDerivacionTramites"/>: responde en orden lo que se le encola (por defecto, creado).</summary>
internal sealed class ClienteDerivacionGuionado : IClienteDerivacionTramites
{
    public ConcurrentQueue<ResultadoBorrador> Respuestas { get; } = new();

    public int Llamadas;

    public Dictionary<Guid, TramiteReferencia> Referencias { get; } = [];

    public bool ReferenciasDisponibles { get; set; } = true;

    public Task<ResultadoBorrador> CrearAsync(Guid empresa, CrearBorradorDesdeDiagnosticoRequest solicitud, CancellationToken ct)
    {
        Interlocked.Increment(ref Llamadas);
        return Task.FromResult(Respuestas.TryDequeue(out var r) ? r : new ResultadoBorrador.Creado(Guid.NewGuid(), "TR-1", false));
    }

    public Task<IReadOnlyDictionary<Guid, TramiteReferencia>?> ObtenerPorReferenciaAsync(Guid empresa, IReadOnlyCollection<Guid> vehiculos, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, TramiteReferencia>?>(ReferenciasDisponibles
            ? Referencias.Where(r => vehiculos.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value)
            : null);
}

/// <summary>
/// Punto 4, incremento 3 (diseño §5.3 puntos 3, 5 y 6) — <see cref="EjecutorItemDerivacion"/>, el paso del consumidor que llama a
/// Trámites: espera turno en el freno, llama y decide. Creado o rechazo de negocio → cerrar (y el freno cuenta un éxito);
/// transitorio con intentos disponibles → reintentar con la espera del SDK (30 s, 2 min) conservando el crédito; agotado
/// <c>MaxIntentos</c> (3) → cerrar FALLIDO; no transitorio (configuración) → cerrar FALLIDO sin reintentos. Sin base ni red.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EjecutorItemDerivacionTests
/// </code>
/// </example>
public sealed class EjecutorItemDerivacionTests
{
    private static readonly Guid Empresa = Guid.Parse("0199f3a1-1111-7000-8000-000000000011");

    private static (EjecutorItemDerivacion Ejecutor, ClienteDerivacionGuionado Cliente, FrenoTramites Freno, RelojManual Reloj) Crear(int maxIntentos = 3)
    {
        var opciones = new OpcionesDerivacion { MaxIntentos = maxIntentos, TasaPorSegundo = 1000, PausaTrasFallos = 5 };
        var reloj = new RelojManual();
        var freno = new FrenoTramites(opciones, reloj, autoReponer: true);
        var cliente = new ClienteDerivacionGuionado();
        return (new EjecutorItemDerivacion(cliente, freno, Options.Create(opciones), reloj), cliente, freno, reloj);
    }

    [Fact]
    public async Task Creado_cierra_con_un_intento_y_cuenta_un_exito_en_el_freno()
    {
        var (ejecutor, cliente, freno, _) = Crear();
        for (var i = 0; i < 4; i++)
            freno.RegistrarFalloTransitorio();
        var tramite = Guid.NewGuid();
        cliente.Respuestas.Enqueue(new ResultadoBorrador.Creado(tramite, "TR-9", YaExistia: true));

        var paso = await ejecutor.EjecutarAsync(Empresa, new CrearBorradorDesdeDiagnosticoRequest(), intentosPrevios: 0, TestContext.Current.CancellationToken);

        paso.Should().BeEquivalentTo(new PasoDerivacion.Cerrar(new ResultadoBorrador.Creado(tramite, "TR-9", true), Intentos: 1));
        freno.RegistrarFalloTransitorio();
        freno.EnPausa.Should().BeFalse("el éxito reinició la cuenta de fallos seguidos");
    }

    [Fact]
    public async Task Rechazo_de_negocio_cierra_y_no_se_reintenta()
    {
        var (ejecutor, cliente, _, _) = Crear();
        cliente.Respuestas.Enqueue(new ResultadoBorrador.Rechazado(MotivoDerivacion.OtNoHabilitado, "OT no habilitado para la compañía"));

        var paso = await ejecutor.EjecutarAsync(Empresa, new CrearBorradorDesdeDiagnosticoRequest(), 0, TestContext.Current.CancellationToken);

        paso.Should().BeOfType<PasoDerivacion.Cerrar>().Which.Resultado.Should().BeOfType<ResultadoBorrador.Rechazado>()
            .Which.Codigo.Should().Be(MotivoDerivacion.OtNoHabilitado);
    }

    [Fact]
    public async Task Transitorio_reintenta_con_la_espera_del_SDK_y_agotado_cierra_FALLIDO()
    {
        var (ejecutor, cliente, _, reloj) = Crear(maxIntentos: 3);
        var ct = TestContext.Current.CancellationToken;
        var ahora = reloj.GetUtcNow();
        for (var i = 0; i < 3; i++)
            cliente.Respuestas.Enqueue(new ResultadoBorrador.NoDisponible(Transitorio: true));

        var primero = await ejecutor.EjecutarAsync(Empresa, new CrearBorradorDesdeDiagnosticoRequest(), 0, ct);
        var segundo = await ejecutor.EjecutarAsync(Empresa, new CrearBorradorDesdeDiagnosticoRequest(), 1, ct);
        var tercero = await ejecutor.EjecutarAsync(Empresa, new CrearBorradorDesdeDiagnosticoRequest(), 2, ct);

        primero.Should().BeEquivalentTo(new PasoDerivacion.Reintentar(1, ahora + TimeSpan.FromSeconds(30)));
        segundo.Should().BeEquivalentTo(new PasoDerivacion.Reintentar(2, ahora + TimeSpan.FromMinutes(2)));
        tercero.Should().BeEquivalentTo(new PasoDerivacion.Cerrar(new ResultadoBorrador.NoDisponible(true), 3), "agotado MaxIntentos queda FALLIDO, reintentable a mano");
        cliente.Llamadas.Should().Be(3);
    }

    [Fact]
    public async Task No_transitorio_cierra_FALLIDO_sin_reintentar_ni_contar_en_el_freno()
    {
        var (ejecutor, cliente, freno, _) = Crear();
        cliente.Respuestas.Enqueue(new ResultadoBorrador.NoDisponible(Transitorio: false));

        var paso = await ejecutor.EjecutarAsync(Empresa, new CrearBorradorDesdeDiagnosticoRequest(), 0, TestContext.Current.CancellationToken);

        paso.Should().BeEquivalentTo(new PasoDerivacion.Cerrar(new ResultadoBorrador.NoDisponible(false), 1));
        for (var i = 0; i < 4; i++)
            freno.RegistrarFalloTransitorio();
        freno.EnPausa.Should().BeFalse("un error de configuración no es un fallo transitorio de Trámites");
    }

    [Fact]
    public void Esperas_del_SDK_son_30_s_2_min_y_10_min_y_cubren_MaxIntentos()
    {
        ReintentoDerivacion.Esperas.Should().Equal(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));
        var ahora = DateTimeOffset.UnixEpoch;
        ReintentoDerivacion.Siguiente(intentos: 1, maxIntentos: 3, ahora).Should().Be(ahora + TimeSpan.FromSeconds(30));
        ReintentoDerivacion.Siguiente(2, 3, ahora).Should().Be(ahora + TimeSpan.FromMinutes(2));
        ReintentoDerivacion.Siguiente(3, 3, ahora).Should().BeNull("agotado");
        ReintentoDerivacion.Siguiente(1, 1, ahora).Should().BeNull("con MaxIntentos = 1 no hay reintento automático");
    }
}
