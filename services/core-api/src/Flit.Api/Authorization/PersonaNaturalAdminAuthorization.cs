namespace Flit.Api.Authorization;

/// <summary>
/// Permiso de la administración del portal de Persona Natural en Plataforma (Feature J, Épica #14021). Exclusivo del
/// Super Admin FLIT: el slug lo crea el DDL 163 y se concede solo al rol SuperAdmin; los endpoints exigen además el ROL
/// (<see cref="AdminAuthorization.SuperAdminPolicy"/>) para que un rol personalizado al que se le asigne el slug a mano
/// tampoco pueda usarlo (mismo patrón de <see cref="IdentidadAdminAuthorization"/>).
/// </summary>
public static class PersonaNaturalAdminAuthorization
{
    /// <summary>Módulo RBAC bajo el que vive el permiso.</summary>
    public const string ModuleCode = "persona-natural-admin";

    /// <summary>Administrar tarifas, configuración y contenido del portal de Persona Natural.</summary>
    public const string ManageSlug = "platform.persona_natural.manage";
}
