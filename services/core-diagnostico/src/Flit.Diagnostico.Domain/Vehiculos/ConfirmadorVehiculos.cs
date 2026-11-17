using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Estimacion;

namespace Flit.Diagnostico.Domain.Vehiculos;

/// <summary>
/// Vehículo tal como llega a la confirmación del lote (HU #13751), sin confiar en lo que el cliente ya validó: el
/// identificador y, si se declaró, el documento del propietario actual (HU #14582). <see cref="ToString"/> no revela el documento.
/// </summary>
public sealed record VehiculoCapturado(string? Identificador, string? TipoDocumentoPropietario, string? DocumentoPropietario)
{
    public override string ToString() =>
        $"VehiculoCapturado {{ Identificador = {Identificador}, TipoDocumentoPropietario = {TipoDocumentoPropietario}, DocumentoPropietario = {(DocumentoPropietario is null ? "null" : "****")} }}";
}

/// <summary>
/// Vehículo válido del lote, en el orden de la captura (1, 2, ...): el identificador escrito, el normalizado, su clase y el
/// documento del propietario actual declarado (HU #14582; en claro solo en memoria, hasta cifrarlo). <see cref="ToString"/> no
/// revela el documento.
/// </summary>
public sealed record VehiculoConfirmado(
    int Orden, string IdentificadorOriginal, string IdentificadorNorm, TipoIdentificador Tipo, string? TipoDocumentoPropietario, string? DocumentoPropietario)
{
    public override string ToString() =>
        $"VehiculoConfirmado {{ Orden = {Orden}, IdentificadorNorm = {IdentificadorNorm}, Tipo = {Tipo}, TipoDocumentoPropietario = {TipoDocumentoPropietario}, DocumentoPropietario = {(DocumentoPropietario is null ? "null" : "****")} }}";
}

/// <summary>
/// Resultado de <see cref="ConfirmadorVehiculos.Confirmar"/>: los válidos, cuántos se descartaron por inválidos y por
/// duplicados, el límite del lote y el estimado de consultas de los válidos (F01-H3, HU #13747).
/// </summary>
public sealed record ResultadoConfirmacion(
    IReadOnlyList<VehiculoConfirmado> Validos, int Invalidos, int Duplicados, int LimiteVehiculosLote, IReadOnlyList<EstimadoPorFuente> Estimado)
{
    /// <summary>No quedó ningún vehículo válido: la confirmación no procede.</summary>
    public bool SinValidos => Validos.Count == 0;

    /// <summary>Los válidos superan <c>limite_vehiculos_lote</c>: la confirmación no procede.</summary>
    public bool SuperaLimite => Validos.Count > LimiteVehiculosLote;
}

/// <summary>
/// Revalidación de la confirmación de un lote (HU #13751, F02-H5, F02-C4/C9/C10/C11/C14), sin base ni HTTP: cada vehículo
/// pasa por <see cref="ValidadorFilaVehiculo"/> (la misma regla del análisis de la carga), los inválidos se descartan, de
/// los identificadores válidos repetidos gana el primero y se calcula el estimado (<see cref="EstimadorConsultas"/>) de
/// los válidos. Marca sin válidos y el límite del lote; quien persiste decide no guardar nada en esos casos. La captura
/// Express (#13753) lo reusa con un solo vehículo.
/// </summary>
/// <example>
/// <code>
/// var r = ConfirmadorVehiculos.Confirmar([new("abc-123", null, null), new("MAL", null, null)], [Fuente.Runt], 500);
/// // r.Validos: [ (1, "abc-123", "ABC123", PlacaCarro) ], r.Invalidos == 1
/// </code>
/// </example>
public static class ConfirmadorVehiculos
{
    public static ResultadoConfirmacion Confirmar(IEnumerable<VehiculoCapturado> capturados, IEnumerable<Fuente> alcance, int limiteVehiculosLote)
    {
        ArgumentNullException.ThrowIfNull(capturados);
        ArgumentNullException.ThrowIfNull(alcance);
        var validos = new List<VehiculoConfirmado>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var invalidos = 0;
        var duplicados = 0;
        foreach (var capturado in capturados)
        {
            var fila = ValidadorFilaVehiculo.Validar(capturado?.Identificador, capturado?.TipoDocumentoPropietario, capturado?.DocumentoPropietario);
            if (!fila.EsValida || fila.Tipo is not { } tipo)
            {
                invalidos++;
                continue;
            }

            if (!vistos.Add(fila.Normalizado))
            {
                duplicados++;
                continue;
            }

            var documento = fila.Documento.Presente ? fila.Documento : null;
            validos.Add(new VehiculoConfirmado(
                validos.Count + 1, fila.Original.Trim(), fila.Normalizado, tipo, documento?.Tipo, documento?.Numero));
        }

        // HU #14582 (contrato §1.1): el declarado es el propietario actual y no alimenta al locatario; antes del RUNT el locatario
        // no se conoce y sus fuentes cuentan 0 (mínimo).
        var estimado = EstimadorConsultas.Estimar(alcance, validos.Select(_ => new VehiculoAEstimar(true, TieneDocumentoLocatario: false)));
        return new ResultadoConfirmacion(validos, invalidos, duplicados, limiteVehiculosLote, estimado);
    }
}
