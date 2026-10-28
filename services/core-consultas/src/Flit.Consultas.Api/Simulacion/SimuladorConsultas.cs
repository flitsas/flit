using Flit.Consultas.Api.Bus;
using Flit.Consultas.Grpc.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Flit.Consultas.Api.Simulacion;

/// <summary>
/// Proveedor simulado por fuente para LOCAL y DEV (ADR-0075, U3 = a; T10): responde el <c>*Response</c> proto de cada rpc del
/// diagnóstico, determinista por el ÚLTIMO carácter del identificador, con los mismos escenarios que tenían las fuentes simuladas de
/// core-diagnostico. Solo lo usa <see cref="EjecutorTrabajo"/> con <see cref="OpcionesSimulacion.Habilitada"/> o las fuentes de <see cref="OpcionesSimulacion.Fuentes"/>; no llama a ningún
/// proveedor ni registra nada. Los errores van como los daría el servicio real: un tiempo agotado es <see cref="RpcException"/>
/// <see cref="StatusCode.DeadlineExceeded"/> (Diagnóstico lo traduce como <c>TIMEOUT</c>) y una «respuesta sin la forma esperada»
/// es un <c>*Response</c> sin resultado (<c>RESPUESTA_INVALIDA</c>).
/// <list type="table">
/// <item><term>RUNT</term><description>fixtures de <see cref="FixturesIntempo"/>: 1 persona natural, 2 NIT, 3 leasing con locatario C,
/// 4 leasing con locatario N, 5 servicio público, 6 departamento no soportado, 7 error <c>TIMEOUT</c>, 8 respuesta inválida, otro
/// persona natural; el vehículo lleva la placa o el VIN consultado.</description></item>
/// <item><term>RUNT persona</term><description>2 inscrita sin licencias, 3 licencia suspendida, 7 tiempo agotado, 8 respuesta
/// inválida, otro inscrita con licencia B1.</description></item>
/// <item><term>SIMIT</term><description>2 rojo con 2 comparendos, 3 amarillo con acuerdo de pago, 6 rojo con 60, 7 tiempo agotado, 8
/// respuesta inválida, otro verde.</description></item>
/// <item><term>RUES</term><description>6 cancelada, 7 tiempo agotado, 8 respuesta inválida, otro activa.</description></item>
/// <item><term>Avalúo</term><description>2 solo Fasecolda (48 M), 4 sin datos, 7 tiempo agotado, otro tres fuentes (52 M).</description></item>
/// <item><term>Impuesto</term><description>0 pagado, 7 tiempo agotado, otro con deuda (400 000).</description></item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// var r = (ConsultarVehiculoIntempoResponse)simulador.Responder(RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = "ABC123" });
/// // "ABC123" termina en 3 → leasing con locatario C
/// </code>
/// </example>
internal sealed class SimuladorConsultas(TimeProvider time)
{
    /// <summary>Proveedor con que se mide el consumo simulado.</summary>
    public const string Proveedor = "simulado";

    private const string Organismo = "SECRETARIA DE TRANSITO DE PRUEBA";

    private const string RuntIntempoPorDefecto = "intempo-persona-natural.json";

    private static readonly Dictionary<char, string> EscenariosRunt = new()
    {
        ['1'] = RuntIntempoPorDefecto,
        ['2'] = "intempo-nit.json",
        ['3'] = "intempo-leasing-locatario-c.json",
        ['4'] = "intempo-leasing-locatario-n.json",
        ['5'] = "intempo-servicio-publico.json",
        ['6'] = "intempo-departamento-no-soportado.json",
        ['7'] = "intempo-error-timeout.json",
        ['8'] = "intempo-error-respuesta-invalida.json",
    };

    private static readonly JsonParser Lector = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(false));

    public IMessage Responder(string rpc, IMessage solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return (rpc, solicitud) switch
        {
            (RpcConsultas.PorPlaca, ConsultarPorPlacaRequest r) => Runt(r.Placa, esVin: false),
            (RpcConsultas.PorVin, ConsultarPorVinRequest r) => Runt(r.Vin, esVin: true),
            (RpcConsultas.Conductor, ConsultarConductorRequest r) => Conductor(r.Documento?.Numero),
            (RpcConsultas.Multas, ConsultarMultasRequest r) => Multas(r.Placa?.Valor ?? r.Documento?.Numero),
            (RpcConsultas.Rues, ConsultarRuesRequest r) => Rues(r.Nit),
            (RpcConsultas.Avaluos, ConsultarAvaluosRequest r) => Avaluos(r.Placa?.Valor ?? r.Vin?.Valor),
            (RpcConsultas.Impuesto, ConsultarImpuestoVehicularRequest r) => Impuesto(r.Placa?.Valor),
            _ => throw new RpcException(new Status(StatusCode.Unimplemented, "Rpc sin simulación.")),
        };
    }

    private static ConsultarVehiculoIntempoResponse Runt(string identificador, bool esVin)
    {
        var valor = identificador?.Trim() ?? string.Empty;
        var archivo = valor.Length > 0 && EscenariosRunt.TryGetValue(valor[^1], out var a) ? a : RuntIntempoPorDefecto;
        var respuesta = Lector.Parse<ConsultarVehiculoIntempoResponse>(FixturesIntempo.Todos()[archivo]);
        if (respuesta.Vehiculo is { } vehiculo && valor.Length > 0)
        {
            vehiculo.InformacionGeneral ??= new InformacionGeneralIntempo();
            if (esVin)
            {
                vehiculo.Vin = valor;
                vehiculo.InformacionGeneral.NoVin = valor;
            }
            else
            {
                vehiculo.Plate = valor;
                vehiculo.InformacionGeneral.NoPlaca = valor;
            }
        }

        return respuesta;
    }

    private ConsultarConductorResponse Conductor(string? documento)
    {
        ResultadoConsulta Inscrita(string estado, string? categorias)
        {
            var r = Resultado("kyverum_runt", Semaforo.Verde);
            r.Chequeos.Add(new Chequeo { Clave = "conductor_identidad", Etiqueta = "Persona en RUNT", Estado = EstadoChequeo.Ok, Fuente = "RUNT" });
            r.Campos.Add(new Campo { Clave = "person_license_status", ValorTexto = estado });
            if (categorias is not null)
                r.Campos.Add(new Campo { Clave = "person_license_categories", ValorTexto = categorias });
            return r;
        }

        return UltimoDigito(documento) switch
        {
            '2' => new ConsultarConductorResponse { Resultado = Inscrita("ACTIVO", null) },
            '3' => new ConsultarConductorResponse { Resultado = Inscrita("SUSPENDIDO", "B1") },
            '7' => throw TiempoAgotado(),
            '8' => new ConsultarConductorResponse(),
            _ => new ConsultarConductorResponse { Resultado = Inscrita("ACTIVO", "B1") },
        };
    }

    private ConsultarMultasResponse Multas(string? clave)
    {
        ConsultarMultasResponse Con(Semaforo semaforo, params Comparendo[] comparendos)
        {
            var r = Resultado("smartcloud", semaforo);
            var chequeo = new Chequeo { Clave = "multas", Etiqueta = "Multas", Estado = comparendos.Length == 0 ? EstadoChequeo.Ok : EstadoChequeo.Fail, Fuente = "SIMIT" };
            chequeo.Comparendos.AddRange(comparendos);
            r.Chequeos.Add(chequeo);
            return new ConsultarMultasResponse { Resultado = r };
        }

        static Comparendo C(string numero, string fecha, string valor, string estado) =>
            new() { Numero = numero, Fecha = fecha, Valor = valor, Estado = estado, Organismo = Organismo };

        return UltimoCaracter(clave) switch
        {
            '2' => Con(Semaforo.Rojo, C("SIM-0001", "2026-01-10", "604100.00", "PENDIENTE"), C("SIM-0002", "2026-02-11", "302050.00", "PENDIENTE")),
            '3' => Con(Semaforo.Amarillo, C("SIM-0003", "2025-11-03", "604100.00", "ACUERDO DE PAGO")),
            '6' => Con(Semaforo.Rojo, [.. Enumerable.Range(1, 60).Select(i =>
                C($"SIM-6{i:D3}", $"2026-{(i % 12) + 1:D2}-{(i % 28) + 1:D2}", "302050.00", "PENDIENTE"))]),
            '7' => throw TiempoAgotado(),
            '8' => new ConsultarMultasResponse(),
            _ => Con(Semaforo.Verde),
        };
    }

    private ConsultarRuesResponse Rues(string? nit)
    {
        ConsultarRuesResponse Con(string estado, string renovacion)
        {
            var r = Resultado("verifik_rues", Semaforo.Verde);
            r.Certificaciones = new Certificaciones();
            r.Certificaciones.MatriculasMercantiles.Add(new MatriculaMercantil
            {
                Nit = new ValorCertificado { Valor = nit },
                Estado = new EstadoVigencia { Valor = estado == "ACTIVA" ? Vigencia.Vigente : Vigencia.Vencido, Crudo = estado },
                RenovadaEn = new FechaCertificada { Valor = renovacion },
            });
            return new ConsultarRuesResponse { Resultado = r };
        }

        return UltimoDigito(nit) switch
        {
            '6' => Con("CANCELADA", "2024-03-31"),
            '7' => throw TiempoAgotado(),
            '8' => new ConsultarRuesResponse(),
            _ => Con("ACTIVA", "2026-03-31"),
        };
    }

    private static ConsultarAvaluosResponse Avaluos(string? clave)
    {
        const string Fasecolda = "fasecolda";
        const string MercadoLibre = "mercado_libre";
        const string BaseGravable = "base_gravable";
        static Avaluo Ok(string fuente, long valor, int muestras = 0) => new() { Fuente = fuente, Estado = EstadoAvaluo.Ok, Valor = valor, Muestras = muestras };
        static Avaluo SinDatos(string fuente) => new() { Fuente = fuente, Estado = EstadoAvaluo.SinDatos };
        static ConsultarAvaluosResponse Con(long sugerido, string principal, params Avaluo[] avaluos)
        {
            var r = new ConsultarAvaluosResponse { ValorSugerido = sugerido, FuentePrincipal = principal };
            r.Avaluos.AddRange(avaluos);
            return r;
        }

        return UltimoCaracter(clave) switch
        {
            '2' => Con(48_000_000, Fasecolda, Ok(Fasecolda, 48_000_000), SinDatos(MercadoLibre), SinDatos(BaseGravable)),
            '4' => Con(0, string.Empty, SinDatos(Fasecolda), SinDatos(MercadoLibre), SinDatos(BaseGravable)),
            '7' => throw TiempoAgotado(),
            _ => Con(52_000_000, Fasecolda, Ok(Fasecolda, 52_000_000), Ok(MercadoLibre, 50_500_000, 9), Ok(BaseGravable, 45_000_000)),
        };
    }

    private ConsultarImpuestoVehicularResponse Impuesto(string? placa)
    {
        ConsultarImpuestoVehicularResponse Con(params VigenciaImpuesto[] vigencias)
        {
            var monto = vigencias.Where(v => !v.Pagada).Sum(v => v.Valor);
            var r = new ConsultarImpuestoVehicularResponse { Resultado = Resultado("impuesto_simulado", monto > 0 ? Semaforo.Rojo : Semaforo.Verde), MontoTotal = monto };
            r.Vigencias.AddRange(vigencias);
            return r;
        }

        return UltimoCaracter(placa) switch
        {
            '0' => Con(new() { Anio = 2025, Valor = 180_000, Pagada = true }, new() { Anio = 2026, Valor = 175_000, Pagada = true }),
            '7' => throw TiempoAgotado(),
            _ => Con(new() { Anio = 2025, Valor = 210_000, Pagada = false }, new() { Anio = 2026, Valor = 190_000, Pagada = false }),
        };
    }

    private ResultadoConsulta Resultado(string proveedor, Semaforo semaforo) =>
        new() { Proveedor = proveedor, Semaforo = semaforo, ConsultadoEn = Timestamp.FromDateTimeOffset(time.GetUtcNow()) };

    private static RpcException TiempoAgotado() => new(new Status(StatusCode.DeadlineExceeded, "Proveedor simulado: tiempo de espera agotado."));

    private static char UltimoDigito(string? numero) => numero?.LastOrDefault(char.IsAsciiDigit) ?? '0';

    private static char UltimoCaracter(string? valor) => valor?.Trim() is { Length: > 0 } t ? t[^1] : '1';
}
