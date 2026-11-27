using System.Security.Cryptography;
using System.Text;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Avisos;

/// <summary>
/// Recibe un aviso del webhook del proveedor (plan §1.5, guía §6) y decide la respuesta, sin aplicar el resultado (eso lo hace
/// <see cref="ColaAvisos"/> en segundo plano, para responder en menos de 10 s):
/// <list type="number">
///   <item>Token sin la forma (43 caracteres base64url) o cuyo hash no es de ningún ítem: <c>410</c>.</item>
///   <item>Compañía sin secreto, o firma/timestamp inválidos (<see cref="VerificadorFirma"/>, con el secreto vigente y, en su
///   ventana de 24 h, el anterior; M07b): <c>401</c>. Con firma inválida cuenta para el modo respaldo:
///   <see cref="FirmasInvalidasParaRespaldo"/> seguidas en <see cref="VentanaFirmasInvalidas"/> lo encienden y encolan un alta
///   para refrescar el secreto.</item>
///   <item>Firma válida: otro evento que <c>consulta.finalizada</c> se confirma con <c>200</c> y se ignora, sin cambiar nada. Si
///   es el evento, apaga el modo respaldo, confirma la firma (<c>LISTO</c>, F1a-CF11), deduplica por <c>X-Flit-Delivery</c>
///   y por (<c>consulta_id</c>, <c>attempt</c>) del cuerpo firmado (duplicado ⇒ 200 sin repetir), descarta con alerta un <c>consulta.id</c> distinto del ítem y encola el resto. Siempre 200,
///   también con la compañía suspendida (S6) o el ítem cerrado.</item>
/// </list>
/// El token se busca por SQL (solo el id de la compañía) y lo demás en el contexto con esa compañía fijada. No registra el
/// token, el secreto ni el cuerpo.
/// </summary>
/// <example>
/// <code>
/// var status = await procesador.ProcesarAsync(token, new CabecerasAviso(timestamp, firma, delivery, evento), cuerpoCrudo, ct);
/// </code>
/// </example>
public sealed partial class ProcesadorAviso(
    ImpuestosDb db,
    EmpresaDelTrabajo empresaDelTrabajo,
    AlmacenCredenciales almacen,
    ColaAvisos cola,
    Aprovisionamiento.GuardadoCredencialAlta guardado,
    TimeProvider reloj,
    ILogger<ProcesadorAviso> log)
{
    public const int LargoToken = 43;
    public const int FirmasInvalidasParaRespaldo = 5;
    public const string MotivoFirmaConfirmada = "FIRMA_CONFIRMADA";
    public const string ActorSistema = "SISTEMA";
    public const string ResultadoRecibido = "RECIBIDO";
    public const string ResultadoConsultaDistinta = "CONSULTA_DISTINTA";
    public const string ResultadoColaLlena = "COLA_LLENA";
    public static readonly TimeSpan VentanaFirmasInvalidas = TimeSpan.FromHours(1);

    private const int LargoDelivery = 80;

    /// <summary>SHA-256 del token (bytes ASCII): lo único que se guarda en <c>liquidacion_items.token_hash</c>. M09 usa el mismo.</summary>
    public static byte[] HashDelToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SHA256.HashData(Encoding.ASCII.GetBytes(token));
    }

    public async Task<int> ProcesarAsync(string token, CabecerasAviso cabeceras, ReadOnlyMemory<byte> cuerpo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cabeceras);
        if (!TieneForma(token))
            return StatusCodes.Status410Gone;
        var hash = HashDelToken(token);
        var empresas = await db.Database.SqlQuery<Guid>($"SELECT tenant_id AS \"Value\" FROM impuestos.liquidacion_items WHERE token_hash = {hash}")
            .ToListAsync(ct).ConfigureAwait(false);
        if (empresas.Count != 1)
            return StatusCodes.Status410Gone;

        var empresa = empresas[0];
        empresaDelTrabajo.Fijar(empresa);
        var item = await db.Set<LiquidacionItem>().AsNoTracking().Where(i => i.TokenHash == hash)
            .Select(i => new { i.Id, i.ConsultaApiId }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (item is null)
            return StatusCodes.Status410Gone;

        var credenciales = await almacen.LeerAsync(empresa, ct).ConfigureAwait(false);
        if (credenciales?.WebhookSecret is null)
        {
            SinSecreto(log, item.Id);
            return StatusCodes.Status401Unauthorized;
        }

        var ahora = reloj.GetUtcNow();
        // Ventana de rotación (M07b): el vigente y, si no pasaron 24 h desde que cambió, el anterior (guía §6).
        var firma = VerificadorFirma.Verificar(credenciales.SecretosDeVerificacion(), cabeceras.Timestamp, cabeceras.Firma, cuerpo.Span, ahora);
        if (firma != ResultadoFirma.Valida)
        {
            await FirmaInvalidaAsync(empresa, ahora, ct).ConfigureAwait(false);
            Rechazado(log, item.Id, firma);
            return StatusCodes.Status401Unauthorized;
        }

        var aviso = LectorResultado.LeerAviso(cuerpo);
        var evento = string.IsNullOrEmpty(cabeceras.Evento) ? aviso?.Evento : cabeceras.Evento;
        if (!string.Equals(evento, VerificadorFirma.EventoFinalizada, StringComparison.Ordinal))
        {
            EventoIgnorado(log, item.Id);
            return StatusCodes.Status200OK;
        }

        await FirmaValidaAsync(empresa, ahora, ct).ConfigureAwait(false);
        if (aviso is null)
        {
            MetricasImpuestos.ContarAlerta("aviso_ilegible");
            Ilegible(log, item.Id);
            return StatusCodes.Status200OK;
        }

        var consulta = aviso.Consulta;
        var deliveryId = Delivery(cabeceras.Delivery ?? aviso.DeliveryId, consulta);
        var distinta = item.ConsultaApiId is not null && !string.Equals(item.ConsultaApiId, consulta.ConsultaId, StringComparison.Ordinal);
        var resultado = distinta ? ResultadoConsultaDistinta : ResultadoRecibido;
        var consultaId = consulta.ConsultaId.Length > 64 ? consulta.ConsultaId[..64] : consulta.ConsultaId;
        // Dedupe por X-Flit-Delivery (pk, no va firmado) y por (consulta_id, attempt) del cuerpo firmado
        // (ux_avisos_recibidos_consulta_attempt): un replay con otro X-Flit-Delivery tampoco entra (MEN-9 / security B-1).
        var nuevas = await db.Database.ExecuteSqlAsync(
            $"INSERT INTO impuestos.avisos_recibidos (delivery_id, item_id, tenant_id, consulta_id, attempt, recibido_en, resultado) VALUES ({deliveryId}, {item.Id}, {empresa}, {consultaId}, {consulta.Attempt}, {ahora}, {resultado}) ON CONFLICT DO NOTHING",
            ct).ConfigureAwait(false);
        if (nuevas == 0)
        {
            Duplicado(log, item.Id);
            return StatusCodes.Status200OK;
        }

        if (distinta)
        {
            MetricasImpuestos.ContarAlerta("aviso_consulta_distinta");
            ConsultaDistinta(log, item.Id);
            return StatusCodes.Status200OK;
        }

        if (!cola.TryEncolar(new AvisoPendiente(empresa, item.Id, deliveryId, consulta)))
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE impuestos.avisos_recibidos SET resultado = {ResultadoColaLlena} WHERE delivery_id = {deliveryId}", ct).ConfigureAwait(false);
            MetricasImpuestos.ContarAlerta("aviso_cola_llena");
            ColaLlena(log, item.Id);
        }

        return StatusCodes.Status200OK;
    }

    private static bool TieneForma(string? token) =>
        token is { Length: LargoToken } && token.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');

    /// <summary><c>X-Flit-Delivery</c> (o el <c>delivery_id</c> del cuerpo); uno más largo que la columna se guarda por su hash.</summary>
    private static string Delivery(string? delivery, ResultadoLeido consulta)
    {
        var valor = string.IsNullOrWhiteSpace(delivery) ? $"sin-delivery:{consulta.ConsultaId}:{consulta.Attempt}" : delivery;
        return valor.Length <= LargoDelivery ? valor : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
    }

    /// <summary>
    /// Cuenta una firma inválida de un token conocido; la cuenta se reinicia si la anterior fue hace más de una hora. La ventana se
    /// mide contra <c>ultima_firma_invalida_en</c> (MEN-8): contar no es un cambio del aprovisionamiento y no toca
    /// <c>actualizado_en</c>. Es un <c>ExecuteUpdate</c> atómico (toma solo la fila, como pide <c>BloqueoAprovisionamiento</c>) y
    /// cambia <c>xmin</c>: quien tuviera la fila leída de antes recibe un choque de concurrencia y relee.
    /// </summary>
    private async Task FirmaInvalidaAsync(Guid empresa, DateTimeOffset ahora, CancellationToken ct)
    {
        var limite = ahora - VentanaFirmasInvalidas;
        var filas = db.Set<AprovisionamientoEmpresa>().Where(a => a.TenantId == empresa);
        await filas.ExecuteUpdateAsync(s => s
            .SetProperty(a => a.FirmasInvalidasConsecutivas, a => a.FirmasInvalidasConsecutivas > 0 && a.UltimaFirmaInvalidaEn >= limite ? a.FirmasInvalidasConsecutivas + 1 : 1)
            .SetProperty(a => a.UltimaFirmaInvalidaEn, ahora), ct).ConfigureAwait(false);
        var encendidas = await filas.Where(a => !a.ModoRespaldoFirma && a.FirmasInvalidasConsecutivas >= FirmasInvalidasParaRespaldo)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ModoRespaldoFirma, true), ct).ConfigureAwait(false);
        if (encendidas > 0)
        {
            MetricasImpuestos.ContarAlerta("modo_respaldo_firma");
            RespaldoEncendido(log, empresa);
            // M07b (plan §5.8 punto 7): el proveedor pudo rotar el secreto; un alta lo trae. Solo al encenderse.
            guardado.Encolar(db, Aprovisionamiento.ConsumidorCredenciales.TipoRefrescarSecreto, empresa);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Apaga el modo respaldo y la cuenta, y confirma la firma de una compañía <c>LISTO</c> (sube la versión e historial).</summary>
    private async Task FirmaValidaAsync(Guid empresa, DateTimeOffset ahora, CancellationToken ct)
    {
        var filas = db.Set<AprovisionamientoEmpresa>().Where(a => a.TenantId == empresa);
        var apagadas = await filas.Where(a => a.ModoRespaldoFirma || a.FirmasInvalidasConsecutivas != 0)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ModoRespaldoFirma, false).SetProperty(a => a.FirmasInvalidasConsecutivas, 0), ct)
            .ConfigureAwait(false);
        if (apagadas > 0)
            CuentaReiniciada(log, empresa);

        // Transición PrimerWebhookValido de la tabla del dominio: solo en LISTO y una vez (la condición la hace atómica). El
        // ExecuteUpdate toma la fila y cambia xmin: un escritor con la fila leída de antes choca y relee (BloqueoAprovisionamiento).
        var confirmadas = await filas.Where(a => a.Estado == EstadoAprovisionamiento.Listo && !a.FirmaConfirmada)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.FirmaConfirmada, true).SetProperty(a => a.Version, a => a.Version + 1).SetProperty(a => a.ActualizadoEn, ahora), ct)
            .ConfigureAwait(false);
        if (confirmadas == 0)
            return;
        db.Add(new HistorialAprovisionamiento
        {
            TenantId = empresa,
            Desde = EstadoAprovisionamiento.Listo,
            Hacia = EstadoAprovisionamiento.Listo,
            MotivoCodigo = MotivoFirmaConfirmada,
            ActorTipo = ActorSistema,
            En = ahora,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        FirmaConfirmada(log, empresa);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Aviso para el ítem {Item} rechazado (401): la compañía no tiene secreto de webhook.")]
    private static partial void SinSecreto(ILogger logger, Guid item);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Aviso para el ítem {Item} rechazado (401): {Motivo}.")]
    private static partial void Rechazado(ILogger logger, Guid item, ResultadoFirma motivo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Aviso con firma válida para el ítem {Item} con un evento que no es consulta.finalizada: se ignora.")]
    private static partial void EventoIgnorado(ILogger logger, Guid item);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Aviso con firma válida para el ítem {Item} pero sin una consulta legible: se descarta.")]
    private static partial void Ilegible(ILogger logger, Guid item);

    [LoggerMessage(Level = LogLevel.Information, Message = "Aviso duplicado para el ítem {Item}: ya se había recibido ese X-Flit-Delivery o esa consulta con ese attempt.")]
    private static partial void Duplicado(ILogger logger, Guid item);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Aviso para el ítem {Item} con un consulta.id distinto del del ítem: se descarta (aviso_consulta_distinta).")]
    private static partial void ConsultaDistinta(ILogger logger, Guid item);

    [LoggerMessage(Level = LogLevel.Error, Message = "La cola de avisos está llena: el resultado del ítem {Item} queda para la conciliación por GET.")]
    private static partial void ColaLlena(ILogger logger, Guid item);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Modo respaldo de firma encendido para la empresa {Empresa}: las consultas nuevas salen sin callbackUrl.")]
    private static partial void RespaldoEncendido(ILogger logger, Guid empresa);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook válido de la empresa {Empresa}: se reinicia la cuenta de firmas inválidas y se apaga el modo respaldo.")]
    private static partial void CuentaReiniciada(ILogger logger, Guid empresa);

    [LoggerMessage(Level = LogLevel.Information, Message = "Primer webhook válido de la empresa {Empresa}: firma confirmada.")]
    private static partial void FirmaConfirmada(ILogger logger, Guid empresa);
}

/// <summary>Cabeceras del proveedor que usa el receptor (guía §6). <see cref="ToString"/> no imprime la firma.</summary>
public sealed record CabecerasAviso(string? Timestamp, string? Firma, string? Delivery, string? Evento)
{
    public override string ToString() => $"CabecerasAviso {{ Timestamp = {Timestamp}, Firma = [REDACTADO], Delivery = {Delivery}, Evento = {Evento} }}";
}
