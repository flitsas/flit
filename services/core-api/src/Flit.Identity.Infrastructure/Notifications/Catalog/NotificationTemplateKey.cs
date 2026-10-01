namespace Flit.Infrastructure.Notifications.Catalog;

/// <summary>
/// Pareja (plantilla, canal) que identifica una resolución en el catálogo (HU #11353).
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TChannel"/> es genérico A PROPÓSITO: el catálogo resuelve por esta pareja
/// sin mantener una lista fija de canales soportados. Dar de alta un canal nuevo (hoy
/// <see cref="Flit.Admin.Domain.Companies.Settings.NotificationChannel"/> tiene
/// <c>FlitSmtp</c>/<c>TenantApi</c>) NO exige tocar <see cref="NotificationTemplateCatalog"/>: el
/// canal solo viaja como parte de la llave, nunca se inspecciona dentro del catálogo. El arnés de
/// pruebas lo verifica resolviendo con un canal FALSO (un enum que el catálogo jamás ha visto) —
/// si esa prueba compila y resuelve, es la evidencia de que no hay ningún <c>switch</c> ni lista
/// por canal escondida.
/// </para>
/// <para>
/// Ningún actor de negocio (handler, endpoint) puede observar esta genericidad: siempre se
/// instancia con el <c>NotificationChannel</c> real. Es una restricción de diseño verificable en
/// pruebas, no un AC de negocio.
/// </para>
/// </remarks>
public sealed record NotificationTemplateKey<TChannel>(string TemplateId, TChannel Channel);
