namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>Estado del NIT de la compañía para el alta (D-TAXID, DA-8). Todo lo que no es <see cref="Valido"/> pide revisión.</summary>
public enum EstadoNitAlta
{
    Valido,
    Ambiguo,
    Ausente,
    IgualACabeza,
}

/// <summary>Datos del alta que devuelve Identidad (<c>EmpresasService.ObtenerDatosAltaProveedor</c>). No imprime el correo.</summary>
public sealed record DatosAltaProveedor(string LegalName, string TaxIdBase, EstadoNitAlta EstadoNit, string ContactEmail, bool EmpresaActiva)
{
    public override string ToString() =>
        $"DatosAltaProveedor {{ TaxIdBase = {TaxIdBase}, EstadoNit = {EstadoNit}, LegalName = [{LegalName.Length} car.], ContactEmail = [REDACTADO], EmpresaActiva = {EmpresaActiva} }}";
}

/// <summary>La llave de Diagnóstico para copiarla (D-CRED-DIAG). No imprime el valor.</summary>
public sealed record LlaveDiagnostico(bool Existe, string? ApiKey, long Version)
{
    public override string ToString() => $"LlaveDiagnostico {{ Existe = {Existe}, ApiKey = {(ApiKey is null ? "null" : "[REDACTADO]")}, Version = {Version} }}";
}

/// <summary>Reporte del estado a la plataforma (<c>ReportarEstadoAprovisionamiento</c>); vacío = sin valor.</summary>
public sealed record ReporteEstadoAprovisionamiento(string Estado, string MotivoCodigo, string Mensaje, string Accion, bool FirmaConfirmada, long Version);

/// <summary>Respuesta del reporte: si se guardó y la versión que queda en la plataforma.</summary>
public sealed record RespuestaReporte(bool Aplicado, long VersionGuardada);

/// <summary>No se pudo hablar con Identidad (red, token de servicio, error del rpc). El mensaje no lleva datos ni secretos.</summary>
public sealed class IdentidadNoDisponibleException : Exception
{
    public IdentidadNoDisponibleException()
    {
    }

    public IdentidadNoDisponibleException(string message)
        : base(message)
    {
    }

    public IdentidadNoDisponibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Los rpc de Identidad que usa el aprovisionamiento (M04 y M04b), siempre con <c>x-flit-tenant-id</c> de la compañía.</summary>
public interface IClienteIdentidadAlta
{
    Task<DatosAltaProveedor> ObtenerDatosAltaAsync(Guid empresa, CancellationToken ct);

    Task<LlaveDiagnostico> ObtenerLlaveDiagnosticoAsync(Guid empresa, CancellationToken ct);

    /// <summary>
    /// M04b (D-ALTA-IDEM): reemplaza la llave del impuesto de Diagnóstico por <paramref name="apiKey"/> tras una rotación del
    /// proveedor. true si la compañía tenía llave de Diagnóstico y se reemplazó; false si no tenía (no se crea).
    /// </summary>
    Task<bool> ActualizarLlaveDiagnosticoAsync(Guid empresa, string apiKey, CancellationToken ct);

    Task<RespuestaReporte> ReportarAsync(Guid empresa, ReporteEstadoAprovisionamiento reporte, CancellationToken ct);
}

/// <summary>
/// Los rpc de Identidad por gRPC (M04): <c>EmpresasService.ObtenerDatosAltaProveedor</c> (scope
/// <c>platform.identidad.empresas.alta</c>), <c>CredencialesProductoService.ObtenerCredencialImpuestoParaCopia</c> (scope
/// <c>platform.identidad.credenciales.impuesto.copia</c>) y <c>AprovisionamientoProductoService.ReportarEstadoAprovisionamiento</c>
/// (scope <c>platform.aprovisionamiento.reportar</c>), cada uno con <c>x-flit-tenant-id</c>. Cualquier fallo del rpc (red, token,
/// INTERNAL de una empresa inexistente, PERMISSION_DENIED) es <see cref="IdentidadNoDisponibleException"/> con el código, nunca
/// con datos. Nunca registra la llave ni el correo.
/// </summary>
internal sealed class ClienteIdentidadAltaGrpc(
    Flit.Identidad.Grpc.V1.EmpresasService.EmpresasServiceClient empresas,
    Flit.Identidad.Grpc.V1.CredencialesProductoService.CredencialesProductoServiceClient credenciales,
    Flit.Identidad.Grpc.V1.AprovisionamientoProductoService.AprovisionamientoProductoServiceClient aprovisionamiento) : IClienteIdentidadAlta
{
    public const string CabeceraEmpresa = "x-flit-tenant-id";

    public Task<DatosAltaProveedor> ObtenerDatosAltaAsync(Guid empresa, CancellationToken ct) => LlamarAsync(async () =>
    {
        var r = await empresas.ObtenerDatosAltaProveedorAsync(new Flit.Identidad.Grpc.V1.ObtenerDatosAltaProveedorRequest(), Cabeceras(empresa), cancellationToken: ct)
            .ConfigureAwait(false);
        return new DatosAltaProveedor(r.LegalName, r.TaxIdBase, Estado(r.EstadoNit), r.ContactEmail, r.EmpresaActiva);
    }, "ObtenerDatosAltaProveedor", ct);

    public Task<LlaveDiagnostico> ObtenerLlaveDiagnosticoAsync(Guid empresa, CancellationToken ct) => LlamarAsync(async () =>
    {
        var r = await credenciales.ObtenerCredencialImpuestoParaCopiaAsync(new Flit.Identidad.Grpc.V1.ObtenerCredencialImpuestoParaCopiaRequest(), Cabeceras(empresa), cancellationToken: ct)
            .ConfigureAwait(false);
        return new LlaveDiagnostico(r.Existe && !string.IsNullOrEmpty(r.ApiKey), r.Existe && !string.IsNullOrEmpty(r.ApiKey) ? r.ApiKey : null, r.Version);
    }, "ObtenerCredencialImpuestoParaCopia", ct);

    public Task<bool> ActualizarLlaveDiagnosticoAsync(Guid empresa, string apiKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        return LlamarAsync(async () =>
        {
            var r = await credenciales.ActualizarCredencialImpuestoDiagnosticoAsync(
                    new Flit.Identidad.Grpc.V1.ActualizarCredencialImpuestoDiagnosticoRequest { ApiKey = apiKey }, Cabeceras(empresa), cancellationToken: ct)
                .ConfigureAwait(false);
            return r.Actualizada;
        }, "ActualizarCredencialImpuestoDiagnostico", ct);
    }

    public Task<RespuestaReporte> ReportarAsync(Guid empresa, ReporteEstadoAprovisionamiento reporte, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reporte);
        return LlamarAsync(async () =>
        {
            var r = await aprovisionamiento.ReportarEstadoAprovisionamientoAsync(new Flit.Identidad.Grpc.V1.ReportarEstadoAprovisionamientoRequest
            {
                Estado = reporte.Estado,
                MotivoCodigo = reporte.MotivoCodigo,
                Mensaje = reporte.Mensaje,
                Accion = reporte.Accion,
                FirmaConfirmada = reporte.FirmaConfirmada,
                Version = reporte.Version,
            }, Cabeceras(empresa), cancellationToken: ct).ConfigureAwait(false);
            return new RespuestaReporte(r.Aplicado, r.VersionGuardada);
        }, "ReportarEstadoAprovisionamiento", ct);
    }

    internal static EstadoNitAlta Estado(Flit.Identidad.Grpc.V1.EstadoNit estado) => estado switch
    {
        Flit.Identidad.Grpc.V1.EstadoNit.Valido => EstadoNitAlta.Valido,
        Flit.Identidad.Grpc.V1.EstadoNit.Ausente => EstadoNitAlta.Ausente,
        Flit.Identidad.Grpc.V1.EstadoNit.IgualACabeza => EstadoNitAlta.IgualACabeza,
        // AMBIGUO y un valor que este servicio no conoce (UNSPECIFIED o uno nuevo): pide revisión, nunca se da de alta a ciegas.
        _ => EstadoNitAlta.Ambiguo,
    };

    private static global::Grpc.Core.Metadata Cabeceras(Guid empresa) => new() { { CabeceraEmpresa, empresa.ToString() } };

    private static async Task<T> LlamarAsync<T>(Func<Task<T>> llamada, string rpc, CancellationToken ct)
    {
        try
        {
            return await llamada().ConfigureAwait(false);
        }
        catch (global::Grpc.Core.RpcException ex)
        {
            throw new IdentidadNoDisponibleException($"{rpc}: {ex.StatusCode}", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Token de servicio, red o cualquier otro fallo al preguntar: el aprovisionamiento lo trata como «no salió».
            throw new IdentidadNoDisponibleException($"{rpc}: {ex.GetType().Name}", ex);
        }
    }
}

/// <summary>Sin Identidad configurada (<c>Impuestos:Identidad:Address</c> vacío): ningún rpc responde; se aprovisiona cuando la haya.</summary>
internal sealed class ClienteIdentidadSinConfigurar : IClienteIdentidadAlta
{
    private static IdentidadNoDisponibleException SinConfigurar() =>
        new("Identidad no está configurada (Impuestos__Identidad__Address).");

    public Task<DatosAltaProveedor> ObtenerDatosAltaAsync(Guid empresa, CancellationToken ct) => throw SinConfigurar();

    public Task<LlaveDiagnostico> ObtenerLlaveDiagnosticoAsync(Guid empresa, CancellationToken ct) => throw SinConfigurar();

    public Task<bool> ActualizarLlaveDiagnosticoAsync(Guid empresa, string apiKey, CancellationToken ct) => throw SinConfigurar();

    public Task<RespuestaReporte> ReportarAsync(Guid empresa, ReporteEstadoAprovisionamiento reporte, CancellationToken ct) => throw SinConfigurar();
}
