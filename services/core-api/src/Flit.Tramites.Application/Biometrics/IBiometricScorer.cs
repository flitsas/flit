namespace Flit.Tramites.Application.Biometrics;

/// <summary>Las 3 fotos de una validación biométrica, ya leídas a memoria para el scorer.</summary>
/// <param name="Rostro">Bytes de la selfie (rostro).</param>
/// <param name="CedulaFrontal">Bytes de la cédula frontal.</param>
/// <param name="CedulaReverso">Bytes de la cédula reverso.</param>
public sealed record BiometricPhotos(byte[]? Rostro, byte[]? CedulaFrontal, byte[]? CedulaReverso);

/// <summary>Resultado de evaluar una biométrica: score 0-100, aprobado y motivo legible.</summary>
/// <param name="Score">0-100. Se aprueba con score &gt;= threshold (60).</param>
/// <param name="Aprobado">true → estado aprobado; false → rechazado.</param>
/// <param name="Motivo">Razón legible (especialmente útil en rechazo).</param>
public sealed record BiometricScoreResult(int Score, bool Aprobado, string Motivo);

/// <summary>
/// Contrato del evaluador biométrico. La implementación actual es un MOCK determinista
/// (<see cref="MockBiometricScorer"/>); reemplazable por un scorer real (Anthropic / proveedor
/// biométrico) sin tocar los handlers — mismo patrón contract-first que los consultation providers.
/// </summary>
public interface IBiometricScorer
{
    /// <summary>Evalúa las 3 fotos y devuelve score + veredicto.</summary>
    BiometricScoreResult Score(BiometricPhotos photos);
}
