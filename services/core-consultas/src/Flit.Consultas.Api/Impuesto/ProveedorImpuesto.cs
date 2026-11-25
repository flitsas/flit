using System.Text.RegularExpressions;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.Consultations;
using Grpc.Core;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Fachada del impuesto asíncrono (diseño §2.3–§2.4, D9). Departamento inactivo en el catálogo: <c>FAILED_PRECONDITION</c>. Con los
/// 33 códigos DANE activos (P5' del 2026-11-23) el filtro sigue, pero la cobertura la decide la API: 422
/// <c>department_unsupported</c> → <c>impuesto_departamento_no_soportado</c> (Diagnóstico lo cierra en N/A), 409 o <c>failed</c>
/// asíncrono <c>source_inactive</c> → <c>impuesto_fuente_inactiva</c>, y el 422 por falta de código de departamento →
/// <c>impuesto_rechazo</c>. Cuando la fuente cierra el trabajo sin veredicto confirmado (rechazo de negocio, solicitud inválida, no
/// confirmado, respuesta ilegible o tiempo agotado) deja un Warning 7436 con identificadores y códigos de forma estricta; nunca texto
/// libre ni valores de la fuente. HU #14711: si hay aviso del proveedor (<see cref="Avisos.CallbackAvisoImpuesto"/>), el POST lleva
/// <c>callbackUrl</c> y la espera entre sondeos la corta el aviso (<see cref="Avisos.BuzonAvisosImpuesto"/>); el polling queda de
/// respaldo y los dos cierran por <see cref="CerrarConResultadoAsync"/>.
/// </summary>
internal sealed partial class ProveedorImpuesto(
    ITransporteImpuesto transporte,
    OpcionesImpuesto opciones,
    ICatalogoDepartamentosImpuesto catalogo,
    IFuenteCredencialImpuesto credenciales,
    ISeguimientoImpuesto seguimiento,
    PlanSondeoImpuesto plan,
    TimeProvider time,
    ILogger<ProveedorImpuesto> logger,
    Avisos.CallbackAvisoImpuesto? callback = null,
    Avisos.BuzonAvisosImpuesto? buzon = null)
{
    public const string ClaveProveedor = "impuesto";

    /// <summary>La fuente no tiene consulta para el departamento (422 <c>department_unsupported</c>): Diagnóstico la cierra en N/A.</summary>
    public const string ClaveDepartamentoNoSoportado = "impuesto_departamento_no_soportado";

    /// <summary>La fuente del departamento está inactiva (409 o <c>failed</c> con <c>source_inactive</c>): ERROR reprocesable.</summary>
    public const string ClaveFuenteInactiva = "impuesto_fuente_inactiva";

    private const string MensajeDepartamentoNoSoportado =
        "La liquidación del impuesto aún no está disponible para el departamento donde está matriculado el vehículo.";

    private const string MensajeFuenteInactiva =
        "La consulta del impuesto de este departamento no está disponible en este momento. Intenta de nuevo más tarde.";

    private const string MensajeNoConfirmado = "El impuesto no quedó confirmado.";

    private static readonly Regex ArchivoId = ArchivoIdRegex();
    private const string CodigoWebhookNoConfigurado = "webhook_not_configured";
    private const string OrigenSondeo = "sondeo";
    private const string OrigenAviso = "aviso";
    private const int MaxPdfBytes = 3 * 1024 * 1024;

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
        string? cuerpoConAviso = null;
        async Task<string> CuerpoConAvisoAsync()
        {
            if (cuerpoConAviso is not null)
                return cuerpoConAviso;
            var url = callback is null ? null : await callback.UrlAsync(caller.TenantId, ct).ConfigureAwait(false);
            cuerpoConAviso = url is null ? cuerpo : SolicitudServicioImpuesto.Crear(request, homologado, opciones.Contacto, url);
            return cuerpoConAviso;
        }

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
            (var envio, cred, var respaldo) = await EnviarConAvisoAsync(await CuerpoConAvisoAsync().ConfigureAwait(false), cuerpo, cred, caller.TenantId, ct)
                .ConfigureAwait(false);
            if (respaldo)
                cuerpoConAviso = cuerpo;
            if (envio.Fallo is { } falloEnvio)
            {
                await seguimiento.CerrarAsync(trabajoId, "error", ct).ConfigureAwait(false);
                RegistroFallosProveedor.Anotar(falloEnvio);
                return CerrarEnvioFallido(trabajoId, request.DepartamentoCodigo, falloEnvio, envio.CodigoError);
            }

            var ahoraEnvio = time.GetUtcNow();
            await seguimiento.GuardarIdExternoAsync(trabajoId, envio.IdExterno!, ahoraEnvio, ct).ConfigureAwait(false);
            seg = new SeguimientoImpuesto(trabajoId, envio.IdExterno, ahoraEnvio, "queued");
        }

        var limite = (seg.EnviadoEn ?? time.GetUtcNow()) + plan.Presupuesto;
        var n = 0;
        var fallosSeguidos = 0;
        string? ultimoEstado = null;

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

            // HU #14711: con buzón, la espera la corta el aviso del proveedor; si llega, cierra sin GET por el mismo camino.
            if (buzon is null)
            {
                await Task.Delay(espera, time, ct).ConfigureAwait(false);
            }
            else if (await buzon.EsperarAsync(caller.TenantId, seg.IdExterno!, espera, ct).ConfigureAwait(false) is { } aviso)
            {
                return await CerrarConResultadoAsync(
                    trabajoId, caller.TenantId, seg.IdExterno, request.DepartamentoCodigo, aviso.Estado, aviso.CuerpoConsulta, OrigenAviso, ct)
                    .ConfigureAwait(false);
            }

            var estado = await transporte.ConsultarAsync(seg.IdExterno!, cred.ApiKey!, ct).ConfigureAwait(false);
            n++;
            ultimoEstado = EstadoObservado(estado.Estado);

            // NoEncontrado (404) se trata como estado, aunque el transporte adjunte Fallo.
            if (estado.Estado == EstadoTrabajoExterno.NoEncontrado
                || (estado.Fallo?.HttpStatus == 404 && estado.Estado is EstadoTrabajoExterno.Desconocido or EstadoTrabajoExterno.NoEncontrado))
            {
                if (reanudado && !reenviado && limite - time.GetUtcNow() >= TimeSpan.FromSeconds(60))
                {
                    await seguimiento.OlvidarIdExternoAsync(trabajoId, ct).ConfigureAwait(false);
                    reenviado = true;
                    reanudado = false;
                    (var envio, cred, var respaldo) = await EnviarConAvisoAsync(await CuerpoConAvisoAsync().ConfigureAwait(false), cuerpo, cred, caller.TenantId, ct)
                        .ConfigureAwait(false);
                    if (respaldo)
                        cuerpoConAviso = cuerpo;
                    if (envio.Fallo is { } falloRe)
                    {
                        await seguimiento.CerrarAsync(trabajoId, "error", ct).ConfigureAwait(false);
                        RegistroFallosProveedor.Anotar(falloRe);
                        return CerrarEnvioFallido(trabajoId, request.DepartamentoCodigo, falloRe, envio.CodigoError);
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
                    return await CerrarConResultadoAsync(
                        trabajoId, caller.TenantId, seg.IdExterno, request.DepartamentoCodigo, estado.Estado, estado.Cuerpo ?? "", OrigenSondeo, ct)
                        .ConfigureAwait(false);

                default:
                    fallosSeguidos = 0;
                    continue;
            }
        }

        await seguimiento.CerrarAsync(trabajoId, "timeout", ct).ConfigureAwait(false);
        RegistroFallosProveedor.Anotar((FalloProveedor?)null);
        AvisarSinVeredicto(trabajoId, seg.IdExterno, request.DepartamentoCodigo, ultimoEstado, null, null,
            "impuesto_no_confirmado_tiempo", []);
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

    /// <summary>
    /// Cierre de la consulta con un resultado terminal, el MISMO para el GET del sondeo y para el aviso del proveedor (HU #14711):
    /// cierra el seguimiento, descarta lo que quede en el buzón, interpreta el cuerpo (el aviso trae el <c>consulta</c> con la forma
    /// del GET) y devuelve la respuesta que el consumidor publica a Diagnóstico. Regla de idempotencia: cada ejecución del trabajo
    /// pasa por aquí una sola vez (el primer resultado terminal gana y el método retorna), así que hay una sola publicación; un aviso
    /// que llega después, aunque traiga un <c>attempt</c> mayor, encuentra el trabajo cerrado y se descarta (el polling de hoy
    /// tampoco reabre una consulta cerrada). Si el consumidor se cae antes de confirmar, el SDK reentrega el trabajo, su publicación
    /// anterior se revirtió y el sondeo reanudado vuelve a cerrar y publicar una vez.
    /// </summary>
    private async Task<ConsultarImpuestoVehicularResponse> CerrarConResultadoAsync(
        Guid trabajoId, Guid tenantId, string? idExterno, string? departamento, EstadoTrabajoExterno estado, string cuerpo, string origen,
        CancellationToken ct)
    {
        await seguimiento.CerrarAsync(trabajoId, EstadoCierre(estado), ct).ConfigureAwait(false);
        buzon?.Olvidar(tenantId, idExterno);
        RegistroFallosProveedor.Anotar((FalloProveedor?)null);
        var veredicto = InterpreteImpuesto.Interpretar(cuerpo);
        var respuesta = AProto(veredicto);
        if (!veredicto.Confirmado)
        {
            AvisarSinVeredicto(trabajoId, idExterno, departamento, veredicto.EstadoFuente,
                veredicto.EstadoResultadoFuente, veredicto.CodigoErrorFuente, respuesta.Resultado.Chequeos[0].Clave,
                veredicto.Advertencias);
        }

        ProveedorImpuestoLog.Cerrada(logger, trabajoId, origen);
        return respuesta;
    }

    /// <summary>
    /// POST con el <c>callbackUrl</c> si lo hay (<paramref name="cuerpo"/>); si el proveedor responde 422 <c>webhook_not_configured</c>
    /// (la empresa perdió el secreto después de cachearlo), se reenvía sin él (<paramref name="cuerpoSinAviso"/>) y sigue el polling.
    /// </summary>
    /// <summary>
    /// Envía con <c>callbackUrl</c>; ante 422 <c>webhook_not_configured</c> reenvía el cuerpo sin él y lo indica en <c>Respaldo</c>
    /// para que quien llama memorice ese cuerpo (N-3) y un reenvío posterior no repita la ida y vuelta del 422.
    /// </summary>
    private async Task<(EnvioImpuesto Envio, CredencialImpuesto Cred, bool Respaldo)> EnviarConAvisoAsync(
        string cuerpo, string cuerpoSinAviso, CredencialImpuesto cred, Guid tenantId, CancellationToken ct)
    {
        var (envio, credUsada) = await EnviarConRefrescoAsync(cuerpo, cred, tenantId, ct).ConfigureAwait(false);
        if (envio.Fallo is { HttpStatus: 422 } && string.Equals(envio.CodigoError, CodigoWebhookNoConfigurado, StringComparison.Ordinal)
            && !ReferenceEquals(cuerpo, cuerpoSinAviso))
        {
            var (respaldo, credRespaldo) = await EnviarConRefrescoAsync(cuerpoSinAviso, credUsada, tenantId, ct).ConfigureAwait(false);
            return (respaldo, credRespaldo, true);
        }

        return (envio, credUsada, false);
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

    /// <summary>
    /// Warning 7436: la fuente no dejó veredicto confirmado. Solo identificadores y códigos que pasan una forma estricta
    /// (los demás quedan en <c>no_reconocido</c>); nunca mensajes ni valores de campos de la fuente (Ley 1581).
    /// </summary>
    private void AvisarSinVeredicto(
        Guid trabajoId, string? idExterno, string? departamento, string? estadoFuente, string? estadoResultado,
        string? codigoError, string clave, IReadOnlyList<AdvertenciaLeida> advertencias)
    {
        var avisos = advertencias.Count == 0
            ? "ninguna"
            : string.Join(",", advertencias.Select(a => a.Tipo == TipoAdvertenciaLeida.Validacion
                ? "validacion:" + Identificador(a.Campo)
                : "aviso:" + InterpreteImpuesto.CodigoSeguro(a.Codigo)));
        ProveedorImpuestoLog.SinVeredicto(
            logger,
            trabajoId,
            Identificador(idExterno),
            Identificador(departamento),
            InterpreteImpuesto.CodigoSeguro(estadoFuente),
            InterpreteImpuesto.CodigoSeguro(estadoResultado),
            InterpreteImpuesto.CodigoSeguro(codigoError),
            InterpreteImpuesto.CodigoSeguro(clave),
            avisos);
    }

    private static string Identificador(string? valor) =>
        valor is not null && ArchivoId.IsMatch(valor) ? valor : InterpreteImpuesto.NoReconocido;

    private static string? EstadoObservado(EstadoTrabajoExterno estado) => estado switch
    {
        EstadoTrabajoExterno.EnCola => "queued",
        EstadoTrabajoExterno.Ejecutando => "running",
        _ => null,
    };

    private static string EstadoCierre(EstadoTrabajoExterno estado) => estado switch
    {
        EstadoTrabajoExterno.Exitoso => "succeeded",
        EstadoTrabajoExterno.Parcial => "partial",
        EstadoTrabajoExterno.Fallido => "failed",
        _ => "error",
    };

    /// <summary>
    /// POST rechazado. Un 422/409 de negocio tiene su propia clave y deja el Warning 7436 con el <c>error.code</c> (forma estricta);
    /// lo demás cae en la clave del transporte. El <c>RegistroFallosProveedor</c> ya quedó con la clase <c>Rechazo</c> del transporte,
    /// igual que cualquier 400/422: no cuenta como caída.
    /// </summary>
    private ConsultarImpuestoVehicularResponse CerrarEnvioFallido(Guid trabajoId, string? departamento, FalloProveedor fallo, string? codigoError)
    {
        if (ClaveRechazoNegocio(fallo.HttpStatus, codigoError) is not { } clave)
            return Error(ClaveTransporte(fallo));

        AvisarSinVeredicto(trabajoId, null, departamento, "http_" + fallo.HttpStatus, null, codigoError, clave, []);
        return Error(clave);
    }

    private static string? ClaveRechazoNegocio(int? httpStatus, string? codigoError) => (httpStatus, codigoError) switch
    {
        (422, "department_unsupported") => ClaveDepartamentoNoSoportado,
        (422, TransporteImpuestoHttp.CodigoDepartamentoAusente) => "impuesto_rechazo",
        (409, "source_inactive") => ClaveFuenteInactiva,
        (409, _) => "impuesto_rechazo",
        _ => null,
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
        Resultado = Resultado(clave, EstadoChequeo.Error, Semaforo.Amarillo, clave switch
        {
            ClaveDepartamentoNoSoportado => MensajeDepartamentoNoSoportado,
            ClaveFuenteInactiva => MensajeFuenteInactiva,
            _ => MensajeNoConfirmado,
        }),
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

internal static partial class ProveedorImpuestoLog
{
    [LoggerMessage(EventId = 7437, Level = LogLevel.Information, Message = "Impuesto: trabajo {TrabajoId} cerrado por {Origen}.")]
    public static partial void Cerrada(ILogger logger, Guid trabajoId, string origen);

    [LoggerMessage(EventId = 7436, Level = LogLevel.Warning, Message = "Impuesto sin veredicto confirmado: trabajo {TrabajoId}, consulta externa {IdExterno}, departamento {Departamento}, estado {EstadoFuente}, estado del resultado {EstadoResultado}, error_code {CodigoError}, clave {Clave}, advertencias {Advertencias}.")]
    public static partial void SinVeredicto(
        ILogger logger, Guid trabajoId, string idExterno, string departamento, string estadoFuente, string estadoResultado,
        string codigoError, string clave, string advertencias);
}
