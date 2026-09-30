using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners;

/// <summary>
/// HU #13179 (Feature #13119 F7) — validación de las compañías asociadas de un mandatario al crearlo o editarlo,
/// compartida por los tres caminos de escritura (compañía, cliente hijo y OT), que confluyen en
/// <c>CreateMandateSignerHandler</c> / <c>UpdateMandateSignerHandler</c>.
///
/// <list type="bullet">
///   <item>Admin de Compañía (origen <c>compania</c>): solo sus hijas directas activas; una que no lo sea se
///   rechaza con <see cref="AssociatedCompanyOutOfScopeException"/> (403) y NO se guarda nada.</item>
///   <item>OT / Super Admin: cualquier compañía gestora activa, y además RF33: debe operar en el organismo
///   (lista efectiva de red, <see cref="OtCompanyVisibility.WholeNetwork"/>), sin exigir trámites previos.</item>
///   <item>Para todos: la compañía propia del mandatario, una inactiva o un id inexistente dan 422 con el motivo
///   por elemento.</item>
/// </list>
/// Un mandatario sin compañías asociadas es válido: aplica solo a su propia compañía.
/// </summary>
public static class MandateSignerAssociationRules
{
    public const string Field = "associatedCompanyTenantIds";

    public const string PropiaMessage = "La compañía propia del mandatario no puede asociarse a sí misma.";
    public const string InactivaMessage = "La compañía asociada está inactiva o bloqueada.";
    public const string InexistenteMessage = "La compañía asociada no existe.";
    public const string NoOperaEnOrganismoMessage =
        "La compañía no está habilitada o está inactiva en el organismo de tránsito.";
    public const string OrganismoAjenoMessage =
        "Las compañías asociadas solo pueden indicarse para organismos del mandatario.";

    /// <summary>Origen de configuración que identifica al Admin de Compañía (ver <c>MandateSignerOrigins</c>).</summary>
    private const string OrigenCompania = "compania";

    /// <summary>
    /// Devuelve los errores 422 (vacío si todo es válido) o lanza <see cref="AssociatedCompanyOutOfScopeException"/>.
    /// Sin servicio inyectado (constructores antiguos de los handlers) no se valida.
    /// </summary>
    public static async Task<List<MandateSignerValidationError>> ValidateAsync(
        IMandatarioAssociableCompanies? service,
        IMandateSignerReader reader,
        IReadOnlyList<MandateSignerOfficeCompanies>? officeCompanies,
        IReadOnlyList<Guid> ownerCompanyTenantIds,
        IReadOnlyCollection<Guid> mandatarioOfficeIds,
        string configuredByScope,
        CancellationToken cancellationToken)
    {
        var errors = new List<MandateSignerValidationError>();
        if (service is null || officeCompanies is null)
        {
            return errors;
        }

        var candidates = officeCompanies
            .SelectMany(o => o.AssociatedCompanyTenantIds ?? [])
            .Distinct()
            .ToList();

        foreach (var entry in officeCompanies.Where(o => !mandatarioOfficeIds.Contains(o.TransitOfficeId)))
        {
            if ((entry.AssociatedCompanyTenantIds ?? []).Count > 0)
            {
                errors.Add(new MandateSignerValidationError(
                    Field, OrganismoAjenoMessage, entry.TransitOfficeId.ToString()));
            }
        }

        if (candidates.Count == 0)
        {
            return errors;
        }

        var porCompania = string.Equals(configuredByScope, OrigenCompania, StringComparison.Ordinal);
        Guid? scope = porCompania && ownerCompanyTenantIds.Count > 0 ? ownerCompanyTenantIds[0] : null;

        var rechazos = await service
            .RejectionsAsync(scope, ownerCompanyTenantIds, candidates, cancellationToken)
            .ConfigureAwait(false);

        // 403 primero: nada de lo demás se revela ni se guarda si alguna compañía está fuera del alcance.
        if (rechazos.Values.Any(r => r == AssociableCompanyRejections.FueraDeAlcance))
        {
            throw new AssociatedCompanyOutOfScopeException();
        }

        foreach (var (id, motivo) in rechazos)
        {
            errors.Add(new MandateSignerValidationError(Field, MessageFor(motivo), id.ToString()));
        }

        if (!porCompania)
        {
            await AddOperaEnOrganismoErrorsAsync(errors, reader, officeCompanies, rechazos.Keys.ToHashSet(), cancellationToken)
                .ConfigureAwait(false);
        }

        return errors;
    }

    /// <summary>RF33 (se conserva): el OT solo asocia compañías que operan en el organismo.</summary>
    private static async Task AddOperaEnOrganismoErrorsAsync(
        List<MandateSignerValidationError> errors,
        IMandateSignerReader reader,
        IReadOnlyList<MandateSignerOfficeCompanies> officeCompanies,
        HashSet<Guid> yaRechazadas,
        CancellationToken cancellationToken)
    {
        foreach (var entry in officeCompanies)
        {
            var pendientes = (entry.AssociatedCompanyTenantIds ?? []).Distinct().Where(id => !yaRechazadas.Contains(id)).ToList();
            if (pendientes.Count == 0)
            {
                continue;
            }

            var opera = (await reader
                    .ListOtCompaniesAsync(entry.TransitOfficeId, OtCompanyVisibility.WholeNetwork, cancellationToken)
                    .ConfigureAwait(false))
                .Where(c => c.IsEnabled && c.IsActive)
                .Select(c => c.CompanyTenantId)
                .ToHashSet();

            foreach (var id in pendientes.Where(id => !opera.Contains(id)))
            {
                errors.Add(new MandateSignerValidationError(Field, NoOperaEnOrganismoMessage, id.ToString()));
            }
        }
    }

    private static string MessageFor(string motivo) => motivo switch
    {
        AssociableCompanyRejections.CompaniaPropia => PropiaMessage,
        AssociableCompanyRejections.CompaniaInactiva => InactivaMessage,
        _ => InexistenteMessage,
    };
}
