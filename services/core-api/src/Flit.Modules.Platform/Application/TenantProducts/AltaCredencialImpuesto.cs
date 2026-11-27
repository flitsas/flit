namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// HU #14711 (plan docs/diagnosticos/plan-alta-credencial-impuesto-diagnostico.md §3.1) — alta idempotente de la empresa en el
/// proveedor del impuesto (<c>POST /api/v1/integracion/empresas</c>, guía de integración §3). La implementación vive en
/// infraestructura con un <c>HttpClient</c> tipado; los fallos salen como <see cref="AltaCredencialImpuestoException"/> con un
/// código de <see cref="CodigosAltaCredencialImpuesto"/>. Nunca registra el token, la api_key ni el secreto.
/// </summary>
public interface IClienteAltaProveedorImpuesto
{
    /// <summary>Da de alta (o recupera) la empresa. Reintenta una vez ante 5xx, 429 o timeout.</summary>
    /// <exception cref="AltaCredencialImpuestoException">
    /// <see cref="CodigosAltaCredencialImpuesto.ProveedorNoConfigurado"/>, <see cref="CodigosAltaCredencialImpuesto.ProveedorTokenInvalido"/>,
    /// <see cref="CodigosAltaCredencialImpuesto.ProveedorIntegracionDeshabilitada"/>, <see cref="CodigosAltaCredencialImpuesto.ProveedorSolicitudInvalida"/>
    /// o <see cref="CodigosAltaCredencialImpuesto.ProveedorNoDisponible"/>.
    /// </exception>
    Task<RespuestaAltaProveedor> DarDeAltaAsync(PeticionAltaProveedor peticion, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cuerpo del alta: razón social, NIT base (sin DV; el proveedor lo calcula) y correo. <see cref="ToString"/> sin PII: del NIT solo
/// los 3 últimos dígitos.
/// </summary>
public sealed record PeticionAltaProveedor(string LegalName, string TaxId, string ContactEmail)
{
    public override string ToString() =>
        $"PeticionAltaProveedor {{ TaxId = {EnmascararNit(TaxId)}, LegalName = [{LegalName.Length} car.], ContactEmail = [REDACTADO] }}";

    /// <summary>El NIT con solo sus 3 últimos caracteres a la vista (<c>***456</c>); sin el largo.</summary>
    public static string EnmascararNit(string? nit) =>
        string.IsNullOrEmpty(nit) ? string.Empty : nit.Length <= 3 ? "***" : "***" + nit[^3..];
}

/// <summary>Respuesta 200/201 del alta. <see cref="ToString"/> nunca imprime la llave ni el secreto.</summary>
/// <param name="ApiKey">La llave de la empresa (viene siempre).</param>
/// <param name="WebhookSecret">El secreto del webhook (viene siempre según la guía; null si el proveedor no lo mandó).</param>
/// <param name="Created"><c>true</c> si la empresa nació en esta llamada (201).</param>
/// <param name="ApiKeyRotated"><c>true</c> si el proveedor reemplazó una llave irrecuperable.</param>
public sealed record RespuestaAltaProveedor(string ApiKey, string? WebhookSecret, bool Created, bool ApiKeyRotated)
{
    public override string ToString() =>
        $"RespuestaAltaProveedor {{ Created = {Created}, ApiKeyRotated = {ApiKeyRotated}, ApiKey = [REDACTADO], WebhookSecret = {(WebhookSecret is null ? "null" : "[REDACTADO]")} }}";
}

/// <summary>Rechazo del alta de la credencial. <see cref="Code"/> es estable; el mensaje es el literal cerrado del código, sin valores.</summary>
public sealed class AltaCredencialImpuestoException : Exception
{
    public AltaCredencialImpuestoException()
    {
        Code = string.Empty;
    }

    public AltaCredencialImpuestoException(string code)
        : base(CodigosAltaCredencialImpuesto.Literal(code))
    {
        Code = code;
    }

    public AltaCredencialImpuestoException(string code, Exception innerException)
        : base(CodigosAltaCredencialImpuesto.Literal(code), innerException)
    {
        Code = code;
    }

    /// <summary>Código de <see cref="CodigosAltaCredencialImpuesto"/>.</summary>
    public string Code { get; }
}

/// <summary>
/// HU #14711 — códigos y literales del alta de la credencial (endpoint <c>POST …/credentials/impuesto/obtener</c> y estado de
/// aprovisionamiento de <c>diagnostico</c>). El endpoint los traduce a HTTP: NIT_* y SIN_ADMIN 422, PRODUCTO_NO_HABILITADO y
/// ALTA_EN_CURSO 409, TENANT_NOT_FOUND 404, PROVEEDOR_NO_CONFIGURADO 503 y los demás PROVEEDOR_* 502. ERROR_INTERNO solo lo
/// deja el alta automática en el estado (una excepción inesperada de la ronda).
/// </summary>
public static class CodigosAltaCredencialImpuesto
{
    public const string NitAusente = "NIT_AUSENTE";
    public const string NitAmbiguo = "NIT_AMBIGUO";
    public const string NitIgualACabeza = "NIT_IGUAL_A_CABEZA";
    public const string SinAdmin = "SIN_ADMIN";
    public const string ProductoNoHabilitado = "PRODUCTO_NO_HABILITADO";
    public const string TenantNotFound = "TENANT_NOT_FOUND";
    public const string ProveedorNoConfigurado = "PROVEEDOR_NO_CONFIGURADO";
    public const string ProveedorTokenInvalido = "PROVEEDOR_TOKEN_INVALIDO";
    public const string ProveedorIntegracionDeshabilitada = "PROVEEDOR_INTEGRACION_DESHABILITADA";
    public const string ProveedorSolicitudInvalida = "PROVEEDOR_SOLICITUD_INVALIDA";
    public const string ProveedorNoDisponible = "PROVEEDOR_NO_DISPONIBLE";

    /// <summary>M-3: el botón y la ronda guardaron a la vez y la relectura sigue sin coincidir con el alta.</summary>
    public const string AltaEnCurso = "ALTA_EN_CURSO";

    /// <summary>M-2: una excepción inesperada en la ronda del alta automática.</summary>
    public const string ErrorInterno = "ERROR_INTERNO";

    /// <summary>L-3: valor de la fila resumen de la auditoría cuando el alta no cambió nada.</summary>
    public const string AuditoriaSinCambios = "SIN_CAMBIOS";

    /// <summary>Motivo de <c>LISTO_SIN_WEBHOOK</c> (mismo código que Impuestos).</summary>
    public const string SinWebhookSecret = "SIN_WEBHOOK_SECRET";

    /// <summary>Motivo de la auditoría cuando la credencial se obtiene sin reemplazar una llave distinta.</summary>
    public const string AuditoriaCredencialObtenida = "CREDENCIAL_OBTENIDA";

    /// <summary>Motivo de la auditoría cuando el alta reemplazó una llave guardada distinta.</summary>
    public const string AuditoriaClaveRotada = "CLAVE_ROTADA";

    /// <summary>Literal legible de cada código (≤ 300 caracteres, sin valores ni datos personales).</summary>
    public static string Literal(string code) => code switch
    {
        NitAusente => "La compañía no tiene NIT registrado.",
        NitAmbiguo => "No se puede separar el dígito de verificación del NIT de la compañía.",
        NitIgualACabeza => "El NIT de la compañía es el mismo de su compañía cabeza.",
        SinAdmin => SetTenantProductEnabledHandler.ProvisioningPrerequisiteMissingDetail,
        ProductoNoHabilitado => "Diagnóstico no está habilitado para la compañía.",
        TenantNotFound => "La empresa no existe.",
        ProveedorNoConfigurado => "La integración con el proveedor del impuesto no está configurada en este ambiente.",
        ProveedorTokenInvalido => "El proveedor del impuesto rechazó el token de integración.",
        ProveedorIntegracionDeshabilitada => "La integración con el proveedor del impuesto no está habilitada en este ambiente.",
        ProveedorSolicitudInvalida => "El proveedor del impuesto rechazó los datos de la compañía.",
        ProveedorNoDisponible => "El proveedor del impuesto no responde. Intenta más tarde.",
        SinWebhookSecret => "El proveedor no entregó el secreto del webhook.",
        AltaEnCurso => "Ya hay un alta de la credencial del impuesto en curso para la compañía. Intenta de nuevo en unos segundos.",
        ErrorInterno => "No se pudo completar el alta de la credencial del impuesto por un error interno.",
        _ => "No se pudo obtener la credencial del proveedor del impuesto.",
    };
}

/// <summary>Lo que responde el endpoint del alta: solo metadatos, nunca valores.</summary>
/// <param name="ApiKeyPrefix">Primeros 8 caracteres de la llave (los mismos que <c>api_key_prefix</c> del proveedor).</param>
/// <param name="Created">El proveedor creó la empresa en esta llamada (201).</param>
/// <param name="Rotated">La llave guardada era otra y se reemplazó (auditoría <c>CLAVE_ROTADA</c>).</param>
/// <param name="HasWebhookSecret">Queda un secreto de webhook guardado.</param>
/// <param name="Changed">Se escribió algo; <c>false</c> si los valores eran los mismos (no sube versión ni audita).</param>
/// <param name="UpdatedAt">Último cambio de la llave.</param>
public sealed record ResultadoObtencionCredencialImpuesto(
    string? ApiKeyPrefix,
    bool Created,
    bool Rotated,
    bool HasWebhookSecret,
    bool Changed,
    DateTimeOffset UpdatedAt);
