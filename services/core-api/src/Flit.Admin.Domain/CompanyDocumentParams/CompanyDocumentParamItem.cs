namespace Flit.Admin.Domain.CompanyDocumentParams;

/// <summary>
/// Parámetro documental de una compañía gestora (HU #10521, RF31). <see cref="State"/> es uno de
/// <c>OCULTO | OBLIGATORIO | OPCIONAL</c>.
/// </summary>
public sealed record CompanyDocumentParamItem(
    Guid Id,
    Guid TenantId,
    string DocumentTypeCode,
    string State);

/// <summary>Estados válidos de un parámetro documental por gestora.</summary>
public static class CompanyDocumentParamStates
{
    public const string Oculto = "OCULTO";
    public const string Obligatorio = "OBLIGATORIO";
    public const string Opcional = "OPCIONAL";

    public static bool IsValid(string? state) =>
        state is Oculto or Obligatorio or Opcional;
}
