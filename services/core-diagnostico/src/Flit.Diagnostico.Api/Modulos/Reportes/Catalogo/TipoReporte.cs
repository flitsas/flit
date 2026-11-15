using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>Un filtro del catálogo (contrato-api §2): <c>enum</c> con su lista cerrada de valores, o <c>prefijo</c> (la placa).</summary>
internal sealed record FiltroCatalogo(string Clave, string Tipo, bool Repetible, IReadOnlyList<string> Valores);

/// <summary>Un tipo de reporte del catálogo: nombre, descripción, si lleva documentos, fecha que filtra y sus filtros.</summary>
internal sealed record DescriptorReporte(
    TipoReporte Tipo, string Nombre, string Descripcion, bool ContienePii, string FechaReferencia, IReadOnlyList<FiltroCatalogo> Filtros)
{
    public FiltroCatalogo? Filtro(string clave) => Filtros.FirstOrDefault(f => f.Clave == clave);
}

/// <summary>
/// Catálogo cerrado de reportes (HU #14579, BE-2; contrato-api §2, diseno.md «Catálogo»). La compañía (R0) ve los cuatro primeros; el
/// backoffice (B0, BE-4) además <c>consumo</c>. <c>desde</c> y <c>hasta</c> no van en los filtros: son implícitos y obligatorios.
/// </summary>
internal static class CatalogoReportes
{
    public const string FiltroModalidad = "modalidad";
    public const string FiltroTipoAnalisis = "tipo_analisis";
    public const string FiltroVeredicto = "veredicto";
    public const string FiltroFuente = "fuente";
    public const string FiltroEstado = "estado";
    public const string FiltroPlaca = RegistroAccesosReporte.FiltroPlaca;

    public const string Express = "EXPRESS";
    public const string Masivo = "MASIVO";
    public const string SinDictamen = "SIN_DICTAMEN";

    private static readonly FiltroCatalogo Placa = new(FiltroPlaca, "prefijo", false, []);

    private static readonly DescriptorReporte[] Todos =
    [
        new(TipoReporte.Vehiculos, "Vehículos diagnosticados", "Un renglón por vehículo de los diagnósticos creados en el periodo.", true,
            "Fecha de creación del diagnóstico",
        [
            new(FiltroModalidad, "enum", false, [Express, Masivo]),
            new(FiltroTipoAnalisis, "enum", false, [.. Enum.GetValues<Domain.Diagnosticos.ModoDiagnostico>().Select(m => CodigosDeEnum.Codigo(m))]),
            new(FiltroVeredicto, "enum", false, ["VIABLE", "BLOQUEADO", SinDictamen]),
            Placa,
        ]),
        new(TipoReporte.Consultas, "Consultas a fuentes", "Un renglón por consulta a una fuente de los diagnósticos creados en el periodo.", false,
            "Fecha de creación del diagnóstico",
        [
            new(FiltroFuente, "enum", true, [.. Enum.GetValues<Fuente>().Select(f => CodigosDeEnum.Codigo(f))]),
            new(FiltroEstado, "enum", true, [.. Enum.GetValues<EstadoCelda>().Select(e => CodigosDeEnum.Codigo(e))]),
            Placa,
        ]),
        new(TipoReporte.Saneamientos, "Saneamientos", "Un renglón por saneamiento solicitado en el periodo.", false, "Fecha de solicitud del saneamiento",
        [
            new(FiltroEstado, "enum", true, [.. Enum.GetValues<EstadoSaneamiento>().Select(e => CodigosDeEnum.Codigo(e))]),
            Placa,
        ]),
        new(TipoReporte.Derivaciones, "Derivaciones a trámite", "Un renglón por vehículo enviado a crear trámite en el periodo.", false,
            "Fecha de la operación de derivación",
        [
            new(FiltroEstado, "enum", true, [.. Enum.GetValues<EstadoItemDerivacion>().Select(e => CodigosDeEnum.Codigo(e))]),
            Placa,
        ]),
        new(TipoReporte.Consumo, "Consumo por compañía", "Un renglón por compañía habilitada con diagnósticos en el periodo.", false,
            "Fecha de creación del diagnóstico", []),
    ];

    /// <summary>Los tipos del ámbito: la compañía no ve <c>consumo</c>.</summary>
    public static IReadOnlyList<DescriptorReporte> Del(AlcanceReporte alcance) =>
        alcance == AlcanceReporte.Compania ? [.. Todos.Where(d => d.Tipo != TipoReporte.Consumo)] : Todos;

    public static DescriptorReporte De(TipoReporte tipo) => Todos.Single(d => d.Tipo == tipo);

    /// <summary>
    /// El tipo del segmento <c>{tipo}</c> (código exacto en minúscula); 404 <c>REPORTE_NO_ENCONTRADO</c> si no existe o no es del
    /// ámbito (consumo en la compañía).
    /// </summary>
    public static TipoReporte LeerTipo(string? codigo, AlcanceReporte alcance)
    {
        if (TiposDeReporte.TryLeer(codigo, out var tipo) && Del(alcance).Any(d => d.Tipo == tipo))
            return tipo;
        throw new Plataforma.ReglaException(CodigoNoEncontrado, "El reporte pedido no existe.", StatusCodes.Status404NotFound);
    }

    public const string CodigoNoEncontrado = "REPORTE_NO_ENCONTRADO";

    /// <summary>El catálogo del ámbito con los máximos de la configuración.</summary>
    public static CatalogoReportesDto Dto(AlcanceReporte alcance, OpcionesReportes opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        return new CatalogoReportesDto([.. Del(alcance).Select(d => new ReporteCatalogoDto(
            TiposDeReporte.Codigo(d.Tipo), d.Nombre, d.Descripcion, d.ContienePii, d.FechaReferencia,
            [.. d.Filtros.Select(f => new FiltroCatalogoDto(f.Clave, f.Tipo, f.Repetible, f.Valores))],
            opciones.MaximoFilasExcel, opciones.MaximoFilasPdf))]);
    }
}
