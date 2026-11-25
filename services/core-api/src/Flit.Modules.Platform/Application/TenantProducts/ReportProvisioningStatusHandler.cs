using Flit.Modules.Platform.Domain.TenantProducts;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// HU #14694 (plan §1.2 decisión R1; <c>flit.identidad.v1.AprovisionamientoProductoService.ReportarEstadoAprovisionamiento</c>)
/// — guarda el estado de aprovisionamiento que reporta el servicio del producto para que el admin de compañías lo muestre.
/// Idempotente por versión: el almacén solo guarda una versión mayor que la guardada.
/// </summary>
/// <remarks>
/// Valida el contrato antes de escribir: estado del plan §3.1 (los de <c>ProvisioningStatus</c> de platform.v1), acción
/// de <c>ProvisioningAction</c> o vacía, motivo ≤ 64, mensaje ≤ 300 y versión ≥ 1. Los vacíos se guardan como <c>null</c>;
/// <c>firma_confirmada</c> solo cuenta en LISTO (en los demás estados, <c>null</c>).
/// </remarks>
public sealed class ReportProvisioningStatusHandler
{
    /// <summary>Estado en que <c>firma_confirmada</c> tiene sentido.</summary>
    public const string EstadoListo = "LISTO";

    public const int MotivoMaxLength = 64;
    public const int MensajeMaxLength = 300;

    /// <summary>Estados del plan §3.1 (<c>ProvisioningStatus</c> de platform.v1 1.1.0).</summary>
    public static readonly IReadOnlySet<string> Estados = new HashSet<string>(StringComparer.Ordinal)
    {
        "PENDIENTE", EstadoListo, "LISTO_SIN_WEBHOOK", "ERROR", "INCIERTO", "REQUIERE_CREDENCIALES", "REQUIERE_REVISION_NIT", "SUSPENDIDO",
    };

    /// <summary>Acciones del SuperAdmin (<c>ProvisioningAction</c> de platform.v1 1.1.0).</summary>
    public static readonly IReadOnlySet<string> Acciones = new HashSet<string>(StringComparer.Ordinal)
    {
        "REINTENTAR", "CARGAR_SECRETO", "CARGAR_CREDENCIALES", "CORREGIR_NIT",
    };

    private readonly ITenantProductProvisioningStore _store;
    private readonly TimeProvider _clock;

    public ReportProvisioningStatusHandler(ITenantProductProvisioningStore store, TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
    }

    /// <exception cref="TenantProductException">
    /// <see cref="TenantProductException.ProductWithoutProvisioning"/>, <see cref="TenantProductException.ProvisioningReportInvalid"/>
    /// o <see cref="TenantProductException.TenantNotFound"/>.
    /// </exception>
    public async Task<ProvisioningGuardado> HandleAsync(ReportProvisioningStatusCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var producto = (command.ProductCode ?? string.Empty).Trim().ToLowerInvariant();
        if (!ProductosConAprovisionamiento.Tiene(producto))
        {
            throw new TenantProductException(
                TenantProductException.ProductWithoutProvisioning,
                $"El producto '{producto}' no tiene aprovisionamiento.");
        }

        var estado = command.Estado ?? string.Empty;
        if (!Estados.Contains(estado))
            throw Invalido("El estado no es uno de los del contrato.");

        var accion = Vacio(command.Accion);
        if (accion is not null && !Acciones.Contains(accion))
            throw Invalido("La acción no es una de las del contrato.");

        var motivo = Vacio(command.MotivoCodigo);
        if (motivo is { Length: > MotivoMaxLength })
            throw Invalido($"El motivo no puede pasar de {MotivoMaxLength} caracteres.");

        var mensaje = Vacio(command.Mensaje);
        if (mensaje is { Length: > MensajeMaxLength })
            throw Invalido($"El mensaje no puede pasar de {MensajeMaxLength} caracteres.");

        if (command.Version < 1)
            throw Invalido("La versión debe ser mayor o igual a 1.");

        var reporte = new TenantProductProvisioning(
            command.TenantId,
            producto,
            estado,
            motivo,
            mensaje,
            accion,
            estado == EstadoListo ? command.FirmaConfirmada : null,
            command.Version,
            _clock.GetUtcNow());

        var guardado = await _store.GuardarSiEsMasNuevoAsync(reporte, cancellationToken).ConfigureAwait(false);
        if (!guardado.EmpresaExiste)
            throw new TenantProductException(TenantProductException.TenantNotFound, "La empresa no existe.");

        return guardado;
    }

    private static string? Vacio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static TenantProductException Invalido(string mensaje) =>
        new(TenantProductException.ProvisioningReportInvalid, mensaje);
}

/// <summary>Reporte tal como llega del rpc (el producto sale del token del cliente, nunca del mensaje).</summary>
public sealed record ReportProvisioningStatusCommand(
    Guid TenantId,
    string ProductCode,
    string? Estado,
    string? MotivoCodigo,
    string? Mensaje,
    string? Accion,
    bool FirmaConfirmada,
    long Version);
