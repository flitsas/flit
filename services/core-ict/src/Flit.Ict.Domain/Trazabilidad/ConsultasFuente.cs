namespace Flit.Ict.Domain.Trazabilidad;

/// <summary>
/// Una consulta a una fuente externa (RUNT) hecha para un pre-trámite (HU #11817).
/// </summary>
/// <param name="Bloquea">
/// La consulta que impide avanzar al trámite. Lo decide el servidor y no la pantalla: si cada
/// consumidor lo dedujera por su cuenta, la tabla y una eventual alerta podrían señalar consultas
/// distintas para el mismo trámite.
/// </param>
/// <param name="Respuesta">
/// Lo que devolvió la fuente, ya enmascarado. Puede ser null cuando la consulta nunca se resolvió,
/// que es justamente el caso interesante para soporte.
/// </param>
public sealed record ConsultaFuente(
    Guid Id,
    string NivelActor,
    string NivelActorEtiqueta,
    string TipoConsulta,
    string TipoConsultaEtiqueta,
    string? Identificador,
    bool Consultada,
    bool Valida,
    int Intentos,
    bool Bloquea,
    DateTime CreadaEn,
    string? Respuesta);

/// <summary>Lectura de las consultas a fuentes de un pre-trámite. Solo lectura.</summary>
public interface IConsultasFuenteQuery
{
    /// <summary>
    /// Devuelve null cuando el trámite no existe o es de otro tenant, y lista vacía cuando el trámite
    /// existe pero todavía no llegó a la etapa de consulta. Distinguir ambos importa: lo primero es un
    /// 404 y lo segundo una pantalla legítimamente vacía.
    /// </summary>
    Task<IReadOnlyList<ConsultaFuente>?> ConsultarAsync(long numero, Guid? tenantId, CancellationToken ct = default);
}

/// <summary>
/// Traduce el vocabulario técnico de las consultas a fuentes.
/// </summary>
/// <remarks>
/// Los códigos (MAIN, LERE, VIDEN…) son los de FLIT 1.0 y siguen guardándose así en la base. Se
/// traducen en el servidor y no en la pantalla para que la exportación, la API y la interfaz digan
/// exactamente lo mismo, y para que el código crudo siga viajando por si soporte necesita cruzarlo
/// con la base.
/// </remarks>
public static class EtiquetasConsultaFuente
{
    public static string NivelActor(string? codigo) => codigo?.ToUpperInvariant() switch
    {
        "MAIN" => "Principal",
        "LERE" => "Representante legal",
        "VEHI" => "Vehículo",
        null or "" => "Sin especificar",
        _ => codigo!,
    };

    public static string TipoConsulta(string? codigo) => codigo?.ToUpperInvariant() switch
    {
        "DRIVER" => "Conductor",
        "VEHICLE" => "Vehículo",
        "VIDEN" => "Validación de identidad",
        "VIN" => "VIN",
        null or "" => "Sin especificar",
        _ => codigo!,
    };

    /// <summary>
    /// Una consulta bloquea cuando se pidió y no dio un dato válido. Las que ni siquiera se han
    /// intentado todavía no bloquean: están en cola, que es distinto de haber fallado.
    /// </summary>
    public static bool Bloquea(bool consultada, bool valida, int intentos) =>
        !valida && (consultada || intentos > 0);
}
