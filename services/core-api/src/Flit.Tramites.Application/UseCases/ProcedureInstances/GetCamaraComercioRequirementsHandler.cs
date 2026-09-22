using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12775 — un requisito de Cámara de Comercio por actor persona jurídica del trámite.
/// <para><c>exencion</c> viaja como texto (<c>ninguna</c> | <c>firma_precargada</c> |
/// <c>escritura_vigente</c>) y no como número: es lo que el paso del actor usa para elegir el texto
/// que lee el gestor, y un entero en el JSON obligaría al cliente a mantener su propia tabla de
/// equivalencias.</para>
/// </summary>
public sealed record CamaraComercioRequirementDto(
    string Rol,
    string Tipo,
    bool EsObligatorio,
    string Exencion);

public sealed record CamaraComercioRequirementsResponse(
    IReadOnlyList<CamaraComercioRequirementDto> Requirements);

/// <summary>
/// HU #12775 — expone la escalera de obligatoriedad del certificado de Cámara de Comercio para los
/// actores del trámite. Lo consume el paso del actor en el asistente (HU #12777).
/// </summary>
public sealed class GetCamaraComercioRequirementsHandler(
    IProcedureInstanceRepository repo,
    CamaraComercioRequirementResolver resolver)
{
    public async Task<(CamaraComercioRequirementsResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithActorsAsync(id, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, "not_found");

        var requirements = await resolver
            .ResolveAsync(tenantId, instance.Actors, ct)
            .ConfigureAwait(false);

        return (new CamaraComercioRequirementsResponse(
            [.. requirements.Select(r => new CamaraComercioRequirementDto(
                r.Rol, r.Tipo, r.EsObligatorio, ToWire(r.Exencion)))]), null);
    }

    /// <summary>
    /// Nombre estable de la exención en el contrato. Se escribe a mano en vez de derivarlo del enum
    /// con <c>ToString()</c>: renombrar un valor del enum es refactor interno y no puede cambiar en
    /// silencio lo que el cliente ya interpreta.
    /// </summary>
    public static string ToWire(CamaraComercioExencion exencion) => exencion switch
    {
        CamaraComercioExencion.FirmaPrecargada => "firma_precargada",
        CamaraComercioExencion.EscrituraVigente => "escritura_vigente",
        _ => "ninguna",
    };
}
