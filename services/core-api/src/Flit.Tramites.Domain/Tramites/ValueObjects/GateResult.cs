namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Resultado de un gate del wizard (paridad <c>TraspasoGateResult</c> de Johan).
/// <c>Ok</c> = puede avanzar; si no, <c>Code</c>/<c>Message</c> explican el bloqueo.
/// </summary>
public sealed record GateResult(bool Ok, string? Code = null, string? Message = null)
{
    public static GateResult Allowed { get; } = new(true);

    public static GateResult Block(string code, string message) => new(false, code, message);
}
