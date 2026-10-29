using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.PersonaNatural;

/// <summary>
/// Lo que la elegibilidad necesita saber de un tipo de trámite: sale de <c>tramites.procedure_types</c> y de su matriz
/// documental, nunca de un código escrito a mano.
/// </summary>
/// <param name="DocumentosObligatorios">Códigos de los documentos obligatorios de la matriz por defecto del tipo (sin los
/// informativos ni los condicionales por grupo).</param>
public sealed record PerfilTipoParaElegibilidad(
    string Codigo,
    string Familia,
    bool Publicado,
    bool Activo,
    bool WizardHabilitado,
    ProcedureTypeGateProfile Perfil,
    IReadOnlyCollection<string> DocumentosObligatorios);

/// <summary>Resultado de la elegibilidad: <see cref="Motivo"/> vacío cuando el tipo es elegible.</summary>
public sealed record ResultadoElegibilidad(bool Elegible, IReadOnlyList<string> Motivos)
{
    public string Motivo => string.Join("; ", Motivos);

    public static ResultadoElegibilidad Si { get; } = new(true, []);
}

/// <summary>
/// HU #14079 (Feature K, Épica #14021) — ¿puede un ciudadano sin cuenta tramitar este tipo desde el portal de persona
/// natural? Se calcula del perfil de capacidades del tipo: un tipo nuevo, publicado y con el wizard encendido, queda
/// evaluado por la misma regla sin tocar código ni agregar una lista por tipo.
/// <para>
/// Lo único «escrito a mano» es la lista CERRADA de capacidades que el portal aún no soporta
/// (<see cref="Capacidades"/>), en un solo lugar y con su motivo en lenguaje claro. Cada capacidad se detecta con un
/// dato que el tipo ya tiene (flag del <c>gate_profile</c> o documento obligatorio de su matriz). Lo que FLIT 2 resuelve
/// solo en código (p. ej. si el mandato lo exige una persona jurídica concreta) no se puede deducir del tipo: por eso la
/// habilitación final sigue siendo de una lista humana (HU J2) y esta regla solo descarta lo que es seguro que no
/// funciona.
/// </para>
/// </summary>
public static class ElegibilidadPersonaNatural
{
    public const string MotivoWizardApagado = "El tipo de trámite no está habilitado en FLIT 2.";

    /// <summary>Una capacidad que el portal no soporta: cómo se detecta y cómo se le explica a quien configura.</summary>
    public sealed record Capacidad(string Codigo, string Motivo, Func<PerfilTipoParaElegibilidad, bool> Detecta);

    private static readonly string[] DocumentosJuridica =
        ["camara_comercio", "camara_comercio_vendedor", "camara_comercio_comprador", "camara_comercio_locatario"];

    private static readonly string[] DocumentosLeasing = ["contrato_leasing", "declaracion_arrendadora"];

    /// <summary>Lista cerrada de capacidades no soportadas por el portal (un solo lugar; se amplía aquí).</summary>
    public static readonly IReadOnlyList<Capacidad> Capacidades =
    [
        new("juridica", "exige persona jurídica o NIT", p => TieneAlguno(p, DocumentosJuridica)),
        new("mandato", "exige mandato o tramitador", p => TieneAlguno(p, ["mandato"])),
        new("impronta", "exige impronta con verificación física", p => TieneAlguno(p, ["impronta"])),
        new("leasing", "exige leasing o locatario", p => p.Perfil.RequiresLessee || TieneAlguno(p, DocumentosLeasing)),
        new("causales", "exige causales de cancelación con documentos externos",
            p => p.DocumentosObligatorios.Any(d => CancelacionCausales.TodosLosDocumentos.Contains(d, StringComparer.OrdinalIgnoreCase))),
        new("prenda", "exige gravamen o acreedor prendario", p => p.Perfil.HasPrendaGate || ProcedureTypeLayers.EsTipoPrendaBase(p.Codigo)),
        new("operador", "depende de un operador de tránsito para elegir el organismo",
            p => string.Equals(p.Perfil.TransitOfficeSource, ProcedureTypeGateProfile.TransitOfficeSourceOperator, StringComparison.OrdinalIgnoreCase)),
        new("organismo_destino", "exige declarar un organismo de destino", p => p.Perfil.RequiresDestinationTransitOffice),
        new("placa_posterior", "solicita la placa después de la entrega", p => p.Perfil.RequiresPlateRequest),
    ];

    public static ResultadoElegibilidad Evaluar(PerfilTipoParaElegibilidad tipo)
    {
        ArgumentNullException.ThrowIfNull(tipo);

        if (!tipo.WizardHabilitado || !tipo.Activo || !tipo.Publicado)
            return new ResultadoElegibilidad(false, [MotivoWizardApagado]);

        var motivos = Capacidades.Where(c => c.Detecta(tipo)).Select(c => $"El tipo {c.Motivo}.").ToList();
        return motivos.Count == 0 ? ResultadoElegibilidad.Si : new ResultadoElegibilidad(false, motivos);
    }

    private static bool TieneAlguno(PerfilTipoParaElegibilidad p, IEnumerable<string> codigos) =>
        p.DocumentosObligatorios.Any(d => codigos.Contains(d, StringComparer.OrdinalIgnoreCase));
}
