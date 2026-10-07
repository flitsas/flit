using Flit.Consultas.Api.Persistence;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.Identity;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Flit.Consultas.Api.Avisos;

/// <summary>
/// <c>flit.consultas.v1.ValidacionIdentidadService</c> (HU #13351, ADR-0065 §6-7): Consultas habla con Kyverum Verify
/// por el servicio que valida la identidad y se queda con el secreto con el que Kyverum firmará el aviso. El resultado
/// vuelve por el bus desde <see cref="AvisosKyverumEndpoints"/>.
/// </summary>
internal sealed class ValidacionIdentidadGrpcService(
    ConsultasDb db,
    IKyverumVerifyClient kyverum,
    IKyverumCertificateClient certificados,
    IDataProtectionProvider protection,
    TimeProvider time) : ValidacionIdentidadService.ValidacionIdentidadServiceBase
{
    public const string Scope = "platform.consultas";
    public const string ProveedorRechazo = "PROVEEDOR_RECHAZO";

    public override async Task<IniciarValidacionResponse> Iniciar(IniciarValidacionRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var caller = PlatformServiceCaller.From(context);
        if (!Guid.TryParse(request.ValidacionId, out var validacionId) || validacionId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "validacion_id debe ser un uuid."));
        Guid? tramiteId = request.HasTramiteId && Guid.TryParse(request.TramiteId, out var t) ? t : null;

        KyverumVerifyStartResult inicio;
        try
        {
            inicio = await kyverum.StartVerificationAsync(
                new KyverumVerifyStartRequest(
                    tramiteId, validacionId, request.HasParte ? request.Parte : null, request.Nombre, request.TipoDocumento, request.Documento, request.Email),
                context.CancellationToken).ConfigureAwait(false);
        }
        catch (KyverumVerifyException ex) when (ex.Transient)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, ex.Message));
        }
        catch (KyverumVerifyException ex)
        {
            throw new PlatformException(ProveedorRechazo, ex.Message);
        }

        // Un reenvío de la misma validación trae un secreto nuevo: el aviso del intento anterior deja de verificar.
        var fila = await db.ValidacionesKyverum.FirstOrDefaultAsync(v => v.ValidacionId == validacionId, context.CancellationToken).ConfigureAwait(false);
        if (fila is null)
        {
            fila = new ValidacionKyverum { ValidacionId = validacionId };
            db.ValidacionesKyverum.Add(fila);
        }
        else if (fila.TenantId != caller.TenantId)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "La validación es de otra empresa."));
        }

        fila.TenantId = caller.TenantId;
        fila.Producto = ProductoDe(caller.ClientId);
        fila.VerificationId = inicio.VerificationId;
        fila.SecretoCifrado = string.IsNullOrEmpty(inicio.WebhookSecret) ? null : AvisosKyverumEndpoints.Protector(protection).Protect(inicio.WebhookSecret);
        fila.CreadaEn = time.GetUtcNow();
        await db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);

        var respuesta = new IniciarValidacionResponse
        {
            VerificationId = inicio.VerificationId,
            CaptureUrl = inicio.CaptureUrl,
            ProviderStatus = inicio.ProviderStatus,
            RawPayloadSanitized = inicio.RawPayloadSanitized,
        };
        if (inicio.ExpiresAt is { } expira)
            respuesta.Expira = Timestamp.FromDateTimeOffset(expira);
        return respuesta;
    }

    public override async Task<ConsultarEstadoValidacionResponse> ConsultarEstado(ConsultarEstadoValidacionRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        KyverumVerifyStatus? estado;
        try
        {
            estado = await kyverum.GetStatusAsync(request.VerificationId, request.HasParte ? request.Parte : null, context.CancellationToken).ConfigureAwait(false);
        }
        catch (KyverumVerifyException ex)
        {
            throw new RpcException(new Status(ex.Transient ? StatusCode.Unavailable : StatusCode.FailedPrecondition, ex.Message));
        }

        if (estado is null)
            return new ConsultarEstadoValidacionResponse { Encontrada = false };

        var respuesta = new ConsultarEstadoValidacionResponse
        {
            Encontrada = true,
            Status = estado.Status,
            RawPayloadSanitized = estado.RawPayloadSanitized,
        };
        if (estado.Score is { } score)
            respuesta.Score = score;
        if (estado.AttemptAt is not null)
            respuesta.AttemptAt = estado.AttemptAt;
        if (estado.Motivo is not null)
            respuesta.Motivo = estado.Motivo;
        if (estado.FirmaSerie is not null)
            respuesta.FirmaSerie = estado.FirmaSerie;
        return respuesta;
    }

    public override async Task<DescargarCertificadoResponse> DescargarCertificado(DescargarCertificadoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        KyverumCertificate? certificado;
        try
        {
            certificado = await certificados.DownloadCertificateAsync(request.VerificationId, context.CancellationToken).ConfigureAwait(false);
        }
        catch (KyverumCertificateException ex)
        {
            throw new RpcException(new Status(ex.Transient ? StatusCode.Unavailable : StatusCode.FailedPrecondition, ex.Message));
        }

        return certificado is null
            ? new DescargarCertificadoResponse { Encontrado = false }
            : new DescargarCertificadoResponse
            {
                Encontrado = true,
                Contenido = ByteString.CopyFrom(certificado.Content),
                ContentType = certificado.ContentType,
                NombreArchivo = certificado.FileName,
            };
    }

    private static string ProductoDe(string clientId) =>
        clientId.StartsWith("svc-", StringComparison.Ordinal) ? clientId["svc-".Length..] : clientId;
}
