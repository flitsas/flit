using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Identity;

/// <summary>
/// HU #13285 (Feature #13280 A3, Épica #13202) — ORIGEN de una validación de identidad (a quién pertenece la fila) para que el
/// consumidor del flujo manual (revisión C1, listado C8) la distinga sin repetir la heurística. Valores estables del contrato:
/// <see cref="Tramite"/> | <see cref="Prevalidacion"/> | <see cref="Mandatario"/> | <see cref="RepresentanteLegal"/>.
/// <para>
/// Reglas (en este orden, una sola fuente):
/// <list type="number">
/// <item><c>MandateSignerId</c> informado (o <c>PartyRole = mandatario</c>, ligados por CHECK en BD, DDL 129) → <see cref="Mandatario"/>.
/// La validación es EXCLUSIVA de esa ficha (HU #13246) y no entra en consultas por documento.</item>
/// <item>Ligada a un trámite (<c>ProcedureInstanceId</c>) → <see cref="Tramite"/>.</item>
/// <item>Standalone cuya persona es jurídica → <see cref="RepresentanteLegal"/>: la biométrica standalone de una persona
/// jurídica valida a su representante legal (ADR-0030, <c>Person.LegalRep*</c>). Necesita <c>Person</c> cargada.</item>
/// <item>Cualquier otra standalone → <see cref="Prevalidacion"/>.</item>
/// </list>
/// Limitación conocida: el representante legal que valida DENTRO de un trámite queda como <see cref="Tramite"/> (la fila no
/// guarda marca de representante; su rol de parte es comprador/vendedor).
/// </para>
/// Uso: <c>ManualValidationOrigin.From(row)</c> con <c>row.Person</c> cargada (o <c>From(row, person)</c>).
/// </summary>
public static class ManualValidationOrigin
{
    public const string Tramite = "tramite";
    public const string Prevalidacion = "prevalidacion";
    public const string Mandatario = "mandatario";
    public const string RepresentanteLegal = "representante_legal";

    /// <summary>Todos los valores del contrato.</summary>
    public static readonly IReadOnlyList<string> Todos = [Tramite, Prevalidacion, Mandatario, RepresentanteLegal];

    /// <summary>Origen de la fila usando su navegación <see cref="ProcedureInstanceBiometricValidation.Person"/>.</summary>
    public static string From(ProcedureInstanceBiometricValidation row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return From(row, row.Person);
    }

    /// <summary>Origen de la fila con la persona dueña indicada (para quien no la tiene cargada en la navegación).</summary>
    public static string From(ProcedureInstanceBiometricValidation row, Person? person)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.MandateSignerId is not null
            || string.Equals(row.PartyRole, BiometricRules.ParteMandatario, StringComparison.Ordinal))
            return Mandatario;
        if (row.ProcedureInstanceId is not null)
            return Tramite;
        return string.Equals(person?.PersonType, PersonTypes.Juridical, StringComparison.Ordinal)
            ? RepresentanteLegal
            : Prevalidacion;
    }
}
