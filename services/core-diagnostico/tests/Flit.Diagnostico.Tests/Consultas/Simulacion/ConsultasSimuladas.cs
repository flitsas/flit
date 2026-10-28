using System.Globalization;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TipoErrorFuente = Flit.Diagnostico.Api.Modulos.Consultas.Fuentes.TipoErrorFuente;
using TipoErrorIntempo = Flit.Consultas.Grpc.V1.TipoErrorFuente;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// core-consultas en las pruebas de Diagnóstico (ADR-0075, U3 = a: la simulación de LOCAL y DEV vive en core-consultas). Lee el
/// <c>*Request</c> del trabajo, consulta el doble de la fuente que la prueba registró en la API (<see cref="IFuenteRunt"/>,
/// <see cref="IFuenteRuntPersona"/>, <see cref="IFuenteRues"/>, <see cref="IFuenteSimit"/>, <see cref="IFuenteAvaluo"/>,
/// <see cref="IFuenteImpuesto"/>; sin doble, la fuente responde como no conectada) y convierte su respuesta normalizada en el
/// <c>*Response</c> proto que publicaría core-consultas (o en su <c>grpcStatus</c>): así Diagnóstico la vuelve a leer con sus
/// <c>Mapeos/*</c> reales. Equivalencias de los errores: <c>TIMEOUT</c> → <c>DeadlineExceeded</c>, <c>RED</c> →
/// <c>Unavailable</c>, <c>AUTENTICACION</c> → <c>Unauthenticated</c>, <c>NO_CONECTADO</c> → <c>Unimplemented</c>, <c>HTTP_5XX</c> →
/// un chequeo en ERROR del proveedor, <c>RESPUESTA_INVALIDA</c> → una respuesta ilegible y el resto → <c>InvalidArgument</c>. La
/// RUNT va dentro del mensaje de Intempo (como el proveedor real); la no conectada es <c>FailedPrecondition</c>. Nunca guarda nada.
/// </summary>
/// <example>
/// <code>
/// var respuesta = await ConsultasSimuladas.ResponderAsync(api, trabajo, ct); // RespuestaSimulada
/// </code>
/// </example>
internal static class ConsultasSimuladas
{
    public static async Task<RespuestaSimulada> ResponderAsync(ApiDePrueba api, TrabajoAtendido trabajo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(trabajo);
        try
        {
            return await AtenderAsync(api, trabajo, ct);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or InvalidOperationException))
        {
            // Q4 del diseño: core-consultas atrapa la excepción inesperada y publica ERROR con grpcStatus = Internal (definitivo).
            return RespuestaSimulada.Fallo(StatusCode.Internal);
        }
    }

    private static async Task<RespuestaSimulada> AtenderAsync(ApiDePrueba api, TrabajoAtendido trabajo, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var servicios = scope.ServiceProvider;
        var empresa = trabajo.Sobre.TenantId;
        var solicitud = trabajo.Solicitud.GetRawText();
        if (trabajo.Rpc.StartsWith("ConsultasService/", StringComparison.Ordinal)
            && servicios.GetService<ConsultasService.ConsultasServiceClient>() is { } grpc
            && await GrpcAsync(grpc, trabajo.Rpc, solicitud, empresa, ct) is { } porGrpc)
            return porGrpc;

        switch (trabajo.Rpc)
        {
            case RpcConsultas.PorPlaca:
            case RpcConsultas.PorVin:
                {
                    var valor = trabajo.Rpc == RpcConsultas.PorVin
                        ? JsonParser.Default.Parse<ConsultarPorVinRequest>(solicitud).Vin
                        : JsonParser.Default.Parse<ConsultarPorPlacaRequest>(solicitud).Placa;
                    var tipo = await TipoIdentificadorAsync(api, empresa, trabajo.ConsultaId, ct);
                    var fuente = servicios.GetService<IFuenteRunt>() ?? new FuenteRuntNoConectada();
                    return Runt(await fuente.ConsultarVehiculoAsync(empresa, new IdentificadorVehiculo(tipo, valor), ct));
                }

            case RpcConsultas.Conductor:
                {
                    var r = JsonParser.Default.Parse<ConsultarConductorRequest>(solicitud);
                    if (servicios.GetService<IFuenteRuntPersona>() is not { } fuente)
                        return RespuestaSimulada.Fallo(StatusCode.Unimplemented);
                    var conductor = Respuesta(await fuente.ConsultarAsync(empresa, new DocumentoPersona(r.Documento.Tipo, r.Documento.Numero), ct), Conductor);
                    return r.Opciones?.IncluirRespuestaCruda == true ? ConCrudo(conductor, "kyverum_runt_conductor", "person", CrudoConductor(r.Documento.Numero)) : conductor;
                }

            case RpcConsultas.Rues:
                {
                    var r = JsonParser.Default.Parse<ConsultarRuesRequest>(solicitud);
                    if (servicios.GetService<IFuenteRues>() is not { } fuente)
                        return RespuestaSimulada.Fallo(StatusCode.Unimplemented);
                    var rues = Respuesta(await fuente.ConsultarAsync(empresa, r.Nit, ct), Rues);
                    return r.Opciones?.IncluirRespuestaCruda == true ? ConCrudo(rues, "verifik_rues", "company", CrudoRues(r.Nit)) : rues;
                }

            case RpcConsultas.Multas:
                {
                    var r = JsonParser.Default.Parse<ConsultarMultasRequest>(solicitud);
                    if (servicios.GetService<IFuenteSimit>() is not { } fuente)
                        return RespuestaSimulada.Fallo(StatusCode.Unimplemented);
                    // Como core-consultas con el proveedor por placa (Bug SIMIT por placa, 2026-10-27): con placa consulta el
                    // vehículo aunque venga el documento del propietario; sin placa, la persona.
                    var simit = r.Placa is { Valor.Length: > 0 }
                        ? SolicitudSimit.PorPlaca(r.Placa.Valor)
                        : SolicitudSimit.PorDocumento(new DocumentoPersona(r.Documento.Tipo, r.Documento.Numero));
                    return Respuesta(await fuente.ConsultarAsync(empresa, simit, ct), Multas);
                }

            case RpcConsultas.Avaluos:
                {
                    var r = JsonParser.Default.Parse<ConsultarAvaluosRequest>(solicitud);
                    if (servicios.GetService<IFuenteAvaluo>() is not { } fuente)
                        return RespuestaSimulada.Fallo(StatusCode.Unimplemented);
                    var avaluo = new SolicitudAvaluo(
                        Texto(r.Placa?.Valor), Texto(r.Vin?.Valor), r.Modelo > 0 ? r.Modelo : null, r.Cilindraje > 0 ? r.Cilindraje : null,
                        Texto(r.Combustible), r.Pasajeros > 0 ? r.Pasajeros : null);
                    return Avaluos(await fuente.ConsultarAsync(empresa, avaluo, ct));
                }

            case RpcConsultas.Impuesto:
                {
                    var r = JsonParser.Default.Parse<ConsultarImpuestoVehicularRequest>(solicitud);
                    var fuente = servicios.GetService<IFuenteImpuesto>() ?? new FuenteImpuestoNoConectada();
                    return Respuesta(await fuente.ConsultarAsync(empresa, new SolicitudImpuesto(r.Placa.Valor, r.DepartamentoCodigo), ct), Impuesto);
                }

            default:
                throw new InvalidOperationException($"Trabajo con un rpc desconocido: {trabajo.Rpc}.");
        }
    }

    /// <summary>La RUNT dentro del mensaje de Intempo, como el proveedor real.</summary>
    public static RespuestaSimulada Runt(ResultadoRunt resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        if (resultado.Vehiculo is { } vehiculo)
            return RespuestaSimulada.Ok(new ConsultarVehiculoIntempoResponse { Proveedor = "intempo", Resultado = ResultadoRuntIntempo.Encontrado, Vehiculo = VehiculoIntempo(vehiculo) });

        var respuesta = new ConsultarVehiculoIntempoResponse { Proveedor = "intempo", Resultado = ResultadoRuntIntempo.Error };
        switch (resultado.Error)
        {
            case TipoErrorFuente.NoConectado:
                return RespuestaSimulada.Fallo(StatusCode.FailedPrecondition);
            case TipoErrorFuente.EstadoNoOk:
                respuesta.Resultado = ResultadoRuntIntempo.NoEncontrado;
                break;
            case TipoErrorFuente.Timeout:
                respuesta.TipoError = TipoErrorIntempo.Timeout;
                break;
            case TipoErrorFuente.Red:
                respuesta.TipoError = TipoErrorIntempo.Red;
                break;
            case TipoErrorFuente.Http5xx:
                respuesta.TipoError = TipoErrorIntempo.HttpNoExitoso;
                respuesta.HttpStatus = 503;
                break;
            case TipoErrorFuente.Autenticacion:
                respuesta.TipoError = TipoErrorIntempo.Autenticacion;
                break;
            default:
                respuesta.TipoError = TipoErrorIntempo.RespuestaInvalida;
                break;
        }

        return RespuestaSimulada.Ok(respuesta);
    }

    /// <summary>El vehículo normalizado como lo devolvería Intempo (claves de §5), sin nombres.</summary>
    public static VehiculoIntempo VehiculoIntempo(VehiculoRunt v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var vehiculo = new VehiculoIntempo
        {
            Plate = v.Placa ?? string.Empty,
            Vin = v.Vin ?? string.Empty,
            InformacionGeneral = new InformacionGeneralIntempo
            {
                TipoServicio = v.Servicio ?? string.Empty,
                ClaseVehiculo = v.Clase ?? string.Empty,
                OrganismoTransito = v.Organismo ?? string.Empty,
                Marca = v.Marca ?? string.Empty,
                Linea = v.Linea ?? string.Empty,
                Modelo = Numero(v.Modelo),
                Cilidraje = Numero(v.Cilindraje),
                TipoCombustible = v.Combustible ?? string.Empty,
            },
            DatosTecnicos = new DatosTecnicosIntempo { PasajerosSentados = Numero(v.Pasajeros) },
        };
        foreach (var p in v.Propietarios)
        {
            var propietario = new PropietarioActualIntempo
            {
                IdTipoDocumento = p.TipoDocumento,
                NoDocumento = p.NumeroDocumento,
                DetallePropiedad = p.TipoPropiedad switch
                {
                    TipoPropiedad.Propio => "PROPIO",
                    TipoPropiedad.Leasing => "LEASING",
                    _ => string.Empty,
                },
            };
            propietario.Locatarios.AddRange(p.Locatarios.Select(l => new LocatarioIntempo { Tdoc = l.TipoDocumento, Ndoc = l.NumeroDocumento }));
            vehiculo.PropietariosActuales.Add(propietario);
        }

        vehiculo.Lessee.AddRange(v.Locatarios.Select(l => new PersonaIntempo { DocumentType = l.TipoDocumento, DocumentNumber = l.NumeroDocumento }));
        return vehiculo;
    }

    /// <summary>La respuesta normalizada de una fuente por <c>ConsultasService</c>, o su error.</summary>
    public static RespuestaSimulada Respuesta<T>(RespuestaFuente<T> respuesta, Func<T, string?, IMessage> mensaje)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentNullException.ThrowIfNull(mensaje);
        var proveedor = respuesta.Servicio is { } s ? CodigosDeEnum.Codigo(s).ToLowerInvariant() : null;
        if (respuesta.Error is not { } error)
            return RespuestaSimulada.Ok(mensaje(respuesta.Resultado!, proveedor));

        return error.TipoError switch
        {
            "HTTP_5XX" => RespuestaSimulada.Ok(ConChequeoCaido(mensaje, proveedor)),
            "RESPUESTA_INVALIDA" => new RespuestaSimulada(null),
            _ => RespuestaSimulada.Fallo(Codigo(error.TipoError)),
        };
    }

    /// <summary>El código gRPC con que core-consultas respondería un error de la fuente.</summary>
    public static StatusCode Codigo(string tipoError) => tipoError switch
    {
        "TIMEOUT" => StatusCode.DeadlineExceeded,
        "RED" => StatusCode.Unavailable,
        "AUTENTICACION" => StatusCode.Unauthenticated,
        "NO_CONECTADO" => StatusCode.Unimplemented,
        _ => StatusCode.InvalidArgument,
    };

    /// <summary>
    /// HU #14153 — la respuesta cruda FICTICIA del conductor (forma de Kyverum RUNT persona) que el doble pone en
    /// <c>resultado.respuesta_cruda.json</c> cuando el trabajo pide <c>incluir_respuesta_cruda</c>.
    /// </summary>
    public static string CrudoConductor(string documento) =>
        $$"""{"ok":true,"identidad":{"nombreCompleto":"PERSONA FICTICIA DOBLE"},"persona":{"documento":"{{documento}}","estadoConductor":"ACTIVO"},"licencias":[{"numeroLicencia":"99999000201","estadoLicencia":"ACTIVA","detalleLicencia":[{"categoria":"B1","fechaVencimiento":"2034-01-15"}]},{"numeroLicencia":"99999000202","estadoLicencia":"VENCIDA","detalleLicencia":[{"categoria":"C1","fechaVencimiento":"2022-06-01"}]}],"fromCache":false}""";

    /// <summary>HU #14153 — la respuesta cruda FICTICIA de una empresa (forma de Verifik RUES) con representantes legales y CIIU.</summary>
    public static string CrudoRues(string nit) =>
        $$$"""{"data":{"commercialRegistry":{"NIT":"{{{nit}}}","businessName":"EMPRESA FICTICIA DOBLE S.A.S.","legalRepresentatives":{"legalRepresentatives":[{"documentNumber":"9999900901","documentType":"CC","name":"PERSONA FICTICIA RL","role":"Representante Legal"}]}},"economicActivities":[{"code":"4923","name":"ciiu_act_econ_pri"}]},"id":"DOBLE1"}""";

    /// <summary>La respuesta OK <paramref name="simulada"/> con la respuesta cruda del proveedor en su resultado (un ERROR queda igual).</summary>
    public static RespuestaSimulada ConCrudo(RespuestaSimulada simulada, string proveedor, string tipoSujeto, string json)
    {
        ArgumentNullException.ThrowIfNull(simulada);
        var resultado = simulada.Respuesta switch
        {
            ConsultarConductorResponse c => c.Resultado,
            ConsultarRuesResponse r => r.Resultado,
            _ => null,
        };
        if (resultado is not null)
            resultado.RespuestaCruda = new RespuestaCruda { Proveedor = proveedor, TipoSujeto = tipoSujeto, Json = json };
        return simulada;
    }

    public static IMessage Conductor(ResultadoPersonaRunt r, string? proveedor)
    {
        var resultado = new ResultadoConsulta { Proveedor = proveedor ?? string.Empty };
        resultado.Chequeos.Add(new Chequeo { Clave = "conductor_identidad", Estado = r.Inscrito ? EstadoChequeo.Ok : EstadoChequeo.Fail });
        if (r.EstadoConductor is { } estado)
            resultado.Campos.Add(new Campo { Clave = "person_license_status", ValorTexto = estado });
        if (r.Licencias.Count > 0)
            resultado.Campos.Add(new Campo { Clave = "person_license_categories", ValorTexto = string.Join(",", r.Licencias.Select(l => l.Categoria)) });
        return new ConsultarConductorResponse { Resultado = resultado };
    }

    public static IMessage Rues(ResultadoRues r, string? proveedor)
    {
        var resultado = new ResultadoConsulta { Proveedor = proveedor ?? string.Empty };
        if (r.EstadoMatricula is { } estado)
            resultado.Campos.Add(new Campo { Clave = "rues_estado", ValorTexto = estado });
        if (r.UltimaRenovacion is { } renovacion)
            resultado.Campos.Add(new Campo { Clave = "rues_fecha_renovacion", ValorTexto = renovacion });
        return new ConsultarRuesResponse { Resultado = resultado };
    }

    public static IMessage Multas(ResultadoSimit r, string? proveedor)
    {
        var resultado = new ResultadoConsulta
        {
            Proveedor = proveedor ?? string.Empty,
            Semaforo = r.Semaforo switch
            {
                "VERDE" => Semaforo.Verde,
                "AMARILLO" => Semaforo.Amarillo,
                "ROJO" => Semaforo.Rojo,
                _ => Semaforo.Unspecified,
            },
        };
        var multas = new Chequeo { Clave = "multas", Etiqueta = "Multas", Estado = r.CantidadComparendos == 0 ? EstadoChequeo.Ok : EstadoChequeo.Fail, Fuente = "SIMIT" };
        foreach (var c in r.Comparendos)
        {
            var comparendo = new Comparendo();
            if (c.Numero is { } numero)
                comparendo.Numero = numero;
            if (c.Fecha is { } fecha)
                comparendo.Fecha = fecha;
            if (c.Valor is { } valor)
                comparendo.Valor = valor;
            if (c.Estado is { } estadoComparendo)
                comparendo.Estado = estadoComparendo;
            if (c.Organismo is { } organismo)
                comparendo.Organismo = organismo;
            multas.Comparendos.Add(comparendo);
        }

        resultado.Chequeos.Add(multas);
        return new ConsultarMultasResponse { Resultado = resultado };
    }

    public static IMessage Impuesto(ResultadoImpuesto r, string? proveedor)
    {
        var respuesta = new ConsultarImpuestoVehicularResponse { Resultado = new ResultadoConsulta { Proveedor = proveedor ?? string.Empty }, MontoTotal = r.MontoTotal };
        respuesta.Vigencias.AddRange(r.Vigencias.Select(v => new VigenciaImpuesto { Anio = v.Anio, Valor = v.Valor, Pagada = v.Pagada }));
        return respuesta;
    }

    /// <summary>El avalúo: sus fuentes con su estado, valor y muestras; todas en ERROR si el proveedor no respondió.</summary>
    public static RespuestaSimulada Avaluos(RespuestaFuente<ResultadoAvaluo> respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        if (respuesta.Error is { } error)
        {
            if (error.TipoError != "HTTP_5XX")
                return error.TipoError == "RESPUESTA_INVALIDA" ? new RespuestaSimulada(null) : RespuestaSimulada.Fallo(Codigo(error.TipoError));
            var caidas = new ConsultarAvaluosResponse();
            caidas.Avaluos.Add(new Avaluo { Fuente = respuesta.Servicio is { } s ? CodigosDeEnum.Codigo(s).ToLowerInvariant() : "fasecolda", Estado = EstadoAvaluo.Error });
            return RespuestaSimulada.Ok(caidas);
        }

        var r = respuesta.Resultado!;
        var avaluos = new ConsultarAvaluosResponse { ValorSugerido = r.ValorSugerido ?? 0, FuentePrincipal = r.FuentePrincipal ?? string.Empty };
        avaluos.Avaluos.AddRange(r.Fuentes.Select(f => new Avaluo
        {
            Fuente = f.Fuente,
            Estado = f.Estado switch
            {
                ValorFuenteAvaluo.Ok => EstadoAvaluo.Ok,
                ValorFuenteAvaluo.Error => EstadoAvaluo.Error,
                _ => EstadoAvaluo.SinDatos,
            },
            Valor = f.Valor ?? 0,
            Muestras = f.Muestras ?? 0,
        }));
        return RespuestaSimulada.Ok(avaluos);
    }

    private static IMessage ConChequeoCaido<T>(Func<T, string?, IMessage> mensaje, string? proveedor)
        where T : class
    {
        var caido = new ResultadoConsulta { Proveedor = proveedor ?? string.Empty };
        caido.Chequeos.Add(new Chequeo { Clave = "proveedor", Estado = EstadoChequeo.Error });
        // El mensaje del rpc con un resultado cuyo chequeo quedó en ERROR (el proveedor no respondió).
        var vacio = mensaje.Method.Name switch
        {
            nameof(Conductor) => (IMessage)new ConsultarConductorResponse { Resultado = caido },
            nameof(Rues) => new ConsultarRuesResponse { Resultado = caido },
            nameof(Multas) => new ConsultarMultasResponse { Resultado = caido },
            nameof(Impuesto) => new ConsultarImpuestoVehicularResponse { Resultado = caido },
            _ => throw new InvalidOperationException("Mensaje sin chequeo de proveedor."),
        };
        return vacio;
    }

    /// <summary>
    /// La rpc del trabajo contra el cliente gRPC falso de la prueba (que mira la solicitud y la empresa de la metadata), como la
    /// ejecutaría core-consultas: su respuesta tal cual o su código de error; null si el cliente falso no atiende esa rpc.
    /// </summary>
    private static async Task<RespuestaSimulada?> GrpcAsync(
        ConsultasService.ConsultasServiceClient grpc, string rpc, string solicitud, Guid empresa, CancellationToken ct)
    {
        var opciones = new CallOptions(new Metadata { { Flit.Platform.Sdk.Grpc.PlatformServiceCallInterceptor.TenantMetadata, empresa.ToString() } }, cancellationToken: ct);
        try
        {
            IMessage respuesta = rpc switch
            {
                RpcConsultas.Conductor => await grpc.ConsultarConductorAsync(JsonParser.Default.Parse<ConsultarConductorRequest>(solicitud), opciones).ResponseAsync,
                RpcConsultas.Rues => await grpc.ConsultarRuesAsync(JsonParser.Default.Parse<ConsultarRuesRequest>(solicitud), opciones).ResponseAsync,
                RpcConsultas.Multas => await grpc.ConsultarMultasAsync(JsonParser.Default.Parse<ConsultarMultasRequest>(solicitud), opciones).ResponseAsync,
                RpcConsultas.Avaluos => await grpc.ConsultarAvaluosAsync(JsonParser.Default.Parse<ConsultarAvaluosRequest>(solicitud), opciones).ResponseAsync,
                RpcConsultas.Impuesto => await grpc.ConsultarImpuestoVehicularAsync(JsonParser.Default.Parse<ConsultarImpuestoVehicularRequest>(solicitud), opciones).ResponseAsync,
                _ => throw new InvalidOperationException($"Trabajo con un rpc desconocido: {rpc}."),
            };
            return RespuestaSimulada.Ok(respuesta);
        }
        catch (RpcException ex)
        {
            return RespuestaSimulada.Fallo(ex.StatusCode);
        }
        catch (NotImplementedException)
        {
            // El cliente falso no sobrescribe esa rpc: la atiende el doble de la fuente.
            return null;
        }
    }

    private static async Task<TipoIdentificador> TipoIdentificadorAsync(ApiDePrueba api, Guid empresa, Guid consultaId, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa);
        return await (from c in db.Set<CeldaConsulta>().AsNoTracking()
                      join v in db.Set<VehiculoDiagnostico>().AsNoTracking() on c.VehiculoId equals v.Id
                      where c.Id == consultaId
                      select v.TipoIdentificador).SingleAsync(ct);
    }

    private static string Numero(int? valor) => valor is { } n ? n.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
