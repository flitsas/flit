namespace Flit.Modules.Security.Domain.PermissionOverrides;

/// <summary>
/// Hallazgo de code review de la Épica #12753 (HU #13644): un permiso con <c>enforced = 'true'</c> solo se exige de verdad
/// en la API cuando el flag que lo protege está encendido (<c>Security:OtPermissionEnforcement</c>,
/// <c>Security:ModulePermissionEnforcement</c> y los de familia que registre F-F). Con el flag apagado, denegarlo oculta el
/// menú pero la API lo deja pasar (falsa revocación), y otorgarlo no cambia nada. Esta abstracción responde "¿hoy se exige?"
/// leyendo el flag en vivo, para que no se ofrezcan ni se acepten overrides que no tienen efecto real.
/// </summary>
public interface IPermissionEnforcementState
{
    /// <summary><c>true</c> si la API exige hoy el slug (flag encendido o slug sin flag). Lectura viva: respeta cambios de configuración.</summary>
    bool IsEffectivelyEnforced(string slug);
}
