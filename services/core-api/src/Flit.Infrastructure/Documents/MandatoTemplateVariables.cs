using Flit.Tramites.Application.Documents;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Datos ya resueltos que comparten todas las variables de una plantilla de mandato: se calculan una vez por
/// documento para que cada variable solo proyecte su valor.
/// </summary>
internal sealed record MandatoVariableContext(
    MandatoData Data,
    DocumentParte? Parte,
    string MandatarioNombre,
    string MandatarioDocumento,
    string ObjetoTramite,
    DateTime FechaEvento);

/// <summary>
/// Una variable <c>{{nombre}}</c> permitida en las plantillas de mandato (HU #13170, Feature #13118).
/// </summary>
/// <param name="Name">Nombre canónico, en minúsculas y sin llaves.</param>
/// <param name="Description">Qué dato sustituye (se muestra al Super Admin que edita la plantilla).</param>
/// <param name="Aliases">
/// Nombres equivalentes de las plantillas de FLIT 1 (<c>MandatosFile</c>): se aceptan y se sustituyen por el
/// mismo valor que el nombre canónico.
/// </param>
/// <param name="Resolve">Valor a imprimir; <c>___</c> cuando el dato falta (igual que los formatos del sistema).</param>
internal sealed record MandatoTemplateVariable(
    string Name,
    string Description,
    IReadOnlyList<string> Aliases,
    Func<MandatoVariableContext, string> Resolve);

/// <summary>
/// <b>Única definición</b> de las variables de plantilla de mandato: el generador las sustituye
/// (<see cref="MandatoPdfGenerator"/>) y el validador (<see cref="MandatoTemplateValidator"/>) las acepta
/// leyendo esta misma lista; agregar una variable aquí basta para ambos.
/// <para><b>Mapeo de FLIT 1 (decidido al implementar, pendiente de ratificación del PO):</b> solo tienen
/// equivalencia las variables que el generador ya sabe llenar con el mismo significado:
/// <c>nombre_mandante</c>, <c>cedula_mandante</c>, <c>nombre_mandatario</c>, <c>cedula_mandatario</c>,
/// <c>nombre_union_temporal</c> y <c>nit_union_temporal</c>. El resto (<c>hash_mandante</c>,
/// <c>hash_mandatario</c>, <c>nit_mandante</c>, <c>razon_social_mandante</c>, <c>sigla_union_temporal</c>,
/// <c>ciudad_camara_comercio</c>, <c>nombre_representante_mandatario</c>, <c>cedula_representante_mandatario</c>)
/// no tiene dato equivalente en el contrato actual y se rechaza como variable desconocida.</para>
/// <para><b>Fechas de firma:</b> el contrato no guarda una marca de tiempo por firmante; el evento de firma del
/// trámite es <c>FechaTramite</c> (la misma fecha de la cláusula «se firmó entre las partes el…»). Las tres
/// variables de firma usan ese evento y no existe configuración de fecha en el formato.</para>
/// </summary>
internal static class MandatoTemplateVariables
{
    private static readonly string[] None = [];

    public static IReadOnlyList<MandatoTemplateVariable> All { get; } =
    [
        new("placa", "Placa del vehículo.", None,
            c => MandatoPdfGenerator.Val(c.Data.Tramite.Placa, string.Empty)),
        new("tramite", "Nombre del trámite (objeto del contrato).", None, c => c.ObjetoTramite),
        new("organismo", "Organismo de tránsito.", None,
            c => MandatoPdfGenerator.Val(c.Data.Tramite.Organismo.Nombre, "___")),
        new("ciudad", "Ciudad del organismo.", None,
            c => c.Data.Tramite.Organismo.Ciudad?.Trim() ?? string.Empty),
        new("fecha", "Fecha del trámite, en letras (7 de mayo de 2026).", None,
            c => MandatoPdfGenerator.FormatFechaEs(c.FechaEvento)),
        new("mandante_nombre", "Nombre del mandante (o lista de otorgantes).", ["nombre_mandante"],
            c => MandatoPdfGenerator.MandanteNombrePlaceholder(c.Data, c.Parte)),
        new("mandante_documento", "Documento del mandante (o lista de otorgantes).", ["cedula_mandante"],
            c => MandatoPdfGenerator.MandanteDocumentoPlaceholder(c.Data, c.Parte)),
        new("mandatario_nombre", "Nombre del mandatario persona.", ["nombre_mandatario"], c => c.MandatarioNombre),
        new("mandatario_documento", "Documento del mandatario persona.", ["cedula_mandatario"],
            c => c.MandatarioDocumento),
        new("mandatario_institucional", "Razón social del mandatario institucional.", ["nombre_union_temporal"],
            c => MandatoPdfGenerator.Val(
                c.Data.PartesVisibles ? c.Data.InstitutionalMandataryName : null, "___")),
        new("mandatario_nit", "NIT del mandatario institucional.", ["nit_union_temporal"],
            c => MandatoPdfGenerator.Val(
                c.Data.PartesVisibles ? c.Data.InstitutionalMandataryNit : null, "___")),
        new("fecha_firma", "Fecha de la firma del contrato, en letras.", None,
            c => MandatoPdfGenerator.FormatFechaEs(c.FechaEvento)),
        new("fecha_hora_firma_mandante", "Fecha y hora de la firma del mandante (DD/MM/YYYY HH:mm).", None,
            c => MandatoPdfGenerator.FormatFechaHora(c.FechaEvento)),
        new("fecha_firma_mandatario", "Fecha y hora de la firma del mandatario (DD/MM/YYYY HH:mm).", None,
            c => MandatoPdfGenerator.FormatFechaHora(c.FechaEvento)),
    ];

    /// <summary>Resuelve el nombre (canónico o alias, sin distinguir mayúsculas) a su variable; null si no existe.</summary>
    public static MandatoTemplateVariable? Find(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return All.FirstOrDefault(v =>
            string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)
            || v.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Todos los nombres aceptados (canónicos y alias).</summary>
    public static IEnumerable<string> AcceptedNames => All.SelectMany(v => v.Aliases.Prepend(v.Name));
}
