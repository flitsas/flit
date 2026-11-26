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
///   <item>Lo guardado se lee y se descifra antes de llamar al proveedor (si falla, no se le llama). Se compara con el alta: si
///   la llave y el secreto son los mismos no se escribe ninguna credencial ni sube la versión (AC-DC-02); queda solo la fila
///   resumen de auditoría con <c>SIN_CAMBIOS</c>. Los rechazos (salvo <c>TENANT_NOT_FOUND</c>) también dejan esa fila con su
///   código.</item>
///   <item>Si el guardado choca con otro (el botón y la ronda a la vez), se relee y se compara una vez: igual ⇒ sin cambios;
///   distinto ⇒ <c>ALTA_EN_CURSO</c>.</item>
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
            throw await RechazoAuditadoAsync(tenantId, CodigosAltaCredencialImpuesto.ProductoNoHabilitado, changedBy, cancellationToken).ConfigureAwait(false);

        var nit = NitEmpresa.NormalizarBase(empresa.TaxId, empresa.TaxIdsAncestros);
        if (nit is not { Estado: EstadoNit.Valido, Base: { } taxIdBase })
            throw await RechazoAuditadoAsync(tenantId, CodigoDe(nit.Estado), changedBy, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(empresa.ContactEmail))
            throw await RechazoAuditadoAsync(tenantId, CodigosAltaCredencialImpuesto.SinAdmin, changedBy, cancellationToken).ConfigureAwait(false);

        // M-2: lo guardado se lee y se descifra antes de llamar al proveedor; si falla, no se le llama.
        var guardado = await LeerGuardadoAsync(tenantId, cancellationToken).ConfigureAwait(false);

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
            await AuditarAsync(tenantId, ex.Code, exito: false, changedBy, cancellationToken).ConfigureAwait(false);
            throw;
        }

        var cambiaLlave = !string.Equals(guardado.Llave, alta.ApiKey, StringComparison.Ordinal);
        var cambiaSecreto = alta.WebhookSecret is not null && !string.Equals(guardado.Secreto, alta.WebhookSecret, StringComparison.Ordinal);
        var rotada = cambiaLlave && guardado.Llave is not null;
        var tieneSecreto = alta.WebhookSecret is not null || guardado.FilaSecreto is not null;

        if (!cambiaLlave && !cambiaSecreto)
            return await SinCambiosAsync(tenantId, alta, tieneSecreto, guardado.FilaLlave!.UpdatedAt, changedBy, cancellationToken).ConfigureAwait(false);

        var escrituras = new List<EscrituraCredencialAlta>(3);
        if (cambiaLlave)
            escrituras.Add(new EscrituraCredencialAlta(TenantProductCredentialCodes.ImpuestoApiKey, Proteger(tenantId, TenantProductCredentialCodes.ImpuestoApiKey, alta.ApiKey)));
        if (cambiaSecreto)
        {
            escrituras.Add(new EscrituraCredencialAlta(
                TenantProductCredentialCodes.ImpuestoWebhookSecret, Proteger(tenantId, TenantProductCredentialCodes.ImpuestoWebhookSecret, alta.WebhookSecret!)));
            if (guardado.Secreto is not null)
            {
                // El propósito de Data Protection incluye el código: el anterior se vuelve a cifrar con el suyo.
                escrituras.Add(new EscrituraCredencialAlta(
                    TenantProductCredentialCodes.ImpuestoWebhookSecretAnterior,
                    Proteger(tenantId, TenantProductCredentialCodes.ImpuestoWebhookSecretAnterior, guardado.Secreto)));
            }
        }

        var version = guardado.FilaLlave is null ? 0 : guardado.FilaLlave.RowVersion + 1;
        var motivo = rotada ? CodigosAltaCredencialImpuesto.AuditoriaClaveRotada : CodigosAltaCredencialImpuesto.AuditoriaCredencialObtenida;
        DateTimeOffset? encolarEn = null;
        DateTimeOffset guardadoEn;
        try
        {
            guardadoEn = await _credenciales.GuardarAltaProveedorAsync(
                    tenantId,
                    Producto,
                    escrituras,
                    motivo,
                    changedBy,
                    cambiaLlave ? en => encolarEn = Encolar(tenantId, version, en) : null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TenantProductCredentialConflictException)
        {
            // M-3: el botón y la ronda guardaron a la vez. El alta es idempotente: se relee y se compara una vez.
            return await ResolverChoqueAsync(tenantId, alta, changedBy, cancellationToken).ConfigureAwait(false);
        }

        Guardadas(_log, tenantId, motivo, cambiaLlave, cambiaSecreto, alta.Created, encolarEn is not null);
        return new ResultadoObtencionCredencialImpuesto(
            Prefijo(alta.ApiKey), alta.Created, rotada, tieneSecreto, Changed: true, cambiaLlave ? guardadoEn : guardado.FilaLlave!.UpdatedAt);
    }

    private async Task<Guardado> LeerGuardadoAsync(Guid tenantId, CancellationToken ct)
    {
        var filaLlave = await _credenciales.GetAsync(tenantId, Producto, TenantProductCredentialCodes.ImpuestoApiKey, ct).ConfigureAwait(false);
        var filaSecreto = await _credenciales.GetAsync(tenantId, Producto, TenantProductCredentialCodes.ImpuestoWebhookSecret, ct).ConfigureAwait(false);
        return new Guardado(filaLlave, filaSecreto, Descifrar(tenantId, filaLlave), Descifrar(tenantId, filaSecreto));
    }

    private async Task<ResultadoObtencionCredencialImpuesto> ResolverChoqueAsync(
        Guid tenantId, RespuestaAltaProveedor alta, Guid? changedBy, CancellationToken ct)
    {
        var actual = await LeerGuardadoAsync(tenantId, ct).ConfigureAwait(false);
        var mismaLlave = string.Equals(actual.Llave, alta.ApiKey, StringComparison.Ordinal);
        var mismoSecreto = alta.WebhookSecret is null || string.Equals(actual.Secreto, alta.WebhookSecret, StringComparison.Ordinal);
        if (mismaLlave && mismoSecreto)
        {
            var tieneSecreto = alta.WebhookSecret is not null || actual.FilaSecreto is not null;
            return await SinCambiosAsync(tenantId, alta, tieneSecreto, actual.FilaLlave!.UpdatedAt, changedBy, ct).ConfigureAwait(false);
        }

        throw await RechazoAuditadoAsync(tenantId, CodigosAltaCredencialImpuesto.AltaEnCurso, changedBy, ct).ConfigureAwait(false);
    }

    private async Task<ResultadoObtencionCredencialImpuesto> SinCambiosAsync(
        Guid tenantId, RespuestaAltaProveedor alta, bool tieneSecreto, DateTimeOffset updatedAt, Guid? changedBy, CancellationToken ct)
    {
        SinCambios(_log, tenantId, alta.Created);
        await AuditarAsync(tenantId, CodigosAltaCredencialImpuesto.AuditoriaSinCambios, exito: true, changedBy, ct).ConfigureAwait(false);
        return new ResultadoObtencionCredencialImpuesto(Prefijo(alta.ApiKey), alta.Created, Rotated: false, tieneSecreto, Changed: false, updatedAt);
    }

    /// <summary>
    /// L-3: la fila resumen sin credenciales escritas. Es rastro, no parte del alta: si su guardado falla no oculta el desenlace
    /// (se registra solo el tipo de la excepción).
    /// </summary>
    private async Task AuditarAsync(Guid tenantId, string resultado, bool exito, Guid? changedBy, CancellationToken ct)
    {
        try
        {
            await _credenciales.RegistrarAuditoriaAltaProveedorAsync(tenantId, Producto, resultado, exito, changedBy, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AuditoriaFallida(_log, tenantId, ex.GetType().Name);
        }
    }

    private async Task<AltaCredencialImpuestoException> RechazoAuditadoAsync(Guid tenantId, string codigo, Guid? changedBy, CancellationToken ct)
    {
        var rechazo = Rechazo(tenantId, codigo);
        await AuditarAsync(tenantId, codigo, exito: false, changedBy, ct).ConfigureAwait(false);
        return rechazo;
    }

    /// <summary>Lo guardado de la empresa: las filas (metadatos) y sus valores descifrados.</summary>
    private sealed record Guardado(TenantProductCredential? FilaLlave, TenantProductCredential? FilaSecreto, string? Llave, string? Secreto)
    {
        public override string ToString() =>
            $"Guardado {{ Llave = {(Llave is null ? "null" : "[REDACTADO]")}, Secreto = {(Secreto is null ? "null" : "[REDACTADO]")} }}";
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo auditar el alta de la credencial del impuesto de la empresa {Empresa} ({Error}).")]
    private static partial void AuditoriaFallida(ILogger logger, Guid empresa, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Credencial del impuesto de la empresa {Empresa} guardada ({Motivo}; llave: {Llave}; secreto: {Secreto}; empresa creada: {Creada}; aviso a Impuestos: {Aviso}).")]
    private static partial void Guardadas(ILogger logger, Guid empresa, string motivo, bool llave, bool secreto, bool creada, bool aviso);
}
