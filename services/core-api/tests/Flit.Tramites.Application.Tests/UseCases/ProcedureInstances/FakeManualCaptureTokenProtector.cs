using Flit.Tramites.Application.Identity;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>Protector reversible de pruebas (HU #14305): «cifra» con un prefijo; un blob ajeno no se puede descifrar.</summary>
internal sealed class FakeManualCaptureTokenProtector : IManualCaptureTokenProtector
{
    public const string Prefix = "enc:";

    public string Protect(string plaintextToken) => Prefix + plaintextToken;

    public string? TryUnprotect(string protectedToken) =>
        protectedToken.StartsWith(Prefix, StringComparison.Ordinal) ? protectedToken[Prefix.Length..] : null;
}
