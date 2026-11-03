using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>
/// Gate a Pago, validación de archivo y avalúo (HU #14243). Alineado a portal PN / FLIT 2:
/// mismos límites que <c>AdjuntosService</c> (20 MB, PDF/JPG/PNG/WEBP) y avalúo vía
/// <see cref="IConsultasRunt.ConsultarAvaluosAsync"/> (RPC <c>ConsultarAvaluos</c> de core-consultas).
/// La persistencia de archivos y valor comercial reutiliza <c>/solicitudes/{id}/adjuntos</c> y
/// <c>PUT …/comercial</c> del borrador abierto en Actores.
/// </summary>
public sealed class CompletitudRequisitosPuntoService(IFachadaDefiniciones definiciones, IConsultasRunt consultas)
{
    /// <summary>Misma lista que <c>FirmaDeArchivo</c> / portal <c>EXTENSIONES_PERMITIDAS</c>.</summary>
    public static readonly HashSet<string> ExtensionesPermitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".webp",
    };

    public const long TamanoMaximoBytes = 20 * 1024 * 1024;

    public ResultadoValidacionArchivoPunto ValidarArchivo(string? nombreArchivo, long tamanoBytes)
    {
        if (string.IsNullOrWhiteSpace(nombreArchivo))
            return new ResultadoValidacionArchivoPunto.Rechazo("archivo_invalido", "Indique el nombre del archivo.");

        var ext = Path.GetExtension(nombreArchivo.Trim());
        if (string.IsNullOrEmpty(ext) || !ExtensionesPermitidas.Contains(ext))
            return new ResultadoValidacionArchivoPunto.Rechazo(
                "formato_no_permitido",
                "Solo se aceptan archivos PDF, JPG, PNG o WEBP.");

        if (tamanoBytes <= 0 || tamanoBytes > TamanoMaximoBytes)
            return new ResultadoValidacionArchivoPunto.Rechazo(
                "tamano_excedido",
                "El archivo supera los 20 MB permitidos.");

        return new ResultadoValidacionArchivoPunto.Ok();
    }

    /// <summary>
    /// Misma fuente que FLIT 2 (core-consultas → Fasecolda/agregador). Si falla o no hay valor, vacío para edición manual.
    /// </summary>
    public async Task<ValorVentaSugeridoPuntoDto> SugerirValorVentaAsync(string? placa, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(placa))
            return new ValorVentaSugeridoPuntoDto(null, "sin_placa");

        try
        {
            var avaluo = await consultas.ConsultarAvaluosAsync(placa.Trim(), vin: null, ct).ConfigureAwait(false);
            if (avaluo.Valor is null or <= 0)
                return new ValorVentaSugeridoPuntoDto(null, string.IsNullOrWhiteSpace(avaluo.Fuente) ? "sin_datos" : avaluo.Fuente);
            return new ValorVentaSugeridoPuntoDto(avaluo.Valor, avaluo.Fuente);
        }
        catch (ProveedorNoDisponibleException)
        {
            return new ValorVentaSugeridoPuntoDto(null, "no_disponible");
        }
    }

    public async Task<ResultadoCompletitudPunto> EvaluarAsync(
        OrganismoPunto sesion,
        string? tipoCodigo,
        IReadOnlyList<string>? docCodesCargados,
        decimal? valorVenta,
        CancellationToken ct)
    {
        var codigo = (tipoCodigo ?? string.Empty).Trim();
        if (codigo.Length == 0)
            return new ResultadoCompletitudPunto.DatosInvalidos("Indique el tipo de trámite.");

        DefinicionTipoDto def;
        try
        {
            def = await definiciones.ObtenerDefinicionAsync(codigo, sesion.OrganismoId.ToString(), ct).ConfigureAwait(false);
        }
        catch (FachadaException ex) when (ex.EstadoHttp is StatusCodes.Status404NotFound or StatusCodes.Status422UnprocessableEntity)
        {
            return new ResultadoCompletitudPunto.DatosInvalidos(ex.Message);
        }

        var cargados = new HashSet<string>(
            (docCodesCargados ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var pendientes = new List<string>();
        foreach (var doc in def.Documentos.Where(d => d.Obligatorio && string.IsNullOrWhiteSpace(d.Condicion)))
        {
            if (!cargados.Contains(doc.DocCode))
                pendientes.Add(doc.DocCode);
        }

        if (def.Flags.RequiereComercial)
        {
            if (valorVenta is null || valorVenta <= 0)
                pendientes.Add("valor_venta");
        }

        return new ResultadoCompletitudPunto.Ok(new CompletitudRequisitosPuntoDto(
            pendientes.Count == 0,
            pendientes,
            def.Flags.RequiereComercial));
    }
}

public sealed record ValorVentaSugeridoPuntoDto(decimal? ValorSugerido, string Fuente);

public sealed record CompletitudRequisitosPuntoDto(
    bool PuedeContinuarPago,
    IReadOnlyList<string> Pendientes,
    bool RequiereValorVenta);

public abstract record ResultadoValidacionArchivoPunto
{
    public sealed record Ok : ResultadoValidacionArchivoPunto;
    public sealed record Rechazo(string Codigo, string Mensaje) : ResultadoValidacionArchivoPunto;
}

public abstract record ResultadoCompletitudPunto
{
    public sealed record Ok(CompletitudRequisitosPuntoDto Completitud) : ResultadoCompletitudPunto;
    public sealed record DatosInvalidos(string Mensaje) : ResultadoCompletitudPunto;
}

public sealed record CompletitudRequisitosPuntoRequest(
    string? TipoCodigo,
    IReadOnlyList<string>? DocCodesCargados,
    decimal? ValorVenta);

public sealed record ValidarArchivoPuntoRequest(string? NombreArchivo, long TamanoBytes);
