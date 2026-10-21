using Flit.Comparendos.Domain.Modelo;
using Flit.Consultas.Grpc.V1;
using Grpc.Core;

namespace Flit.Comparendos.Api.Modulos.Contraventor;

/// <summary>El conductor que devuelve la API de contratos de la gestora.</summary>
public sealed record ConductorEncontrado(TipoDocumento? TipoDocumento, string? NumeroDocumento, string Nombres, string? Correo, string? Telefono,
    string? Direccion, string? Ciudad, string? Sucursal, string? TipoContrato);

public enum ResultadoContrato
{
    Encontrado,
    NoEncontrado,
    Error,
}

public sealed record RespuestaContrato(ResultadoContrato Resultado, ConductorEncontrado? Conductor, string? Mensaje, string? CuerpoCrudo);

/// <summary>
/// API de contratos de la gestora: quién tenía el vehículo en una fecha (ADO #12978). Hoy solo existe la de Renting
/// [Excel P12, P3] y su contrato se recibe al final; por eso el modo simulado: placa terminada en número par → conductor
/// de prueba; impar → no encontrado; terminada en 0 → error.
/// </summary>
public interface IApiContratos
{
    Task<RespuestaContrato> ConsultarAsync(Guid tenantId, string placa, DateOnly fecha, CancellationToken ct);
}

/// <summary>Fuera de DEV, mientras no se conecte la API real: responde error (se reintenta) y nunca inventa un conductor.</summary>
internal sealed class ApiContratosNoConectada : IApiContratos
{
    public Task<RespuestaContrato> ConsultarAsync(Guid tenantId, string placa, DateOnly fecha, CancellationToken ct) =>
        Task.FromResult(new RespuestaContrato(ResultadoContrato.Error, null, "La API de contratos no está conectada en este ambiente.", null));
}

internal sealed class ApiContratosSimulada : IApiContratos
{
    public Task<RespuestaContrato> ConsultarAsync(Guid tenantId, string placa, DateOnly fecha, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(placa);
        var ultimo = placa.Length > 0 ? placa[^1] : 'X';
        if (ultimo == '0')
            return Task.FromResult(new RespuestaContrato(ResultadoContrato.Error, null, "API de contratos simulada no disponible.", null));
        if (!char.IsAsciiDigit(ultimo) || (ultimo - '0') % 2 == 1)
            return Task.FromResult(new RespuestaContrato(ResultadoContrato.NoEncontrado, null, $"Sin contrato vigente para {placa} el {fecha:yyyy-MM-dd}.", "{\"contratos\":[]}"));

        var documento = $"10{Math.Abs(placa.GetHashCode(StringComparison.Ordinal)) % 100000000:D8}";
        return Task.FromResult(new RespuestaContrato(ResultadoContrato.Encontrado,
            new ConductorEncontrado(TipoDocumento.CC, documento, $"CONDUCTOR DE PRUEBA {placa}", $"conductor.{placa.ToLowerInvariant()}@example.test", "3001234567",
                "CALLE 10 # 20-30", "BOGOTA", "SUCURSAL NORTE", "RENTING OPERATIVO"),
            null, $"{{\"placa\":\"{placa}\",\"simulado\":true}}"));
    }
}

/// <summary>
/// La API de contratos por core-consultas (HU #13712, gRPC <c>ConsultarContratoConductor</c>): la llamada a Renting, su
/// certificado y el desvío del contacto viven allá. Un fallo de la llamada es error (se reintenta), nunca «no encontrado».
/// Si Renting no está conectado en el ambiente y el modo simulado está permitido (DEV), responde la simulada, como antes.
/// </summary>
internal sealed class ApiContratosCoreConsultas(FuentesComparendosService.FuentesComparendosServiceClient cliente, ApiContratosSimulada? simulada) : IApiContratos
{
    public async Task<RespuestaContrato> ConsultarAsync(Guid tenantId, string placa, DateOnly fecha, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(placa);
        var pedido = new ConsultarContratoConductorRequest { Proveedor = "renting", Placa = placa, Fecha = fecha.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) };
        ConsultarContratoConductorResponse r;
        try
        {
            r = await cliente.ConsultarContratoConductorAsync(pedido, new Metadata { { "x-flit-tenant-id", tenantId.ToString() } }, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition && simulada is not null)
        {
            return await simulada.ConsultarAsync(tenantId, placa, fecha, ct).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode != StatusCode.Cancelled || !ct.IsCancellationRequested)
        {
            return new RespuestaContrato(ResultadoContrato.Error, null, $"core-consultas: {ex.Status.Detail}", null);
        }

        switch (r.Resultado)
        {
            case ResultadoContratoConductor.Encontrado when r.Conductor is { } k:
                // Sin nombre no hay a quién asignar: se deja para asignarlo a mano, sin reintentos.
                if (!k.HasNombres || string.IsNullOrWhiteSpace(k.Nombres))
                    return new RespuestaContrato(ResultadoContrato.NoEncontrado, null,
                        $"Renting encontró el contrato de {placa} pero no trae el nombre del cliente: asígnalo a mano.", null);
                // Renting real (2026-10-20) no devuelve el documento: se asigna con nombre y correo y queda «documento pendiente».
                // Si algún día lo trae completo y válido, se usa.
                TipoDocumento? tipo = k.HasNumeroDocumento && k.HasTipoDocumento && Enum.TryParse<TipoDocumento>(k.TipoDocumento, true, out var t) && Enum.IsDefined(t) ? t : null;
                return new RespuestaContrato(ResultadoContrato.Encontrado, new ConductorEncontrado(tipo, tipo is null ? null : k.NumeroDocumento,
                    k.Nombres, k.HasCorreo ? k.Correo : null, k.HasTelefono ? k.Telefono : null, k.HasDireccion ? k.Direccion : null,
                    k.HasCiudad ? k.Ciudad : null, k.HasSucursal ? k.Sucursal : null, k.HasTipoContrato ? k.TipoContrato : null), null, null);
            case ResultadoContratoConductor.NoEncontrado:
                return new RespuestaContrato(ResultadoContrato.NoEncontrado, null, $"Sin contrato vigente para {placa} el {fecha:yyyy-MM-dd}.", null);
            default:
                return new RespuestaContrato(ResultadoContrato.Error, null, r.HasMensajeError ? r.MensajeError : "La API de contratos respondió con error.", null);
        }
    }
}
