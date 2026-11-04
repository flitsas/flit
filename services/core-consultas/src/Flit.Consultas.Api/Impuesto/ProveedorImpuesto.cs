using Flit.Consultas.Api.Bus;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using Grpc.Core;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Fachada del impuesto. Departamento fuera de la lista: <c>FAILED_PRECONDITION</c>, sin llamada.
/// Par sin homologar: chequeo en ERROR, sin llamada. El mensaje del chequeo es fijo.
/// </summary>
internal sealed class ProveedorImpuesto(ITransporteImpuesto transporte, OpcionesImpuesto opciones)
{
    public const string ClaveProveedor = "impuesto";

    public IReadOnlyList<string> Departamentos() => transporte.Listo ? opciones.Departamentos : [];

    public async Task<ConsultarImpuestoVehicularResponse> ConsultarAsync(ConsultarImpuestoVehicularRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!opciones.Soporta(request.DepartamentoCodigo))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Departamento sin consulta de impuesto."));

        var homologado = HomologacionDocumentoImpuesto.Homologar(request.DepartamentoCodigo, request.Documento?.Tipo);
        if (homologado is null || string.IsNullOrWhiteSpace(request.Documento?.Numero))
            return Error("impuesto_documento_no_homologado");

        var cuerpo = SolicitudServicioImpuesto.Crear(
            request.Placa?.Valor ?? "",
            homologado,
            request.Documento!.Numero,
            request.DepartamentoCodigo,
            request.HasOrganismoNombre ? request.OrganismoNombre : null,
            request.HasMunicipio ? request.Municipio : null,
            request.HasDepartamentoNombre ? request.DepartamentoNombre : null,
            request.HasModelo ? request.Modelo : null,
            request.HasPropietarioNombre ? request.PropietarioNombre : null,
            opciones.Contacto);
        var transporteResultado = await transporte.EnviarAsync(cuerpo, ct).ConfigureAwait(false);
        if (transporteResultado.Fallo is { } fallo)
        {
            RegistroFallosProveedor.Anotar(fallo);
            return Error(ClaveTransporte(fallo));
        }

        RegistroFallosProveedor.Anotar((Flit.Tramites.Application.UseCases.Consultations.FalloProveedor?)null);
        return AProto(InterpreteImpuesto.Interpretar(transporteResultado.Cuerpo ?? ""));
    }

    private static string ClaveTransporte(Flit.Tramites.Application.UseCases.Consultations.FalloProveedor fallo) => fallo.Codigo switch
    {
        "CREDENCIALES" => "impuesto_autenticacion",
        "TIMEOUT" => "impuesto_timeout",
        "RED" => "impuesto_red",
        "LIMITE" => "impuesto_limite",
        "RECHAZO" => "impuesto_rechazo",
        _ => "impuesto_caida",
    };

    private static ConsultarImpuestoVehicularResponse Error(string clave) => new()
    {
        Resultado = Resultado(clave, EstadoChequeo.Error, Semaforo.Amarillo, "El impuesto no quedó confirmado."),
    };

    private static ConsultarImpuestoVehicularResponse AProto(VeredictoImpuesto veredicto)
    {
        if (!veredicto.Confirmado)
        {
            var respuesta = Error("impuesto_" + veredicto.Codigo!.ToLowerInvariant());
            respuesta.DatoParcial = veredicto.DatoParcial;
            respuesta.TieneDeuda = veredicto.TieneDeuda;
            if (veredicto.MontoTotal is { } monto)
            {
                respuesta.MontoTotal = monto;
                respuesta.MontoInformado = true;
            }

            return respuesta;
        }

        var confirmada = new ConsultarImpuestoVehicularResponse
        {
            Resultado = Resultado(
                "impuesto",
                veredicto.TieneDeuda ? EstadoChequeo.Fail : EstadoChequeo.Ok,
                veredicto.TieneDeuda ? Semaforo.Rojo : Semaforo.Verde,
                veredicto.TieneDeuda ? "Hay deuda de impuesto." : "Sin deuda de impuesto."),
            TieneDeuda = veredicto.TieneDeuda,
            MontoInformado = veredicto.MontoInformado,
            MontoTotal = veredicto.MontoTotal ?? 0,
        };
        if (veredicto.InteresesMora is { } intereses)
            confirmada.InteresesMora = intereses;
        if (veredicto.Sanciones is { } sanciones)
            confirmada.Sanciones = sanciones;
        if (veredicto.AvaluoComercial is { } avaluo)
            confirmada.AvaluoComercial = avaluo;
        if (veredicto.LiquidacionConsolidadaId is { } liquidacion)
            confirmada.LiquidacionConsolidadaId = liquidacion;
        if (veredicto.PeriodoCertificacion is { } periodo)
            confirmada.PeriodoCertificacion = periodo;
        if (veredicto.FechaConsulta is { } fecha)
            confirmada.FechaConsultaProveedor = fecha;
        if (veredicto.UltimoPagoAnio is not null || veredicto.UltimoPagoValor is not null || veredicto.UltimoPagoFecha is not null)
        {
            confirmada.UltimoPago = new UltimoPagoImpuesto();
            if (veredicto.UltimoPagoAnio is { } anio)
                confirmada.UltimoPago.Anio = anio;
            if (veredicto.UltimoPagoValor is { } valor)
                confirmada.UltimoPago.Valor = valor;
            if (veredicto.UltimoPagoFecha is { } fechaPago)
                confirmada.UltimoPago.Fecha = fechaPago;
        }

        foreach (var vigencia in veredicto.Vigencias)
        {
            var item = new VigenciaImpuesto { Anio = vigencia.Anio, Valor = vigencia.Valor, Pagada = vigencia.Pagada };
            if (vigencia.LiquidacionId is { } id)
                item.LiquidacionId = id;
            if (vigencia.Avaluo is { } avaluoVigencia)
                item.Avaluo = avaluoVigencia;
            confirmada.Vigencias.Add(item);
        }

        return confirmada;
    }

    private static ResultadoConsulta Resultado(string clave, EstadoChequeo estado, Semaforo semaforo, string mensaje) => new()
    {
        Proveedor = ClaveProveedor,
        Semaforo = semaforo,
        Chequeos =
        {
            new Chequeo { Clave = clave, Etiqueta = "Impuesto vehicular", Estado = estado, Fuente = "IMPUESTO", Mensaje = mensaje },
        },
    };
}
