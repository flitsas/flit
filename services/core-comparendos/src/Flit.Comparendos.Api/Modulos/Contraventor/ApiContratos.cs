using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Api.Modulos.Contraventor;

/// <summary>El conductor que devuelve la API de contratos de la gestora.</summary>
public sealed record ConductorEncontrado(TipoDocumento TipoDocumento, string NumeroDocumento, string Nombres, string? Correo, string? Telefono,
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
            new ConductorEncontrado(TipoDocumento.CC, documento, $"CONDUCTOR DE PRUEBA {placa}", $"conductor.{placa.ToLowerInvariant()}@ejemplo.com", "3001234567",
                "CALLE 10 # 20-30", "BOGOTA", "SUCURSAL NORTE", "RENTING OPERATIVO"),
            null, $"{{\"placa\":\"{placa}\",\"simulado\":true}}"));
    }
}
