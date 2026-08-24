using Flit.Ict.Domain.Enums;

namespace Flit.Ict.Domain.Trazabilidad;

/// <summary>
/// Filtros de la bandeja de trazabilidad ICT (HU #11815).
/// </summary>
/// <param name="TenantId">
/// Alcance obligatorio para quien NO es SuperAdmin: null significa «todos los tenants» y solo el
/// endpoint puede decidirlo. Nunca se rellena desde la petición del cliente.
/// </param>
/// <param name="Numero">
/// Número FLIT del trámite (<c>transaction_number</c>). Búsqueda EXACTA, no parcial: el Log ICT
/// actual usa un LIKE sobre la ruta y por eso «82» devuelve también 182, 820 y 1829.
/// </param>
/// <param name="PlacasOVins">
/// Placas y/o VIN a buscar en una sola consulta. El campo es uno solo en la interfaz porque el
/// analista de soporte pega lo que le mandan sin distinguir: se compara contra ambas columnas.
/// </param>
public sealed record TrazabilidadFiltro(
    Guid? TenantId,
    long? Numero = null,
    IReadOnlyList<string>? PlacasOVins = null,
    Guid? CompaniaTenantId = null,
    int? TipoTramite = null,
    int? Operacion = null,
    string? Estado = null,
    DateTime? Desde = null,
    DateTime? Hasta = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>Una fila de la bandeja: un PRE-TRÁMITE, nunca una petición HTTP.</summary>
public sealed record TrazabilidadFila(
    Guid Id,
    long Numero,
    string? ReferenciaCliente,
    string Placa,
    string? Vin,
    int TipoTramiteId,
    string? TipoTramite,
    int OperacionId,
    string? Operacion,
    Guid ClientTenantId,
    string? Compania,
    string Radicador,
    string Estado,
    /// <summary>
    /// Minutos que el trámite lleva sin avanzar. Se calcula EN SERVIDOR: leer el reloj en el render
    /// ata la cifra a la zona horaria del navegador y viola las reglas de pureza de React vigentes.
    /// Null cuando el trámite ya terminó su recorrido (borrador creado o anulado).
    /// </summary>
    long? MinutosEsperando,
    bool Pausado,
    bool SinAdjuntos,
    bool TieneTramiteFlit,
    DateTime RecibidoEn);

/// <summary>Página de la bandeja más los contadores por estado que alimentan la tira superior.</summary>
public sealed record TrazabilidadPagina(
    IReadOnlyList<TrazabilidadFila> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyDictionary<string, long> ConteoPorEstado);

/// <summary>Lectura de la bandeja de trazabilidad. Solo lectura: no toca el pipeline.</summary>
public interface ITrazabilidadBandejaQuery
{
    Task<TrazabilidadPagina> ConsultarAsync(TrazabilidadFiltro filtro, CancellationToken ct = default);
}

/// <summary>
/// Normaliza el campo libre «placas o VIN» de la bandeja.
/// </summary>
/// <remarks>
/// Vive en el dominio y no en el repositorio porque es la única parte de la búsqueda que tiene reglas
/// de negocio propias (qué se considera un término válido) y la única que se puede probar sin base de
/// datos. El repositorio solo la consume.
/// </remarks>
public static class PlacaVinFiltro
{
    /// <summary>
    /// Tope de términos por búsqueda. No es una restricción de producto sino de plan de consulta:
    /// cada término añade una comparación sobre dos columnas, y sin tope una pegada accidental de mil
    /// placas convierte la bandeja en un escaneo secuencial.
    /// </summary>
    public const int MaximoTerminos = 50;

    /// <summary>
    /// Parte por coma, punto y coma, espacio o salto de línea, recorta, pasa a mayúsculas y elimina
    /// duplicados conservando el orden. Devuelve lista vacía cuando no hay nada que buscar, de modo
    /// que quien la consume no tiene que distinguir entre null y cadena en blanco.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada))
        {
            return [];
        }

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var salida = new List<string>();

        foreach (var bruto in entrada.Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var termino = bruto.Trim().ToUpperInvariant();
            if (termino.Length == 0 || !vistos.Add(termino))
            {
                continue;
            }

            salida.Add(termino);
            if (salida.Count == MaximoTerminos)
            {
                break;
            }
        }

        return salida;
    }
}

/// <summary>Los siete estados de la bandeja, en el orden en que ocurren.</summary>
public static class TrazabilidadEstados
{
    public static readonly IReadOnlyList<string> Todos =
    [
        IctEstado.Recibido,
        IctEstado.EnValidacionNegocio,
        IctEstado.EnValidacionExterna,
        IctEstado.Procesado,
        IctEstado.BorradorCreado,
        IctEstado.ConNovedades,
        IctEstado.Anulado,
    ];

    public static bool EsValido(string? estado) => estado is not null && Todos.Contains(estado);

    /// <summary>Estados en los que el trámite ya no avanza y el tiempo en espera deja de contar.</summary>
    public static bool EsTerminal(string estado) =>
        estado is IctEstado.BorradorCreado or IctEstado.Anulado;
}
