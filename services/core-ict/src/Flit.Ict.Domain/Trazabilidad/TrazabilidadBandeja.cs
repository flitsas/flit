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
    // Familia del tipo (MATRICULAS | TRASPASO | OTROS), para poder pedir «todos los traspasos» sin
    // enumerar sus tipos. Convive con TipoTramite porque el desplegable ofrece los dos niveles: si
    // llegan ambos manda el tipo, que es el más específico.
    string? Familia = null,
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

/// <summary>Un tipo de trámite del desplegable de la bandeja, con la familia que lo encabeza.</summary>
/// <remarks>
/// La familia sale de <c>ict.procedure_type_mapping</c>, que es donde ADR-0050 la dejó: ICT tiene su
/// PROPIO catálogo —los tipos de transacción que manda la integración— y el mapeo es lo que los ata a
/// las tres familias de FLIT. Sin ella el desplegable son dieciséis opciones en fila, y quien busca
/// tiene que saberse de memoria cuál es cuál.
/// </remarks>
public sealed record TipoTramiteOpcion(int Id, string Nombre, string? Familia);

/// <summary>
/// Catálogo de tipos de trámite para el filtro de la bandeja (HU #11815).
/// </summary>
/// <remarks>
/// Devuelve solo los tipos que APARECEN en los trámites que quien pregunta puede ver, no los 20 del
/// catálogo maestro. Un desplegable lleno de opciones que siempre devuelven cero le hace perder el
/// tiempo a quien busca y, en el caso de una empresa, delataría qué trámites tramitan las demás.
/// </remarks>
public interface ITiposTramiteQuery
{
    Task<IReadOnlyList<TipoTramiteOpcion>> ConsultarAsync(
        Guid? tenantId, Guid? companiaTenantId, CancellationToken ct = default);
}

/// <summary>
/// Normaliza el campo libre «placas o VIN» de la bandeja.
/// </summary>
/// <remarks>
/// Vive en el dominio y no en el repositorio porque es la única parte de la búsqueda que tiene reglas
/// de negocio propias (qué se considera un término válido) y la única que se puede probar sin base de
/// datos. El repositorio solo la consume.
/// </remarks>
/// <summary>
/// Normaliza la familia que llega por la petición.
/// </summary>
/// <remarks>
/// <para>
/// Una familia desconocida se descarta (se devuelve <c>null</c>) en vez de pasarse tal cual a la
/// consulta. Pasarla devolvería cero filas, y la bandeja vacía se leería como «esta empresa no tiene
/// trámites de esa familia» cuando lo que pasó es que el valor no existe. Descartarla enseña todo, y
/// que sobren filas se nota; que falten, no.
/// </para>
/// <para>
/// Los tres códigos se repiten aquí y no se importan de core-api a propósito: ICT es un servicio
/// aparte y no depende de su dominio. Lo que los ata es <c>ict.procedure_type_mapping.family</c>, que
/// tiene su propio CHECK con estos mismos tres valores.
/// </para>
/// </remarks>
public static class FamiliaFiltro
{
    public static readonly IReadOnlyList<string> Validas = ["MATRICULAS", "TRASPASO", "OTROS"];

    public static string? Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var code = valor.Trim().ToUpperInvariant();
        return Validas.Contains(code) ? code : null;
    }
}

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
