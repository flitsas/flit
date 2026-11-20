using System.Text.RegularExpressions;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.Consultations;
using Grpc.Core;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Fachada del impuesto asíncrono (diseño §2.3–§2.4, D9). Departamento fuera de la lista: <c>FAILED_PRECONDITION</c>.
/// </summary>
internal sealed partial class ProveedorImpuesto(
    ITransporteImpuesto transporte,
    OpcionesImpuesto opciones,
    ICatalogoDepartamentosImpuesto catalogo,
    IFuenteCredencialImpuesto credenciales,
    ISeguimientoImpuesto seguimiento,
    PlanSondeoImpuesto plan,
    TimeProvider time,
    ILogger<ProveedorImpuesto> logger)
{
    public const string ClaveProveedor = "impuesto";
    private static readonly Regex ArchivoId = ArchivoIdRegex();
    private const int MaxPdfBytes = 3 * 1024 * 1024;

    // logger: parámetro exigido por DI/tests; no se registra la llave ni PII aquí.
    private ILogger<ProveedorImpuesto> Logger => logger;

    /// <summary>Códigos activos. Vacía si el transporte no está listo.</summary>
    public async Task<IReadOnlyList<string>> DepartamentosAsync(CancellationToken ct)
    {
        if (!transporte.Listo)
            return [];
        var filas = await catalogo.ActivosAsync(ct).ConfigureAwait(false);
        return filas.Select(f => f.Codigo).ToArray();
    }

    public async Task<ConsultarImpuestoVehicularResponse> ConsultarAsync(
        ConsultarImpuestoVehicularRequest request,
        PlatformServiceCaller caller,
        Guid trabajoId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);
        _ = Logger;
        if (!transporte.Listo || !await catalogo.ActivoAsync(request.DepartamentoCodigo, ct).ConfigureAwait(false))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Departamento sin consulta de impuesto."));

        var homologado = HomologacionDocumentoImpuesto.Homologar(request.Documento?.Tipo);
        if (homologado is null || string.IsNullOrWhiteSpace(request.Documento?.Numero))
            return Error("impuesto_documento_no_homologado");

        CredencialImpuesto cred;
        try
        {
            cred = await credenciales.ObtenerAsync(caller.TenantId, forzar: false, ct).ConfigureAwait(false);
        }
        catch (FalloCredencialImpuestoException ex)
        {
            RegistroFallosProveedor.Anotar(ex.Fallo);
            return Error(ex.Clave);
        }

        if (!cred.Disponible || string.IsNullOrEmpty(cred.ApiKey))
        {
            RegistroFallosProveedor.Anotar(new FalloProveedor(ClaseFalloProveedor.Credenciales));
            return Error("impuesto_credencial_no_configurada");
        }

        var cuerpo = SolicitudServicioImpuesto.Crear(request, homologado, opciones.Contacto);
        var campoInvalido = SolicitudServicioImpuesto.Validar(cuerpo, departamentoActivo: true);
        if (campoInvalido is not null)
        {
            RegistroFallosProveedor.Anotar((FalloProveedor?)null);
            return Error("impuesto_solicitud_invalida");
        }

        var seg = await seguimiento.LeerAsync(trabajoId, ct).ConfigureAwait(false)
            ?? await seguimiento.ReservarAsync(trabajoId, caller.TenantId, request.DepartamentoCodigo, ct).ConfigureAwait(false);

        var reanudado = seg.IdExterno is not null;
        var reenviado = false;

        if (seg.IdExterno is null)
        {
            (var envio, cred) = await EnviarConRefrescoAsync(cuerpo, cred, caller.TenantId, ct).ConfigureAwait(false);
            if (envio.Fallo is { } falloEnvio)
            {
                await seguimiento.CerrarAsync(trabajoId, "error", ct).ConfigureAwait(false);
                RegistroFallosProveedor.Anotar(falloEnvio);
                return Error(ClaveTransporte(falloEnvio));
            }

            var ahoraEnvio = time.GetUtcNow();
            await seguimiento.GuardarIdExternoAsync(trabajoId, envio.IdExterno!, ahoraEnvio, ct).ConfigureAwait(false);
            seg = new SeguimientoImpuesto(trabajoId, envio.IdExterno, ahoraEnvio, "queued");
        }

        var limite = (seg.EnviadoEn ?? time.GetUtcNow()) + plan.Presupuesto;
        var n = 0;
        var fallosSeguidos = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var ahora = time.GetUtcNow();
            if (ahora >= limite)
                break;

            var espera = plan.Intervalo(n);
            var restante = limite - ahora;
            if (espera > restante)
                espera = restante;

            await Task.Delay(espera, time, ct).ConfigureAwait(false);
            var estado = await transporte.ConsultarAsync(seg.IdExterno!, cred.ApiKey!, ct).ConfigureAwait(false);
            n++;

            // NoEncontrado (404) se trata como estado, aunque el transporte adjunte Fallo.
            if (estado.Estado == EstadoTrabajoExterno.NoEncontrado
                || (estado.Fallo?.HttpStatus == 404 && estado.Estado is EstadoTrabajoExterno.Desconocido or EstadoTrabajoExterno.NoEncontrado))
            {
                if (reanudado && !reenviado && limite - time.GetUtcNow() >= TimeSpan.FromSeconds(60))
                {
                    await seguimiento.OlvidarIdExternoAsync(trabajoId, ct).ConfigureAwait(false);
                    reenviado = true;
                    reanudado = false;
                    (var envio, cred) = await EnviarConRefrescoAsync(cuerpo, cred, caller.TenantId, ct).ConfigureAwait(false);
                    if (envio.Fallo is { } falloRe)
                    {
                        await seguimiento.CerrarAsync(trabajoId, "error", ct).ConfigureAwait(false);
                        RegistroFallosProveedor.Anotar(falloRe);
                        return Error(ClaveTransporte(falloRe));
                    }

                    var ahoraRe = time.GetUtcNow();
                    await seguimiento.GuardarIdExternoAsync(trabajoId, envio.IdExterno!, ahoraRe, ct).ConfigureAwait(false);
                    seg = new SeguimientoImpuesto(trabajoId, envio.IdExterno, ahoraRe, "queued");
                    fallosSeguidos = 0;
                    continue;
                }

                RegistroFallosProveedor.Anotar(estado.Fallo ?? new FalloProveedor(ClaseFalloProveedor.Caida, 404));
                return Error("impuesto_caida");
            }

            if (estado.Fallo is { } falloEstado)
            {
                if (falloEstado.Clase == ClaseFalloProveedor.Credenciales && falloEstado.HttpStatus == 401)
                {
                    var cred2 = await credenciales.ObtenerAsync(caller.TenantId, forzar: true, ct).ConfigureAwait(false);
                    if (cred2.Disponible && cred2.Version != cred.Version && !string.IsNullOrEmpty(cred2.ApiKey))
                    {
                        cred = cred2;
                        fallosSeguidos = 0;
                        continue;
                    }

                    RegistroFallosProveedor.Anotar(falloEstado);
                    return Error("impuesto_autenticacion");
                }

                if (falloEstado.Clase is ClaseFalloProveedor.Caida or ClaseFalloProveedor.Red or ClaseFalloProveedor.Timeout
                    || (falloEstado.HttpStatus is >= 500 and <= 599))
                {
                    fallosSeguidos++;
                    if (fallosSeguidos >= 3)
                    {
                        RegistroFallosProveedor.Anotar(falloEstado);
                        return Error(ClaveTransporte(falloEstado));
                    }

                    continue;
                }

                RegistroFallosProveedor.Anotar(falloEstado);
                return Error(ClaveTransporte(falloEstado));
            }

            switch (estado.Estado)
            {
                case EstadoTrabajoExterno.EnCola:
                case EstadoTrabajoExterno.Ejecutando:
                    fallosSeguidos = 0;
                    continue;

                case EstadoTrabajoExterno.Exitoso:
                case EstadoTrabajoExterno.Parcial:
                case EstadoTrabajoExterno.Fallido:
                    await seguimiento.CerrarAsync(trabajoId, EstadoCierre(estado.Estado), ct).ConfigureAwait(false);
                    RegistroFallosProveedor.Anotar((FalloProveedor?)null);
                    return AProto(InterpreteImpuesto.Interpretar(estado.Cuerpo ?? ""));

                default:
                    fallosSeguidos = 0;
                    continue;
            }
        }

        await seguimiento.CerrarAsync(trabajoId, "timeout", ct).ConfigureAwait(false);
        RegistroFallosProveedor.Anotar((FalloProveedor?)null);
        return Error("impuesto_no_confirmado_tiempo");
    }

    public async Task<DescargarLiquidacionImpuestoResponse> DescargarAsync(
        string archivoId, PlatformServiceCaller caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (string.IsNullOrWhiteSpace(archivoId) || !ArchivoId.IsMatch(archivoId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "archivo_id inválido."));

        CredencialImpuesto cred;
        try
        {
            cred = await credenciales.ObtenerAsync(caller.TenantId, forzar: false, ct).ConfigureAwait(false);
        }
        catch (FalloCredencialImpuestoException)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Fuente de credencial no disponible."));
        }

        if (!cred.Disponible || string.IsNullOrEmpty(cred.ApiKey))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "CREDENCIAL_NO_CONFIGURADA"));

        var archivo = await transporte.DescargarAsync(archivoId, cred.ApiKey, ct).ConfigureAwait(false);
        if (archivo.Fallo is { Clase: ClaseFalloProveedor.Credenciales, HttpStatus: 401 })
        {
            var cred2 = await credenciales.ObtenerAsync(caller.TenantId, forzar: true, ct).ConfigureAwait(false);
            if (cred2.Disponible && cred2.Version != cred.Version && !string.IsNullOrEmpty(cred2.ApiKey))
                archivo = await transporte.DescargarAsync(archivoId, cred2.ApiKey, ct).ConfigureAwait(false);
            else
                throw new RpcException(new Status(StatusCode.PermissionDenied, "CREDENCIAL_RECHAZADA"));
        }

        if (archivo.NoEncontrado || archivo.Fallo?.HttpStatus == 404)
            throw new RpcException(new Status(StatusCode.NotFound, "Liquidación no encontrada."));
        if (archivo.Fallo is not null)
            throw new RpcException(new Status(StatusCode.Unavailable, "Fuente de liquidación no disponible."));
        if (archivo.Contenido is null || archivo.Contenido.Length == 0)
            throw new RpcException(new Status(StatusCode.Internal, "la fuente no devolvió un PDF"));
        if (archivo.Contenido.Length > MaxPdfBytes)
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "PDF demasiado grande."));
        if (archivo.Contenido.AsSpan(0, Math.Min(5, archivo.Contenido.Length)).SequenceEqual("%PDF-"u8) is false)
            throw new RpcException(new Status(StatusCode.Internal, "la fuente no devolvió un PDF"));

        return new DescargarLiquidacionImpuestoResponse
        {
            Contenido = Google.Protobuf.ByteString.CopyFrom(archivo.Contenido),
            ContentType = string.IsNullOrWhiteSpace(archivo.ContentType) ? "application/pdf" : archivo.ContentType,
        };
    }

    private async Task<(EnvioImpuesto Envio, CredencialImpuesto Cred)> EnviarConRefrescoAsync(
        string cuerpo, CredencialImpuesto cred, Guid tenantId, CancellationToken ct)
    {
        var envio = await transporte.EnviarAsync(cuerpo, cred.ApiKey!, ct).ConfigureAwait(false);
        if (envio.Fallo is not { Clase: ClaseFalloProveedor.Credenciales, HttpStatus: 401 })
            return (envio, cred);

        var cred2 = await credenciales.ObtenerAsync(tenantId, forzar: true, ct).ConfigureAwait(false);
        if (cred2.Disponible && cred2.Version != cred.Version && !string.IsNullOrEmpty(cred2.ApiKey))
            return (await transporte.EnviarAsync(cuerpo, cred2.ApiKey, ct).ConfigureAwait(false), cred2);

        return (envio, cred);
    }

    private static string EstadoCierre(EstadoTrabajoExterno estado) => estado switch
    {
        EstadoTrabajoExterno.Exitoso => "succeeded",
        EstadoTrabajoExterno.Parcial => "partial",
        EstadoTrabajoExterno.Fallido => "failed",
        _ => "error",
    };

    private static string ClaveTransporte(FalloProveedor fallo) => fallo.Codigo switch
    {
        "CREDENCIALES" => "impuesto_autenticacion",
        "TIMEOUT" => "impuesto_timeout",
        "RED" => "impuesto_red",
        "LIMITE" => "impuesto_limite",
        "RECHAZO" => "impuesto_rechazo",
        _ => "impuesto_caida",
    };

    private ConsultarImpuestoVehicularResponse Error(string clave) => new()
    {
        Resultado = Resultado(clave, EstadoChequeo.Error, Semaforo.Amarillo, "El impuesto no quedó confirmado."),
    };

    private ConsultarImpuestoVehicularResponse AProto(VeredictoImpuesto veredicto)
    {
        if (!veredicto.Confirmado)
        {
            var clave = veredicto.Codigo switch
            {
                "SOLICITUD_INVALIDA" => "impuesto_solicitud_invalida",
                _ => "impuesto_" + (veredicto.Codigo ?? "no_confirmado").ToLowerInvariant(),
            };
            var respuesta = Error(clave);
            respuesta.DatoParcial = veredicto.DatoParcial;
            respuesta.TieneDeuda = veredicto.TieneDeuda;
            if (veredicto.MontoTotal is { } monto)
            {
                respuesta.MontoTotal = monto;
                respuesta.MontoInformado = true;
            }

            CopiarAdvertencias(respuesta, veredicto);
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
            DatoParcial = veredicto.DatoParcial,
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
            if (vigencia.ReferenciaPago is { } referencia)
                item.ReferenciaPago = referencia;
            if (vigencia.FechaVencimiento is { } vencimiento)
                item.FechaVencimiento = vencimiento;
            confirmada.Vigencias.Add(item);
        }

        CopiarAdvertencias(confirmada, veredicto);
        return confirmada;
    }

    private static void CopiarAdvertencias(ConsultarImpuestoVehicularResponse respuesta, VeredictoImpuesto veredicto)
    {
        foreach (var a in veredicto.Advertencias)
        {
            var item = new AdvertenciaImpuesto
            {
                Tipo = a.Tipo == TipoAdvertenciaLeida.Validacion
                    ? TipoAdvertenciaImpuesto.Validacion
                    : TipoAdvertenciaImpuesto.Aviso,
                Campo = a.Campo,
                Codigo = a.Codigo,
            };
            if (a.Anio is { } anio)
                item.Anio = anio;
            respuesta.Advertencias.Add(item);
        }
    }

    private ResultadoConsulta Resultado(string clave, EstadoChequeo estado, Semaforo semaforo, string mensaje) => new()
    {
        Proveedor = transporte.NombreProveedor,
        Semaforo = semaforo,
        Chequeos =
        {
            new Chequeo { Clave = clave, Etiqueta = "Impuesto vehicular", Estado = estado, Fuente = "IMPUESTO", Mensaje = mensaje },
        },
    };

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ArchivoIdRegex();
}
