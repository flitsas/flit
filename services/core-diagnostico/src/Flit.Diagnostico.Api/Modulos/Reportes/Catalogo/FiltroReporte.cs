using System.Globalization;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Consulta del reporte ya validada (HU #14579, BE-2; contrato-api §0, §2 y §3; SP-9, SP-11). <see cref="Desde"/> y <see cref="Hasta"/>
/// son días de Colombia, los dos inclusive; <see cref="Enums"/>, los filtros de enum del tipo con sus valores; <see cref="Placa"/>, el
/// prefijo normalizado. Reutilizable por el backoffice (BE-4): <see cref="Leer"/> recibe si la ruta rechaza <c>empresa</c>.
/// </summary>
/// <example>
/// <code>
/// var filtro = FiltroReporte.Leer(TipoReporte.Vehiculos, request.Query, hoy, rechazarEmpresa: true, paginar: true);
/// </code>
/// </example>
internal sealed record FiltroReporte(
    TipoReporte Tipo,
    DateOnly Desde,
    DateOnly Hasta,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Enums,
    string? Placa,
    int Pagina,
    int Tamano)
{
    public const int TamanoPorDefecto = 25;
    public const int TamanoMaximo = 100;
    public const int DesplazamientoMaximo = 10_000;
    public const int PlacaMaxima = 20;

    /// <summary>Días del rango, contando los dos extremos (igual que el front; <see cref="RangoDias"/>, CR-BE-10).</summary>
    public const int DiasMaximos = RangoDias.DiasMaximos;

    /// <summary>00:00 de <see cref="Desde"/> en Colombia (inclusive).</summary>
    public DateTimeOffset DesdeInstante => new RangoDias(Desde, Hasta).Inicio;

    /// <summary>00:00 del día siguiente a <see cref="Hasta"/> en Colombia (exclusive).</summary>
    public DateTimeOffset HastaInstante => new RangoDias(Desde, Hasta).Fin;

    public IReadOnlyList<string> Valores(string clave) => Enums.TryGetValue(clave, out var v) ? v : [];

    /// <summary>Los filtros para <c>accesos_reporte</c>: los enums y la placa (el registro guarda solo que se filtró).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ParaRegistro()
    {
        var salida = new Dictionary<string, IReadOnlyList<string>>(Enums, StringComparer.Ordinal);
        if (Placa is not null)
            salida[RegistroAccesosReporte.FiltroPlaca] = [Placa];
        return salida;
    }

    /// <summary>
    /// Lee la consulta. 400 con <c>campo</c>: <c>FILTRO_INVALIDO</c> (documento, nombre, empresa si <paramref name="rechazarEmpresa"/>,
    /// fecha ausente o que no es AAAA-MM-DD, valor fuera del enum del tipo, placa vacía o de más de 20, parámetro único repetido),
    /// <c>RANGO_INVALIDO</c> (hasta antes de desde, hasta después de <paramref name="hoy"/> o más de 366 días con los dos extremos) o
    /// <c>PAGINACION_INVALIDA</c> (solo si <paramref name="paginar"/>). Un parámetro desconocido se ignora.
    /// </summary>
    public static FiltroReporte Leer(TipoReporte tipo, IQueryCollection query, DateOnly hoy, bool rechazarEmpresa, bool paginar)
    {
        ArgumentNullException.ThrowIfNull(query);
        RangoDias.RechazarProhibidos(query, _ => "Los reportes no se filtran por documento ni por nombre.");
        if (rechazarEmpresa && query.ContainsKey("empresa"))
            throw Invalido(FiltroListado.CodigoFiltroInvalido, "Esta ruta no recibe empresa.", "empresa");

        var (desde, hasta) = RangoDias.Leer(query, hoy);

        var descriptor = CatalogoReportes.De(tipo);
        var enums = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        string? placa = null;
        foreach (var filtro in descriptor.Filtros)
        {
            if (filtro.Clave == CatalogoReportes.FiltroPlaca)
            {
                placa = LeerPlaca(Unico(query, filtro.Clave));
                continue;
            }

            var valores = filtro.Repetible
                ? query[filtro.Clave].Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList()
                : Unico(query, filtro.Clave) is { Length: > 0 } unico ? [unico] : [];
            if (valores.Any(v => !filtro.Valores.Contains(v, StringComparer.Ordinal)))
                throw Invalido(FiltroListado.CodigoFiltroInvalido, $"{filtro.Clave} es uno de: {string.Join(", ", filtro.Valores)}.", filtro.Clave);
            if (valores.Count > 0)
                enums[filtro.Clave] = [.. valores.Distinct(StringComparer.Ordinal)];
        }

        var pagina = 1;
        var tamano = TamanoPorDefecto;
        if (paginar)
        {
            pagina = Entero(Unico(query, "pagina"), "pagina", 1);
            if (pagina < 1)
                throw Invalido(FiltroListado.CodigoPaginacionInvalida, "pagina empieza en 1.", "pagina");
            tamano = Entero(Unico(query, "tamano"), "tamano", TamanoPorDefecto);
            if (tamano is < 1 or > TamanoMaximo)
                throw Invalido(FiltroListado.CodigoPaginacionInvalida, $"tamano va de 1 a {TamanoMaximo}.", "tamano");
            if ((long)pagina * tamano > DesplazamientoMaximo)
                throw Invalido(FiltroListado.CodigoPaginacionInvalida, $"pagina × tamano no puede pasar de {DesplazamientoMaximo}.", "pagina");
        }

        return new FiltroReporte(tipo, desde, hasta, enums, placa, pagina, tamano);
    }

    /// <summary>Hoy en Colombia según el reloj.</summary>
    public static DateOnly Hoy(TimeProvider reloj) => RangoDias.Hoy(reloj);

    /// <summary>Fuentes del filtro <c>fuente</c> como enum.</summary>
    public IReadOnlyList<Fuente> Fuentes() => [.. Valores(CatalogoReportes.FiltroFuente).Select(Leer<Fuente>)];

    /// <summary>El filtro <c>estado</c> como el enum <typeparamref name="T"/> del tipo.</summary>
    public IReadOnlyList<T> Estados<T>()
        where T : struct, Enum => [.. Valores(CatalogoReportes.FiltroEstado).Select(Leer<T>)];

    private static T Leer<T>(string codigo)
        where T : struct, Enum =>
        Persistence.Configuraciones.CodigosDeEnum.TryLeer<T>(codigo, out var v) ? v : throw new ArgumentOutOfRangeException(nameof(codigo));

    private static string? LeerPlaca(string? crudo)
    {
        if (string.IsNullOrEmpty(crudo))
            return null;
        if (crudo.Length > PlacaMaxima)
            throw Invalido(FiltroListado.CodigoFiltroInvalido, $"placa admite hasta {PlacaMaxima} caracteres.", "placa");
        var placa = ValidadorIdentificador.Normalizar(crudo);
        return placa.Length > 0 ? placa : throw Invalido(FiltroListado.CodigoFiltroInvalido, "placa debe tener letras o dígitos.", "placa");
    }

    private static string? Unico(IQueryCollection query, string campo)
    {
        var valores = query[campo];
        if (valores.Count > 1)
            throw Invalido(FiltroListado.CodigoFiltroInvalido, $"{campo} va una sola vez.", campo);
        return valores.Count == 0 ? null : valores[0];
    }

    private static int Entero(string? valor, string campo, int porDefecto)
    {
        if (string.IsNullOrEmpty(valor))
            return porDefecto;
        return int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw Invalido(FiltroListado.CodigoPaginacionInvalida, $"{campo} es un número entero.", campo);
    }

    private static ReglaException Invalido(string codigo, string mensaje, string campo) => RangoDias.Invalido(codigo, mensaje, campo);
}
