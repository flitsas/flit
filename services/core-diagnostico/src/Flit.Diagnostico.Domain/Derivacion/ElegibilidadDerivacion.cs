using Flit.Diagnostico.Domain.Diagnosticos;

namespace Flit.Diagnostico.Domain.Derivacion;

/// <summary>
/// Lo que decide si un vehículo se puede derivar (Punto 4, diseño §5.2): si terminó (todas sus celdas en COMPLETADO o NA, R-7), si
/// el lote exige dictamen (Con análisis), el veredicto del dictamen vigente (null si no hay), si ya tiene trámite y si tiene un
/// ítem vivo (PENDIENTE o ENVIADO).
/// </summary>
public sealed record DatosElegibilidad(bool Terminado, bool ExigeDictamen, string? Veredicto, bool TieneTramite, bool ItemVivo);

/// <summary>
/// Una sola regla de elegibilidad para la grilla, el individual y (incremento 3) la ejecución de cada ítem (decisiones 4.2 y 4.6,
/// AC7): sin trámite, sin ítem vivo, terminado y, en Con análisis, dictamen vigente VIABLE. El primer motivo que aplica, en ese
/// orden: <c>YA_TIENE_TRAMITE</c>, <c>EN_CURSO</c>, <c>NO_TERMINADO</c>, <c>NO_VIABLE</c>.
/// <para>
/// Lote Simple: no tiene dictamen (el emisor solo dictamina Con análisis), así que no se le exige VIABLE; basta terminado y sin
/// trámite (decisión 4.3: el tipo lo elige el usuario). Si producto decide que Simple no deriva, se cambia
/// <see cref="ExigeDictamen"/> y nada más.
/// </para>
/// </summary>
public static class ElegibilidadDerivacion
{
    /// <summary>Veredicto del dictamen que permite derivar (<c>dictamenes.veredicto</c>).</summary>
    public const string Viable = "VIABLE";

    /// <summary>El lote exige dictamen VIABLE solo si es Con análisis.</summary>
    public static bool ExigeDictamen(ModoDiagnostico modo) => modo == ModoDiagnostico.ConAnalisis;

    /// <summary>El motivo por el que no se deriva; null si es elegible.</summary>
    public static string? MotivoNoElegible(DatosElegibilidad datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (datos.TieneTramite)
            return MotivoDerivacion.YaTieneTramite;
        if (datos.ItemVivo)
            return MotivoDerivacion.EnCurso;
        if (!datos.Terminado)
            return MotivoDerivacion.NoTerminado;
        if (datos.ExigeDictamen && !string.Equals(datos.Veredicto, Viable, StringComparison.Ordinal))
            return MotivoDerivacion.NoViable;
        return null;
    }
}

/// <summary>
/// Tipo de trámite del borrador (decisión 4.3): en Con análisis, el <c>tipo_tramite_codigo</c> del lote (el del cuerpo se ignora);
/// en Simple, el del cuerpo sin espacios. Null si en Simple no viene.
/// </summary>
public static class TipoTramiteDerivacion
{
    public static string? Resolver(ModoDiagnostico modo, string? delLote, string? delCuerpo)
    {
        var tipo = modo == ModoDiagnostico.ConAnalisis ? delLote : delCuerpo;
        return string.IsNullOrWhiteSpace(tipo) ? null : tipo.Trim();
    }
}
