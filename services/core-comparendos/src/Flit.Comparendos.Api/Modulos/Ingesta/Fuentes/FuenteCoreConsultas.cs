using System.Globalization;
using Flit.Comparendos.Domain.Modelo;
using Flit.Consultas.Grpc.V1;
using Grpc.Core;
using CanalProto = Flit.Consultas.Grpc.V1.CanalComparendos;
using TipoErrorFuente = Flit.Comparendos.Domain.Modelo.TipoErrorFuente;
using ResultadoFuente = Flit.Consultas.Grpc.V1.ResultadoFuente;
using ResultadoConsulta = Flit.Comparendos.Domain.Modelo.ResultadoConsulta;

namespace Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;

/// <summary>
/// Las fuentes por core-consultas (HU #13520, gRPC <c>FuentesComparendosService</c>, scope platform.consultas). Traduce
/// el canal y la clave al pedido, y la respuesta a <see cref="RespuestaFuente"/>. Un error de la llamada (servicio caído,
/// proveedor no conectado) es un error de la clave: nunca se lee como «sin comparendos».
/// </summary>
public sealed class FuenteCoreConsultas(FuentesComparendosService.FuentesComparendosServiceClient cliente) : IFuenteComparendos
{
    public static CanalProto Canal(CanalIngesta c) => c switch
    {
        CanalIngesta.SimitNit => CanalProto.SimitIdentificacion,
        CanalIngesta.SimitPlaca => CanalProto.SimitPlaca,
        CanalIngesta.MunicipalNit => CanalProto.MunicipalIdentificacion,
        _ => CanalProto.MunicipalPlaca,
    };

    public static CanalIngesta? Canal(CanalProto c) => c switch
    {
        CanalProto.SimitIdentificacion => CanalIngesta.SimitNit,
        CanalProto.SimitPlaca => CanalIngesta.SimitPlaca,
        CanalProto.MunicipalIdentificacion => CanalIngesta.MunicipalNit,
        CanalProto.MunicipalPlaca => CanalIngesta.MunicipalPlaca,
        _ => null,
    };

    public async Task<IReadOnlyList<ProveedorFuente>> ProveedoresAsync(CancellationToken ct)
    {
        var r = await cliente.ListarProveedoresComparendosAsync(new ListarProveedoresComparendosRequest(), cancellationToken: ct).ConfigureAwait(false);
        return r.Proveedores.Select(p => new ProveedorFuente(p.Clave, p.Nombre, p.Canales.Select(Canal).OfType<CanalIngesta>().ToList(), p.Conectado,
            p.HasMotivo ? p.Motivo : null)).ToList();
    }

    public async Task<RespuestaFuente> ConsultarAsync(ClaveConsulta clave, string proveedor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clave);
        var porPlaca = clave.Canal is CanalIngesta.SimitPlaca or CanalIngesta.MunicipalPlaca;
        var pedido = new ConsultarComparendosRequest { Canal = Canal(clave.Canal), Proveedor = proveedor };
        if (porPlaca)
            pedido.Placa = clave.Clave;
        else
            (pedido.TipoDocumento, pedido.NumeroDocumento) = ((clave.TipoDocumento ?? TipoDocumento.NIT).ToString(), clave.Clave);
        try
        {
            var r = await cliente.ConsultarComparendosAsync(pedido, cancellationToken: ct).ConfigureAwait(false);
            var parciales = r.Parciales.Select(p => new ResultadoParcial(p.HasMunicipio ? p.Municipio : null, p.Resultado switch
            {
                ResultadoFuente.ExitosaConDatos => ResultadoConsulta.ExitosaConDatos,
                ResultadoFuente.ExitosaVacia => ResultadoConsulta.ExitosaVacia,
                _ => ResultadoConsulta.Error,
            }, p.Comparendos.Select(Item).ToList(), p.Resultado == ResultadoFuente.Error ? Tipo(p.TipoError) : null, p.HasMensajeError ? p.MensajeError : null)).ToList();
            if (parciales.Count == 0)
                return RespuestaFuente.Error(TipoErrorFuente.RespuestaInvalida, "core-consultas no devolvió resultados.");
            return new RespuestaFuente(parciales, r.HasHttpStatus ? r.HttpStatus : null, r.DuracionMs, r.HasRespuestaCruda ? r.RespuestaCruda : null,
                r.HasContentType ? r.ContentType : "application/json");
        }
        catch (RpcException ex) when (ex.StatusCode != StatusCode.Cancelled || !ct.IsCancellationRequested)
        {
            var tipo = ex.StatusCode switch
            {
                StatusCode.DeadlineExceeded => TipoErrorFuente.Timeout,
                StatusCode.Unavailable => TipoErrorFuente.Red,
                StatusCode.Unauthenticated or StatusCode.PermissionDenied => TipoErrorFuente.Autenticacion,
                _ => TipoErrorFuente.Excepcion,
            };
            return RespuestaFuente.Error(tipo, $"core-consultas: {ex.Status.Detail}");
        }
    }

    private static TipoErrorFuente Tipo(Flit.Consultas.Grpc.V1.TipoErrorFuente t) => t switch
    {
        Flit.Consultas.Grpc.V1.TipoErrorFuente.Timeout => TipoErrorFuente.Timeout,
        Flit.Consultas.Grpc.V1.TipoErrorFuente.HttpNoExitoso => TipoErrorFuente.HttpNoExitoso,
        Flit.Consultas.Grpc.V1.TipoErrorFuente.SoapFault => TipoErrorFuente.SoapFault,
        Flit.Consultas.Grpc.V1.TipoErrorFuente.RespuestaInvalida => TipoErrorFuente.RespuestaInvalida,
        Flit.Consultas.Grpc.V1.TipoErrorFuente.Red => TipoErrorFuente.Red,
        Flit.Consultas.Grpc.V1.TipoErrorFuente.Autenticacion => TipoErrorFuente.Autenticacion,
        Flit.Consultas.Grpc.V1.TipoErrorFuente.EstadoNoOk => TipoErrorFuente.EstadoNoOk,
        _ => TipoErrorFuente.Excepcion,
    };

    private static DateOnly? Fecha(bool tiene, string valor) =>
        tiene && DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal? Pesos(bool tiene, string valor) =>
        tiene && decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static ItemFuente Item(ComparendoDeFuente c) => new()
    {
        Numero = c.Numero,
        Placa = c.HasPlaca ? c.Placa : null,
        TipoDocumentoInfractor = c.HasTipoDocumentoInfractor && Enum.TryParse<TipoDocumento>(c.TipoDocumentoInfractor, true, out var td) && Enum.IsDefined(td) ? td : null,
        NumeroDocumentoInfractor = c.HasNumeroDocumentoInfractor ? c.NumeroDocumentoInfractor : null,
        NombreInfractor = c.HasNombreInfractor ? c.NombreInfractor : null,
        CodigoInfraccion = c.HasCodigoInfraccion ? c.CodigoInfraccion : null,
        DescripcionInfraccion = c.HasDescripcionInfraccion ? c.DescripcionInfraccion : null,
        DireccionInfraccion = c.HasDireccionInfraccion ? c.DireccionInfraccion : null,
        FechaImposicion = Fecha(c.HasFechaImposicion, c.FechaImposicion),
        HoraImposicion = c.HasHoraImposicion && TimeOnly.TryParseExact(c.HoraImposicion, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var h) ? h : null,
        FechaNotificacion = Fecha(c.HasFechaNotificacion, c.FechaNotificacion),
        EsFotomulta = c.HasEsFotomulta ? c.EsFotomulta : null,
        Secretaria = c.HasSecretaria ? c.Secretaria : null,
        TipoVehiculo = c.HasTipoVehiculo ? c.TipoVehiculo : null,
        ServicioVehiculo = c.HasServicioVehiculo ? c.ServicioVehiculo : null,
        ValorCapital = Pesos(c.HasValorCapital, c.ValorCapital),
        ValorAdicional = Pesos(c.HasValorAdicional, c.ValorAdicional),
        ValorTotal = Pesos(c.HasValorTotal, c.ValorTotal),
        EstadoFuente = c.HasEstadoFuente ? c.EstadoFuente : null,
        FotoEvidenciaUrl = c.HasFotoEvidenciaUrl ? c.FotoEvidenciaUrl : null,
    };
}
