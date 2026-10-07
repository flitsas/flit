namespace Flit.Tramites.Domain.Identity;

/// <summary>
/// HU #13284 — se intentó activar el flujo manual sobre una validación APROBADA y VIGENTE (Épica #13202): esa identidad
/// ya vale y no se reemplaza. El caso de uso comprueba antes con <c>PuedeActivarFlujoManual</c> y responde 409; la
/// excepción es la red de seguridad de la entidad. Sin PII en el mensaje.
/// </summary>
public sealed class IdentidadManualNoActivableException : InvalidOperationException
{
    public IdentidadManualNoActivableException()
        : base("La validación de identidad está aprobada y vigente: no admite el flujo manual.")
    {
    }
}
