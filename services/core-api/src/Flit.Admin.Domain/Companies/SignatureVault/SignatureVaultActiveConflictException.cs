namespace Flit.Admin.Domain.Companies.SignatureVault;

/// <summary>
/// Se intentó dar de alta una segunda firma <c>activa</c> para el mismo (tenant, NIT, documento),
/// violando el índice único parcial <c>uq_signature_vault_activa</c> (ADR-0025 §2). La capa de
/// persistencia traduce el <c>23505</c> de PostgreSQL a esta excepción de dominio (checklist §B12)
/// para que el handler la mapee a un 422 legible sin filtrar detalles de BD. El mensaje NO contiene
/// PII (Ley 1581): no incluye el número de documento.
/// </summary>
public sealed class SignatureVaultActiveConflictException : Exception
{
    public SignatureVaultActiveConflictException()
        : base("Ya existe una firma activa para esta compañía y documento.")
    {
    }
}
