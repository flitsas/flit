namespace Flit.Tramites.Domain.Tramites.PersonaNatural;

/// <summary>Conceptos de la tarifa del portal (los mismos del CHECK de <c>admin.persona_natural_tariffs</c>).</summary>
public static class ConceptosTarifa
{
    public const string DerechosTransito = "DERECHOS_TRANSITO";
    public const string ServicioFlit = "SERVICIO_FLIT";
    public const string CambioColor = "CAMBIO_COLOR";
    public const string ConversionCombustible = "CONVERSION_COMBUSTIBLE";
    public const string CambioCarroceria = "CAMBIO_CARROCERIA";
    public const string EnvioDocumento = "ENVIO_DOCUMENTO";

    /// <summary>Transformaciones complementarias: suman solo si la definición del tipo las permite.</summary>
    public static readonly IReadOnlyList<string> Transformaciones = [CambioColor, ConversionCombustible, CambioCarroceria];

    /// <summary>Todo lo que el ciudadano puede pedir además de los dos renglones base.</summary>
    public static readonly IReadOnlyList<string> Adicionales = [.. Transformaciones, EnvioDocumento];

    public static string Nombre(string concepto) => concepto switch
    {
        DerechosTransito => "Valor del Servicio de tránsito",
        ServicioFlit => "Valor de Servicio FLIT",
        CambioColor => "Cambio de color",
        ConversionCombustible => "Conversión de combustible",
        CambioCarroceria => "Cambio de carrocería",
        EnvioDocumento => "Envío del documento",
        _ => concepto,
    };
}

/// <summary>Una tarifa vigente ya filtrada por fecha. <c>TipoId</c>/<c>OrganismoId</c> nulos = valor general.</summary>
public sealed record TarifaVigente(string Concepto, Guid? TipoId, Guid? OrganismoId, long ValorCop, Guid VersionId);

public sealed record LineaCotizacion(string Codigo, string Nombre, string Subtitulo, long ValorCop);

/// <param name="VersionTarifa">Ids de las versiones de tarifa usadas, ordenados y separados por coma: lo que la solicitud del
/// portal congela para que un cambio de tarifa posterior no altere lo ya cotizado.</param>
/// <param name="AdicionalesNoAplicados">Conceptos pedidos que no sumaron (el tipo no los permite o no tienen tarifa).</param>
public sealed record Cotizacion(
    IReadOnlyList<LineaCotizacion> Lineas,
    long TotalCop,
    string VersionTarifa,
    IReadOnlyList<string> AdicionalesNoAplicados);

/// <summary>Resultado: o hay cotización o hay un código de error estable (<see cref="CotizadorPersonaNatural.TarifaNoVigente"/>).</summary>
public sealed record ResultadoCotizacion(Cotizacion? Cotizacion, string? Error);

/// <summary>
/// HU #14043 (Feature B, Épica #14021) — el ÚNICO cálculo de la cotización del portal: dos renglones (Servicio de tránsito y
/// Servicio FLIT) más los adicionales que el tipo permite, y el total. Lo usan la landing y el paso de pago por el mismo RPC,
/// así que el total no puede diferir. Pura: recibe las tarifas ya vigentes; no inventa ningún valor.
/// </summary>
public static class CotizadorPersonaNatural
{
    public const string TarifaNoVigente = "tarifa_no_vigente";

    /// <summary>Qué incluye el servicio FLIT (texto del producto, no un precio).</summary>
    public const string SubtituloServicioFlit =
        "Uso de la plataforma, acompañamiento en el trámite digital, domicilio, 1 subsanación y envío de la tarjeta de propiedad";

    public static ResultadoCotizacion Calcular(
        Guid tipoId,
        Guid organismoId,
        bool permiteTransformaciones,
        IEnumerable<string> adicionalesSolicitados,
        IReadOnlyCollection<TarifaVigente> tarifas)
    {
        ArgumentNullException.ThrowIfNull(adicionalesSolicitados);
        ArgumentNullException.ThrowIfNull(tarifas);

        var derechos = Resolver(ConceptosTarifa.DerechosTransito, tipoId, organismoId, tarifas);
        var servicio = Resolver(ConceptosTarifa.ServicioFlit, tipoId, organismoId, tarifas);
        if (derechos is null || servicio is null)
            return new ResultadoCotizacion(null, TarifaNoVigente);

        var lineas = new List<LineaCotizacion>
        {
            new(ConceptosTarifa.DerechosTransito, ConceptosTarifa.Nombre(ConceptosTarifa.DerechosTransito), string.Empty, derechos.ValorCop),
            new(ConceptosTarifa.ServicioFlit, ConceptosTarifa.Nombre(ConceptosTarifa.ServicioFlit), SubtituloServicioFlit, servicio.ValorCop),
        };
        var usadas = new List<TarifaVigente> { derechos, servicio };
        var noAplicados = new List<string>();

        foreach (var concepto in adicionalesSolicitados.Select(a => a?.Trim().ToUpperInvariant() ?? string.Empty).Distinct())
        {
            var permitido = ConceptosTarifa.Adicionales.Contains(concepto)
                && (permiteTransformaciones || !ConceptosTarifa.Transformaciones.Contains(concepto));
            var tarifa = permitido ? Resolver(concepto, tipoId, organismoId, tarifas) : null;
            if (tarifa is null)
            {
                noAplicados.Add(concepto);
                continue;
            }

            lineas.Add(new LineaCotizacion(concepto, ConceptosTarifa.Nombre(concepto), string.Empty, tarifa.ValorCop));
            usadas.Add(tarifa);
        }

        var version = string.Join(',', usadas.Select(t => t.VersionId.ToString("N")).Order(StringComparer.Ordinal));
        return new ResultadoCotizacion(new Cotizacion(lineas, lineas.Sum(l => l.ValorCop), version, noAplicados), null);
    }

    /// <summary>
    /// Precedencia: (tipo, organismo) &gt; (tipo, general) &gt; (cualquier tipo, organismo) &gt; (cualquier tipo, general).
    /// Mismo orden que documenta el DDL 153.
    /// </summary>
    public static TarifaVigente? Resolver(string concepto, Guid tipoId, Guid organismoId, IEnumerable<TarifaVigente> tarifas) =>
        tarifas
            .Where(t => t.Concepto == concepto
                && (t.TipoId is null || t.TipoId == tipoId)
                && (t.OrganismoId is null || t.OrganismoId == organismoId))
            .OrderByDescending(t => (t.TipoId is null ? 0 : 2) + (t.OrganismoId is null ? 0 : 1))
            .FirstOrDefault();
}
