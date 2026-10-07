namespace Flit.Tramites.Domain.Identity;

/// <summary>
/// HU #13287 — se intentó regenerar el enlace de captura de una validación que NO espera captura (ni <c>manual_activo</c> ni <c>rechazado</c> con motivo, HU #13299) (Épica
/// #13202): sin flujo manual esperando captura no hay enlace que reemplazar. El caso de uso comprueba antes con
/// <c>PuedeRegenerarEnlaceManual</c> y responde 409 <c>flujo_manual_no_activo</c>; la excepción es la red de seguridad de la
/// entidad. Sin PII en el mensaje.
/// </summary>
public sealed class FlujoManualNoActivoException : InvalidOperationException
{
    public FlujoManualNoActivoException()
        : base("La validación de identidad no está en flujo manual activo: no hay enlace de captura que regenerar.")
    {
    }
}
