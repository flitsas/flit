using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Gates de momento para estampar impronta manual (HU #12116):
/// matrícula → placa asignada + identidad de propietarios;
/// resto → identidad de propietarios al componer consolidado OT.
/// </summary>
public static class ImprontaManualStampReadiness
{
    public const string PlacaPendiente = "placa_pendiente";
    public const string PlacaPreasignado = "placa_preasignado";
    public const string SinPropietarios = "sin_propietarios";
    public const string PropietariosSinIdentidad = "propietarios_sin_identidad";

    /// <summary>
    /// ¿Se puede estampar ahora? Si no, <paramref name="reason"/> indica el bloqueo (sin PII).
    /// </summary>
    public static async Task<(bool Ready, string? Reason)> EvaluateAsync(
        ProcedureInstance instance,
        IProcedureInstanceRepository? repo,
        ISignatureVaultPolicy? vaultPolicy = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (IsMatriculaFamily(instance))
        {
            if (string.IsNullOrWhiteSpace(instance.Plate))
                return (false, PlacaPendiente);
            if (string.Equals(instance.PlateFlowStatus, PlateFlowStatus.Preasignado, StringComparison.Ordinal))
                return (false, PlacaPreasignado);
        }

        var profile = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        var rol = profile.RequiresSeller ? "vendedor" : "comprador";

        var actores = instance.Actors
            .Where(a => string.Equals(a.ActorType, rol, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (actores.Count == 0)
            return (false, SinPropietarios);

        // Sin repo no podemos acreditar identidad cross-trámite / baúl: no estampar a medias.
        if (repo is null)
            return (false, PropietariosSinIdentidad);

        var approved = await IdentityApprovalResolver
            .ResolveApprovedPartiesAsync(repo, instance, DateTimeOffset.UtcNow, ct, vaultPolicy)
            .ConfigureAwait(false);

        if (!approved.Contains(rol))
            return (false, PropietariosSinIdentidad);

        return (true, null);
    }

    private static bool IsMatriculaFamily(ProcedureInstance instance)
    {
        try
        {
            return instance.Family == ProcedureFamily.Matriculas;
        }
        catch (InvalidOperationException)
        {
            // Sin ProcedureType cargado: no asumir matrícula (fail-open de familia).
            return false;
        }
    }
}
