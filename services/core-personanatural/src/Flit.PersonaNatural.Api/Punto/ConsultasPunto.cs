using Flit.Consultas.Grpc.V1;
using Flit.PersonaNatural.Api.Consultas;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>Comparendos que devolvió la consulta de multas, sin el documento del infractor.</summary>
public sealed record MultasMinimas(bool Consultado, int Comparendos);

/// <summary>
/// Consultas del punto (HU #14221). Reutilizan vehículo y persona del portal y, para multas,
/// <see cref="IConsultasRunt.ConsultarMultasAsync"/> (la RPC <c>ConsultarMultas</c> de core-consultas).
/// Ninguna crea procedure instance ni solicitud.
/// </summary>
public sealed class ConsultasPuntoService(ConsultaVehiculoService vehiculos, ConsultaConductorService conductores, IConsultasRunt runt)
{
    public Task<VehiculoMinimo> VehiculoAsync(string? placa, string? vin, DocumentoConsulta? propietario, CancellationToken ct) =>
        vehiculos.ConsultarAsync(placa, vin, propietario, "punto", ct);

    public Task<ConductorMinimo> PersonaAsync(DocumentoConsulta documento, CancellationToken ct) =>
        conductores.ConsultarAsync(documento, "punto", ct);

    public async Task<MultasMinimas> MultasAsync(string? placa, DocumentoConsulta? documento, CancellationToken ct)
    {
        var resultado = await runt.ConsultarMultasAsync(placa, documento, "punto", ct).ConfigureAwait(false);
        if (ProyeccionMinima.ProveedorCaido(resultado))
            throw new ProveedorNoDisponibleException("La fuente de multas no respondió.");

        var cantidad = resultado.Chequeos.Sum(c => c.Comparendos.Count);
        return new MultasMinimas(true, cantidad);
    }
}
