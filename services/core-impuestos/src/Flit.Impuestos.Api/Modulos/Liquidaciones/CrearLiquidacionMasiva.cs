using Flit.Impuestos.Api.Modulos.Carga;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Impuestos.Api.Modulos.Liquidaciones;

/// <summary>
/// Carga masiva (plan §2.3; F3-CF1..CF3, F3-CF9):
/// <list type="number">
///   <item><see cref="AnalizarAsync"/>: <see cref="AnalizadorCarga"/> con el tope vigente de la compañía (el de la copia de su
///   configuración: operativo si existe, si no contractual). No escribe nada.</item>
///   <item><see cref="CrearAsync"/>: vuelve a analizar; exige el aprovisionamiento como la individual (409); sin placas válidas, 422
///   <c>ARCHIVO_SIN_PLACAS</c>; con más placas que el tope, 422 <c>LIMITE_SUPERADO</c> con <c>tope</c> (antes de crear nada). En un
///   scope con la empresa fijada y una transacción: lote <c>MASIVA</c> con el nombre del archivo, la copia de la configuración
///   (F3-CF3) y un ítem <c>PENDIENTE</c> por placa única con su fila; luego <see cref="DespachadorCredito.LiberarAsync"/>.</item>
/// </list>
/// Devuelve la cabecera (esquema <c>Liquidacion</c>, sin ítems).
/// </summary>
/// <example>
/// <code>
/// var lote = await masiva.CrearAsync(empresa, new ArchivoCarga("placas.csv", bytes), usuario.Id, await nombres.NombreAsync(usuario, ct), empresaRef, ct);
/// </code>
/// </example>
public sealed partial class CrearLiquidacionMasiva(
    IServiceScopeFactory scopes, ServicioConfiguracion configuracion, ConsultaLiquidaciones consulta, IOptions<OpcionesEjecucion> opciones,
    TimeProvider reloj, ILogger<CrearLiquidacionMasiva> log)
{
    /// <summary>Largo de <c>liquidaciones.nombre_archivo</c>.</summary>
    public const int LargoNombreArchivo = 200;

    public async Task<AnalisisCargaDto> AnalizarAsync(Guid empresa, ArchivoCarga archivo, CancellationToken ct)
    {
        var resultado = AnalizadorCarga.Analizar(archivo);
        var copia = await configuracion.CopiaAsync(empresa, opciones.Value.ConcurrenciaPorEmpresa, ct).ConfigureAwait(false);
        return resultado.Resumen(copia.MaxRegistros);
    }

    public async Task<LiquidacionDto> CrearAsync(Guid empresa, ArchivoCarga archivo, Guid actor, string? actorNombre, ReferenciaDto empresaRef, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        var carga = AnalizadorCarga.Analizar(archivo);
        Guid loteId;
        await using (var scope = scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
            var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
            var despachador = scope.ServiceProvider.GetRequiredService<DespachadorCredito>();
            await CrearLiquidacionIndividual.ExigirAprovisionamientoAsync(db, empresa, ct).ConfigureAwait(false);
            if (carga.Placas.Count == 0)
                throw ErroresCarga.SinPlacas();
            var copia = await configuracion.CopiaAsync(empresa, opciones.Value.ConcurrenciaPorEmpresa, ct).ConfigureAwait(false);
            if (carga.Placas.Count > copia.MaxRegistros)
                throw ErroresCarga.Limite(copia.MaxRegistros);

            loteId = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var ahora = reloj.GetUtcNow();
                var lote = new Liquidacion
                {
                    TenantId = empresa,
                    Tipo = TipoLiquidacion.Masiva,
                    Estado = EstadoLote.Creado,
                    NombreArchivo = NombreArchivo(archivo.Nombre),
                    TotalItems = carga.Placas.Count,
                    CPendientes = carga.Placas.Count,
                    ConfigCopia = copia.ComoJson(),
                    CreadaPor = actor,
                    CreadaEn = ahora,
                };
                RegistrarNombreCreador(lote, actorNombre);
                db.Add(lote);
                db.AddRange(carga.Placas.Select(p => new LiquidacionItem
                {
                    TenantId = empresa,
                    LiquidacionId = lote.Id,
                    Fila = p.Fila,
                    Placa = p.Placa,
                    Estado = EstadoItem.Pendiente,
                    ActualizadoEn = ahora,
                }));
                await despachador.LiberarAsync(empresa, ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return lote.Id;
            }).ConfigureAwait(false);
        }

        Creada(log, empresa, loteId, carga.Placas.Count);
        return await consulta.LiquidacionAsync(empresa, loteId, empresaRef, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Nombre de quien crea la masiva, tal como viene en su token (columna <c>creada_por_nombre</c> de la 1.2.0). Es el único punto que
    /// lo escribe para esta acción.
    /// </summary>
    internal static void RegistrarNombreCreador(Liquidacion lote, string? nombre)
    {
        ArgumentNullException.ThrowIfNull(lote);
        lote.CreadaPorNombre = CerrarLiquidacion.NombreDelActor(nombre);
    }

    /// <summary>El nombre del archivo sin ruta, recortado al largo de la columna.</summary>
    internal static string NombreArchivo(string? nombre)
    {
        var limpio = Path.GetFileName((nombre ?? string.Empty).Replace('\\', '/')).Trim();
        return limpio.Length <= LargoNombreArchivo ? limpio : limpio[..LargoNombreArchivo];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Liquidación masiva {Liquidacion} creada para la empresa {Empresa} con {Placas} placas.")]
    private static partial void Creada(ILogger logger, Guid empresa, Guid liquidacion, int placas);
}
