using Flit.Modules.Platform.Application.Companies;
using Flit.Modules.Platform.Domain.Companies;
using Flit.Modules.Platform.Domain.TenantProducts;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// HU #14711 (plan §3.1) — obtiene del proveedor del impuesto, con el alta idempotente, la <c>api_key</c> y el
/// <c>webhook_secret</c> de la empresa para Diagnóstico. Lo usan el botón «Obtener del proveedor» (SuperAdmin) y el alta
/// automática al habilitar <c>diagnostico</c> (<see cref="AltaCredencialDiagnosticoAutomatica"/>).
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><c>diagnostico</c> debe estar encendido para la empresa (si no, <c>PRODUCTO_NO_HABILITADO</c>).</item>
///   <item>Datos del alta como Impuestos: razón social, NIT base sin DV (<see cref="NitEmpresa.NormalizarBase"/>; el proveedor
///   calcula el DV) y correo del AdminCompany activo más antiguo. NIT no válido ⇒ <c>NIT_AUSENTE</c>, <c>NIT_AMBIGUO</c> o
///   <c>NIT_IGUAL_A_CABEZA</c>; sin AdminCompany ⇒ <c>SIN_ADMIN</c>. En esos casos no se llama al proveedor.</item>
///   <item>Se compara con lo guardado (descifrado): si la llave y el secreto son los mismos no se escribe nada, no sube la
///   versión ni se audita (AC-DC-02).</item>
///   <item>Si algo cambió, UN guardado: la llave y/o el secreto; si el secreto cambió, el guardado pasa (re-cifrado con su
///   propio código) a <c>impuesto_webhook_secret_anterior</c> con <c>updated_at</c> de la rotación (vale 24 h); la auditoría
///   <c>CREDENCIAL_OBTENIDA</c> o <c>CLAVE_ROTADA</c> sin valores; y, solo si la llave cambió, el trabajo
///   <c>impuestos.credencial_diagnostico.actualizada</c> en la outbox del mismo guardado.</item>
/// </list>
/// Nunca registra la llave, el secreto ni el correo.
/// </remarks>
/// <example>
/// <code>
/// var r = await handler.HandleAsync(tenantId, superAdminId, ct); // { ApiKeyPrefix, Created, Rotated, HasWebhookSecret, Changed, UpdatedAt }
/// </code>
/// </example>
public sealed partial class ObtenerCredencialImpuestoDiagnosticoHandler(
    ITenantProductRepository products,
    IDatosAltaProveedorStore datos,
    IClienteAltaProveedorImpuesto proveedor,
    ITenantProductCredentialRepository credenciales,
    ITenantProductSecretProtector protector,
    ITenantProductChangeJobs jobs,
    ILogger<ObtenerCredencialImpuestoDiagnosticoHandler> log)
{
    /// <summary>Caracteres de la llave que se muestran (los mismos de <c>api_key_prefix</c> del proveedor).</summary>
    public const int LargoPrefijo = 8;

    private const string Producto = TenantProductCredentialCodes.Diagnostico;

    private readonly ITenantProductRepository _products = products ?? throw new ArgumentNullException(nameof(products));
    private readonly IDatosAltaProveedorStore _datos = datos ?? throw new ArgumentNullException(nameof(datos));
    private readonly IClienteAltaProveedorImpuesto _proveedor = proveedor ?? throw new ArgumentNullException(nameof(proveedor));
    private readonly ITenantProductCredentialRepository _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
    private readonly ITenantProductSecretProtector _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    private readonly ITenantProductChangeJobs _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    private readonly ILogger _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <exception cref="AltaCredencialImpuestoException">Con un código de <see cref="CodigosAltaCredencialImpuesto"/>.</exception>
    public async Task<ResultadoObtencionCredencialImpuesto> HandleAsync(Guid tenantId, Guid? changedBy, CancellationToken cancellationToken = default)
    {
        var producto = await _products.GetAsync(tenantId, Producto, cancellationToken).ConfigureAwait(false);
        var empresa = await _datos.ObtenerAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (empresa is null)
            throw Rechazo(tenantId, CodigosAltaCredencialImpuesto.TenantNotFound);
        if (producto is not { Enabled: true })
            throw Rechazo(tenantId, CodigosAltaCredencialImpuesto.ProductoNoHabilitado);

        var nit = NitEmpresa.NormalizarBase(empresa.TaxId, empresa.TaxIdsAncestros);
        if (nit is not { Estado: EstadoNit.Valido, Base: { } taxIdBase })
            throw Rechazo(tenantId, CodigoDe(nit.Estado));
        if (string.IsNullOrWhiteSpace(empresa.ContactEmail))
            throw Rechazo(tenantId, CodigosAltaCredencialImpuesto.SinAdmin);

        RespuestaAltaProveedor alta;
        try
        {
            alta = await _proveedor.DarDeAltaAsync(
                    new PeticionAltaProveedor(empresa.LegalName.Trim(), taxIdBase, empresa.ContactEmail.Trim()), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AltaCredencialImpuestoException ex)
        {
            AltaRechazada(_log, tenantId, ex.Code);
            throw;
        }

        var llaveGuardada = await _credenciales.GetAsync(tenantId, Producto, TenantProductCredentialCodes.ImpuestoApiKey, cancellationToken).ConfigureAwait(false);
        var secretoGuardado = await _credenciales.GetAsync(tenantId, Producto, TenantProductCredentialCodes.ImpuestoWebhookSecret, cancellationToken).ConfigureAwait(false);

        var llaveActual = Descifrar(tenantId, llaveGuardada);
        var secretoActual = Descifrar(tenantId, secretoGuardado);
        var cambiaLlave = !string.Equals(llaveActual, alta.ApiKey, StringComparison.Ordinal);
        var cambiaSecreto = alta.WebhookSecret is not null && !string.Equals(secretoActual, alta.WebhookSecret, StringComparison.Ordinal);
        var rotada = cambiaLlave && llaveActual is not null;
        var tieneSecreto = alta.WebhookSecret is not null || secretoGuardado is not null;

        if (!cambiaLlave && !cambiaSecreto)
        {
            SinCambios(_log, tenantId, alta.Created);
            return new ResultadoObtencionCredencialImpuesto(
                Prefijo(alta.ApiKey), alta.Created, Rotated: false, tieneSecreto, Changed: false, llaveGuardada!.UpdatedAt);
        }

        var escrituras = new List<EscrituraCredencialAlta>(3);
        if (cambiaLlave)
            escrituras.Add(new EscrituraCredencialAlta(TenantProductCredentialCodes.ImpuestoApiKey, Proteger(tenantId, TenantProductCredentialCodes.ImpuestoApiKey, alta.ApiKey)));
        if (cambiaSecreto)
        {
            escrituras.Add(new EscrituraCredencialAlta(
                TenantProductCredentialCodes.ImpuestoWebhookSecret, Proteger(tenantId, TenantProductCredentialCodes.ImpuestoWebhookSecret, alta.WebhookSecret!)));
            if (secretoActual is not null)
            {
                // El propósito de Data Protection incluye el código: el anterior se vuelve a cifrar con el suyo.
                escrituras.Add(new EscrituraCredencialAlta(
                    TenantProductCredentialCodes.ImpuestoWebhookSecretAnterior,
                    Proteger(tenantId, TenantProductCredentialCodes.ImpuestoWebhookSecretAnterior, secretoActual)));
            }
        }

        var version = llaveGuardada is null ? 0 : llaveGuardada.RowVersion + 1;
        var motivo = rotada ? CodigosAltaCredencialImpuesto.AuditoriaClaveRotada : CodigosAltaCredencialImpuesto.AuditoriaCredencialObtenida;
        DateTimeOffset? encolarEn = null;
        var guardadoEn = await _credenciales.GuardarAltaProveedorAsync(
                tenantId,
                Producto,
                escrituras,
                motivo,
                changedBy,
                cambiaLlave ? en => encolarEn = Encolar(tenantId, version, en) : null,
                cancellationToken)
            .ConfigureAwait(false);

        Guardadas(_log, tenantId, motivo, cambiaLlave, cambiaSecreto, alta.Created, encolarEn is not null);
        return new ResultadoObtencionCredencialImpuesto(
            Prefijo(alta.ApiKey), alta.Created, rotada, tieneSecreto, Changed: true, cambiaLlave ? guardadoEn : llaveGuardada!.UpdatedAt);
    }

    private DateTimeOffset Encolar(Guid tenantId, long version, DateTimeOffset en)
    {
        _jobs.EncolarCredencialDiagnosticoActualizada(tenantId, version, en);
        return en;
    }

    /// <summary>Los primeros <see cref="LargoPrefijo"/> caracteres; null si la llave es más corta que 16 (sería casi toda la llave).</summary>
    public static string? Prefijo(string apiKey) => apiKey.Length < 16 ? null : apiKey[..LargoPrefijo];

    private string? Descifrar(Guid tenantId, TenantProductCredential? fila) =>
        fila is null ? null : _protector.Unprotect(tenantId, fila.CredentialCode, fila.SecretProtected);

    private string Proteger(Guid tenantId, string codigo, string valor) => _protector.Protect(tenantId, codigo, valor);

    private AltaCredencialImpuestoException Rechazo(Guid tenantId, string codigo)
    {
        AltaRechazada(_log, tenantId, codigo);
        return new AltaCredencialImpuestoException(codigo);
    }

    private static string CodigoDe(EstadoNit estado) => estado switch
    {
        EstadoNit.Ausente => CodigosAltaCredencialImpuesto.NitAusente,
        EstadoNit.IgualACabeza => CodigosAltaCredencialImpuesto.NitIgualACabeza,
        _ => CodigosAltaCredencialImpuesto.NitAmbiguo,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alta de la credencial del impuesto de la empresa {Empresa} rechazada: {Codigo}.")]
    private static partial void AltaRechazada(ILogger logger, Guid empresa, string codigo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Credencial del impuesto de la empresa {Empresa} obtenida del proveedor sin cambios (empresa creada: {Creada}).")]
    private static partial void SinCambios(ILogger logger, Guid empresa, bool creada);

    [LoggerMessage(Level = LogLevel.Information, Message = "Credencial del impuesto de la empresa {Empresa} guardada ({Motivo}; llave: {Llave}; secreto: {Secreto}; empresa creada: {Creada}; aviso a Impuestos: {Aviso}).")]
    private static partial void Guardadas(ILogger logger, Guid empresa, string motivo, bool llave, bool secreto, bool creada, bool aviso);
}
