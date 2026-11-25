using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Ejecucion;

/// <summary>
/// F1a-CF12: un 401 del proveedor a la api_key de una compañía lista (al liquidar en M09 o al consultar en la conciliación de M10)
/// lleva su aprovisionamiento a <c>REQUIERE_CREDENCIALES</c> (<see cref="EventoAprovisionamiento.NoAutorizadoEnUso"/>), con motivo
/// <c>CREDENCIAL_RECHAZADA</c>, versión nueva (el reportador de M07 lo lleva a la plataforma), historial con actor <c>SISTEMA</c> y la
/// alerta <see cref="Alerta"/>. Va en su propio contexto de la compañía y se confirma ya: no depende de la transacción de quien llama.
/// Si la compañía no está en <c>LISTO</c> ni <c>LISTO_SIN_WEBHOOK</c> (ya se marcó, o está suspendida) no hace nada.
/// </summary>
/// <example>
/// <code>
/// if (lectura.Clase == ClaseLectura.CredencialRechazada) await credencialRechazada.AplicarAsync(empresa, ct);
/// </code>
/// </example>
public sealed partial class CredencialRechazadaEnUso(AlmacenCredenciales almacen, TimeProvider reloj, ILogger<CredencialRechazadaEnUso> log)
{
    public const string Alerta = "credencial_rechazada_en_uso";

    /// <summary>Aplica la transición; devuelve true si la compañía cambió de estado.</summary>
    public async Task<bool> AplicarAsync(Guid empresa, CancellationToken ct)
    {
        await using var db = almacen.ContextoDe(empresa);
        // Con la fila bloqueada: una suspensión o una carga manual a la vez no se pisan (IMP-4).
        return await BloqueoAprovisionamiento.EnTransaccionAsync(db, empresa, c => AplicarEnFilaAsync(db, empresa, c), ct).ConfigureAwait(false);
    }

    private async Task<bool> AplicarEnFilaAsync(ImpuestosDb db, Guid empresa, CancellationToken ct)
    {
        var fila = await db.Set<AprovisionamientoEmpresa>().SingleOrDefaultAsync(a => a.TenantId == empresa, ct).ConfigureAwait(false);
        if (fila is not { Estado: EstadoAprovisionamiento.Listo or EstadoAprovisionamiento.ListoSinWebhook })
            return false;

        var ahora = reloj.GetUtcNow();
        var desde = fila.Estado;
        var nueva = TransicionesAprovisionamiento.Aplicar(Aprovisionador.Situacion(fila), EventoAprovisionamiento.NoAutorizadoEnUso).Nueva;
        fila.Estado = nueva.Estado!.Value;
        fila.EstadoPrevio = nueva.EstadoPrevio;
        fila.ErrorTipo = nueva.TipoError;
        fila.FirmaConfirmada = nueva.FirmaConfirmada;
        fila.MotivoCodigo = MotivosAprovisionamiento.CredencialRechazada;
        fila.MotivoDetalle = null;
        fila.Version++;
        fila.ActualizadoEn = ahora;
        db.Add(new HistorialAprovisionamiento
        {
            TenantId = empresa,
            Desde = desde,
            Hacia = fila.Estado,
            MotivoCodigo = MotivosAprovisionamiento.CredencialRechazada,
            ActorTipo = Aprovisionador.ActorSistema,
            En = ahora,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        MetricasImpuestos.ContarAlerta(Alerta);
        Aplicada(log, empresa);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "El API rechazó (401) la api_key de la empresa {Empresa} en uso: el aprovisionamiento pasa a REQUIERE_CREDENCIALES.")]
    private static partial void Aplicada(ILogger logger, Guid empresa);
}
