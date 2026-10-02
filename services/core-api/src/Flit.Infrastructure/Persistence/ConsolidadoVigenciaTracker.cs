using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
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
///
/// <para><b>Bug #13055 — regeneración automática.</b> Bajar la marca no bastaba: el consolidado solo
/// fusiona el FUR PERSISTIDO, así que un dato corregido (un actor, el valor comercial, la prenda…)
/// seguía saliendo con el FUR viejo hasta que alguien pulsaba «Limpiar consolidado». Ahora, además:
/// (1) si cambió un DATO que imprime el FUR y el trámite ya tiene FUR, se sella
/// <see cref="ProcedureInstance.ExpedienteActualizadoEn"/> — el FUR anterior a esa marca se
/// regenera antes de consolidar (<c>FurVigenciaExpediente</c>); y (2) cualquier cambio del
/// expediente, datos o documentos, encola la regeneración anticipada de los consolidados que el
/// trámite ya tenga. El FUR solo escribe adjuntos y eventos, nunca datos, así que su propia
/// regeneración no vuelve a sellar la marca: no hay ciclo.</para>
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
    /// Cambios del expediente de un <c>SaveChanges</c>. <see cref="Invalidar"/>: instancias con un
    /// consolidado vigente que bajar. <see cref="Datos"/>: instancias a las que les cambió un dato
    /// que imprime el FUR. <see cref="Expediente"/>: todas las instancias cuyo expediente cambió
    /// (datos o documentos), para anticipar la regeneración de sus consolidados.
    /// </summary>
    public sealed record Cambios(
        IReadOnlyCollection<Guid> Invalidar,
        IReadOnlyCollection<Guid> Datos,
        IReadOnlyCollection<Guid> Expediente)
    {
        public static readonly Cambios Ninguno = new([], [], []);
    }

    /// <summary>
    /// Calcula los <see cref="Cambios"/> de este <c>SaveChanges</c>. Se calcula ANTES de guardar
    /// (después, el ChangeTracker ya está limpio).
    /// </summary>
    public static Cambios Candidatas(ChangeTracker tracker)
    {
        var afectadas = new HashSet<Guid>();
        var datos = new HashSet<Guid>();
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
            {
                afectadas.Add(id);
                if (EsDatoDelFur(entry.Entity))
                    datos.Add(id);
            }
        }

        afectadas.ExceptWith(recienGeneradas);
        datos.ExceptWith(recienGeneradas);
        if (afectadas.Count == 0)
            return Cambios.Ninguno;

        // Solo son candidatas las que pueden tener algo que invalidar. Una instancia rastreada con
        // ambas marcas en false no se toca: es el caso mayoritario (trámite en curso al que todavía
        // no se le ha generado ningún consolidado) y así el rastreador no cuesta nada.
        var rastreadas = tracker.Entries<ProcedureInstance>()
            .ToDictionary(e => e.Entity.Id, e => e.Entity);

        var invalidar = afectadas
            .Where(id => !rastreadas.TryGetValue(id, out var i)
                || i.ConsolidadoMaestroVigente
                || i.ConsolidadoWizardVigente)
            .ToList();

        return new Cambios(invalidar, datos.ToList(), afectadas.ToList());
    }

    /// <summary>
    /// Baja las marcas de las candidatas que efectivamente tengan un consolidado vigente y sella
    /// <see cref="ProcedureInstance.ExpedienteActualizadoEn"/> en las que cambiaron datos del FUR.
    /// Recarga cada instancia primero: los triggers de denormalización pudieron bumpear
    /// <c>row_version</c> durante el save anterior, y sin recargar el UPDATE saldría con el token
    /// obsoleto.
    /// </summary>
    /// <returns>Ids de las instancias que quedaron con cambios pendientes que el llamador debe persistir
    /// (vacío si no hay nada que guardar). Tras persistirlos, el llamador debe llamar a
    /// <see cref="RefrescarRowVersionAsync"/> con esos ids.</returns>
    public static async Task<IReadOnlyList<Guid>> InvalidarAsync(
        DbContext context,
        Cambios cambios,
        CancellationToken ct)
    {
        // Solo se sella la marca si el trámite YA tiene FUR: sin FUR no hay nada desactualizado y así
        // la edición de un borrador que aún no llegó a Preparar no paga el UPDATE extra.
        var conFur = new HashSet<Guid>();
        if (cambios.Datos.Count > 0)
        {
            var datos = cambios.Datos.ToList();
            conFur = (await context.Set<ProcedureInstanceAttachment>()
                    .Where(a => datos.Contains(a.ProcedureInstanceId) && a.Tipo == FurVigenciaExpediente.TipoFur)
                    .Select(a => a.ProcedureInstanceId)
                    .Distinct()
                    .ToListAsync(ct)
                    .ConfigureAwait(false))
                .ToHashSet();
        }

        var candidatas = cambios.Invalidar.Union(conFur).ToList();
        if (candidatas.Count == 0)
            return [];

        var tocadas = new List<Guid>();
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

            if (conFur.Contains(id) && !TramiteEstado.EsFinal(entry.Entity.Status))
            {
                entry.Entity.ExpedienteActualizadoEn = DateTimeOffset.UtcNow;
                tocadas.Add(id);
            }

            if (!entry.Entity.ConsolidadoMaestroVigente && !entry.Entity.ConsolidadoWizardVigente)
                continue;

            entry.Entity.InvalidarConsolidados();
            if (!tocadas.Contains(id))
                tocadas.Add(id);
        }

        return tocadas;
    }

    /// <summary>
    /// Bug #13194 (P4-24) — tras el UPDATE de las marcas, el trigger <c>tr_procedure_instances_row_version</c>
    /// (BEFORE UPDATE) sube <c>row_version</c> en la base, pero EF no lo relee (el token no es generado por
    /// la store en el modelo). La entidad rastreada quedaba con el token VIEJO y el siguiente guardado del
    /// MISMO contexto —p. ej. «Enviar al OT»: fase 1 persiste los checks (dato del FUR, sella
    /// <c>expediente_actualizado_en</c>) y fase 2 transiciona— salía con <c>WHERE row_version = viejo</c>:
    /// <c>DbUpdateConcurrencyException</c>, 409 <c>conflicto_concurrencia</c> en cada intento. Se relee el
    /// token de esas instancias y se fija como valor original y actual, sin marcar la propiedad modificada.
    /// </summary>
    public static async Task RefrescarRowVersionAsync(
        DbContext context,
        IReadOnlyList<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return;

        var versiones = await context.Set<ProcedureInstance>()
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.RowVersion })
            .ToDictionaryAsync(p => p.Id, p => p.RowVersion, ct)
            .ConfigureAwait(false);

        // Copia: fijar valores dispara detección de cambios y modificaría la colección enumerada.
        foreach (var entry in context.ChangeTracker.Entries<ProcedureInstance>().ToList())
        {
            if (entry.State == EntityState.Detached || !versiones.TryGetValue(entry.Entity.Id, out var version))
                continue;

            var propiedad = entry.Property(p => p.RowVersion);
            propiedad.OriginalValue = version;
            propiedad.CurrentValue = version;
            propiedad.IsModified = false;
        }
    }

    /// <summary>
    /// Bug #13055 — encola la regeneración anticipada de los consolidados que cada trámite cambiado
    /// YA tiene (no crea consolidados que nadie pidió). Excluye estados finales y migrados de V1; el
    /// worker aplica además el resto de excepciones (cargado a mano, maestro radicado en Quipux) y el
    /// debounce funde las ediciones seguidas en una sola regeneración. Best-effort: un descarte de la
    /// cola no falla el guardado; el camino perezoso reconstruye igual en la próxima vista.
    /// </summary>
    public static async Task EncolarRegeneracionAsync(
        DbContext context,
        IConsolidadoRegeneracionQueue? queue,
        Cambios cambios,
        CancellationToken ct)
    {
        if (queue is null || cambios.Expediente.Count == 0)
            return;

        var ids = cambios.Expediente.ToList();
        var pendientes = await context.Set<ProcedureInstance>()
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id)
                && !p.IsMigrated
                && p.Status != TramiteEstado.Aprobado
                && p.Status != TramiteEstado.Anulado
                && p.Status != TramiteEstado.Revocado)
            .Select(p => new
            {
                p.Id,
                p.TenantId,
                Wizard = p.Attachments.Any(a => a.Tipo == "consolidado"),
                Maestro = p.Attachments.Any(a => a.Tipo == "consolidado_maestro"),
            })
            .Where(p => p.Wizard || p.Maestro)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var p in pendientes)
        {
            if (p.Wizard)
                queue.Encolar(p.TenantId, p.Id, TipoConsolidado.Wizard);
            if (p.Maestro)
                queue.Encolar(p.TenantId, p.Id, TipoConsolidado.Maestro);
        }
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

    /// <summary>
    /// Bug #13055 — ¿la fila es un DATO que imprime el FUR? Los adjuntos no: el FUR no los imprime (el
    /// consolidado sí, y para eso basta bajar la marca y encolar). Firmas e identidad sí, porque el
    /// FUR estampa los sellos de firma y la serie del certificado.
    /// </summary>
    private static bool EsDatoDelFur(object entity) => entity is
        ProcedureInstanceFieldValue
        or ProcedureInstanceActor
        or ProcedureInstanceParticipant
        or ProcedureInstanceCommercial
        or ProcedureInstancePrenda
        or ProcedureInstanceSignature
        or ProcedureInstanceBiometricValidation;

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
