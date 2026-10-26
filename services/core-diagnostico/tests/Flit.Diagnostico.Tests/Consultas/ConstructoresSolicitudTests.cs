using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13727 (ADR-0075, T6) — <see cref="IConstructorSolicitud"/>: para cada fuente, el <c>*Request</c> que arma el constructor
/// es igual (<c>Equals</c> de proto) al que HOY envía a core-consultas el ejecutor de la celda con su <c>Fuente*Real</c> (capturado
/// en un cliente gRPC falso que responde Unavailable), con el mismo vehículo y las mismas personas, y en el mismo orden. Los
/// documentos se descifran de <c>personas_vehiculo</c> con la protección de documentos. El <c>*Request</c> solo trae campos de
/// identificación (placa o VIN, tipo y número de documento o NIT, los datos técnicos del avalúo o el departamento del impuesto).
/// PostgreSQL real; RUNT simulado de Intempo: TST001 persona natural, TST003 leasing (propietario NIT y locatario con cédula) y
/// un VIN de prueba que termina en 1.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ConstructoresSolicitudTests
/// </code>
/// </example>
public sealed class ConstructoresSolicitudTests
{
    internal const string Vin = "9ZZPRUEBA00000011";

    /// <summary>Rutas de campo (proto-JSON) que puede traer el <c>*Request</c> de cada rpc: solo identificación.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> CamposPermitidos = new Dictionary<string, string[]>
    {
        [RpcConsultas.PorPlaca] = ["placa"],
        [RpcConsultas.PorVin] = ["vin"],
        [RpcConsultas.Conductor] = ["opciones", "documento", "documento.tipo", "documento.numero"],
        [RpcConsultas.Rues] = ["opciones", "nit"],
        [RpcConsultas.Multas] = ["opciones", "documento", "documento.tipo", "documento.numero", "tipoPersona", "placa", "placa.valor"],
        [RpcConsultas.Avaluos] = ["opciones", "placa", "placa.valor", "vin", "vin.valor", "modelo", "cilindraje", "combustible", "pasajeros"],
        [RpcConsultas.Impuesto] = ["opciones", "placa", "placa.valor", "departamentoCodigo"],
    };

    [Fact]
    public async Task Cada_constructor_arma_el_mismo_request_que_hoy_envia_la_fuente_real_y_solo_con_identificacion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteConRuntAsync(api, empresa, ct);

        var porRpc = new Dictionary<string, int>();
        await using var scope = OutboxDePrueba.Scope(api, empresa.Id);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var constructores = scope.ServiceProvider.GetServices<IConstructorSolicitud>().ToList();
        foreach (var celda in await db.Set<CeldaConsulta>().Where(c => c.DiagnosticoId == lote).OrderBy(c => c.VehiculoId).ThenBy(c => c.Fuente).ToListAsync(ct))
        {
            var hoy = await CapturarAsync(scope.ServiceProvider, db, celda, ct);
            var constructor = constructores.Should().ContainSingle(c => c.Fuente == celda.Fuente, $"hay un constructor para {celda.Fuente}").Subject;

            var construidas = await constructor.ConstruirAsync(db, celda, ct);

            construidas.Select(s => s.Rpc).Should().Equal(hoy.Select(h => h.Rpc), $"mismos rpc en {celda.Fuente}");
            construidas.Select(s => s.Solicitud).Should().Equal(hoy.Select(h => h.Solicitud), (a, b) => a.Equals(b), $"mismo *Request en {celda.Fuente}");
            foreach (var solicitud in construidas)
            {
                Campos(solicitud.Solicitud).Should().BeSubsetOf(CamposPermitidos[solicitud.Rpc], $"{solicitud.Rpc} solo lleva identificación");
                porRpc[solicitud.Rpc] = porRpc.GetValueOrDefault(solicitud.Rpc) + 1;
            }
        }

        porRpc.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            [RpcConsultas.PorPlaca] = 2,
            [RpcConsultas.PorVin] = 1,
            // RUNT persona: los propietarios naturales de TST001 y del VIN y el locatario de TST003; RUES: el propietario NIT de TST003.
            [RpcConsultas.Conductor] = 3,
            [RpcConsultas.Rues] = 1,
            // SIMIT por placa (3 vehículos) y por documento (3 propietarios y 1 locatario).
            [RpcConsultas.Multas] = 7,
            [RpcConsultas.Avaluos] = 3,
            [RpcConsultas.Impuesto] = 3,
        }, "cada fuente del alcance tiene su solicitud en los tres vehículos");
    }

    [Fact]
    public async Task Una_celda_de_persona_solo_pide_las_personas_que_no_respondieron()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteConRuntAsync(api, empresa, ct);
        await using var scope = OutboxDePrueba.Scope(api, empresa.Id);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var vehiculo = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.DiagnosticoId == lote && v.IdentificadorNorm == "TST003", ct);
        var celda = await db.Set<CeldaConsulta>().SingleAsync(c => c.VehiculoId == vehiculo.Id && c.Fuente == Fuente.SimitLocatario, ct);
        var locatario = (await DocumentosVehiculo.PersonasDelVehiculoAsync(db, empresa.Id, vehiculo.Id, ct, RolPersona.Locatario)).Single();
        var constructor = scope.ServiceProvider.GetServices<IConstructorSolicitud>().Single(c => c.Fuente == Fuente.SimitLocatario);

        (await constructor.ConstruirAsync(db, celda, ct)).Should().ContainSingle().Which.PersonaId.Should().Be(locatario.Id);
        celda.Payload = $$$"""{"personas":[{"persona_id":"{{{locatario.Id}}}","orden":1,"consulta":"SIMIT","estado":"OK","resultado":{}}]}""";

        (await constructor.ConstruirAsync(db, celda, ct)).Should().BeEmpty("la persona que ya respondió no se vuelve a consumir");
    }

    /// <summary>
    /// Lote con RUNT y todas las fuentes de D3 para TST001, TST003 y el VIN de prueba; procesa solo las RUNT (las demás nacen
    /// diferidas y quedan liberadas, sin ejecutarse) y deja el departamento del impuesto soportado en los tres vehículos.
    /// </summary>
    internal static async Task<Guid> LoteConRuntAsync(ApiDePrueba api, EmpresaDePrueba empresa, CancellationToken ct)
    {
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001"), Lotes.Fila("TST003"), Lotes.Fila(Vin)],
            [Fuente.Runt, Fuente.SimitVehiculo, Fuente.Avaluo, Fuente.ImpuestoDepartamental],
            [Fuente.RuntPropietario, Fuente.SimitPropietario], [Fuente.RuntLocatario, Fuente.SimitLocatario], ct);
        (await Motor.Procesador(api).ProcesarLoteAsync(ct)).Should().Be(3, "la primera vuelta solo toma las RUNT");
        // Ningún organismo de prueba deriva un departamento: se fija el soportado de la réplica (MotorNa).
        await Motor.EjecutarAsync(api,
            $"UPDATE diagnostico.vehiculos SET departamento_codigo = '{MotorNa.DepartamentoSoportadoFicticio}' WHERE diagnostico_id = '{lote}'", ct);
        return lote;
    }

    /// <summary>Lo que hoy envía el ejecutor de la celda con su <c>Fuente*Real</c> a core-consultas, en orden.</summary>
    internal static async Task<IReadOnlyList<(string Rpc, IMessage Solicitud)>> CapturarAsync(IServiceProvider sp, DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct)
    {
        var consultas = new ConsultasCaptura();
        var runt = new RuntIntempoCaptura();
        var cliente = new ClienteConsultas(consultas, NullLogger<ClienteConsultas>.Instance);
        var protector = sp.GetRequiredService<IProtectorDocumentoPersona>();
        var reloj = TimeProvider.System;
        IEjecutorCelda ejecutor = celda.Fuente switch
        {
            Fuente.Runt => new EjecutorCeldaRunt(new FuenteRuntReal(new ClienteRuntIntempo(runt, NullLogger<ClienteRuntIntempo>.Instance)), protector, reloj, NullLogger<EjecutorCeldaRunt>.Instance),
            Fuente.RuntPropietario or Fuente.RuntLocatario => new EjecutorCeldaRuntPersona(
                celda.Fuente, new FuenteRuntPersonaReal(cliente), new FuenteRuesReal(cliente), protector, reloj, NullLogger<EjecutorCeldaRuntPersona>.Instance),
            Fuente.SimitVehiculo => new EjecutorCeldaSimitVehiculo(new FuenteSimitReal(cliente), reloj, NullLogger<EjecutorCeldaSimitVehiculo>.Instance),
            Fuente.SimitPropietario or Fuente.SimitLocatario => new EjecutorCeldaSimitPersona(
                celda.Fuente, new FuenteSimitReal(cliente), protector, reloj, NullLogger<EjecutorCeldaSimitPersona>.Instance),
            Fuente.Avaluo => new EjecutorCeldaAvaluo(new FuenteAvaluoReal(cliente), reloj, NullLogger<EjecutorCeldaAvaluo>.Instance),
            Fuente.ImpuestoDepartamental => new EjecutorCeldaImpuesto(new FuenteImpuestoReal(cliente), reloj, NullLogger<EjecutorCeldaImpuesto>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(celda)),
        };
        var tomada = new CeldaTomada(celda.Id, celda.TenantId, celda.DiagnosticoId, celda.VehiculoId, celda.Fuente, DateTimeOffset.UtcNow.AddMinutes(2));
        await ejecutor.EjecutarAsync(new ContextoCelda(tomada, db), ct);
        return [.. runt.Llamadas, .. consultas.Llamadas];
    }

    /// <summary>Rutas de los campos presentes en el proto-JSON del mensaje (<c>documento.numero</c>, <c>placa.valor</c>…).</summary>
    internal static IEnumerable<string> Campos(IMessage mensaje)
    {
        using var json = System.Text.Json.JsonDocument.Parse(JsonFormatter.Default.Format(mensaje));
        return Rutas(json.RootElement, string.Empty).ToList();

        static IEnumerable<string> Rutas(System.Text.Json.JsonElement elemento, string prefijo)
        {
            foreach (var propiedad in elemento.EnumerateObject())
            {
                var ruta = prefijo.Length == 0 ? propiedad.Name : $"{prefijo}.{propiedad.Name}";
                yield return ruta;
                if (propiedad.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var hija in Rutas(propiedad.Value, ruta))
                        yield return hija;
                }
            }
        }
    }
}

/// <summary>Cliente gRPC de <c>ConsultasService</c> que anota cada solicitud y responde Unavailable (nunca core-consultas).</summary>
internal sealed class ConsultasCaptura : ConsultasService.ConsultasServiceClient
{
    public List<(string Rpc, IMessage Solicitud)> Llamadas { get; } = [];

    public override AsyncUnaryCall<ConsultarConductorResponse> ConsultarConductorAsync(ConsultarConductorRequest request, CallOptions options) =>
        Anotar<ConsultarConductorResponse>(RpcConsultas.Conductor, request);

    public override AsyncUnaryCall<ConsultarRuesResponse> ConsultarRuesAsync(ConsultarRuesRequest request, CallOptions options) =>
        Anotar<ConsultarRuesResponse>(RpcConsultas.Rues, request);

    public override AsyncUnaryCall<ConsultarMultasResponse> ConsultarMultasAsync(ConsultarMultasRequest request, CallOptions options) =>
        Anotar<ConsultarMultasResponse>(RpcConsultas.Multas, request);

    public override AsyncUnaryCall<ConsultarAvaluosResponse> ConsultarAvaluosAsync(ConsultarAvaluosRequest request, CallOptions options) =>
        Anotar<ConsultarAvaluosResponse>(RpcConsultas.Avaluos, request);

    public override AsyncUnaryCall<ConsultarImpuestoVehicularResponse> ConsultarImpuestoVehicularAsync(ConsultarImpuestoVehicularRequest request, CallOptions options) =>
        Anotar<ConsultarImpuestoVehicularResponse>(RpcConsultas.Impuesto, request);

    private AsyncUnaryCall<T> Anotar<T>(string rpc, IMessage solicitud)
    {
        Llamadas.Add((rpc, solicitud));
        return LlamadaFallida.De<T>();
    }
}

/// <summary>Cliente gRPC de <c>RuntIntempoService</c> que anota cada solicitud y responde Unavailable.</summary>
internal sealed class RuntIntempoCaptura : RuntIntempoService.RuntIntempoServiceClient
{
    public List<(string Rpc, IMessage Solicitud)> Llamadas { get; } = [];

    public override AsyncUnaryCall<ConsultarVehiculoIntempoResponse> ConsultarPorPlacaAsync(ConsultarPorPlacaRequest request, CallOptions options)
    {
        Llamadas.Add((RpcConsultas.PorPlaca, request));
        return LlamadaFallida.De<ConsultarVehiculoIntempoResponse>();
    }

    public override AsyncUnaryCall<ConsultarVehiculoIntempoResponse> ConsultarPorVinAsync(ConsultarPorVinRequest request, CallOptions options)
    {
        Llamadas.Add((RpcConsultas.PorVin, request));
        return LlamadaFallida.De<ConsultarVehiculoIntempoResponse>();
    }
}

internal static class LlamadaFallida
{
    public static AsyncUnaryCall<T> De<T>() => new(
        Task.FromException<T>(new RpcException(new Status(StatusCode.Unavailable, "captura de prueba"))),
        Task.FromResult(new Metadata()),
        () => new Status(StatusCode.Unavailable, "captura de prueba"),
        () => [],
        () => { });
}
