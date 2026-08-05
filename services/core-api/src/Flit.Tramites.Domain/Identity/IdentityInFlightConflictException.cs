namespace Flit.Tramites.Domain.Identity;

/// <summary>
/// Se intentó crear una segunda validación EN VUELO para el mismo (tenant, documento normalizado),
/// violando el índice único parcial <c>uq_biometric_validations_inflight_doc_norm</c> (HU #11266 / D12).
/// La capa de persistencia traduce el <c>23505</c> de PostgreSQL a esta excepción (checklist §B12)
/// para que el handler responda 409 informativo sin filtrar detalles de BD. Sin PII en el mensaje.
/// </summary>
public sealed class IdentityInFlightConflictException : Exception
{
    public IdentityInFlightConflictException()
        : base("Ya existe una validación de identidad en curso para este documento.")
    {
    }
}
