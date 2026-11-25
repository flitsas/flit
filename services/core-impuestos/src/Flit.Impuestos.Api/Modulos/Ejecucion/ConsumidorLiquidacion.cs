using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Catalogos;
using Flit.Impuestos.Api.Modulos.Conciliacion;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Impuestos.Api.Modulos.Ejecucion;

/// <summary>
/// Datos de <c>impuestos.liquidacion.solicitada</c> v1 (<c>impuestos-events.v1.yaml</c>): solo ids, ni placa ni documento.
/// </summary>
public sealed record LiquidacionSolicitada(Guid LiquidacionId, Guid ItemId)
{
    public const string Tipo = "impuestos.liquidacion.solicitada";
    public const int Version = 1;
    public const string Cola = "impuestos.liquidaciones";

    /// <summary>Lo publica y lo consume el propio servicio (exchange <c>flit.impuestos</c>).</summary>
    public const string Productor = "impuestos";
}

/// <summary>
/// Consumidor de <see cref="LiquidacionSolicitada"/> en la cola <see cref="LiquidacionSolicitada.Cola"/> (plan §2.2; F2a-CF2, F2a-CF7,
/// F1a-CF12, F3-CF4, F3-CF11). El SDK abre la transacción y anota la bandeja: una re-entrega del mismo <c>eventId</c> no repite el
/// efecto. Por cada mensaje:
/// <list type="number">
///   <item>Fija la empresa del sobre (<see cref="EmpresaDelTrabajo"/>), lo primero: un ítem de otra empresa no se ve y el mensaje se
///   descarta.</item>
///   <item>Solo actúa sobre un ítem en vuelo: <c>CONSULTANDO_RUNT</c>, o <c>ENVIANDO</c> con <c>proximo_intento_en</c> vencido (la vez
///   anterior «no salió»). Un <c>ENVIANDO</c> sin esa marca es un POST que quedó en el aire (el proceso cayó tras el registro previo):
///   pasa a <c>INCIERTO</c> sin volver a llamar.</item>
///   <item>Con el <see cref="FrenoGlobal"/> en pausa deja el ítem para el fin de la pausa sin consultar nada.</item>
///   <item>RUNT por <c>svc-impuestos</c> (<see cref="IClienteRunt"/>), homologación de la secretaría, <c>/fuentes</c> de la caché y
///   <see cref="ConstructorSolicitud"/> con la copia de configuración del lote. Datos incompletos, departamento no habilitado o placa
///   que el RUNT no registra cierran el ítem sin llamar al API; un RUNT transitorio lo reprograma (30 s, 2 min; al tercero,
///   <c>ERROR</c> reintentable).</item>
///   <item>Registro previo: <c>ENVIANDO</c>, hash del token nuevo, <c>con_callback</c>, <c>enviado_en</c> y la PII cifrada se
///   confirman en su propio contexto ANTES del POST (el API no tiene idempotencia).</item>
///   <item>POST con la api_key de la compañía y el <c>callbackUrl</c> solo si <see cref="ConstructorSolicitud.ConCallback"/>. 202 ⇒
///   <c>ENVIADO</c> con el primer GET de respaldo; 400/409/422 ⇒ <c>ERROR</c> con el código y su clase; 422
///   <c>webhook_not_configured</c> ⇒ se reenvía el mismo ítem sin <c>callbackUrl</c> (como <c>LISTO_SIN_WEBHOOK</c>) y queda un aviso
///   en el log; 401 ⇒ <c>ERROR</c> y el aprovisionamiento pasa a <c>REQUIERE_CREDENCIALES</c> con alerta; «no salió» ⇒ sigue
///   <c>ENVIANDO</c> y se reintenta a los <see cref="OpcionesEjecucion.EsperaNoSalio"/> con token nuevo (el token no se guarda);
///   timeout, corte o 5xx ⇒ <c>INCIERTO</c>, nunca se reenvía.</item>
///   <item>Al cerrar el paso: la transición va por <see cref="AplicadorResultado.TransicionarAsync"/> (contadores y estado del lote
///   de M10) y, si el ítem dejó de estar en vuelo, el <see cref="DespachadorCredito"/> libera el siguiente en la transacción del SDK.
///   El 401 lo aplica <see cref="CredencialRechazadaEnUso"/>; el primer GET de respaldo, <see cref="CalendarioConciliacion.Primero"/>.</item>
/// </list>
/// Logs: ids, estados y códigos; nunca placa, documento, nombre, payload ni credenciales.
/// </summary>
/// <example>
/// <code>
/// services.AddFlitConsumer&lt;ImpuestosDb, ConsumidorLiquidacion, LiquidacionSolicitada&gt;(configuration, LiquidacionSolicitada.Cola,
///     LiquidacionSolicitada.Productor, [LiquidacionSolicitada.Tipo]);
/// </code>
/// </example>
public sealed partial class ConsumidorLiquidacion(
    IServiceScopeFactory scopes,
    EmpresaDelTrabajo empresa,
    ImpuestosDb db,
    DespachadorCredito despachador,
    IClienteRunt runt,
    HomologacionSecretaria homologacion,
    SincronizacionFuentes fuentes,
    AlmacenCredenciales almacen,
    ClienteApiLiquidacion api,
    GeneradorTokenAviso tokens,
    FrenoGlobal freno,
    IProteccionImpuestos proteccion,
    CredencialRechazadaEnUso credencialRechazada,
    OpcionesConciliacion conciliacion,
    IOptions<OpcionesEjecucion> opciones,
    TimeProvider reloj,
    ILogger<ConsumidorLiquidacion> log) : IEventConsumer<LiquidacionSolicitada>
{
    private const int LargoCodigoError = 48;

    private OpcionesEjecucion Opciones => opciones.Value;

    public async Task HandleAsync(EventEnvelope envelope, LiquidacionSolicitada data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);
        if (envelope.TenantId == Guid.Empty)
        {
            Descartado(log, data.ItemId, "SIN_EMPRESA");
            return;
        }

        empresa.Fijar(envelope.TenantId);
        var item = await db.Set<LiquidacionItem>().AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == data.ItemId && i.LiquidacionId == data.LiquidacionId, ct).ConfigureAwait(false);
        var ahora = reloj.GetUtcNow();
        switch (item)
        {
            case null:
                Descartado(log, data.ItemId, "SIN_ITEM");
                return;
            case { Estado: EstadoItem.ConsultandoRunt } when item.ProximoIntentoEn is null || item.ProximoIntentoEn <= ahora:
                await ProcesarAsync(envelope.TenantId, item, reenvio: false, ct).ConfigureAwait(false);
                return;
            case { Estado: EstadoItem.Enviando, ProximoIntentoEn: null }:
                EnElAire(log, item.Id);
                await AplicarAsync(envelope.TenantId, item.Id, new EventoItem.FallaTrasEnviar(), null, ct).ConfigureAwait(false);
                return;
            case { Estado: EstadoItem.Enviando } when item.ProximoIntentoEn <= ahora:
                await ProcesarAsync(envelope.TenantId, item, reenvio: true, ct).ConfigureAwait(false);
                return;
            default:
                FueraDeVuelo(log, item.Id, item.Estado);
                return;
        }
    }

    /// <summary>
    /// Aplica una transición ya calculada a la fila (estado, intentos, error, campo faltante y la espera del reintento:
    /// <see cref="OpcionesEjecucion.EsperaReintento"/> según el intento). Un reintento con token nuevo (<c>rate_limited</c>) suelta la
    /// consulta anterior: la siguiente es una consulta nueva. Lo usa también la aplicación del resultado (M10) para el
    /// <c>rate_limited</c> que llega por webhook o GET; quien llama guarda.
    /// </summary>
    public static void AplicarReintento(LiquidacionItem item, ResultadoItem resultado, DateTimeOffset ahora, OpcionesEjecucion opciones)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(opciones);
        var nueva = resultado.Nueva;
        item.Estado = nueva.Estado;
        item.IntentosRunt = nueva.IntentosRunt;
        item.IntentosReenvio = nueva.IntentosReenvio;
        if (nueva.Estado == EstadoItem.Error)
        {
            item.ErrorClase = nueva.ClaseError;
            item.ErrorCodigo = resultado.ErrorCodigo is { Length: > LargoCodigoError } largo ? largo[..LargoCodigoError] : resultado.ErrorCodigo;
        }

        if (resultado.CampoFaltante is not null)
            item.CampoFaltante = resultado.CampoFaltante;
        item.ProximoIntentoEn = nueva.Estado == EstadoItem.ReintentoProgramado
            ? ahora + opciones.EsperaReintento(resultado.TokenNuevo ? nueva.IntentosReenvio : nueva.IntentosRunt)
            : null;
        if (resultado.TokenNuevo)
        {
            item.TokenHash = null;
            item.ConsultaApiId = null;
            item.ConCallback = false;
            item.ProximoPollEn = null;
            item.AttemptApiMax = 0;
        }

        item.ActualizadoEn = ahora;
    }

    private async Task ProcesarAsync(Guid tenant, LiquidacionItem item, bool reenvio, CancellationToken ct)
    {
        if (freno.PausaHasta is { } hasta)
        {
            await ReprogramarAsync(item.Id, hasta, ct).ConfigureAwait(false);
            EnPausa(log, item.Id);
            return;
        }

        var consulta = await runt.ConsultarPorPlacaAsync(tenant, item.Placa, ct).ConfigureAwait(false);
        if (consulta is not ConsultaRunt.Encontrado encontrado)
        {
            var transitorio = consulta is ConsultaRunt.Transitorio;
            if (transitorio)
                freno.RegistrarTransitorio();
            RuntSinDatos(log, item.Id, transitorio ? "transitorio" : "no encontrado");
            if (reenvio)
                await ReprogramarAsync(item.Id, reloj.GetUtcNow() + Opciones.EsperaNoSalio, ct).ConfigureAwait(false);
            else
                await AplicarAsync(tenant, item.Id, transitorio ? new EventoItem.RuntTransitorio() : new EventoItem.RuntPlacaNoEncontrada(), null, ct).ConfigureAwait(false);
            return;
        }

        var datos = encontrado.Datos;
        var secretaria = await homologacion.HomologarAsync(null, datos.OrganismoNombre, ct).ConfigureAwait(false);
        var copia = CopiaConfiguracion.DesdeJson(await db.Set<Liquidacion>().AsNoTracking().Where(l => l.Id == item.LiquidacionId)
            .Select(l => l.ConfigCopia).SingleAsync(ct).ConfigureAwait(false));
        var aprovisionamiento = await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var credenciales = await almacen.LeerAsync(db, tenant, ct).ConfigureAwait(false);
        var token = tokens.Generar();
        var conCallback = aprovisionamiento is not null && ConstructorSolicitud.ConCallback(
            aprovisionamiento.Estado, aprovisionamiento.ModoRespaldoFirma, credenciales?.WebhookSecret is not null, tokens.Configurado);
        var url = conCallback ? tokens.CallbackUrl(token.Token) : null;
        var construccion = ConstructorSolicitud.Construir(item.Placa, datos, secretaria, copia, await FuentesAsync(tenant, ct).ConfigureAwait(false), url);

        if (construccion is not Construccion.Lista lista)
        {
            if (reenvio)
            {
                await ReprogramarAsync(item.Id, reloj.GetUtcNow() + Opciones.EsperaNoSalio, ct).ConfigureAwait(false);
                return;
            }

            EventoItem evento = construccion is Construccion.Incompleta incompleta
                ? new EventoItem.RuntDatoFaltante(incompleta.Campo)
                : new EventoItem.DepartamentoFueraDeContrato();
            await AplicarAsync(tenant, item.Id, evento, i => GuardarDatos(i, construccion.Datos), ct).ConfigureAwait(false);
            return;
        }

        var solicitud = lista.Solicitud;
        await freno.EsperarTurnoAsync(ct).ConfigureAwait(false);
        if (!await RegistrarEnvioAsync(tenant, item.Id, lista.Datos, url is null ? null : token.Hash, desdeRunt: !reenvio, ct).ConfigureAwait(false))
            return;
        if (credenciales is null)
        {
            await AplicarAsync(tenant, item.Id, new EventoItem.NoAutorizado(), null, ct).ConfigureAwait(false);
            return;
        }

        var resultado = await EnviarAsync(credenciales.ApiKey, solicitud, ct).ConfigureAwait(false);
        if (solicitud.CallbackUrl is not null && resultado is { Clase: ClaseEnvio.Respuesta, Status: StatusCodes.Status422UnprocessableEntity, CodigoError: ErroresApi.WebhookNotConfigured })
        {
            // El proveedor no tiene el secreto de la compañía: no es un error de datos. Mismo ítem, sin callbackUrl (se concilia por GET).
            SinWebhookEnProveedor(log, tenant, item.Id);
            solicitud = solicitud with { CallbackUrl = null };
            await freno.EsperarTurnoAsync(ct).ConfigureAwait(false);
            if (!await RegistrarEnvioAsync(tenant, item.Id, lista.Datos, null, desdeRunt: false, ct).ConfigureAwait(false))
                return;
            resultado = await EnviarAsync(credenciales.ApiKey, solicitud, ct).ConfigureAwait(false);
        }

        await AplicarEnvioAsync(tenant, item.Id, resultado, aprovisionamiento?.ModoRespaldoFirma ?? false, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// POST al proveedor. El turno del <see cref="FrenoGlobal"/> se toma antes del registro previo, para que entre la relectura del
    /// lote (<see cref="RegistrarEnvioAsync"/>) y la llamada no quede una espera en la que el lote se pueda cerrar.
    /// </summary>
    private async Task<ResultadoEnvio<ConsultaCreada>> EnviarAsync(string apiKey, SolicitudConsulta solicitud, CancellationToken ct)
    {
        try
        {
            return await api.CrearConsultaAsync(apiKey, solicitud, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            // El cliente no llama si falta su configuración: con certeza no salió.
            return ResultadoEnvio<ConsultaCreada>.NoSalio(ex.GetType().Name);
        }
    }

    private Task AplicarEnvioAsync(Guid tenant, Guid itemId, ResultadoEnvio<ConsultaCreada> r, bool modoRespaldo, CancellationToken ct)
    {
        if (r.Exitoso)
        {
            freno.RegistrarExito();
            Enviada(log, itemId);
            return AplicarAsync(tenant, itemId, new EventoItem.Aceptado(), i =>
            {
                i.ConsultaApiId = r.Valor!.Id;
                i.ProximoPollEn = CalendarioConciliacion.Primero(
                    i.EnviadoEn ?? reloj.GetUtcNow(), CalendarioConciliacion.SinWebhook(i.ConCallback, modoRespaldo), conciliacion);
            }, ct);
        }

        ConsultaNoAceptada(log, itemId, r.Clase, r.Status, r.CodigoError);
        switch (r.Clase)
        {
            case ClaseEnvio.NoSalio:
                freno.RegistrarTransitorio();
                return AplicarAsync(tenant, itemId, new EventoItem.NoSalio(), i => i.ProximoIntentoEn = reloj.GetUtcNow() + Opciones.EsperaNoSalio, ct);
            case ClaseEnvio.Incierto:
                freno.RegistrarTransitorio();
                return AplicarAsync(tenant, itemId, new EventoItem.FallaTrasEnviar(), null, ct);
            default:
                return r.Status == StatusCodes.Status401Unauthorized
                    ? AplicarAsync(tenant, itemId, new EventoItem.NoAutorizado(), null, ct)
                    : AplicarAsync(tenant, itemId, new EventoItem.Rechazado(r.CodigoError ?? "http_status"), null, ct);
        }
    }

    /// <summary>
    /// Registro previo al POST en un contexto propio que se confirma ya (F2a-CF7): <c>ENVIANDO</c>, hash del token (null sin
    /// callbackUrl), <c>con_callback</c>, <c>enviado_en</c> y los datos del propietario cifrados. Va en una transacción con la cabecera
    /// del lote bloqueada (<c>FOR UPDATE</c>, el mismo bloqueo que toman <see cref="Flit.Impuestos.Api.Modulos.Liquidaciones.CerrarLiquidacion"/> y el aplicador
    /// de resultados), así que el cierre y el envío quedan en serie: si el lote ya está cerrado, el ítem no se envía y queda
    /// <c>SIN_RESULTADO_LOTE_CERRADO</c> (la transición del cierre manual). False si no hay que enviar: otro proceso movió el ítem o el
    /// lote está cerrado.
    /// </summary>
    private async Task<bool> RegistrarEnvioAsync(Guid tenant, Guid itemId, DatosItem datos, byte[]? tokenHash, bool desdeRunt, CancellationToken ct)
    {
        await using var propio = almacen.ContextoDe(tenant);
        var registro = await propio.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            propio.ChangeTracker.Clear();
            await using var tx = await propio.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var loteId = await propio.Set<LiquidacionItem>().AsNoTracking().Where(i => i.Id == itemId)
                .Select(i => i.LiquidacionId).SingleAsync(ct).ConfigureAwait(false);
            await propio.Database.ExecuteSqlAsync($"SELECT 1 FROM impuestos.liquidaciones WHERE id = {loteId} FOR UPDATE", ct).ConfigureAwait(false);
            var estadoLote = await propio.Set<Liquidacion>().AsNoTracking().Where(l => l.Id == loteId)
                .Select(l => l.Estado).SingleAsync(ct).ConfigureAwait(false);
            var fila = await propio.Set<LiquidacionItem>().SingleAsync(i => i.Id == itemId, ct).ConfigureAwait(false);
            if (estadoLote == EstadoLote.CerradoManualmente)
                return EstadosItem.EsFinal(fila.Estado) ? Registro.LoteCerradoItemFinal : Registro.LoteCerrado;
            var esperado = desdeRunt ? EstadoItem.ConsultandoRunt : EstadoItem.Enviando;
            if (fila.Estado != esperado)
                return Registro.CambioConcurrente;

            if (desdeRunt)
                fila.Estado = TransicionesItem.Aplicar(Situacion(fila), new EventoItem.RuntCompleto()).Nueva.Estado;
            var ahora = reloj.GetUtcNow();
            GuardarDatos(fila, datos);
            fila.TokenHash = tokenHash;
            fila.ConCallback = tokenHash is not null;
            fila.EnviadoEn = ahora;
            fila.ProximoIntentoEn = null;
            fila.ActualizadoEn = ahora;
            await propio.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return Registro.Listo;
        }).ConfigureAwait(false);

        switch (registro)
        {
            case Registro.Listo:
                return true;
            case Registro.LoteCerradoItemFinal:
                // El cierre ya lo pasó a SIN_RESULTADO_LOTE_CERRADO.
                LoteCerradoSinEnviar(log, itemId);
                return false;
            case Registro.LoteCerrado:
                // Sigue en vuelo en un lote cerrado: la misma transición del cierre manual (contadores y crédito incluidos).
                LoteCerradoSinEnviar(log, itemId);
                await AplicarAsync(tenant, itemId, new EventoItem.CierreManual(), null, ct).ConfigureAwait(false);
                return false;
            default:
                Descartado(log, itemId, "CAMBIO_CONCURRENTE");
                return false;
        }
    }

    private enum Registro
    {
        Listo,
        CambioConcurrente,
        LoteCerrado,
        LoteCerradoItemFinal,
    }

    /// <summary>
    /// Transición del dominio sobre la fila con <see cref="AplicadorResultado.TransicionarAsync"/> (M10: su propia transacción con la
    /// cabecera bloqueada y el recálculo de contadores y estado del lote), en un scope propio que se confirma ya. Después, en la
    /// transacción del SDK, la alerta de credencial si hubo 401 y, si el ítem salió de vuelo, la liberación del crédito. Si el ítem ya
    /// admite <paramref name="evento"/> (otro proceso lo movió), no hace nada.
    /// </summary>
    private async Task AplicarAsync(Guid tenant, Guid itemId, EventoItem evento, Action<LiquidacionItem>? ajustar, CancellationToken ct)
    {
        ResultadoItem? resultado = null;
        EstadoItem? final = null;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var aplicador = scope.ServiceProvider.GetRequiredService<AplicadorResultado>();
            await aplicador.TransicionarAsync(tenant, itemId, (fila, ahora) =>
            {
                try
                {
                    resultado = TransicionesItem.Aplicar(Situacion(fila), evento);
                }
                catch (TransicionInvalida)
                {
                    return Aplicacion.NoAplicable;
                }

                AplicarReintento(fila, resultado, ahora, Opciones);
                ajustar?.Invoke(fila);
                final = fila.Estado;
                return Aplicacion.Aplicado;
            }, ct).ConfigureAwait(false);
        }

        if (resultado is null || final is not { } estado)
        {
            Descartado(log, itemId, "CAMBIO_CONCURRENTE");
            return;
        }

        if (resultado.AprovisionamientoRequiereCredenciales)
            await credencialRechazada.AplicarAsync(tenant, ct).ConfigureAwait(false);
        if (!DespachadorCredito.EnVuelo.Contains(estado))
            await despachador.LiberarAsync(tenant, ct).ConfigureAwait(false);
        Aplicado(log, itemId, estado, resultado.ErrorCodigo ?? resultado.CampoFaltante);
    }

    /// <summary>El ítem sigue en vuelo y se vuelve a intentar desde <paramref name="desde"/> (lo republica el reconciliador).</summary>
    private async Task ReprogramarAsync(Guid itemId, DateTimeOffset desde, CancellationToken ct)
    {
        var fila = await db.Set<LiquidacionItem>().SingleAsync(i => i.Id == itemId, ct).ConfigureAwait(false);
        fila.ProximoIntentoEn = desde;
        fila.ActualizadoEn = reloj.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Las fuentes vigentes (refresca la caché si venció); si el proveedor no responde, la caché tal como está.</summary>
    private async Task<IReadOnlyList<FuenteApi>> FuentesAsync(Guid tenant, CancellationToken ct)
    {
        try
        {
            return await fuentes.ObtenerAsync(db, tenant, ct).ConfigureAwait(false);
        }
        catch (ReglaException)
        {
            return await SincronizacionFuentes.CacheAsync(db, ct).ConfigureAwait(false);
        }
    }

    private void GuardarDatos(LiquidacionItem fila, DatosItem datos)
    {
        fila.DocumentoTipo = datos.DocumentoTipo;
        fila.DocumentoNumeroCifrado = datos.DocumentoNumero is null ? null : proteccion.ProtegerPii(datos.DocumentoNumero);
        fila.PropietarioNombreCifrado = datos.NombrePropietario is null ? null : proteccion.ProtegerPii(datos.NombrePropietario);
        fila.DepartamentoCodigo = datos.DepartamentoCodigo;
        fila.DepartamentoNombre = datos.DepartamentoNombre;
        fila.SecretariaCiudad = datos.Ciudad;
        fila.SecretariaNombre = datos.SecretariaNombre;
    }

    private static SituacionItem Situacion(LiquidacionItem i) => new(i.Estado, i.IntentosRunt, i.IntentosReenvio, i.ErrorClase);

    [LoggerMessage(Level = LogLevel.Information, Message = "Liquidación: mensaje del ítem {ItemId} descartado ({Motivo}).")]
    private static partial void Descartado(ILogger logger, Guid itemId, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Liquidación: el ítem {ItemId} quedó ENVIANDO sin respuesta (el proceso cayó con la consulta en el aire): pasa a INCIERTO sin reenviar.")]
    private static partial void EnElAire(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Liquidación: freno global en pausa; el ítem {ItemId} espera el fin de la pausa.")]
    private static partial void EnPausa(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Liquidación: el RUNT no dio datos para el ítem {ItemId} ({Motivo}).")]
    private static partial void RuntSinDatos(ILogger logger, Guid itemId, string motivo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Liquidación: consulta del ítem {ItemId} aceptada por el proveedor (202).")]
    private static partial void Enviada(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Liquidación: consulta del ítem {ItemId} no aceptada ({Clase}, estado {Status}, código {Codigo}).")]
    private static partial void ConsultaNoAceptada(ILogger logger, Guid itemId, ClaseEnvio clase, int? status, string? codigo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Liquidación: el proveedor respondió 422 webhook_not_configured para la empresa {Empresa} (no tiene el secreto del webhook): el ítem {ItemId} se reenvía sin callbackUrl.")]
    private static partial void SinWebhookEnProveedor(ILogger logger, Guid empresa, Guid itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Liquidación: el lote del ítem {ItemId} está cerrado; no se envía al proveedor.")]
    private static partial void LoteCerradoSinEnviar(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Liquidación: ítem {ItemId} en {Estado} ({Codigo}).")]
    private static partial void Aplicado(ILogger logger, Guid itemId, EstadoItem estado, string? codigo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Liquidación: mensaje del ítem {ItemId} descartado (estado {Estado}, fuera de vuelo o en espera).")]
    private static partial void FueraDeVuelo(ILogger logger, Guid itemId, EstadoItem estado);
}
