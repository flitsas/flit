using System.Text.Json;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Platform.Sdk.Messaging;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>Datos v1 de <c>impuestos.credencial_diagnostico.actualizada</c> (<c>impuestos-events.v1.yaml</c> 1.2.0, HU #14711).</summary>
public sealed record CredencialDiagnosticoActualizada(Guid TenantId, string? ProductCode, long Version, DateTimeOffset OccurredAt);

/// <summary>
/// HU #14711 (P5, plan §3.4) — atiende el trabajo <see cref="Tipo"/> que deja la plataforma cuando el alta de Diagnóstico cambió
/// la llave del impuesto de la compañía (llega por la cola <see cref="ConsumidorCambioProducto.Cola"/>, de los productores
/// <c>plataforma</c> y <c>tramites</c>). En la transacción del SDK (bandeja de entrada en el mismo guardado):
/// <list type="number">
///   <item>Sin credencial en Impuestos, o con Diagnóstico sin llave: sin efecto (el aprovisionamiento normal se encarga).</item>
///   <item>Se pide la llave vigente a Identidad (<c>ObtenerCredencialImpuestoParaCopia</c>); si Identidad no responde, el SDK
///   reintenta.</item>
///   <item>Si es la misma llave que ya hay: sin efecto (idempotente).</item>
///   <item>Si la credencial es <see cref="OrigenCredencial.CopiaDiagnostico"/> o su prefijo difiere del de la llave nueva, se
///   reemplaza (origen <c>COPIA_DIAGNOSTICO</c>, conservando el secreto del webhook y el id de empresa del API); si es propia con
///   el mismo prefijo, no se toca.</item>
/// </list>
/// Otra empresa en los datos o una versión desconocida van a la DLQ sin reintentos; otro producto se confirma sin efecto. Nunca
/// registra la llave.
/// </summary>
/// <example>
/// <code>
/// await credencialDiagnostico.AplicarAsync(envelope, data, ct); // desde ConsumidorCambioProducto
/// </code>
/// </example>
public sealed partial class ConsumidorCredencialDiagnostico(
    EmpresaDelTrabajo empresa,
    ImpuestosDb db,
    AlmacenCredenciales almacen,
    IClienteIdentidadAlta identidad,
    ILogger<ConsumidorCredencialDiagnostico> log)
{
    public const string Tipo = "impuestos.credencial_diagnostico.actualizada";

    public const string ProductoDiagnostico = "diagnostico";

    public async Task AplicarAsync(EventEnvelope envelope, JsonElement data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var aviso = ConsumidorCambioProducto.Leer<CredencialDiagnosticoActualizada>(envelope, data);
        if (aviso.TenantId != envelope.TenantId)
            throw new SinReintentoAutomaticoException("La empresa de los datos no es la del sobre.");
        if (!string.Equals(aviso.ProductCode, ProductoDiagnostico, StringComparison.Ordinal))
        {
            SinEfecto(log, envelope.TenantId, "otro producto");
            return;
        }

        var compania = envelope.TenantId;
        empresa.Fijar(compania);
        // Orden de bloqueos: aprovisionamiento antes que credencial (BloqueoAprovisionamiento).
        await BloqueoAprovisionamiento.BloquearAsync(db, compania, ct).ConfigureAwait(false);
        var fila = await almacen.FilaAsync(db, compania, ct).ConfigureAwait(false);
        if (fila is null)
        {
            SinEfecto(log, compania, "sin credencial en Impuestos");
            return;
        }

        var llave = await identidad.ObtenerLlaveDiagnosticoAsync(compania, ct).ConfigureAwait(false);
        if (!llave.Existe || string.IsNullOrEmpty(llave.ApiKey))
        {
            SinEfecto(log, compania, "Diagnóstico sin llave");
            return;
        }

        var actual = await almacen.LeerAsync(db, compania, ct).ConfigureAwait(false);
        if (string.Equals(actual?.ApiKey, llave.ApiKey, StringComparison.Ordinal))
        {
            SinEfecto(log, compania, "la misma llave");
            return;
        }

        if (fila.Origen != OrigenCredencial.CopiaDiagnostico
            && string.Equals(fila.ApiKeyPrefijo, AlmacenCredenciales.Prefijo(llave.ApiKey), StringComparison.Ordinal))
        {
            SinEfecto(log, compania, "credencial propia con el mismo prefijo");
            return;
        }

        almacen.Escribir(
            db, fila, compania, new CredencialesEnClaro(llave.ApiKey, actual?.WebhookSecret, fila.EmpresaApiId), OrigenCredencial.CopiaDiagnostico, null);
        Recopiada(log, compania, aviso.Version);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Llave del impuesto de la empresa {Empresa} recopiada de Diagnóstico (versión {Version}).")]
    private static partial void Recopiada(ILogger logger, Guid empresa, long version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Aviso de llave de Diagnóstico de la empresa {Empresa} sin efecto: {Motivo}.")]
    private static partial void SinEfecto(ILogger logger, Guid empresa, string motivo);
}
