namespace Flit.Consultas.Api;

/// <summary>
/// Secretos de los proveedores (HU #13347 AC2): un proveedor en modo <c>real</c> sin su credencial no arranca el
/// servicio; el mensaje dice qué variable falta. Mismas claves que core-api (configuración primero y, si no, la
/// variable de entorno cruda; Kyverum RUNT al revés, como en <c>ConfigureKyverumRunt</c>). Kyverum RUNT no tiene modo
/// mock y encabeza la cadena por defecto: su llave siempre se exige.
/// </summary>
internal static class ProveedoresSettings
{
    public static void Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? Cfg(string key, string env) => configuration[key] is { Length: > 0 } v ? v : Environment.GetEnvironmentVariable(env);
        string? EnvPrimero(string key, string env) => Environment.GetEnvironmentVariable(env) is { Length: > 0 } v ? v : configuration[key];
        bool Real(string key, string env, string porDefecto) =>
            string.Equals(Cfg(key, env) ?? porDefecto, "real", StringComparison.OrdinalIgnoreCase);

        var faltan = new List<string>();
        var verifikReal = new[]
            {
                ("Consultations:VerifikVehicleMode", "VERIFIK_VEHICLE_MODE", "real"),
                ("Consultations:VerifikSimitMode", "VERIFIK_SIMIT_MODE", "mock"),
                ("Consultations:VerifikRnmcMode", "VERIFIK_RNMC_MODE", "mock"),
                ("Consultations:VerifikConductorMode", "VERIFIK_CONDUCTOR_MODE", "mock"),
                ("Consultations:VerifikRuesMode", "VERIFIK_RUES_MODE", "mock"),
            }
            .Where(m => Real(m.Item1, m.Item2, m.Item3))
            .Select(m => m.Item2)
            .ToList();
        if (verifikReal.Count > 0 && string.IsNullOrWhiteSpace(Cfg("Verifik:BearerToken", "VERIFIK_API_TOKEN")))
            faltan.Add($"VERIFIK_API_TOKEN (lo piden {string.Join(", ", verifikReal)}=real)");

        if (Real("Consultations:KyverumFinesMode", "KYVERUM_FINES_MODE", "mock") && string.IsNullOrWhiteSpace(Cfg("KyverumFines:ApiKey", "KYVERUM_FINES_API_KEY")))
            faltan.Add("KYVERUM_FINES_API_KEY (lo pide KYVERUM_FINES_MODE=real)");

        if (Real("Consultations:FasecoldaMode", "FASECOLDA_MODE", "mock"))
        {
            if (string.IsNullOrWhiteSpace(Cfg("Fasecolda:Username", "FASECOLDA_API_USERNAME")))
                faltan.Add("FASECOLDA_API_USERNAME (lo pide FASECOLDA_MODE=real)");
            if (string.IsNullOrWhiteSpace(Cfg("Fasecolda:Password", "FASECOLDA_API_PASSWORD")))
                faltan.Add("FASECOLDA_API_PASSWORD (lo pide FASECOLDA_MODE=real)");
        }

        if (string.IsNullOrWhiteSpace(EnvPrimero("ImprontaRunt:ApiKey", "KYVERUM_RUNT_API_KEY")))
            faltan.Add("KYVERUM_RUNT_API_KEY (Kyverum RUNT encabeza la cadena por defecto y no tiene modo mock)");

        if (faltan.Count > 0)
            throw new InvalidOperationException($"core-consultas no puede arrancar; faltan secretos de proveedores: {string.Join("; ", faltan)}.");
    }
}
