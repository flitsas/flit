using Flit.Consultas.Grpc.V1;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Consultas;

/// <summary>
/// HU #13351 (ADR-0065 §6-7) — Kyverum Verify a través de core-consultas, detrás de
/// <see cref="ConsultasRemoto.ValidacionIdentidadFlagKey"/>. Consultas crea la validación y se queda con el secreto del
/// aviso: aquí el secreto vuelve vacío, así que la validación no guarda ninguno y su aviso llega por el bus
/// (<see cref="AvisoKyverumConsumer"/>). Consultas caído al crear = fallo transitorio: la validación queda en
/// <c>pendiente_envio</c> y la reintenta el worker de siempre.
/// </summary>
internal sealed class KyverumVerifyPorConsultas(
    ValidacionIdentidadService.ValidacionIdentidadServiceClient client,
    FlitDbContext db) : IKyverumVerifyClient
{
    public async Task<KyverumVerifyStartResult> StartVerificationAsync(KyverumVerifyStartRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = request.TenantId
            ?? throw new InvalidOperationException("Con Kyverum por Consultas, KyverumVerifyStartRequest.TenantId es obligatorio.");
        var pedido = new IniciarValidacionRequest
        {
            ValidacionId = request.CorrelationId.ToString(),
            Nombre = request.Nombre,
            TipoDocumento = request.TipoDoc,
            Documento = request.Documento,
            Email = request.Email ?? string.Empty,
        };
        if (request.ProcedureInstanceId is { } tramite)
            pedido.TramiteId = tramite.ToString();
        if (request.Parte is not null)
            pedido.Parte = request.Parte;

        try
        {
            var r = await client.IniciarValidacionAsync(pedido, ConsultasRemotoMetadata.Empresa(tenantId), cancellationToken: ct).ConfigureAwait(false);
            return new KyverumVerifyStartResult(
                r.VerificationId, r.CaptureUrl, WebhookSecret: string.Empty, r.ProviderStatus, r.RawPayloadSanitized, r.Expira?.ToDateTimeOffset());
        }
        catch (RpcException ex)
        {
            throw new KyverumVerifyException($"Consultas respondió {ex.StatusCode} al crear la validación.", ConsultasRemotoMetadata.Transitorio(ex));
        }
    }

    public async Task<KyverumVerifyStatus?> GetStatusAsync(string verificationId, string? parte, CancellationToken ct = default)
    {
        var tenantId = await ConsultasRemotoMetadata.EmpresaDeLaVerificacionAsync(db, verificationId, ct).ConfigureAwait(false)
            ?? throw new KyverumVerifyException("No hay validación con ese id de verificación.", transient: false);
        var pedido = new ConsultarEstadoValidacionRequest { VerificationId = verificationId };
        if (parte is not null)
            pedido.Parte = parte;

        try
        {
            var r = await client.ConsultarEstadoValidacionAsync(pedido, ConsultasRemotoMetadata.Empresa(tenantId), cancellationToken: ct).ConfigureAwait(false);
            return r.Encontrada
                ? new KyverumVerifyStatus(
                    r.Status, r.HasScore ? r.Score : null, r.RawPayloadSanitized,
                    r.HasAttemptAt ? r.AttemptAt : null, r.HasMotivo ? r.Motivo : null, r.HasFirmaSerie ? r.FirmaSerie : null)
                : null;
        }
        catch (RpcException ex)
        {
            throw new KyverumVerifyException($"Consultas respondió {ex.StatusCode} al consultar el estado.", ConsultasRemotoMetadata.Transitorio(ex));
        }
    }
}

/// <summary>HU #13351 — certificado de la validación (PDF) a través de core-consultas.</summary>
internal sealed class KyverumCertificadoPorConsultas(
    ValidacionIdentidadService.ValidacionIdentidadServiceClient client,
    FlitDbContext db) : IKyverumCertificateClient
{
    public async Task<KyverumCertificate?> DownloadCertificateAsync(string verificationId, CancellationToken ct = default)
    {
        var tenantId = await ConsultasRemotoMetadata.EmpresaDeLaVerificacionAsync(db, verificationId, ct).ConfigureAwait(false)
            ?? throw new KyverumCertificateException("No hay validación con ese id de verificación.", transient: false);
        try
        {
            var r = await client.DescargarCertificadoAsync(
                new DescargarCertificadoRequest { VerificationId = verificationId }, ConsultasRemotoMetadata.Empresa(tenantId), cancellationToken: ct).ConfigureAwait(false);
            return r.Encontrado ? new KyverumCertificate(r.Contenido.ToByteArray(), r.ContentType, r.NombreArchivo) : null;
        }
        catch (RpcException ex)
        {
            throw new KyverumCertificateException($"Consultas respondió {ex.StatusCode} al descargar el certificado.", ConsultasRemotoMetadata.Transitorio(ex));
        }
    }
}

internal static class ConsultasRemotoMetadata
{
    /// <summary>La empresa va explícita: los workers (reintento de envío, reconciliación) no tienen petición en curso.</summary>
    public static Metadata Empresa(Guid tenantId) => new() { { PlatformServiceCallInterceptor.TenantMetadata, tenantId.ToString() } };

    /// <summary>Solo un rechazo del proveedor o un pedido inválido es definitivo; lo demás se reintenta.</summary>
    public static bool Transitorio(RpcException ex) => ex.StatusCode is not (StatusCode.FailedPrecondition or StatusCode.InvalidArgument);

    /// <summary>Empresa de la validación con ese id de verificación de Kyverum (cualquiera: trámite, prevalidación, mandatario).</summary>
    public static Task<Guid?> EmpresaDeLaVerificacionAsync(FlitDbContext db, string verificationId, CancellationToken ct) =>
        db.Set<ProcedureInstanceBiometricValidation>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.KyverumVerificationId == verificationId)
            .Select(v => (Guid?)v.TenantId)
            .FirstOrDefaultAsync(ct);
}
