using Flit.Consultas.Grpc.V1;
using Flit.PersonaNatural.Api.Consultas;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>Comparendos que devolvió la consulta de multas, sin el documento del infractor.</summary>
public sealed record MultasMinimas(bool Consultado, int Comparendos);

/// <summary>Respuesta de <c>/punto/consultas/vehiculo</c>: <see cref="VehiculoMinimo"/> más <c>color</c> (aditivo).</summary>
public sealed record VehiculoMinimoPunto(
    bool Existe, string? Placa, string? Clase, string? Marca, string? Linea, string? ModeloAno, string? TipoServicio, string? Estado,
    bool TienePrenda, bool TieneLimitacion, bool TieneRestricciones, string? Organismo, string? Color);

/// <summary>
/// Consultas del punto (HU #14221). Reutilizan vehículo y persona del portal y, para multas,
/// <see cref="IConsultasRunt.ConsultarMultasAsync"/> (la RPC <c>ConsultarMultas</c> de core-consultas).
/// Ninguna crea procedure instance ni solicitud.
/// </summary>
public sealed class ConsultasPuntoService(ConsultaConductorService conductores, IConsultasRunt runt)
{
    /// <summary>El dato mínimo del vehículo más el color (atributo del vehículo, no de personas), de la misma consulta al RUNT.</summary>
    public async Task<VehiculoMinimoPunto> VehiculoAsync(string? placa, string? vin, DocumentoConsulta? propietario, CancellationToken ct)
    {
        var resultado = await runt.ConsultarVehiculoAsync(placa, vin, propietario, "punto", ct).ConfigureAwait(false);
        if (ProyeccionMinima.ProveedorCaido(resultado))
            throw new ProveedorNoDisponibleException("El proveedor del RUNT no respondió.");
        var v = ProyeccionMinima.Vehiculo(resultado, placa);
        return new VehiculoMinimoPunto(
            v.Existe, v.Placa, v.Clase, v.Marca, v.Linea, v.ModeloAno, v.TipoServicio, v.Estado, v.TienePrenda, v.TieneLimitacion,
            v.TieneRestricciones, v.Organismo, v.Existe ? ProyeccionMinima.ColorDe(resultado) : null);
    }

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
