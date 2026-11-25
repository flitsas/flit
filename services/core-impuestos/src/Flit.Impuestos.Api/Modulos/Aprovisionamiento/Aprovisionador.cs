using System.Text.Json;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Flit.Impuestos.Domain.Aprovisionamiento;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>
/// Lo que salió de evaluar una compañía: el evento de la máquina de estados, el código del motivo y, si hay, la credencial a
/// guardar con su origen. <see cref="Detalle"/> es técnico (sin secretos ni PII). No imprime la credencial (EP-CF3).
/// </summary>
public sealed record ResultadoEvaluacion(EventoAprovisionamiento Evento, string? Motivo, CredencialesEnClaro? Credenciales = null, OrigenCredencial? Origen = null)
{
    public string? Detalle { get; init; }

    /// <summary>El alta respondió <c>api_key_rotated=true</c>: la llave anterior dejó de servir (M07b, D-ALTA-IDEM).</summary>
    public bool ClaveRotada { get; init; }
}

/// <summary>
/// Evalúa el aprovisionamiento de una compañía en <c>PENDIENTE</c> (plan §2.1, §3.1 y §5.8; M07b, D-ALTA-IDEM, DA-2=b):
/// <list type="number">
///   <item>Reserva la compañía con un <c>UPDATE</c> condicionado (estado <c>PENDIENTE</c> y espera vencida): entre réplicas o
///   rondas solo una la evalúa, y la reserva vence a los <see cref="Reserva"/> si el proceso cae antes de terminar.</item>
///   <item>Llama SIEMPRE primero al alta idempotente (<see cref="AltaEmpresaApi"/>): repetirla es seguro y devuelve las mismas
///   credenciales, así que ya no hay marca del envío ni <c>INCIERTO</c> desde el alta.</item>
///   <item>Si el alta no deja credenciales, no es un 409 y no le quedan reintentos automáticos (o no se pudo hacer: NIT, datos,
///   ambiente), usa de respaldo <see cref="EvaluadorCredencialesExistentes"/> (credencial propia, luego copia de Diagnóstico).
///   Si tampoco hay, queda el resultado del alta.</item>
///   <item>Aplica la transición en UNA transacción: estado, motivo, versión, historial y, si hay, la credencial cifrada con
///   <see cref="GuardadoCredencialAlta"/> (llave rotada: historial <c>CLAVE_ROTADA</c> y propagación a Diagnóstico por la
///   outbox). «No salió» deja <c>PENDIENTE</c> con la espera de <see cref="EsperaReintento"/> (1, 5, 15 y 60 min) hasta el máximo
///   del dominio; luego <c>ERROR(AMBIENTE)</c>.</item>
///   <item>Reporta a la plataforma (<see cref="ReportadorEstado"/>); si no puede, la ronda siguiente lo reintenta.</item>
/// </list>
/// Si la plataforma suspendió la compañía mientras se evaluaba, no pisa <c>SUSPENDIDO</c>; una credencial nueva se guarda igual y
/// el estado listo al que se habría llegado queda como estado previo (D-DESACT: reactivar no vuelve a dar de alta). Alerta
/// operativa (métrica <c>impuestos.alertas</c>) en <c>ERROR(AMBIENTE)</c> por 401/404 o configuración.
/// </summary>
/// <example>
/// <code>
/// var evaluo = await aprovisionador.EvaluarAsync(empresa, ct); // false si no estaba PENDIENTE o su espera no vencía
/// </code>
/// </example>
public sealed partial class Aprovisionador(
    AlmacenCredenciales almacen,
    EvaluadorCredencialesExistentes evaluador,
    AltaEmpresaApi alta,
    ReportadorEstado reportador,
    GuardadoCredencialAlta guardado,
    TimeProvider reloj,
    ILogger<Aprovisionador> log)
{
    public const string ActorSistema = "SISTEMA";

    /// <summary>Motivo de la métrica de alertas para un error de ambiente.</summary>
    public const string AlertaAmbiente = "aprovisionamiento_ambiente";

    /// <summary>Cuánto dura la reserva de una evaluación en curso.</summary>
    public static readonly TimeSpan Reserva = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan[] Esperas = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)];

    /// <summary>Espera antes del reintento automático número <paramref name="intento"/> (1 en adelante; desde el 4.º, 60 min).</summary>
    public static TimeSpan EsperaReintento(int intento) => Esperas[Math.Clamp(intento - 1, 0, Esperas.Length - 1)];

    public async Task<bool> EvaluarAsync(Guid empresa, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        int intentos;
        await using (var db = almacen.ContextoDe(empresa))
        {
            var hasta = ahora + Reserva;
            var reservadas = await db.Set<AprovisionamientoEmpresa>()
                .Where(a => a.TenantId == empresa && a.Estado == EstadoAprovisionamiento.Pendiente && (a.ProximoIntentoEn == null || a.ProximoIntentoEn <= ahora))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ProximoIntentoEn, hasta), ct).ConfigureAwait(false);
            if (reservadas == 0)
                return false;
            intentos = await db.Set<AprovisionamientoEmpresa>().AsNoTracking()
                .Where(a => a.TenantId == empresa).Select(a => a.IntentosAutomaticos).SingleAsync(ct).ConfigureAwait(false);
        }

        var resultado = await alta.EjecutarAsync(empresa, ct).ConfigureAwait(false);
        if (UsaRespaldo(resultado, intentos))
        {
            var existente = await evaluador.EvaluarAsync(empresa, ct).ConfigureAwait(false);
            if (existente is not null)
            {
                Respaldo(log, empresa, resultado.Evento, existente.Evento);
                resultado = existente;
            }
        }

        await AplicarAsync(empresa, resultado, ct).ConfigureAwait(false);
        await reportador.ReportarAsync(empresa, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// D-ALTA-IDEM: las credenciales existentes (propia o copia de Diagnóstico) solo son respaldo de un alta que no las dio. No en
    /// un 409 (contrato inesperado, tiene que verse) ni mientras el alta tenga reintentos automáticos.
    /// </summary>
    internal static bool UsaRespaldo(ResultadoEvaluacion alta, int intentos) =>
        alta.Credenciales is null
        && alta.Evento != EventoAprovisionamiento.Alta409
        && (alta.Evento != EventoAprovisionamiento.NoSalio || intentos >= TransicionesAprovisionamiento.MaximoIntentosAutomaticos);

    private async Task AplicarAsync(Guid empresa, ResultadoEvaluacion resultado, CancellationToken ct)
    {
        await using var db = almacen.ContextoDe(empresa);
        var fila = await db.Set<AprovisionamientoEmpresa>().SingleOrDefaultAsync(a => a.TenantId == empresa, ct).ConfigureAwait(false);
        if (fila is null)
            return;
        if (fila.Estado != EstadoAprovisionamiento.Pendiente)
        {
            await ConservarAsync(db, fila, empresa, resultado, ct).ConfigureAwait(false);
            return;
        }

        var desde = fila.Estado;
        var transicion = TransicionesAprovisionamiento.Aplicar(Situacion(fila), resultado.Evento);
        var nueva = transicion.Nueva;
        fila.Estado = nueva.Estado!.Value;
        fila.EstadoPrevio = nueva.EstadoPrevio;
        fila.ErrorTipo = nueva.TipoError;
        fila.IntentosAutomaticos = nueva.IntentosAutomaticos;
        fila.FirmaConfirmada = nueva.FirmaConfirmada;
        fila.MotivoCodigo = resultado.Evento == EventoAprovisionamiento.NoSalio && fila.Estado == EstadoAprovisionamiento.Error
            ? MotivosAprovisionamiento.ProveedorInalcanzable
            : resultado.Motivo;
        fila.MotivoDetalle = resultado.Detalle;
        var ahora = reloj.GetUtcNow();
        fila.ProximoIntentoEn = fila.Estado == EstadoAprovisionamiento.Pendiente ? ahora + EsperaReintento(fila.IntentosAutomaticos) : null;
        fila.Version++;
        fila.ActualizadoEn = ahora;
        if (resultado.Credenciales is not null)
        {
            var (credencial, _) = await guardado.EscribirAsync(db, empresa, resultado, ct).ConfigureAwait(false);
            fila.ActualizadoEn = credencial.ActualizadoEn;
            if (resultado.Credenciales.WebhookSecret is not null)
            {
                fila.ModoRespaldoFirma = false;
                fila.FirmasInvalidasConsecutivas = 0;
            }
        }

        db.Add(new HistorialAprovisionamiento
        {
            TenantId = empresa,
            Desde = desde,
            Hacia = fila.Estado,
            MotivoCodigo = fila.MotivoCodigo,
            ActorTipo = ActorSistema,
            En = fila.ActualizadoEn,
        });
        if (resultado.Credenciales is not null)
            guardado.TrasEscribir(db, empresa, resultado, fila.Estado, fila.ActualizadoEn);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        Evaluada(log, empresa, desde, fila.Estado, fila.MotivoCodigo);
        if (transicion.Alerta || (fila.Estado == EstadoAprovisionamiento.Error && fila.ErrorTipo == TipoErrorAprovisionamiento.Ambiente))
        {
            MetricasImpuestos.ContarAlerta(AlertaAmbiente);
            Alerta(log, empresa, fila.Estado, fila.MotivoCodigo);
        }
    }

    /// <summary>
    /// La compañía dejó de estar en <c>PENDIENTE</c> mientras se evaluaba (suspensión): no se pisa su estado. Si el resultado
    /// trae una credencial (llega una sola vez), se guarda y, si estaba suspendida, el estado listo queda como previo.
    /// </summary>
    private async Task ConservarAsync(ImpuestosDb db, AprovisionamientoEmpresa fila, Guid empresa, ResultadoEvaluacion resultado, CancellationToken ct)
    {
        fila.MotivoDetalle = null;
        if (resultado.Credenciales is not null)
        {
            var (credencial, _) = await guardado.EscribirAsync(db, empresa, resultado, ct).ConfigureAwait(false);
            guardado.TrasEscribir(db, empresa, resultado, fila.Estado, credencial.ActualizadoEn);
            var listo = TransicionesAprovisionamiento.Aplicar(new SituacionAprovisionamiento(EstadoAprovisionamiento.Pendiente), resultado.Evento).Nueva.Estado!.Value;
            if (fila.Estado == EstadoAprovisionamiento.Suspendido && TransicionesAprovisionamiento.Liquida(listo))
                fila.EstadoPrevio = listo;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        Concurrente(log, empresa, fila.Estado, resultado.Credenciales is not null);
    }

    internal static SituacionAprovisionamiento Situacion(AprovisionamientoEmpresa fila) =>
        new(fila.Estado, fila.EstadoPrevio, fila.IntentosAutomaticos, fila.FirmaConfirmada, fila.ErrorTipo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Aprovisionamiento de la empresa {Empresa}: {Desde} → {Hacia} (motivo {Motivo}).")]
    private static partial void Evaluada(ILogger logger, Guid empresa, EstadoAprovisionamiento desde, EstadoAprovisionamiento hacia, string? motivo);

    [LoggerMessage(Level = LogLevel.Error, Message = "ALERTA operativa: el aprovisionamiento de la empresa {Empresa} quedó en {Estado} (motivo {Motivo}); revise la integración del ambiente.")]
    private static partial void Alerta(ILogger logger, Guid empresa, EstadoAprovisionamiento estado, string? motivo);

    [LoggerMessage(Level = LogLevel.Information, Message = "El alta de la empresa {Empresa} no dejó credenciales ({Alta}); se usan las existentes de respaldo ({Respaldo}).")]
    private static partial void Respaldo(ILogger logger, Guid empresa, EventoAprovisionamiento alta, EventoAprovisionamiento respaldo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El aprovisionamiento de la empresa {Empresa} cambió a {Estado} mientras se evaluaba; no se pisa (credencial guardada: {ConCredencial}).")]
    private static partial void Concurrente(ILogger logger, Guid empresa, EstadoAprovisionamiento estado, bool conCredencial);
}

/// <summary>Registro del aprovisionamiento (M07).</summary>
public static class AprovisionamientoExtensions
{
    /// <summary>Plazo de cada rpc de Identidad del aprovisionamiento.</summary>
    public static readonly TimeSpan PlazoIdentidad = TimeSpan.FromSeconds(10);

    /// <summary>Scopes del cliente de <c>CredencialesProductoService</c> (separados por espacio; Identidad valida cada rpc por el suyo).</summary>
    public const string ScopesCredencialesProducto = "platform.identidad.credenciales.impuesto.copia platform.identidad.credenciales.impuesto.actualizar";

    /// <summary>
    /// Servicios del aprovisionamiento, el consumidor de <see cref="ConsumidorCambioProducto.Cola"/> (los dos trabajos de la
    /// plataforma, solo de los productores <see cref="ConsumidorCambioProducto.Productores"/>) y, salvo
    /// <see cref="TrabajadorReintentoAlta.ActivoKey"/>=false, el trabajador de rondas. Con <c>Impuestos:Identidad:Address</c>, los
    /// tres clientes gRPC de Identidad con su scope; sin él, ninguna compañía se aprovisiona (se reintenta hasta que haya). Va
    /// después de <c>AddEmpresaDelTrabajo</c>.
    /// </summary>
    public static IServiceCollection AddAprovisionamientoImpuestos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration[GuardSuscripcionExtensions.IdentidadAddressKey] is { Length: > 0 } identidad)
        {
            var direccion = new Uri(identidad);
            // EmpresasService: un solo registro compartido con el backoffice (M13), con los scopes de los dos módulos.
            Backoffice.BackofficeImpuestosExtensions.AddClienteEmpresasIdentidad(services, configuration);
            // Un solo cliente para los dos rpc de Impuestos (copia y, M07b, reemplazo tras rotación): token con los dos scopes.
            services.AddFlitGrpcClient<Flit.Identidad.Grpc.V1.CredencialesProductoService.CredencialesProductoServiceClient>(
                configuration, direccion, ScopesCredencialesProducto, o => o.Deadline = PlazoIdentidad);
            services.AddFlitGrpcClient<Flit.Identidad.Grpc.V1.AprovisionamientoProductoService.AprovisionamientoProductoServiceClient>(
                configuration, direccion, "platform.aprovisionamiento.reportar", o => o.Deadline = PlazoIdentidad);
            services.TryAddScoped<IClienteIdentidadAlta, ClienteIdentidadAltaGrpc>();
        }
        else
        {
            services.TryAddSingleton<IClienteIdentidadAlta, ClienteIdentidadSinConfigurar>();
        }

        services.AddScoped<EvaluadorCredencialesExistentes>();
        services.AddScoped<AltaEmpresaApi>();
        services.AddScoped<GuardadoCredencialAlta>();
        services.AddScoped<ReportadorEstado>();
        services.AddScoped<Aprovisionador>();
        services.AddScoped<ConsumidorReintentoAprovisionamiento>();
        services.AddFlitConsumer<ImpuestosDb, ConsumidorCambioProducto, JsonElement>(
            configuration,
            ConsumidorCambioProducto.Cola,
            ConsumidorCambioProducto.Exchange,
            [ConsumidorCambioProducto.TipoCambio, ConsumidorCambioProducto.TipoReintentar],
            o =>
            {
                foreach (var productor in ConsumidorCambioProducto.Productores)
                    o.ProductoresPermitidos.Add(productor);
            });
        // M07b: trabajos internos de credenciales (propagar una llave rotada a Diagnóstico y refrescar el secreto del webhook),
        // que el propio servicio deja en su outbox; solo los acepta de sí mismo.
        services.AddFlitConsumer<ImpuestosDb, ConsumidorCredenciales, JsonElement>(
            configuration,
            ConsumidorCredenciales.Cola,
            ConsumidorCambioProducto.Exchange,
            [ConsumidorCredenciales.TipoPropagar, ConsumidorCredenciales.TipoRefrescarSecreto],
            o => o.ProductoresPermitidos.Add(ConsumidorCredenciales.Productor));
        if (configuration.GetValue(TrabajadorReintentoAlta.ActivoKey, true))
            services.AddHostedService<TrabajadorReintentoAlta>();
        return services;
    }
}
