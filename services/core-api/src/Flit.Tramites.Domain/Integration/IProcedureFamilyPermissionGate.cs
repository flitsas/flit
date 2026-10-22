using Flit.Tramites.Domain.Enums;

namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753) — permisos Ver/Crear por familia de trámite del usuario que opera.
/// Familias = eje <see cref="ProcedureFamily"/> (ADR-0050): Matrículas, Traspaso y Otros (todo lo demás).
/// <para>
/// Sin exigencia activa (flag <c>Security:ModulePermissionEnforcement:Enabled</c> apagado, o sin usuario HTTP como en
/// gRPC/jobs) todo se permite: el comportamiento es idéntico al previo.
/// </para>
/// </summary>
public interface IProcedureFamilyPermissionGate
{
    /// <summary>¿Hay exigencia activa para esta petición? (evita consultas extra cuando no la hay).</summary>
    bool IsActive { get; }

    /// <summary>¿Puede crear trámites de la familia? (se evalúa en AND con <c>tramites.create</c> de la ruta).</summary>
    bool CanCreate(ProcedureFamily family);

    /// <summary>¿Puede ver trámites de la familia?</summary>
    bool CanRead(ProcedureFamily family);

    /// <summary>Códigos de familia (<see cref="ProcedureFamilyCodes"/>) que puede ver; <c>null</c> = sin restricción.</summary>
    IReadOnlyList<string>? ReadableFamilyCodes();
}

/// <summary>Null-object: todo permitido (tests / entornos sin API).</summary>
public sealed class NullProcedureFamilyPermissionGate : IProcedureFamilyPermissionGate
{
    public bool IsActive => false;

    public bool CanCreate(ProcedureFamily family) => true;

    public bool CanRead(ProcedureFamily family) => true;

    public IReadOnlyList<string>? ReadableFamilyCodes() => null;
}

/// <summary>Slugs del catálogo por familia (DDL 145). Matrículas incluye leasing, cancelación y rematrícula; Otros, todo lo demás.</summary>
public static class FamilyPermissionSlugs
{
    public static string Read(ProcedureFamily family) => family switch
    {
        ProcedureFamily.Matriculas => "matriculas.read",
        ProcedureFamily.Traspaso => "traspasos.read",
        ProcedureFamily.Otros => "otros-tramites.read",
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Familia no soportada."),
    };

    public static string Create(ProcedureFamily family) => family switch
    {
        ProcedureFamily.Matriculas => "matriculas.create",
        ProcedureFamily.Traspaso => "traspasos.create",
        ProcedureFamily.Otros => "otros-tramites.create",
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Familia no soportada."),
    };
}
