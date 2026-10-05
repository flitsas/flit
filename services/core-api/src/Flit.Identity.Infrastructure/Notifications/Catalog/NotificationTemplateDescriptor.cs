namespace Flit.Infrastructure.Notifications.Catalog;

/// <summary>
/// Entrada del catálogo enumerable de plantillas de correo (HU #11353).
/// </summary>
/// <param name="Id">
/// Id ESTABLE de la plantilla. Es un LITERAL escrito a mano (AC3) — nunca <c>nameof</c>,
/// <c>typeof(...).Name</c> ni ningún otro mecanismo derivado del nombre de un tipo, espacio de
/// nombres o ruta de archivo. Renombrar la clase que compone la plantilla NO cambia este id.
/// </param>
/// <param name="Name">Nombre legible para humanos (paneles, logs, futura HU #11356).</param>
/// <param name="Module">Módulo dueño de la plantilla (Seguridad o Analítica).</param>
/// <param name="Triggers">
/// Uno o más disparadores que reutilizan la misma plantilla (AC2). Nunca vacío.
/// </param>
public sealed record NotificationTemplateDescriptor(
    string Id,
    string Name,
    NotificationModule Module,
    IReadOnlyList<NotificationTrigger> Triggers);
