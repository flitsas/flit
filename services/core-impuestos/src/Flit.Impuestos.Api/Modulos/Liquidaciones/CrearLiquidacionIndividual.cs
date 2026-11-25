using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Impuestos.Api.Modulos.Liquidaciones;

/// <summary>
/// Liquidación de una placa (plan §2.2, S-A; F2a-CF1, F1b-CF4):
/// <list type="number">
///   <item>Placa normalizada (S16: mayúsculas, sin espacios ni guiones) de <see cref="LargoMinimoPlaca"/> a
///   <see cref="Placa.LargoMaximo"/> letras o dígitos (literal del contrato y regla del API, guía §5); si no, 422
///   <c>PLACA_INVALIDA</c> en el campo <c>placa</c>.</item>
///   <item>Aprovisionamiento de la compañía: <c>SUSPENDIDO</c> ⇒ 409 <c>PRODUCTO_SUSPENDIDO</c>; otro que no sea <c>LISTO</c> o
///   <c>LISTO_SIN_WEBHOOK</c> (o sin fila) ⇒ 409 <c>APROVISIONAMIENTO_NO_LISTO</c>. Nada se crea.</item>
///   <item>En un scope con la empresa fijada (vale igual para el Super Admin que eligió otra con <c>?empresa</c>) y en una
///   transacción: lote <c>INDIVIDUAL</c> de un ítem <c>PENDIENTE</c> con la copia de la configuración vigente (F3-CF3) y
///   <see cref="DespachadorCredito.LiberarAsync"/>, que le da crédito si cabe y deja <c>impuestos.liquidacion.solicitada</c> en la
///   outbox.</item>
/// </list>
/// Devuelve la liquidación completa con su <c>items[0]</c>.
/// </summary>
/// <example>
/// <code>
/// var creada = await crear.CrearAsync(empresa, " abc-123 ", usuario.Id, empresaRef, ct); // items[0].placa = "ABC123"
/// </code>
/// </example>
public sealed partial class CrearLiquidacionIndividual(
    IServiceScopeFactory scopes, ServicioConfiguracion configuracion, ConsultaLiquidaciones consulta, IOptions<OpcionesEjecucion> opciones,
    TimeProvider reloj, ILogger<CrearLiquidacionIndividual> log)
{
    /// <summary>El API exige placas de 4 a 10 caracteres (guía §5) y el literal de <c>PLACA_INVALIDA</c> lo dice.</summary>
    public const int LargoMinimoPlaca = 4;

    /// <summary>La placa normalizada; 422 <c>PLACA_INVALIDA</c> si no es válida.</summary>
    public static string NormalizarPlaca(string? entrada) =>
        Placa.TryNormalizar(entrada, out var placa) && placa.Length >= LargoMinimoPlaca ? placa : throw ErroresLiquidaciones.Placa();

    /// <summary>
    /// Crea la individual. Con <paramref name="relanzadoDesde"/> es el relanzamiento de ese ítem (<see cref="RelanzarItem"/>): el ítem
    /// nuevo lleva <c>relanzado_desde</c> y <c>posible_duplicado=true</c>, y la transacción toma el ítem original con
    /// <c>FOR UPDATE</c> (<see cref="RelanzarItem.BloquearOriginalAsync"/>): dos relanzamientos del mismo ítem quedan en serie. <paramref name="actorNombre"/> es el nombre de quien actúa (<see cref="Plataforma.INombreActor"/>) en el
    /// momento de crear (<see cref="RegistrarNombreCreador"/>).
    /// </summary>
    public async Task<LiquidacionConItemsDto> CrearAsync(
        Guid empresa, string? placaEntrada, Guid actor, ReferenciaDto empresaRef, CancellationToken ct, Guid? relanzadoDesde = null, string? actorNombre = null)
    {
        var placa = NormalizarPlaca(placaEntrada);
        Guid loteId;
        await using (var scope = scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
            var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
            var despachador = scope.ServiceProvider.GetRequiredService<DespachadorCredito>();
            await ExigirAprovisionamientoAsync(db, empresa, ct).ConfigureAwait(false);

            var copia = await configuracion.CopiaAsync(empresa, opciones.Value.ConcurrenciaPorEmpresa, ct).ConfigureAwait(false);
            loteId = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                if (relanzadoDesde is { } original)
                {
                    // MEN-7: los relanzamientos del mismo ítem van en serie. Orden de bloqueos (DespachadorCredito): primero el candado
                    // del crédito (esta transacción libera al final) y luego el ítem original.
                    await DespachadorCredito.TomarCandadoAsync(db, ct).ConfigureAwait(false);
                    await RelanzarItem.BloquearOriginalAsync(db, empresa, original, ct).ConfigureAwait(false);
                }

                var ahora = reloj.GetUtcNow();
                var lote = new Liquidacion
                {
                    TenantId = empresa,
                    Tipo = TipoLiquidacion.Individual,
                    Estado = EstadoLote.Creado,
                    TotalItems = 1,
                    CPendientes = 1,
                    ConfigCopia = copia.ComoJson(),
                    CreadaPor = actor,
                    CreadaEn = ahora,
                };
                RegistrarNombreCreador(lote, actorNombre);
                db.Add(lote);
                db.Add(new LiquidacionItem
                {
                    TenantId = empresa,
                    LiquidacionId = lote.Id,
                    Placa = placa,
                    Estado = EstadoItem.Pendiente,
                    ActualizadoEn = ahora,
                    RelanzadoDesde = relanzadoDesde,
                    PosibleDuplicado = relanzadoDesde is not null,
                });
                await despachador.LiberarAsync(empresa, ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return lote.Id;
            }).ConfigureAwait(false);
        }

        Creada(log, empresa, loteId);
        return await consulta.ConItemsAsync(empresa, loteId, empresaRef, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Aprovisionamiento que admite crear (individual, masiva o relanzamiento): <c>SUSPENDIDO</c> ⇒ 409 <c>PRODUCTO_SUSPENDIDO</c>; otro que
    /// no sea <c>LISTO</c> o <c>LISTO_SIN_WEBHOOK</c> (o sin fila) ⇒ 409 <c>APROVISIONAMIENTO_NO_LISTO</c>.
    /// </summary>
    internal static async Task ExigirAprovisionamientoAsync(ImpuestosDb db, Guid empresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var estado = await db.Set<AprovisionamientoEmpresa>().AsNoTracking().Where(a => a.TenantId == empresa)
            .Select(a => (EstadoAprovisionamiento?)a.Estado).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (estado == EstadoAprovisionamiento.Suspendido)
            throw ErroresLiquidaciones.Suspendido();
        if (estado is not (EstadoAprovisionamiento.Listo or EstadoAprovisionamiento.ListoSinWebhook))
            throw ErroresLiquidaciones.NoListo();
    }

    /// <summary>
    /// Nombre de quien crea la individual o la relanza, tal como viene en su token (columna <c>creada_por_nombre</c> de la 1.2.0). Es
    /// el único punto que lo escribe para estas dos acciones.
    /// </summary>
    internal static void RegistrarNombreCreador(Liquidacion lote, string? nombre)
    {
        ArgumentNullException.ThrowIfNull(lote);
        lote.CreadaPorNombre = CerrarLiquidacion.NombreDelActor(nombre);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Liquidación individual {Liquidacion} creada para la empresa {Empresa}.")]
    private static partial void Creada(ILogger logger, Guid empresa, Guid liquidacion);
}
