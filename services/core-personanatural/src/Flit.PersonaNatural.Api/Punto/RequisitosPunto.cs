using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>
/// Matriz documental vigente del canal OT (HU #14242). Consume la definición FLIT 2.0 por tipo + organismo
/// de la cookie del punto; no congela snapshot ni crea procedure instance.
/// </summary>
public sealed class RequisitosPuntoService(IFachadaDefiniciones definiciones)
{
    public async Task<ResultadoMatrizRequisitosPunto> ObtenerAsync(
        OrganismoPunto sesion,
        string? tipoCodigo,
        CancellationToken ct)
    {
        var codigo = (tipoCodigo ?? string.Empty).Trim();
        if (codigo.Length == 0)
            return new ResultadoMatrizRequisitosPunto.DatosInvalidos("Indique el tipo de trámite.");

        DefinicionTipoDto def;
        try
        {
            def = await definiciones.ObtenerDefinicionAsync(codigo, sesion.OrganismoId.ToString(), ct).ConfigureAwait(false);
        }
        catch (FachadaException ex) when (ex.EstadoHttp is StatusCodes.Status404NotFound or StatusCodes.Status422UnprocessableEntity)
        {
            return new ResultadoMatrizRequisitosPunto.DatosInvalidos(ex.Message);
        }

        var items = def.Documentos
            .OrderBy(d => d.Orden)
            .Select(d => new ItemRequisitoPuntoDto(
                d.DocCode,
                d.Nombre,
                d.Obligatorio ? "obligatorio" : "opcional",
                d.Orden,
                d.InstruccionCargue,
                d.Condicion))
            .ToArray();

        // La matriz de FLIT 2.0 no asigna documentos a partes (DocumentoRequeridoDto no tiene "parte"): un único grupo
        // «Documentos», sin repetir la lista por actor. El front decide la visibilidad con `condicion`; quien radica carga todo.
        var grupos = new[] { new GrupoRequisitosPuntoDto(null, "Documentos", 1, items) };

        return new ResultadoMatrizRequisitosPunto.Ok(new MatrizRequisitosPuntoDto(
            def.Code,
            sesion.OrganismoId,
            def.Huella,
            def.Version,
            grupos));
    }
}

public sealed record ItemRequisitoPuntoDto(
    string DocCode,
    string Nombre,
    string Clase,
    int Orden,
    string InstruccionCargue,
    string Condicion);

public sealed record GrupoRequisitosPuntoDto(
    string? Parte,
    string Etiqueta,
    short Ordinal,
    IReadOnlyList<ItemRequisitoPuntoDto> Items);

public sealed record MatrizRequisitosPuntoDto(
    string TipoCodigo,
    Guid OrganismoId,
    string Huella,
    int Version,
    IReadOnlyList<GrupoRequisitosPuntoDto> Grupos);

public abstract record ResultadoMatrizRequisitosPunto
{
    public sealed record Ok(MatrizRequisitosPuntoDto Matriz) : ResultadoMatrizRequisitosPunto;
    public sealed record DatosInvalidos(string Mensaje) : ResultadoMatrizRequisitosPunto;
}
