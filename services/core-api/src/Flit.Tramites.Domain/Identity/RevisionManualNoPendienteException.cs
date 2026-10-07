namespace Flit.Tramites.Domain.Identity;

/// <summary>
/// HU #13298/#13299 (Feature #13282 C, Épica #13202) — se intentó aprobar o rechazar una validación que NO está en
/// <c>pendiente_revision_manual</c> (o que no es del flujo manual): solo la captura ya recibida espera revisión humana. El caso de
/// uso comprueba antes con <c>PuedeRevisarManual</c> y responde 409 <c>estado_invalido</c>; la excepción es la red de seguridad de
/// la entidad. Sin PII en el mensaje.
/// </summary>
public sealed class RevisionManualNoPendienteException : InvalidOperationException
{
    public RevisionManualNoPendienteException()
        : base("La validación de identidad no está pendiente de revisión manual.")
    {
    }
}
