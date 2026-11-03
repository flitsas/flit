using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Solicitudes;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>
/// Gate a Pago, validación de archivo y avalúo (HU #14243). Alineado a portal PN / FLIT 2:
/// mismos límites que <c>AdjuntosService</c> (20 MB, PDF/JPG/PNG/WEBP) y avalúo vía
/// <see cref="IConsultasRunt.ConsultarAvaluosAsync"/> (RPC <c>ConsultarAvaluos</c> de core-consultas).
/// El gate lee el borrador por <see cref="IBorradorPunto"/>. La persistencia de archivos y valor comercial reutiliza
/// <c>/solicitudes/{id}/adjuntos</c> y <c>PUT …/comercial</c> del borrador abierto en Actores.
/// </summary>
public sealed class CompletitudRequisitosPuntoService(IFachadaDefiniciones definiciones, IConsultasRunt consultas, IBorradorPunto borradores, IValoresRuntBorrador? valoresRunt = null)
{
    /// <summary>Misma lista que <c>FirmaDeArchivo</c> / portal <c>EXTENSIONES_PERMITIDAS</c>.</summary>
    public static readonly HashSet<string> ExtensionesPermitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".webp",
    };

    public const long TamanoMaximoBytes = 20 * 1024 * 1024;
    public const string CodigoValorVenta = "valor_venta";
    public const string CodigoOrganismoAjeno = "borrador_otro_organismo";

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

    /// <summary>
    /// Gate a Pago (HU #14243 AC4). Todo sale del borrador guardado en servidor (adjuntos reales, valor comercial, prenda,
    /// transformaciones y campos); nada de lo que el navegador diga sobre documentos o valor cuenta.
    /// </summary>
    public async Task<ResultadoCompletitudPunto> EvaluarAsync(
        OrganismoPunto sesion,
        string? tokenBorrador,
        string? tipoCodigo,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        var solicitudId = await borradores.ResolverSesionAsync(tokenBorrador, ct).ConfigureAwait(false);
        if (solicitudId is null)
            return new ResultadoCompletitudPunto.Rechazo(StatusCodes.Status401Unauthorized, "session_invalid", "La sesión del trámite no es válida.");

        var borrador = await borradores.LeerAsync(solicitudId.Value, ct).ConfigureAwait(false);
        if (borrador is null)
            return new ResultadoCompletitudPunto.Rechazo(StatusCodes.Status404NotFound, "solicitud_no_encontrada", "No encontramos ese trámite.");
        if (!borrador.EsCanalPunto)
            return new ResultadoCompletitudPunto.Rechazo(StatusCodes.Status409Conflict, "no_es_canal_punto", "Esta solicitud no se abrió en el punto de atención.");
        if (borrador.OrganismoId != sesion.OrganismoId)
            return new ResultadoCompletitudPunto.Rechazo(StatusCodes.Status403Forbidden, CodigoOrganismoAjeno, "Este trámite pertenece a otro organismo.");

        // El tipo es el del borrador; si el cuerpo trae otro, no se evalúa contra uno distinto.
        var pedido = (tipoCodigo ?? string.Empty).Trim();
        if (pedido.Length > 0 && !string.Equals(pedido, borrador.TipoCodigo, StringComparison.OrdinalIgnoreCase))
            return new ResultadoCompletitudPunto.DatosInvalidos("El tipo de trámite no corresponde al borrador.");

        DefinicionTipoDto def;
        try
        {
            def = await definiciones.ObtenerDefinicionAsync(borrador.TipoCodigo, sesion.OrganismoId.ToString(), ct).ConfigureAwait(false);
        }
        catch (FachadaException ex) when (ex.EstadoHttp is StatusCodes.Status404NotFound or StatusCodes.Status422UnprocessableEntity)
        {
            return new ResultadoCompletitudPunto.DatosInvalidos(ex.Message);
        }

        var ctx = new ContextoCondicionPunto(
            borrador.Datos.Prenda?.Tiene == true,
            borrador.Datos.Requisitos?.Transformaciones ?? [],
            borrador.Campos,
            borrador.HayPersonaJuridica);

        var pendientes = new List<PendienteCompletitudPuntoDto>();
        // Lo que reclama una declaración (certificado de blindaje, documentos de la causal elegida) lo decide ella, no la matriz.
        var deLasDeclaraciones = ReglasBorrador.DocumentosDeDeclaraciones(def);
        foreach (var doc in def.Documentos.OrderBy(d => d.Orden).Where(d => CondicionesDocumentoPunto.Exigible(d, ctx) && !deLasDeclaraciones.Contains(d.DocCode)))
        {
            if (!borrador.DocCodesCargados.Contains(doc.DocCode))
                pendientes.Add(new PendienteCompletitudPuntoDto(doc.DocCode, doc.Nombre));
        }

        // HU 13 — el gate también exige las declaraciones del tipo (valor nuevo, blindaje, causal, destino) y los documentos de lo elegido.
        var runt = valoresRunt is not null && ReglasBorrador.HayTransformaciones(def, borrador.Campos.Keys)
            ? await valoresRunt.ObtenerAsync(solicitudId.Value, ct).ConfigureAwait(false)
            : null;
        foreach (var x in ReglasBorrador.PendientesDeDeclaraciones(def, borrador.Campos, borrador.Datos.Requisitos?.Transformaciones ?? [], borrador.DocCodesCargados, runt))
            pendientes.Add(x.Motivo is null
                ? new PendienteCompletitudPuntoDto(x.Referencia, x.Nombre)
                : new PendienteCompletitudPuntoDto(x.Codigo, x.Motivo));

        if (ReglasBorrador.DocumentoDePrenda(borrador.Datos.Prenda?.Decision) is { } docPrenda && !borrador.DocCodesCargados.Contains(docPrenda))
            pendientes.Add(new PendienteCompletitudPuntoDto(docPrenda, def.Documentos.FirstOrDefault(d => d.DocCode == docPrenda)?.Nombre ?? "Documento de la prenda"));

        if (def.Flags.RequiereComercial && borrador.Datos.Comercial?.Valor is not > 0)
            pendientes.Add(new PendienteCompletitudPuntoDto(CodigoValorVenta, "Valor de venta"));

        return new ResultadoCompletitudPunto.Ok(new CompletitudRequisitosPuntoDto(
            pendientes.Count == 0,
            def.Flags.RequiereComercial,
            pendientes));
    }
}

public sealed record ValorVentaSugeridoPuntoDto(decimal? ValorSugerido, string Fuente);

public sealed record PendienteCompletitudPuntoDto(string Codigo, string Nombre);

public sealed record CompletitudRequisitosPuntoDto(
    bool PuedeContinuarPago,
    bool RequiereValorVenta,
    IReadOnlyList<PendienteCompletitudPuntoDto> Pendientes);

public abstract record ResultadoValidacionArchivoPunto
{
    public sealed record Ok : ResultadoValidacionArchivoPunto;
    public sealed record Rechazo(string Codigo, string Mensaje) : ResultadoValidacionArchivoPunto;
}

public abstract record ResultadoCompletitudPunto
{
    public sealed record Ok(CompletitudRequisitosPuntoDto Completitud) : ResultadoCompletitudPunto;
    public sealed record DatosInvalidos(string Mensaje) : ResultadoCompletitudPunto;
    public sealed record Rechazo(int Http, string Codigo, string Mensaje) : ResultadoCompletitudPunto;
}

/// <summary>Solo el tipo: documentos, valor, prenda y demás se leen del borrador guardado (el navegador no los afirma).</summary>
public sealed record CompletitudRequisitosPuntoRequest(string? TipoCodigo);

public sealed record ValidarArchivoPuntoRequest(string? NombreArchivo, long TamanoBytes);
