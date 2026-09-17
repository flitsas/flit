namespace Flit.Tramites.Domain.RevocationRequests;

/// <summary>
/// AC4 — se intentó insertar una segunda solicitud ACTIVA (<c>solicitada</c>/<c>en_revision</c>) para el
/// mismo trámite, violando el índice único parcial
/// <c>uq_procedure_revocation_requests_active_per_instance</c> (HU #12570). El gate en memoria
/// (<see cref="RevocationRequestGate"/>) ya cubre el caso normal con una lectura previa, pero esta
/// excepción es la traducción del <c>23505</c> de PostgreSQL que cierra la carrera entre dos solicitudes
/// concurrentes (checklist §B12: no reimplementar la unicidad en memoria, la BD es la fuente de verdad).
/// La capa de persistencia (<c>ProcedureRevocationRequestRepository.SaveChangesAsync</c>) la lanza; el
/// endpoint (HU #12572) la traduce a 409 <c>solicitud_activa_existente</c>, igual código que el gate.
/// </summary>
public sealed class ActiveRevocationRequestExistsException : Exception
{
    public ActiveRevocationRequestExistsException()
        : base("Ya existe una solicitud de revocatoria activa para este trámite.")
    {
    }
}
