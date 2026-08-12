namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Regla PURA que resuelve el mandatario SUGERIDO cuando el trámite todavía no trae una elección
/// explícita (bug reportado en DEV: el resumen mostraba a "Carlos Pérez Demo" marcado como mandatario,
/// pero el mandato salía firmado por otra persona).
///
/// <para>Antes de este resolvedor había TRES criterios distintos para la misma pregunta ("¿quién es el
/// mandatario?"): <c>ListMandateSignerOptionsHandler</c> (lo que pinta la pantalla) aplicaba la cascada
/// completa; <c>FurCommand.TryGenerateMandatoAsync</c> (lo que firma el documento) usaba
/// <c>instance.MandateSignerId</c> crudo, sin cascada; y <c>MandatoApprovalHandler</c> delegaba en
/// <see cref="MandateSignerSelector"/>, que resuelve por cotejo de usuario, no por default del OT. El
/// radio quedaba marcado por una SUGERENCIA que nunca se persistió, y el documento salía sin firmante o
/// con uno distinto. Ahora los tres consumidores llaman a este único método.</para>
///
/// <list type="bullet">
///   <item>Elección explícita (ya guardada en el trámite, o la del aprobador) manda siempre.</item>
///   <item>Sin elección: el default parametrizado del OT (módulo Mandatos), SI está entre los candidatos
///   habilitados para ese organismo/compañía — un default que ya no aplica no se impone.</item>
///   <item>Sin elección ni default válido: el único candidato, si solo hay uno.</item>
///   <item>Varios candidatos sin default válido: sin sugerencia (<c>null</c>) — el llamador decide qué
///   hacer (pantalla: no preseleccionar nada; aprobación: cae al cotejo por usuario de
///   <see cref="MandateSignerSelector"/> y, si tampoco hay match único, exige elegir).</item>
/// </list>
/// </summary>
public static class MandateSignerDefaultResolver
{
    public static Guid? Resolve(
        IReadOnlyCollection<Guid> candidateIds,
        Guid? explicitOrSavedSignerId,
        Guid? defaultSignerId)
    {
        ArgumentNullException.ThrowIfNull(candidateIds);

        if (explicitOrSavedSignerId is { } chosen && chosen != Guid.Empty)
            return chosen;

        if (defaultSignerId is { } def && def != Guid.Empty && candidateIds.Contains(def))
            return def;

        return candidateIds.Count == 1 ? candidateIds.Single() : null;
    }
}
