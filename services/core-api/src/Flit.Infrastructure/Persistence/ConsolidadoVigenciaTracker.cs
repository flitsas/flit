using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Flit.Infrastructure.Persistence;

/// <summary>
/// Feature #10701 / HU #10860 (ADR-0032) — mantiene las marcas de vigencia del expediente
/// (<c>consolidado_maestro_vigente</c> y <c>consolidado_wizard_vigente</c>) coherentes con lo que
/// realmente contiene el expediente, sin depender de que cada caso de uso se acuerde de bajarlas.
///
/// <para><b>El problema.</b> Las dos marcas deciden si el consolidado persistido se sirve tal cual o
/// se regenera. Solo cinco sitios llamaban a <see cref="ProcedureInstance.InvalidarConsolidados"/>
/// —transición de estado, decisión del OT, regenerar el FUR, adjuntar la LT y el <c>force</c> del
/// wizard—, así que TODO lo demás dejaba el PDF congelado mientras el expediente cambiaba debajo:
/// subir o borrar un documento, editar datos del vehículo o de las personas, la decisión de prenda,
/// las firmas, la biométrica, los certificados generados, el mandatario o la placa que asigna el OT.
/// El síntoma reportado: el organismo abre «Ver consolidado» tras una gestión y recibe el de antes.</para>
///
/// <para><b>Por qué aquí y no en cada handler.</b> Es el mismo razonamiento que ya documenta
/// <c>GenerarConsolidadoHandler</c> sobre la regeneración del FUR: corregirlo en los llamadores deja
/// el defecto latente para el siguiente que se escriba. Aquí cierra la clase — incluidos los caminos
/// que viven en Infraestructura (el repositorio del OT) y no pasan por <c>Flit.Tramites.Application</c>.</para>
///
/// <para><b>Por qué DESPUÉS del save y no dentro.</b> <c>procedure_instances</c> tiene token de
/// concurrencia (<c>row_version</c>, trigger <c>tr_procedure_instances_row_version</c>) y sus tablas
/// hijas tienen triggers de denormalización que ACTUALIZAN la instancia —<c>vin</c>/<c>plate</c> desde
/// <c>field_values</c>, los nombres desde <c>actors</c>—, lo que bumpea ese token por debajo de EF.
/// Meter el UPDATE de las marcas en el MISMO <c>SaveChanges</c> que el hijo depende de en qué orden
/// ordene EF los dos comandos: si el INSERT del hijo va primero, el UPDATE de la instancia viajaría
/// con un <c>row_version</c> obsoleto, afectaría 0 filas y reventaría con
/// <c>DbUpdateConcurrencyException</c>. EF no garantiza ese orden cuando el principal solo está
/// MODIFICADO (no hay dependencia referencial que ordenar), y es exactamente el fallo que
/// <c>OtClientProcedureRepository.AssignPlateAsync</c> ya documenta y esquiva recargando el token.
/// Por eso se recarga la instancia y se persiste en un segundo save, con el token ya fresco.</para>
///
/// <para><b>Coste.</b> Cero en el caso normal: si la instancia rastreada no tiene ninguna marca en
/// <c>true</c> —un trámite al que todavía nadie le generó el consolidado— no se lee ni se escribe
/// nada. Solo paga (un SELECT de recarga y un UPDATE) el trámite que SÍ tiene un consolidado vigente
/// al que le acaba de cambiar el expediente, que es justo cuando hay que invalidarlo.</para>
/// </summary>
internal static class ConsolidadoVigenciaTracker
{
    /// <summary>
    /// Adjuntos que SON el consolidado. Se excluyen porque la propia generación los inserta (y borra
    /// el anterior) en el mismo <c>SaveChanges</c> en el que sube la marca a <c>true</c>: tratarlos
    /// como «cambió el expediente» la bajaría acto seguido y el PDF se regeneraría en CADA acceso.
    /// </summary>
    private static readonly HashSet<string> TiposConsolidado = new(StringComparer.OrdinalIgnoreCase)
    {
        "consolidado",
        "consolidado_maestro",
    };

    /// <summary>
    /// Instancias cuyo expediente cambia en este <c>SaveChanges</c> y que tienen un consolidado
    /// vigente que invalidar. Se calcula ANTES de guardar (después, el ChangeTracker ya está limpio).
    /// </summary>
    public static IReadOnlyCollection<Guid> Candidatas(ChangeTracker tracker)
    {
        var afectadas = new HashSet<Guid>();
        var recienGeneradas = new HashSet<Guid>();

        foreach (var entry in tracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (entry.Entity is ProcedureInstance instancia)
            {
                // La generación del consolidado sube la marca en el mismo save en el que persiste el
                // PDF. Red de seguridad por si además tocara alguna tabla hija: lo que este save
                // acaba de declarar vigente no se invalida en el mismo acto.
                if (MarcaSubidaAVigente(entry))
                    recienGeneradas.Add(instancia.Id);
                continue;
            }

            if (IdDelExpediente(entry.Entity) is { } id)
                afectadas.Add(id);
        }

        afectadas.ExceptWith(recienGeneradas);
        if (afectadas.Count == 0)
            return [];

        // Solo son candidatas las que pueden tener algo que invalidar. Una instancia rastreada con
        // ambas marcas en false no se toca: es el caso mayoritario (trámite en curso al que todavía
        // no se le ha generado ningún consolidado) y así el rastreador no cuesta nada.
        var rastreadas = tracker.Entries<ProcedureInstance>()
            .ToDictionary(e => e.Entity.Id, e => e.Entity);

        return afectadas
            .Where(id => !rastreadas.TryGetValue(id, out var i)
                || i.ConsolidadoMaestroVigente
                || i.ConsolidadoWizardVigente)
            .ToList();
    }

    /// <summary>
    /// Baja las marcas de las candidatas que efectivamente tengan un consolidado vigente. Recarga
    /// cada instancia primero: los triggers de denormalización pudieron bumpear <c>row_version</c>
    /// durante el save anterior, y sin recargar el UPDATE saldría con el token obsoleto.
    /// </summary>
    /// <returns><c>true</c> si dejó cambios pendientes que el llamador debe persistir.</returns>
    public static async Task<bool> InvalidarAsync(
        DbContext context,
        IReadOnlyCollection<Guid> candidatas,
        CancellationToken ct)
    {
        if (candidatas.Count == 0)
            return false;

        var alguna = false;
        foreach (var id in candidatas)
        {
            var entry = context.ChangeTracker.Entries<ProcedureInstance>()
                .FirstOrDefault(e => e.Entity.Id == id);

            if (entry is not null)
            {
                if (entry.State == EntityState.Detached)
                    continue;

                await entry.ReloadAsync(ct).ConfigureAwait(false);

                // Recargar una fila que ya no existe (borrada en paralelo) deja la entrada Detached.
                if (entry.State == EntityState.Detached)
                    continue;
            }
            else
            {
                var cargada = await context.Set<ProcedureInstance>()
                    .FirstOrDefaultAsync(p => p.Id == id, ct)
                    .ConfigureAwait(false);
                if (cargada is null)
                    continue;

                entry = context.Entry(cargada);
            }

            if (!entry.Entity.ConsolidadoMaestroVigente && !entry.Entity.ConsolidadoWizardVigente)
                continue;

            entry.Entity.InvalidarConsolidados();
            alguna = true;
        }

        return alguna;
    }

    /// <summary>Instancia a la que pertenece una fila hija del expediente, o <c>null</c> si no lo es.</summary>
    private static Guid? IdDelExpediente(object entity) => entity switch
    {
        // Los adjuntos que son el propio consolidado no cuentan como cambio del expediente.
        ProcedureInstanceAttachment a => TiposConsolidado.Contains(a.Tipo) ? null : a.ProcedureInstanceId,
        ProcedureInstanceFieldValue f => f.ProcedureInstanceId,
        ProcedureInstanceActor ac => ac.ProcedureInstanceId,
        ProcedureInstanceParticipant p => p.ProcedureInstanceId,
        ProcedureInstanceCommercial c => c.ProcedureInstanceId,
        ProcedureInstancePrenda pr => pr.ProcedureInstanceId,
        ProcedureInstanceSignature s => s.ProcedureInstanceId,
        ProcedureInstanceBiometricValidation b => b.ProcedureInstanceId,
        // Historial de estados, eventos, snapshots de pre-vuelo y causales de rechazo son
        // trazabilidad: no cambian una sola página del PDF, así que no invalidan nada.
        _ => null,
    };

    /// <summary>¿Este save DECLARA vigente el consolidado (lo acaba de generar)?</summary>
    private static bool MarcaSubidaAVigente(EntityEntry entry)
    {
        if (entry.State != EntityState.Modified)
            return false;

        return Subida(entry, nameof(ProcedureInstance.ConsolidadoMaestroVigente))
            || Subida(entry, nameof(ProcedureInstance.ConsolidadoWizardVigente));

        static bool Subida(EntityEntry e, string propiedad)
        {
            var p = e.Property(propiedad);
            return p.IsModified && p.CurrentValue is true;
        }
    }
}
