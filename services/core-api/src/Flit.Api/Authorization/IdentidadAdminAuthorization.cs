namespace Flit.Api.Authorization;

/// <summary>
/// Permisos de las acciones administrativas sobre validaciones de identidad (HU #13824, Épica #13817). Exclusivos del
/// Super Admin: el slug lo crea el DDL 151 y se concede solo al rol SuperAdmin; el endpoint exige además el ROL
/// (<see cref="AdminAuthorization.SuperAdminPolicy"/>) para que un rol personalizado al que se le asigne el slug a mano
/// tampoco pueda usarlo.
/// </summary>
public static class IdentidadAdminAuthorization
{
    /// <summary>Módulo RBAC bajo el que vive el permiso.</summary>
    public const string ModuleCode = "validaciones-identidad-admin";

    /// <summary>Vencer una identidad aprobada y vigente (HU #13824).</summary>
    public const string VencerSlug = "identidad.validaciones.vencer";
}
