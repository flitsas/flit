using System;
using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Invariante de dominio de la MATRÍCULA INICIAL: un VIN → una matrícula activa.
/// <para>
/// Paridad <c>tramites.vin-policy.ts</c> de Johan, pero PURO (sin IO): recibe los trámites
/// existentes del mismo VIN ya cargados y decide. Reglas:
/// </para>
/// <list type="bullet">
///   <item><c>rechazado</c>/<c>anulado</c> → permite reintentar (no bloquea; son estados finales que
///   liberan la llave, HU #10876/CF-01 AC4).</item>
///   <item><c>aprobado</c> → bloquea de por vida (<see cref="VinConflictCode.TramiteMatriculaCompletada"/>).</item>
///   <item>cualquier otro estado activo → duplicado (<see cref="VinConflictCode.TramiteDuplicado"/>).</item>
/// </list>
/// <para>
/// Rol INFORMATIVO (check <c>warn</c>, HU #10538): distinto del bloqueo DURO (409) de duplicidad EN
/// PROCESO de <see cref="DuplicateActiveProcedurePolicy"/> (HU #10876/CF-01) — no fusionar ambas
/// responsabilidades.
/// </para>
/// </summary>
public static class VinPolicyEvaluator
{
    private const string EstadoRechazado = TramiteEstado.Rechazado;
    private const string EstadoAnulado = TramiteEstado.Anulado;
    private const string EstadoCompletado = TramiteEstado.Aprobado;

    /// <summary>
    /// Matrícula inicial = modalidad VIN-first sin tipología de traspaso/otra
    /// (paridad <c>isMatriculaInicial</c>). Sin tipología → matrícula inicial.
    /// </summary>
    public static bool IsMatriculaInicial(string? tipologiaCodigo) =>
        string.IsNullOrEmpty(tipologiaCodigo) ||
        tipologiaCodigo == TramiteTipologiaCatalog.CodigoMatriculaInicial;

    /// <summary>
    /// Evalúa los trámites existentes del mismo VIN (ya filtrados por VIN normalizado y, si aplica,
    /// excluyendo el trámite en curso) y devuelve el conflicto bloqueante de mayor prioridad, o
    /// <c>null</c> si el VIN está disponible. <paramref name="existentes"/> debe venir ordenado
    /// por recencia desc (el primero bloqueante define el mensaje), igual que en Johan.
    /// Paridad <c>findVinMatriculaInicialConflict</c>.
    /// </summary>
    public static VinConflict? EvaluarConflicto(IReadOnlyList<VinTramiteExistente> existentes)
    {
        ArgumentNullException.ThrowIfNull(existentes);

        var bloqueantes = existentes
            .Where(t => !string.Equals(t.Estado, EstadoRechazado, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(t.Estado, EstadoAnulado, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (bloqueantes.Count == 0)
            return null;

        var latest = bloqueantes[0];

        if (string.Equals(latest.Estado, EstadoCompletado, StringComparison.OrdinalIgnoreCase))
        {
            return new VinConflict(
                VinConflictCode.TramiteMatriculaCompletada,
                "Este vehículo ya tiene una matrícula inicial completada. Un VIN solo puede matricularse una vez.",
                latest);
        }

        var placaHint = string.IsNullOrEmpty(latest.Placa) ? string.Empty : $" (placa {latest.Placa})";
        return new VinConflict(
            VinConflictCode.TramiteDuplicado,
            $"Ya existe un trámite de matrícula inicial para este VIN{placaHint}. " +
            "Continúe el trámite existente en lugar de crear uno nuevo.",
            latest);
    }
}
