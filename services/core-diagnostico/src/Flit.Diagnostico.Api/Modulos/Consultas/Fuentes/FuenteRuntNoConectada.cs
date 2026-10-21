namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Fuente RUNT por defecto (HU #13725, F03-C15): mientras no exista el adaptador real sobre core-consultas (C-PER), QA y PDN
/// fallan cerrado. Devuelve siempre <see cref="TipoErrorFuente.NoConectado"/> (definitivo, no se reintenta), nunca datos, y
/// no tiene dependencias: no hay proveedor al que llamar.
/// </summary>
public sealed class FuenteRuntNoConectada : IFuenteRunt
{
    public const string Mensaje = "La fuente RUNT no está conectada en este ambiente.";

    public Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct) =>
        Task.FromResult(ResultadoRunt.Fallo(TipoErrorFuente.NoConectado, Mensaje));
}
