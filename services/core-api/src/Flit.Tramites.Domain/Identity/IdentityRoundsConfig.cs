using System.Net.Mail;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Identity;

/// <summary>Cuándo se avisa por correo que una ronda de validación de identidad cerró en rechazo.</summary>
public static class IdentityRoundsAlertTriggers
{
    /// <summary>Solo cuando la ronda rechazada es la última permitida (valor por defecto).</summary>
    public const string AlMaximo = "al_maximo";

    /// <summary>En cualquier ronda rechazada.</summary>
    public const string CadaRonda = "cada_ronda";

    public static bool IsValid(string? value) =>
        string.Equals(value, AlMaximo, StringComparison.Ordinal) || string.Equals(value, CadaRonda, StringComparison.Ordinal);
}

/// <summary>Datos de la configuración que no cumplen las reglas de negocio; <see cref="Errors"/> es campo → mensajes.</summary>
public sealed class IdentityRoundsConfigException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("La configuración de rondas de validación de identidad no es válida.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>
/// Configuración GLOBAL (no por empresa) de las rondas de validación de identidad. Una RONDA es un proceso de validación: una
/// fila de <see cref="ProcedureInstanceBiometricValidation"/> con hasta <see cref="ProcedureInstanceBiometricValidation.MaxAttempts"/>
/// intentos de Kyverum dentro del mismo enlace. Un reenvío abre la siguiente ronda sobre la MISMA fila, así que el conteo es
/// POR PERSONA dentro de la empresa: la suma de <c>1 + ResendCount</c> de todas sus filas (tipo y número de documento).
/// <see cref="RoundsUsed"/> es la cuenta de una sola fila.
/// Inmutable; las reglas (rango, disparador, correos) viven aquí y no solo en el endpoint.
/// </summary>
public sealed record IdentityRoundsConfig
{
    public const int MinRondas = 1;
    public const int MaxRondas = 10;
    public const int DefaultRondas = 3;
    public const int MaxCorreos = 20;
    public const int MaxCorreoLength = 254;

    public int RondasMaximas { get; }
    public string DisparadorAlerta { get; }
    public IReadOnlyList<string> CorreosAlerta { get; }

    private IdentityRoundsConfig(int rondasMaximas, string disparadorAlerta, IReadOnlyList<string> correosAlerta)
    {
        RondasMaximas = rondasMaximas;
        DisparadorAlerta = disparadorAlerta;
        CorreosAlerta = correosAlerta;
    }

    /// <summary>Valores de fábrica: 3 rondas, alerta al máximo, sin destinatarios.</summary>
    public static IdentityRoundsConfig Default { get; } = new(DefaultRondas, IdentityRoundsAlertTriggers.AlMaximo, []);

    /// <summary>
    /// Valida y normaliza (correos recortados y en minúscula). Cualquier error se devuelve junto, por campo
    /// (<c>rondasMaximas</c>, <c>disparadorAlerta</c>, <c>correosAlerta</c>).
    /// </summary>
    /// <exception cref="IdentityRoundsConfigException">Alguno de los valores no cumple las reglas.</exception>
    public static IdentityRoundsConfig Create(int rondasMaximas, string? disparadorAlerta, IEnumerable<string?>? correosAlerta)
    {
        var errors = new Dictionary<string, string[]>();

        if (rondasMaximas is < MinRondas or > MaxRondas)
            errors["rondasMaximas"] = [$"Las rondas máximas deben estar entre {MinRondas} y {MaxRondas}."];

        var disparador = disparadorAlerta?.Trim();
        if (!IdentityRoundsAlertTriggers.IsValid(disparador))
            errors["disparadorAlerta"] = [$"El disparador debe ser '{IdentityRoundsAlertTriggers.AlMaximo}' o '{IdentityRoundsAlertTriggers.CadaRonda}'."];

        var correos = new List<string>();
        var problemas = new List<string>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in correosAlerta ?? [])
        {
            var correo = raw?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!EsCorreoValido(correo))
            {
                problemas.Add("Hay un correo con formato inválido.");
                continue;
            }

            if (!vistos.Add(correo))
            {
                problemas.Add("Hay correos repetidos.");
                continue;
            }

            correos.Add(correo);
        }

        if (correos.Count > MaxCorreos)
            problemas.Add($"Máximo {MaxCorreos} correos.");
        if (problemas.Count > 0)
            errors["correosAlerta"] = [.. problemas.Distinct()];

        if (errors.Count > 0)
            throw new IdentityRoundsConfigException(errors);

        return new IdentityRoundsConfig(rondasMaximas, disparador!, correos);
    }

    /// <summary>
    /// Rondas usadas por la fila: la inicial más un reenvío por cada nueva ronda. POR FILA — cada reenvío reutiliza la misma fila
    /// de validación (<see cref="ProcedureInstanceBiometricValidation.ResendCount"/>), así que no se suma entre filas.
    /// </summary>
    public static int RoundsUsed(ProcedureInstanceBiometricValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        return 1 + Math.Max(0, validation.ResendCount);
    }

    /// <summary>¿Puede abrirse otra ronda? Con las rondas usadas ya en el máximo, el reenvío se bloquea.</summary>
    public bool AllowsNewRound(ProcedureInstanceBiometricValidation validation) => AllowsNewRound(RoundsUsed(validation));

    /// <summary>¿Cabe otra ronda con <paramref name="roundsUsed"/> ya usadas por la persona en la empresa?</summary>
    public bool AllowsNewRound(int roundsUsed) => roundsUsed < RondasMaximas;

    /// <summary>
    /// ¿Corresponde alerta por correo cuando la ronda actual cierra en rechazo? <c>cada_ronda</c>: siempre; <c>al_maximo</c>: solo si es
    /// la última permitida (rondas usadas ≥ máximo).
    /// </summary>
    public bool ShouldAlert(ProcedureInstanceBiometricValidation validation) => ShouldAlert(RoundsUsed(validation));

    /// <summary>Igual que <see cref="ShouldAlert(ProcedureInstanceBiometricValidation)"/>, con las rondas usadas por la persona.</summary>
    public bool ShouldAlert(int roundsUsed) =>
        string.Equals(DisparadorAlerta, IdentityRoundsAlertTriggers.CadaRonda, StringComparison.Ordinal)
        || roundsUsed >= RondasMaximas;

    private static bool EsCorreoValido(string correo)
    {
        if (correo.Length is 0 or > MaxCorreoLength)
            return false;
        if (!MailAddress.TryCreate(correo, out var address))
            return false;

        // MailAddress acepta «Nombre <a@b.c>» y dominios sin punto: solo vale la dirección escueta con dominio completo.
        return string.Equals(address.Address, correo, StringComparison.OrdinalIgnoreCase)
            && address.Host.Contains('.', StringComparison.Ordinal);
    }
}
