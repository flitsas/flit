namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #10970 — modo de aplicación de una validación transversal del ciclo de vida del trámite.
/// </summary>
public enum TramiteValidationMode
{
    /// <summary>Bloqueo duro: la validación corta el flujo (409/422). Valor por defecto.</summary>
    Block = 0,

    /// <summary>Solo advierte: no corta el flujo y deja el hallazgo visible en el semáforo (<c>warn</c>).</summary>
    Warn = 1,

    /// <summary>La validación no se evalúa: ni bloquea ni deja señal.</summary>
    Off = 2,
}

/// <summary>
/// HU #10970 — modo configurado de UNA validación. Se expresa como texto en configuración
/// (<c>block</c> / <c>warn</c> / <c>off</c>) para que sea legible en el <c>.env</c> de cada VPS.
/// </summary>
public sealed class TramiteValidationSetting
{
    /// <summary>Valor crudo de configuración. Ausente o vacío ⇒ <see cref="TramiteValidationMode.Block"/>.</summary>
    public string? Mode { get; set; }
}

/// <summary>
/// HU #10970 — sección de configuración <c>TramiteValidations</c>: permite decidir POR AMBIENTE si las
/// reglas transversales CF-01 (duplicidad de trámite en curso) y CF-03 (precondición registral del
/// vehículo) bloquean, solo advierten o no se evalúan.
///
/// <para><b>Por qué configuración y no <c>IsDevelopment()</c>:</b> los tres VPS (DEV, QA y PDN) corren
/// con <c>ASPNETCORE_ENVIRONMENT=Development</c> (ver <c>docker-compose.prod.yml</c>), así que ni el
/// entorno de .NET ni <c>appsettings.{Environment}.json</c> distinguen ambientes. El canal real por
/// ambiente es el <c>.env</c> de cada VPS, que llega como variables
/// <c>TramiteValidations__&lt;Validación&gt;__Mode</c>.</para>
///
/// <para><b>Fail-safe:</b> todo valor ausente, vacío o no reconocido resuelve a
/// <see cref="TramiteValidationMode.Block"/>. Un ambiente NUNCA se relaja por un olvido o un typo en
/// el <c>.env</c>.</para>
/// </summary>
public sealed class TramiteValidationPolicyOptions
{
    public const string SectionName = "TramiteValidations";

    /// <summary>CF-01 (HU #10876) — duplicidad de trámite EN PROCESO por familia (409).</summary>
    public TramiteValidationSetting DuplicateActiveProcedure { get; set; } = new();

    /// <summary>CF-03 (HU #10877) — precondición registral "vehículo ya matriculado" (422).</summary>
    public TramiteValidationSetting VehicleRegistrationState { get; set; } = new();

    /// <summary>
    /// Precondición registral del cambio de carrocería: el RUNT tiene que reportar una carrocería
    /// sobre la que cambiar (422).
    /// </summary>
    public TramiteValidationSetting VehicleBodyTypeRequired { get; set; } = new();
}

/// <summary>
/// HU #10970 — política YA RESUELTA que consumen los handlers: los modos efectivos, sin texto crudo ni
/// dependencia de <c>Microsoft.Extensions.Options</c> (mismo patrón que
/// <see cref="ImprontaValidationPolicyOptions"/>, que Infrastructure expone como POCO resuelto).
/// </summary>
public sealed class TramiteValidationPolicy(
    TramiteValidationMode duplicateActiveProcedure,
    TramiteValidationMode vehicleRegistrationState,
    TramiteValidationMode vehicleBodyTypeRequired = TramiteValidationMode.Block)
{
    /// <summary>CF-01 — modo efectivo del bloqueo de duplicidad.</summary>
    public TramiteValidationMode DuplicateActiveProcedure { get; } = duplicateActiveProcedure;

    /// <summary>CF-03 — modo efectivo del bloqueo por precondición registral.</summary>
    public TramiteValidationMode VehicleRegistrationState { get; } = vehicleRegistrationState;

    /// <summary>Modo efectivo del bloqueo "sin carrocería que cambiar".</summary>
    public TramiteValidationMode VehicleBodyTypeRequired { get; } = vehicleBodyTypeRequired;

    /// <summary>
    /// Política por defecto: todas las validaciones en bloqueo duro. Es la que usan los handlers cuando
    /// no se les inyecta ninguna (tests que no ejercitan la configurabilidad) ⇒ comportamiento idéntico
    /// al previo a esta historia.
    /// </summary>
    public static TramiteValidationPolicy BlockAll { get; } =
        new(TramiteValidationMode.Block, TramiteValidationMode.Block, TramiteValidationMode.Block);

    /// <summary>
    /// Resuelve los modos efectivos desde la configuración. <paramref name="onUnrecognized"/> recibe
    /// (nombre de la validación, valor crudo) por cada valor NO reconocido, para que quien registre la
    /// política lo deje en el log de arranque; el modo resultante en ese caso es
    /// <see cref="TramiteValidationMode.Block"/>.
    /// </summary>
    public static TramiteValidationPolicy Resolve(
        TramiteValidationPolicyOptions options,
        Action<string, string?>? onUnrecognized = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new TramiteValidationPolicy(
            ResolveOne(nameof(options.DuplicateActiveProcedure), options.DuplicateActiveProcedure, onUnrecognized),
            ResolveOne(nameof(options.VehicleRegistrationState), options.VehicleRegistrationState, onUnrecognized),
            ResolveOne(nameof(options.VehicleBodyTypeRequired), options.VehicleBodyTypeRequired, onUnrecognized));
    }

    /// <summary>
    /// Traduce un valor crudo a su modo. Ausente/vacío ⇒ <c>Block</c> SIN avisar (es el default
    /// documentado); valor presente pero desconocido ⇒ <c>Block</c> AVISANDO (típico typo en el
    /// <c>.env</c>: <c>"desactivado"</c>, <c>"false"</c>, …).
    /// </summary>
    private static TramiteValidationMode ResolveOne(
        string name,
        TramiteValidationSetting? setting,
        Action<string, string?>? onUnrecognized)
    {
        var raw = setting?.Mode;
        if (string.IsNullOrWhiteSpace(raw))
            return TramiteValidationMode.Block;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "block":
                return TramiteValidationMode.Block;
            case "warn":
                return TramiteValidationMode.Warn;
            case "off":
                return TramiteValidationMode.Off;
            default:
                onUnrecognized?.Invoke(name, raw);
                return TramiteValidationMode.Block;
        }
    }
}
