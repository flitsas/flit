using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Clasificación y capacidades de un expediente, derivadas del tipo de trámite (ADR-0050).
/// <para>Sustituye a <c>ProcedureInstance.FamilyCode</c> y <c>TipologiaCodigo</c>. La fuente
/// preferente es el snapshot congelado al crear (<c>procedure_type_snapshots</c>), de modo que un
/// cambio posterior del catálogo no reclasifique expedientes en curso; el catálogo vivo es el
/// respaldo.</para>
/// <para>Las decisiones de flujo se toman sobre <see cref="GateProfile"/>, no sobre la familia: una
/// <c>CANCELACION_MATRICULA</c> es <see cref="ProcedureFamily.Matriculas"/> y no pide placa ni
/// comprador. Preguntar por capacidad, no por familia.</para>
/// </summary>
/// <param name="ProcedureTypeId">FK al tipo en el catálogo.</param>
/// <param name="Code">Código canónico (<c>MATRICULA_NUEVA</c>, <c>BLINDAJE</c>, …). Es también la
/// tipología: ADR-0050 elimina el catálogo de tipologías paralelo.</param>
/// <param name="Name">Etiqueta de negocio; es la que deben rotular FUR, portada y mandato.</param>
/// <param name="Family">Familia para clasificación, filtros, causales y gates por familia.</param>
/// <param name="GateProfile">Capacidades declaradas del tipo (CFD-01).</param>
public sealed record ProcedureClassification(
    Guid ProcedureTypeId,
    string Code,
    string Name,
    ProcedureFamily Family,
    ProcedureTypeGateProfile GateProfile)
{
    /// <summary>Código persistido de la familia.</summary>
    public string FamilyCode => ProcedureFamilyCodes.ToCode(Family);

    /// <summary>Entrada por VIN (vehículo aún sin placa).</summary>
    public bool EntersByVin =>
        string.Equals(GateProfile.EntryMode, ProcedureTypeGateProfile.EntryModeVin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(GateProfile.EntryMode, ProcedureTypeGateProfile.EntryModeBoth, StringComparison.OrdinalIgnoreCase);

    /// <summary>Entrada por placa (vehículo ya matriculado).</summary>
    public bool EntersByPlate =>
        string.Equals(GateProfile.EntryMode, ProcedureTypeGateProfile.EntryModePlate, StringComparison.OrdinalIgnoreCase)
        || string.Equals(GateProfile.EntryMode, ProcedureTypeGateProfile.EntryModeBoth, StringComparison.OrdinalIgnoreCase);

    /// <summary>El trámite tiene parte saliente (vendedor / titular que transfiere).</summary>
    public bool RequiresSeller => GateProfile.RequiresSeller;

    /// <summary>El trámite tiene parte entrante (comprador / adquirente).</summary>
    public bool RequiresBuyer => GateProfile.RequiresBuyer;

    /// <summary>Clasificación a partir del catálogo vivo.</summary>
    public static ProcedureClassification FromType(ProcedureType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return new ProcedureClassification(
            type.Id,
            type.Code,
            type.Name,
            ProcedureFamilyCodes.FromCodeOrOtros(type.Family),
            ProcedureTypeGateProfile.FromJson(type.GateProfile));
    }

    /// <summary>
    /// Clasificación a partir del snapshot congelado (<c>procedure_type_snapshots.snapshot</c>).
    /// Devuelve <c>null</c> si el JSON es nulo, inválido o no trae <c>code</c>: el llamador debe caer
    /// al catálogo vivo. Deserialización tolerante, igual que
    /// <see cref="ProcedureTypeGateProfile.FromJson"/> — el snapshot es configuración capturada, no
    /// un invariante del dominio.
    /// </summary>
    public static ProcedureClassification? FromSnapshotJson(Guid procedureTypeId, string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var code = root.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(code))
                return null;

            var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var family = root.TryGetProperty("family", out var familyEl) ? familyEl.GetString() : null;

            var gateProfile = root.TryGetProperty("gateProfile", out var gpEl)
                ? ProcedureTypeGateProfile.FromJson(gpEl.GetRawText())
                : new ProcedureTypeGateProfile();

            return new ProcedureClassification(
                procedureTypeId,
                code!,
                name ?? code!,
                ProcedureFamilyCodes.FromCodeOrOtros(family),
                gateProfile);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
