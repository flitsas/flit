namespace Flit.Api.Authorization;

/// <summary>
/// Slugs de permiso para las acciones de gestión avanzada del administrador sobre
/// trámites en el Dashboard (Feature #12155, HU #12157).
///
/// Cada acción tiene su propio slug para que se pueda habilitar/negar de forma
/// independiente por rol (AC3): tener uno de estos permisos NO habilita los demás.
///
/// Enforcement: mismo mecanismo genérico ya usado por otros módulos (HU #10165) —
/// <see cref="PermissionRequirement"/> + <see cref="PermissionAuthorizationHandler"/>,
/// aplicado a un endpoint minimal API vía <c>.RequirePermission(slug)</c>
/// (<see cref="RequirePermissionExtensions"/>). SuperAdmin conserva el bypass total
/// que ya trae el handler; no requiere el slug explícito.
///
/// Esta HU (#12157) solo define el catálogo y deja el mecanismo listo para consumo;
/// los endpoints de negocio que exigen cada slug los implementan las HUs dependientes:
/// #12158 (limpiar/cargar consolidado), #12159 (cambiar estado), #12160 (anular),
/// #12161 (reenviar validación de identidad), #12162 (reasignar gestor).
///
/// No confundir con <c>AdminAuthorization.OtModulePolicy</c> (Feature #12156, módulo OT):
/// son catálogos de autorización completamente separados.
/// </summary>
public static class AdminTramiteAuthorization
{
    /// <summary>Código del módulo RBAC bajo el que se agrupan estos 6 permisos.</summary>
    public const string ModuleCode = "admin-tramites-avanzado";

    /// <summary>Cambiar el estado de un trámite manualmente (HU #12159).</summary>
    public const string CambiarEstadoSlug = "AdminTramiteCambiarEstado";

    /// <summary>Anular un trámite (HU #12160).</summary>
    public const string AnularSlug = "AdminTramiteAnular";

    /// <summary>Limpiar el consolidado de un trámite (HU #12158).</summary>
    public const string LimpiarConsolidadoSlug = "AdminTramiteLimpiarConsolidado";

    /// <summary>Cargar el consolidado de un trámite (HU #12158).</summary>
    public const string CargarConsolidadoSlug = "AdminTramiteCargarConsolidado";

    /// <summary>Reenviar la validación de identidad de un trámite (HU #12161).</summary>
    public const string ReenviarValidacionSlug = "AdminTramiteReenviarValidacion";

    /// <summary>Reasignar el gestor de un trámite (HU #12162).</summary>
    public const string ReasignarGestorSlug = "AdminTramiteReasignarGestor";

    /// <summary>Los 6 slugs del catálogo, útil para pruebas parametrizadas y validaciones cruzadas.</summary>
    public static readonly IReadOnlyList<string> AllSlugs =
    [
        CambiarEstadoSlug,
        AnularSlug,
        LimpiarConsolidadoSlug,
        CargarConsolidadoSlug,
        ReenviarValidacionSlug,
        ReasignarGestorSlug,
    ];
}
