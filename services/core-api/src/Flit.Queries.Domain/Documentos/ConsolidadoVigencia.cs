namespace Flit.Queries.Domain.Documentos;

/// <summary>
/// HU #12791 (Épica #12760) — estado de vigencia de UN consolidado (wizard o maestro) tal como lo
/// exponen los contratos de API: detalle y listado del gestor, detalle y bandeja OT / SuperAdmin.
/// Sub-objeto reutilizable: la misma forma en todas las superficies para que el frontend distinga un
/// PDF fresco de uno viejo sin recalcular reglas. Serializa como
/// <c>{ estado, generadoEn, origen, definitivo, modo }</c>.
/// </summary>
/// <param name="Estado"><c>vigente</c> | <c>desactualizado</c> | <c>inexistente</c> (ver <see cref="ConsolidadoVigencia"/>).</param>
/// <param name="GeneradoEn">
/// Sello UTC de la generación (columnas <c>consolidado_*_generado_en</c>, HU #12790). <c>null</c> si no
/// hay PDF o si el trámite es histórico (anterior a la columna: «fecha no disponible»).
/// </param>
/// <param name="Origen"><c>system</c> (lo generó FLIT) | <c>user</c> (lo cargó el SuperAdmin) | <c>null</c> sin PDF.</param>
/// <param name="Definitivo"><c>true</c> en estado final: la documentación es la que el organismo tuvo a la vista y no se regenera.</param>
/// <param name="Modo">
/// Marca del caso especial con los MISMOS literales de la ruta de entrega (HU #12785):
/// <c>definitivo_estado_final</c>, <c>migrado_solo_lectura</c>, <c>cargado_por_usuario</c>; <c>null</c> en el caso normal.
/// </param>
public sealed record ConsolidadoVigenciaDto(
    string Estado,
    DateTimeOffset? GeneradoEn,
    string? Origen,
    bool Definitivo,
    string? Modo);

/// <summary>
/// HU #12791 — ÚNICA derivación del <see cref="ConsolidadoVigenciaDto"/>. Vive en el kernel compartido
/// porque la consumen dos módulos que no se referencian entre sí (Trámites: detalle/listado del gestor;
/// Admin: bandeja y detalle OT, vista SuperAdmin). Es pura: recibe los datos ya cargados, no consulta.
/// <para>Precedencia (mismo orden que <c>EntregarConsolidadoHandler</c>):</para>
/// <list type="number">
///   <item>Sin adjunto del tipo ⇒ <c>inexistente</c>, sello <c>null</c> (un migrado final sin PDF lleva
///   <c>modo = migrado_solo_lectura</c>, igual que el error de la ruta de entrega).</item>
///   <item>Estado final ⇒ <c>definitivo = true</c> y <c>estado = vigente</c>: la transición a estado final
///   baja la bandera, pero ese PDF es justo el que se entrega sin regenerar; pintarlo «desactualizado»
///   invitaría a una regeneración que la ruta de entrega nunca hará. <c>modo</c> =
///   <c>migrado_solo_lectura</c> si es migrado V1, si no <c>definitivo_estado_final</c>.</item>
///   <item><c>Source = "user"</c> ⇒ <c>origen = user</c>, <c>modo = cargado_por_usuario</c>; el estado sigue
///   la bandera (el PDF del SuperAdmin prevalece, pero el frontend debe saber si el expediente cambió).</item>
///   <item>Resto ⇒ la bandera decide <c>vigente</c> / <c>desactualizado</c>; <c>origen = system</c>.</item>
/// </list>
/// </summary>
/// <remarks>Uso de ejemplo: <c>ConsolidadoVigencia.Derivar(sourceAdjunto: "system", vigente: true, generadoEn: t, esEstadoFinal: false, esMigrado: false)</c>.</remarks>
public static class ConsolidadoVigencia
{
    /// <summary>Tipo del adjunto del consolidado del wizard (gestor).</summary>
    public const string TipoWizard = "consolidado";

    /// <summary>Tipo del adjunto del consolidado maestro (OT).</summary>
    public const string TipoMaestro = "consolidado_maestro";

    public const string EstadoVigente = "vigente";
    public const string EstadoDesactualizado = "desactualizado";
    public const string EstadoInexistente = "inexistente";

    public const string OrigenSystem = "system";
    public const string OrigenUser = "user";

    /// <summary>Mismo literal que <c>ConsolidadoEntregaModos.DefinitivoEstadoFinal</c> (HU #12785).</summary>
    public const string ModoDefinitivoEstadoFinal = "definitivo_estado_final";

    /// <summary>Mismo literal que <c>ConsolidadoEntregaModos.MigradoSoloLectura</c> (HU #12785).</summary>
    public const string ModoMigradoSoloLectura = "migrado_solo_lectura";

    /// <summary>Mismo literal que <c>ConsolidadoEntregaModos.CargadoPorUsuario</c> (HU #12785).</summary>
    public const string ModoCargadoPorUsuario = "cargado_por_usuario";

    /// <summary>Deriva el estado de vigencia de un consolidado.</summary>
    /// <param name="sourceAdjunto"><c>Source</c> del adjunto más reciente del tipo; <c>null</c> = no existe adjunto.</param>
    /// <param name="vigente">Bandera <c>consolidado_*_vigente</c> de la instancia.</param>
    /// <param name="generadoEn">Sello <c>consolidado_*_generado_en</c> de la instancia.</param>
    /// <param name="esEstadoFinal"><c>TramiteEstado.EsFinal(status)</c>.</param>
    /// <param name="esMigrado"><c>ProcedureInstance.IsMigrated</c>.</param>
    public static ConsolidadoVigenciaDto Derivar(
        string? sourceAdjunto,
        bool vigente,
        DateTimeOffset? generadoEn,
        bool esEstadoFinal,
        bool esMigrado)
    {
        if (sourceAdjunto is null)
        {
            return new ConsolidadoVigenciaDto(
                EstadoInexistente,
                GeneradoEn: null,
                Origen: null,
                Definitivo: false,
                Modo: esEstadoFinal && esMigrado ? ModoMigradoSoloLectura : null);
        }

        var esUser = string.Equals(sourceAdjunto, OrigenUser, StringComparison.OrdinalIgnoreCase);
        var origen = esUser ? OrigenUser : OrigenSystem;

        if (esEstadoFinal)
        {
            return new ConsolidadoVigenciaDto(
                EstadoVigente,
                generadoEn,
                origen,
                Definitivo: true,
                Modo: esMigrado ? ModoMigradoSoloLectura : ModoDefinitivoEstadoFinal);
        }

        return new ConsolidadoVigenciaDto(
            vigente ? EstadoVigente : EstadoDesactualizado,
            generadoEn,
            origen,
            Definitivo: false,
            Modo: esUser ? ModoCargadoPorUsuario : null);
    }
}
