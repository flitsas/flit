using System.Text;
using Flit.Consultas.Grpc.V1;
using Grpc.Core;

namespace Flit.Impuestos.Api.Modulos.Ejecucion;

/// <summary>
/// Lo que la liquidación necesita del RUNT (plan §2.2, D-ENTRADA): el propietario (tipo y número de documento ya en el código del
/// API: <c>CC</c>, <c>NIT</c>, <c>CE</c>, <c>PA</c>, <c>TI</c> o <c>CD</c>), el vehículo, el organismo de tránsito y el contacto y la
/// dirección del propietario. Solo vive en memoria mientras se arma la solicitud: la dirección, el correo y el teléfono no se guardan
/// (DA-7, Ley 1581). <see cref="ToString"/> no imprime ningún dato.
/// </summary>
public sealed record DatosRunt(
    string? TipoDocumento,
    string? NumeroDocumento,
    string? NombrePropietario,
    string? Modelo,
    string? Marca,
    string? OrganismoNombre,
    string? Correo,
    string? Telefono,
    string? DireccionDepartamento,
    string? DireccionCiudad,
    string? Direccion)
{
    public override string ToString() => "DatosRunt { [REDACTADO: PII] }";
}

/// <summary>Respuesta del RUNT para una placa.</summary>
public abstract record ConsultaRunt
{
    /// <summary>El RUNT registra el vehículo.</summary>
    public sealed record Encontrado(DatosRunt Datos) : ConsultaRunt;

    /// <summary>El RUNT respondió y no registra el vehículo: definitivo.</summary>
    public sealed record NoEncontrado : ConsultaRunt;

    /// <summary>No se pudo saber (red, Intempo caído, plazo, error del proveedor): se reintenta. <see cref="Motivo"/> es técnico.</summary>
    public sealed record Transitorio(string Motivo) : ConsultaRunt;
}

/// <summary>RUNT por placa para una compañía. Nunca lanza por la red: todo lo que no es una respuesta es <see cref="ConsultaRunt.Transitorio"/>.</summary>
public interface IClienteRunt
{
    Task<ConsultaRunt> ConsultarPorPlacaAsync(Guid empresa, string placa, CancellationToken ct);
}

/// <summary>
/// RUNT por <c>flit.consultas.v1.RuntIntempoService.ConsultarPorPlaca</c> de core-consultas (ADR-0093: el RUNT sigue pasando por
/// Consultas, que mide el consumo por producto con el token de <c>svc-impuestos</c>, scope <c>platform.consultas</c>), con la empresa
/// en <c>x-flit-tenant-id</c>. Solo usa el contrato gRPC público (EP-CF1). <c>NO_ENCONTRADO</c> es definitivo; un
/// <c>RESULTADO_RUNT_INTEMPO_ERROR</c> o cualquier <see cref="RpcException"/> es transitorio. No registra la placa ni la respuesta.
/// </summary>
/// <example>
/// <code>
/// var r = await runt.ConsultarPorPlacaAsync(empresa, "ABC123", ct);
/// if (r is ConsultaRunt.Encontrado e) { var documento = e.Datos.NumeroDocumento; }
/// </code>
/// </example>
internal sealed class ClienteRuntGrpc(RuntIntempoService.RuntIntempoServiceClient cliente) : IClienteRunt
{
    public const string CabeceraEmpresa = "x-flit-tenant-id";

    public async Task<ConsultaRunt> ConsultarPorPlacaAsync(Guid empresa, string placa, CancellationToken ct)
    {
        try
        {
            var r = await cliente.ConsultarPorPlacaAsync(new ConsultarPorPlacaRequest { Placa = placa },
                new Metadata { { CabeceraEmpresa, empresa.ToString() } }, cancellationToken: ct).ConfigureAwait(false);
            return r.Resultado switch
            {
                ResultadoRuntIntempo.Encontrado when r.Vehiculo is not null => new ConsultaRunt.Encontrado(Mapear(r.Vehiculo)),
                ResultadoRuntIntempo.NoEncontrado => new ConsultaRunt.NoEncontrado(),
                _ => new ConsultaRunt.Transitorio($"RUNT respondió {r.Resultado} ({r.TipoError})"),
            };
        }
        catch (RpcException ex)
        {
            return new ConsultaRunt.Transitorio($"rpc {ex.StatusCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new ConsultaRunt.Transitorio(ex.GetType().Name);
        }
    }

    /// <summary>
    /// El propietario actual (o, si no viene, el primero de <c>owner</c>), su primera dirección, el modelo, la marca y el organismo de
    /// tránsito del vehículo. Los textos vacíos quedan null.
    /// </summary>
    public static DatosRunt Mapear(VehiculoIntempo v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var actual = v.PropietariosActuales.FirstOrDefault();
        var persona = v.Owner.FirstOrDefault();
        var direccion = actual?.Direcciones.FirstOrDefault();
        var tipo = actual is not null
            ? TipoDocumento(actual.IdTipoDocumento) ?? TipoDocumento(actual.TipoDocumento)
            : TipoDocumento(persona?.DocumentType);
        return new DatosRunt(
            tipo,
            Texto(actual?.NoDocumento) ?? Texto(persona?.DocumentNumber),
            Texto(actual?.NombreCompleto) ?? Texto(persona?.Name),
            Texto(v.InformacionGeneral?.Modelo),
            Texto(v.InformacionGeneral?.Marca),
            Texto(v.InformacionGeneral?.OrganismoTransito),
            Texto(direccion?.Email),
            Texto(direccion?.Telefono),
            Texto(direccion?.Departamento),
            Texto(direccion?.Ciudad),
            Texto(direccion?.Direccion));
    }

    /// <summary>
    /// Código del API para el tipo de documento del RUNT: acepta la sigla de una letra (<c>C</c>, <c>N</c>, <c>E</c>, <c>P</c>,
    /// <c>T</c>, <c>D</c>), la del API o el nombre («Cédula Ciudadanía», «NIT»…), sin tildes ni mayúsculas. Otro valor: null.
    /// </summary>
    public static string? TipoDocumento(string? valor)
    {
        var limpio = Normalizar(valor);
        return limpio switch
        {
            "C" or "CC" or "CEDULA CIUDADANIA" or "CEDULA DE CIUDADANIA" => "CC",
            "N" or "NIT" => "NIT",
            "E" or "CE" or "CEDULA EXTRANJERIA" or "CEDULA DE EXTRANJERIA" => "CE",
            "P" or "PA" or "PASAPORTE" => "PA",
            "T" or "TI" or "TARJETA IDENTIDAD" or "TARJETA DE IDENTIDAD" => "TI",
            "D" or "CD" or "CARNE DIPLOMATICO" => "CD",
            _ => null,
        };
    }

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;
        var sinTildes = new StringBuilder(valor.Length);
        foreach (var c in valor.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sinTildes.Append(c == '.' ? ' ' : c);
        }

        return string.Join(' ', sinTildes.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>Sin <c>Impuestos:Consultas:Address</c>: el RUNT no está conectado y toda consulta es transitoria.</summary>
internal sealed class ClienteRuntNoConectado : IClienteRunt
{
    public Task<ConsultaRunt> ConsultarPorPlacaAsync(Guid empresa, string placa, CancellationToken ct) =>
        Task.FromResult<ConsultaRunt>(new ConsultaRunt.Transitorio("RUNT no configurado (Impuestos__Consultas__Address)"));
}
