namespace Flit.Infrastructure.Notifications.Renting;

/// <summary>
/// HU #11359 — configuración del canal de API del cliente Renting (envío de correo vía la API
/// externa del cliente, con mTLS). <see cref="Enabled"/> es el ÚNICO interruptor: en <c>false</c>
/// (valor por defecto, canal desplegado apagado) NINGUNA otra propiedad se valida ni se exige —
/// ni siquiera el material TLS. Solo con <see cref="Enabled"/> en <c>true</c>
/// <c>InfrastructureExtensions.AddRentingChannel</c> valida la presencia de las variables
/// obligatorias y carga el certificado cliente (falla rápida en el arranque, ver
/// <see cref="RentingClientCertificateLoader"/>).
///
/// <para>
/// Esta clase SOLO modela configuración de transporte/login/envío. NO implementa login ni caché de
/// token (HU #11360), NI el adaptador de envío/multipart (HU #11361), NI el desvío de destinatario
/// en no-producción (HU #11364: consume <see cref="SendEmailDevelopmentRecipientEmail"/> /
/// <see cref="SendEmailDevelopmentRecipientUsername"/>), NI el enrutamiento por canal (HU #11362:
/// consume <see cref="Enabled"/> vía <c>IOptions&lt;RentingChannelOptions&gt;</c> para decidir si un
/// tenant con <c>NotificationChannel = "tenant_api"</c> puede enviar por este canal — AC6).
/// </para>
/// </summary>
public sealed class RentingChannelOptions
{
    public const string SectionName = "Notifications:Renting";

    /// <summary>Nombre del <see cref="System.Net.Http.HttpClient"/> registrado con el certificado cliente (mTLS).</summary>
    public const string HttpClientName = "RentingApi";

    /// <summary>
    /// Interruptor único del canal. Por defecto <c>false</c>: el canal se despliega apagado y se
    /// enciende en una ventana acordada (AC2). Sin este interruptor en <c>true</c>, ninguna otra
    /// variable del canal se exige.
    /// </summary>
    public bool Enabled { get; set; }

    // ── Transporte ────────────────────────────────────────────────────────────────

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Nombre de la cabecera HTTP de la clave de suscripción (no el valor).</summary>
    public string ApiKeyName { get; set; } = string.Empty;

    /// <summary>Valor de la clave de suscripción. NUNCA se loguea.</summary>
    public string ApiKeyValue { get; set; } = string.Empty;

    /// <summary>Ruta del archivo PFX del certificado cliente (mTLS).</summary>
    public string PfxCertificatePath { get; set; } = string.Empty;

    /// <summary>Passphrase del PFX. NUNCA se loguea ni se incluye en mensajes de error.</summary>
    public string Passphrase { get; set; } = string.Empty;

    public int SecondsTimeout { get; set; } = 30;

    // ── Login ─────────────────────────────────────────────────────────────────────

    public string LoginPath { get; set; } = string.Empty;

    public int LoginSecondsTimeout { get; set; } = 30;

    /// <summary>Llave de caché del token de login (uso de la HU #11360; solo se modela aquí).</summary>
    public string LoginCacheKey { get; set; } = string.Empty;

    /// <summary>TTL en segundos de la caché del token (uso de la HU #11360; solo se modela aquí).</summary>
    public int LoginCacheSecondsTtl { get; set; } = 3600;

    /// <summary>Nombre del secreto de login (uso de la HU #11360; solo se modela aquí).</summary>
    public string LoginSecretName { get; set; } = string.Empty;

    /// <summary>
    /// Sujeto (Subject) esperado del certificado cliente para el login. DEBE coincidir EXACTAMENTE
    /// con el <c>Subject</c> del certificado cargado desde <see cref="PfxCertificatePath"/> — mTLS
    /// y login comparten la misma identidad (AC5). Una discrepancia (p. ej. tras renovar el
    /// certificado con otro CN sin actualizar esta variable) impide el arranque.
    /// </summary>
    public string LoginSubject { get; set; } = string.Empty;

    // ── Envío ─────────────────────────────────────────────────────────────────────

    public string SendEmailPath { get; set; } = string.Empty;

    public int SendEmailSecondsTimeout { get; set; } = 30;

    /// <summary>
    /// HU #11361 AC5 — longitud máxima (caracteres) del cuerpo de error del proveedor que se
    /// conserva tras sanear secretos (ver <see cref="RentingEmailApiSender"/>). No es una variable
    /// obligatoria (no participa de la validación de arranque de esta HU): sin configurar, aplica
    /// el valor por defecto.
    /// </summary>
    public int SendEmailErrorBodyMaxLength { get; set; } = 500;

    /// <summary>Correo remitente que USA este canal (Renting). No confundir con <see cref="DefaultSenderEmail"/>.</summary>
    public string SendEmailSenderEmail { get; set; } = string.Empty;

    /// <summary>Usuario remitente que USA este canal (Renting). No confundir con <see cref="DefaultSenderUsername"/>.</summary>
    public string SendEmailSenderUsername { get; set; } = string.Empty;

    /// <summary>
    /// Correo remitente por defecto. Pertenece a OTRO proxy del cliente que comparte este mismo
    /// bloque de configuración — este canal (Renting) NO lo usa; se modela solo para no perder la
    /// variable. No participa en la validación de arranque de este canal.
    /// </summary>
    public string DefaultSenderEmail { get; set; } = string.Empty;

    /// <summary>Ver <see cref="DefaultSenderEmail"/>: pertenece a otro proxy, no a este canal.</summary>
    public string DefaultSenderUsername { get; set; } = string.Empty;

    /// <summary>
    /// Destinatario de desvío en ambientes no productivos. Se modela aquí; su USO es de la
    /// HU #11364. No participa en la validación de arranque de este canal.
    /// </summary>
    public string SendEmailDevelopmentRecipientEmail { get; set; } = string.Empty;

    /// <summary>Ver <see cref="SendEmailDevelopmentRecipientEmail"/>: consumo en la HU #11364.</summary>
    public string SendEmailDevelopmentRecipientUsername { get; set; } = string.Empty;

    /// <summary>
    /// HU #11364 — interruptor AFIRMATIVO y PROPIO del desvío de destinatario. Es la ÚNICA señal
    /// que decide si un envío se desvía (AC5): el nombre del ambiente (<c>IHostEnvironment</c>)
    /// NUNCA se consulta para tomar esa decisión — solo participa, en el arranque
    /// (<c>InfrastructureExtensions.AddRentingChannel</c>), para exigir que este interruptor esté
    /// en el valor correcto (AC3: prohibido en producción; AC4: obligatorio fuera de producción con
    /// el canal habilitado). Variable: <c>RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED</c>.
    /// </summary>
    public bool SendEmailDevelopmentRecipientOverrideEnabled { get; set; }
}
