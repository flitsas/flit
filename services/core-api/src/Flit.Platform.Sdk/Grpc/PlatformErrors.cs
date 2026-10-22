using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using GrpcStatusCode = Grpc.Core.StatusCode;

namespace Flit.Platform.Sdk.Grpc;

/// <summary>
/// Códigos de error estables del contrato de plataforma §10 (los mismos de <c>flit.platform.v1.CodigoError</c>). Por
/// gRPC viajan como <c>google.rpc.ErrorInfo.reason</c> con <c>domain = flitsas.com</c> (ADR-0070).
/// </summary>
public static class PlatformErrorCodes
{
    public const string Domain = "flitsas.com";

    public const string ProductNotEnabled = "PRODUCT_NOT_ENABLED";
    public const string ProductRoleRequired = "PRODUCT_ROLE_REQUIRED";
    public const string TokenAudienceMismatch = "TOKEN_AUDIENCE_MISMATCH";
    public const string SessionDomainMismatch = "SESSION_DOMAIN_MISMATCH";
    public const string NetworkDomainRequired = "NETWORK_DOMAIN_REQUIRED";

    /// <summary>Estado gRPC de cada código: 401 del contrato = UNAUTHENTICATED; 403 = PERMISSION_DENIED.</summary>
    public static GrpcStatusCode StatusFor(string code) => code switch
    {
        TokenAudienceMismatch or SessionDomainMismatch => GrpcStatusCode.Unauthenticated,
        ProductNotEnabled or ProductRoleRequired or NetworkDomainRequired => GrpcStatusCode.PermissionDenied,
        _ => GrpcStatusCode.FailedPrecondition,
    };
}

/// <summary>
/// Error de negocio con código del §10. Un handler gRPC lo lanza y el interceptor del SDK lo convierte en el estado
/// correspondiente con <c>ErrorInfo</c>; quien llama lo lee con <see cref="PlatformRpcErrors.Reason"/>.
/// </summary>
public sealed class PlatformException : Exception
{
    public PlatformException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public PlatformException()
        : this("UNSPECIFIED", "Error de plataforma.")
    {
    }

    public PlatformException(string message)
        : this("UNSPECIFIED", message)
    {
    }

    public PlatformException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "UNSPECIFIED";
    }

    public string Code { get; }
}

public static class PlatformRpcErrors
{
    /// <summary>RpcException con el estado del código y <c>ErrorInfo{reason, domain}</c> en los detalles.</summary>
    public static RpcException ToRpcException(string code, string message, GrpcStatusCode? status = null)
    {
        var rpcStatus = new Google.Rpc.Status
        {
            Code = (int)(status ?? PlatformErrorCodes.StatusFor(code)),
            Message = message,
            Details = { Any.Pack(new ErrorInfo { Reason = code, Domain = PlatformErrorCodes.Domain }) },
        };
        return rpcStatus.ToRpcException();
    }

    /// <summary>El código del §10 de un error recibido, o <c>null</c> si no trae <c>ErrorInfo</c> de FLIT.</summary>
    public static string? Reason(RpcException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var info = exception.GetRpcStatus()?.GetDetail<ErrorInfo>();
        return info is { Domain: PlatformErrorCodes.Domain } ? info.Reason : null;
    }
}
