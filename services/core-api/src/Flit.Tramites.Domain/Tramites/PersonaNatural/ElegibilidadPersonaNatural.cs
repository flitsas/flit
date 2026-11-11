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
/// natural? Hoy un tipo es elegible cuando está publicado, activo y con el wizard encendido en FLIT 2: la lista de
/// capacidades no soportadas (<see cref="Capacidades"/>) quedó vacía porque el portal ya cubre prenda, leasing del
/// locatario, causales, organismo elegido por el operador, organismo de destino y placa posterior. Lo que no aplica a una
/// persona natural (jurídica, mandato) se filtra en la definición y se rechaza en la identidad; no excluye el tipo.
/// La habilitación final sigue siendo de una lista humana (HU J2).
/// </summary>
public static class ElegibilidadPersonaNatural
{
    public const string MotivoWizardApagado = "El tipo de trámite no está habilitado en FLIT 2.";

    /// <summary>Una capacidad que el portal no soporta: cómo se detecta y cómo se le explica a quien configura.</summary>
    public sealed record Capacidad(string Codigo, string Motivo, Func<PerfilTipoParaElegibilidad, bool> Detecta);

    /// <summary>Lista cerrada de capacidades no soportadas por el portal (un solo lugar; vacía por decisión del PO).</summary>
    public static readonly IReadOnlyList<Capacidad> Capacidades = [];

    public static ResultadoElegibilidad Evaluar(PerfilTipoParaElegibilidad tipo)
    {
        ArgumentNullException.ThrowIfNull(tipo);

        if (!tipo.WizardHabilitado || !tipo.Activo || !tipo.Publicado)
            return new ResultadoElegibilidad(false, [MotivoWizardApagado]);

        var motivos = Capacidades.Where(c => c.Detecta(tipo)).Select(c => $"El tipo {c.Motivo}.").ToList();
        return motivos.Count == 0 ? ResultadoElegibilidad.Si : new ResultadoElegibilidad(false, motivos);
    }
}
