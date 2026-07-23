using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// CF-01 (HU #10876) — bloqueo DURO (409 <c>DUPLICATE_ACTIVE_PROCEDURE</c>) de duplicidad de trámite
/// EN PROCESO por familia. SOLO dos familias activan el bloqueo, por ACTIVACIÓN HARDCODED (no depende
/// de flags de configuración como <c>validateDuplicateProcedure</c>):
/// <list type="bullet">
///   <item>Matrícula Inicial → llave VIN.</item>
///   <item>Traspaso → llave placa.</item>
/// </list>
/// El resto de tipos de trámite permite múltiples instancias en proceso simultáneas (sin bloqueo).
/// <para>
/// "En proceso" = <see cref="TramiteEstado.EstadosEnProceso"/> (borrador/preparado/entregado). Los
/// estados finales (<see cref="TramiteEstado.Aprobado"/>, <see cref="TramiteEstado.Rechazado"/>,
/// <see cref="TramiteEstado.Anulado"/>) NO cuentan y LIBERAN el bloqueo (AC4).
/// </para>
/// <para>
/// PURO (sin IO): recibe los trámites existentes ya cargados (mismo VIN/placa, tenant, EXCLUYENDO la
/// instancia en curso) y decide. Independiente de <see cref="VinPolicyEvaluator"/> (check informativo
/// de HU #10538, que sugiere la ruta de traspaso cuando el VIN ya tiene una matrícula APROBADA): esta
/// política es un bloqueo duro sobre trámites EN PROCESO, sin relación con ese flujo informativo.
/// </para>
/// </summary>
public static class DuplicateActiveProcedurePolicy
{
    /// <summary>
    /// Devuelve el <c>Id</c> del primer trámite EN PROCESO encontrado entre <paramref name="existentes"/>
    /// (candidato al bloqueo 409), o <c>null</c> si ninguno está en proceso (llave libre — permite
    /// crear/continuar el trámite).
    /// </summary>
    public static Guid? FindActiveDuplicate(IReadOnlyList<(Guid Id, string Estado)> existentes)
    {
        ArgumentNullException.ThrowIfNull(existentes);

        foreach (var t in existentes)
        {
            if (TramiteEstado.EstadosEnProceso.Contains(t.Estado, StringComparer.OrdinalIgnoreCase))
                return t.Id;
        }

        return null;
    }
}
