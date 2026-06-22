using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Biometrics;

/// <summary>
/// MOCK: evaluador biométrico determinista para el Slice 6. No hace IO ni llama a ningún servicio
/// externo. Regla: si las 3 fotos están presentes y no vacías → score 85, aprobado; si falta alguna
/// (o está vacía) → score 0, rechazado con motivo.
///
/// TODO(slice-firma/biométrica-real): reemplazar por un scorer real (proveedor biométrico /
/// Anthropic vision) que compare rostro vs. cédula y valide la cédula. Implementar IBiometricScorer
/// y cambiar el registro en Infrastructure/Application DI — los handlers no cambian.
/// </summary>
public sealed class MockBiometricScorer : IBiometricScorer
{
    private const int ScoreAprobado = 85;

    public BiometricScoreResult Score(BiometricPhotos photos)
    {
        ArgumentNullException.ThrowIfNull(photos);

        var faltantes = new List<string>(3);
        if (Vacia(photos.Rostro)) faltantes.Add("rostro");
        if (Vacia(photos.CedulaFrontal)) faltantes.Add("cedula_frontal");
        if (Vacia(photos.CedulaReverso)) faltantes.Add("cedula_reverso");

        if (faltantes.Count > 0)
            return new BiometricScoreResult(0, false, $"Faltan fotos: {string.Join(", ", faltantes)}");

        // MOCK: 3 fotos presentes → score fijo por encima del threshold (60) → aprobado.
        return new BiometricScoreResult(
            ScoreAprobado,
            ScoreAprobado >= BiometricRules.ThresholdAprobacion,
            "Validación biométrica simulada (mock): coincidencia aceptada");
    }

    private static bool Vacia(byte[]? data) => data is null || data.Length == 0;
}
