namespace Flit.Admin.Domain.Companies.PersonalizedDocuments;

/// <summary>
/// Ciclo de vida de una versión de documento personalizado (HU #11313, §10 del plan técnico).
/// <c>pendiente</c> nace en el alta (el binario todavía no existe); <c>confirm</c> la mueve a
/// <c>activo</c> (y retira a <c>historico</c> la activa previa del mismo tipo) o a <c>rechazado</c>
/// si la validación del servidor falla. Ninguna versión se borra (restricción 9).
/// </summary>
public static class CompanyPersonalizedDocumentStatus
{
    public const string Pendiente = "pendiente";
    public const string Activo = "activo";
    public const string Historico = "historico";
    public const string Rechazado = "rechazado";
}
