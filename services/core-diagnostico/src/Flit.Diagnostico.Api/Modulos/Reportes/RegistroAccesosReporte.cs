using System.Collections.Frozen;
using System.Text.Json;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Lo que se registra de una petición de reporte (HU #14579, BE-0, SP-8). <see cref="Filtros"/> son los filtros ya validados por la ruta,
/// clave → valores; la placa llega con su valor y el registro solo guarda que se filtró.
/// </summary>
public sealed record SolicitudAccesoReporte(
    AlcanceReporte Alcance,
    TipoReporte Tipo,
    FormatoReporte Formato,
    DateOnly Desde,
    DateOnly Hasta,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Filtros,
    int Filas,
    int Empresas,
    Guid? EmpresaConsultada = null);

/// <summary>
/// Escribe <c>diagnostico.accesos_reporte</c> (HU #14579, BE-0, SP-8, ADR-0088) <b>antes</b> de serializar o generar el reporte. Fail-closed:
/// una solicitud fuera de la lista blanca o un fallo de la base lanzan y la ruta no entrega datos.
/// <list type="bullet">
/// <item><see cref="RegistrarAsync"/>: agrega y guarda (compañía, y B4–B6 después de <c>ConEmpresaExplicita</c>).</item>
/// <item><see cref="Agregar"/>: solo agrega, para que el backoffice global guarde esta fila y las de <c>lecturas_backoffice</c> en el
/// mismo <c>SaveChanges</c> (<c>AuditoriaLecturaBackoffice.RegistrarVariasAsync</c>).</item>
/// </list>
/// Los filtros admitidos son los del catálogo (contrato-api §2) con valores de enum; <c>placa</c> se guarda como
/// <c>"placa_filtrada": true</c>. <c>documento</c>, <c>nombre</c> o cualquier otra clave, o un valor que no es de la lista, es un error
/// de programación de la ruta (que debió responder 400 antes): <see cref="ArgumentException"/> con un mensaje fijo, sin el valor (SP-9).
/// Al log solo van tipo, formato, filas, empresas, el id y el tipo de una excepción.
/// </summary>
public sealed partial class RegistroAccesosReporte(DiagnosticoDb db, UsuarioActual usuario, ILogger<RegistroAccesosReporte> logger)
{
    /// <summary>Clave del filtro de placa: su valor nunca se guarda.</summary>
    public const string FiltroPlaca = "placa";

    /// <summary>Lo que se guarda en lugar del valor de la placa.</summary>
    public const string PlacaFiltrada = "placa_filtrada";

    private static readonly string[] Estados =
    [
        "PENDIENTE", "EN_CURSO", "COMPLETADO", "ERROR", "NA",
        "SOLICITADO", "EN_GESTION", "SANEADO", "NO_PROCEDE", "ANULADO",
        "ENVIADO", "CREADO", "OMITIDO", "FALLIDO",
    ];

    private static readonly string[] Fuentes =
    [
        "RUNT", "RUNT_PROPIETARIO", "RUNT_LOCATARIO", "SIMIT_VEHICULO", "SIMIT_PROPIETARIO", "SIMIT_LOCATARIO", "AVALUO", "IMPUESTO_DEPARTAMENTAL",
    ];

    /// <summary>Lista blanca de filtros con sus valores (contrato-api §2).</summary>
    private static readonly FrozenDictionary<string, FrozenSet<string>> ListaBlanca = new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
    {
        ["modalidad"] = new[] { "EXPRESS", "MASIVO" }.ToFrozenSet(StringComparer.Ordinal),
        ["tipo_analisis"] = new[] { "SIMPLE", "CON_ANALISIS" }.ToFrozenSet(StringComparer.Ordinal),
        ["veredicto"] = new[] { "VIABLE", "BLOQUEADO", "SIN_DICTAMEN" }.ToFrozenSet(StringComparer.Ordinal),
        ["fuente"] = Fuentes.ToFrozenSet(StringComparer.Ordinal),
        ["estado"] = Estados.ToFrozenSet(StringComparer.Ordinal),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Valida la solicitud y agrega la fila al contexto, sin guardar. Devuelve la fila (su id ya está asignado).</summary>
    public AccesoReporte Agregar(SolicitudAccesoReporte solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var empresa = usuario.EmpresaPropia();
        if (usuario.Id == Guid.Empty)
            throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: el acceso al reporte no se puede registrar.");
        var filtros = Validar(solicitud);

        var fila = new AccesoReporte
        {
            TenantId = empresa,
            UsuarioId = usuario.Id,
            Alcance = solicitud.Alcance,
            TipoReporte = solicitud.Tipo,
            Formato = solicitud.Formato,
            Desde = solicitud.Desde,
            Hasta = solicitud.Hasta,
            Filtros = filtros,
            EmpresaConsultada = solicitud.EmpresaConsultada,
            Filas = solicitud.Filas,
            Empresas = solicitud.Empresas,
        };
        db.Add(fila);
        return fila;
    }

    /// <summary>Agrega y guarda la fila; si no se puede guardar, lanza (la ruta no entrega datos). Devuelve el id (<c>acceso_id</c>).</summary>
    public async Task<Guid> RegistrarAsync(SolicitudAccesoReporte solicitud, CancellationToken ct)
    {
        var fila = Agregar(solicitud);
        var tipo = TiposDeReporte.Codigo(solicitud.Tipo);
        var formato = CodigosDeEnum.Codigo(solicitud.Formato);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // SP-9: el tipo de la excepción, nunca su Message (puede traer valores de la fila o de la base).
            LogNoRegistrado(logger, tipo, formato, ex.GetType().Name);
            throw;
        }

        LogRegistrado(logger, tipo, formato, solicitud.Filas, solicitud.Empresas, fila.Id);
        return fila.Id;
    }

    /// <summary>El jsonb de <c>filtros</c>: claves ordenadas, valores sin repetir en el orden dado, y la placa como bandera.</summary>
    internal static string FiltrosJson(IReadOnlyDictionary<string, IReadOnlyList<string>> filtros)
    {
        ArgumentNullException.ThrowIfNull(filtros);
        var salida = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (clave, valores) in filtros)
        {
            if (valores is null || valores.Count == 0)
                continue;
            if (clave == FiltroPlaca)
            {
                salida[PlacaFiltrada] = true;
                continue;
            }

            if (!ListaBlanca.TryGetValue(clave, out var permitidos))
                throw new ArgumentException("Filtro fuera de la lista blanca de reportes (contrato-api §2).", nameof(filtros));
            if (valores.Any(v => v is null || !permitidos.Contains(v)))
                throw new ArgumentException($"Valor fuera de la lista del filtro {clave}.", nameof(filtros));
            salida[clave] = valores.Distinct(StringComparer.Ordinal).ToArray();
        }

        return JsonSerializer.Serialize(salida);
    }

    private static string Validar(SolicitudAccesoReporte solicitud)
    {
        if (!Enum.IsDefined(solicitud.Alcance) || !Enum.IsDefined(solicitud.Tipo) || !Enum.IsDefined(solicitud.Formato))
            throw new ArgumentException("Alcance, tipo o formato fuera del enum.", nameof(solicitud));
        var conEmpresa = solicitud.EmpresaConsultada is { } e && e != Guid.Empty;
        if ((solicitud.Alcance == AlcanceReporte.BackofficeEmpresa) != conEmpresa)
            throw new ArgumentException("La empresa consultada va solo, y siempre, en BACKOFFICE_EMPRESA.", nameof(solicitud));
        if (solicitud.Tipo == TipoReporte.Consumo && solicitud.Alcance == AlcanceReporte.Compania)
            throw new ArgumentException("El reporte de consumo es solo del backoffice.", nameof(solicitud));
        if (solicitud.Filas < 0 || solicitud.Empresas < 0)
            throw new ArgumentException("Filas y empresas no pueden ser negativas.", nameof(solicitud));
        return FiltrosJson(solicitud.Filtros);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Acceso a reporte registrado: {TipoReporte} {Formato}, {Filas} filas, {Empresas} empresas, registro {AccesoId}.")]
    private static partial void LogRegistrado(ILogger logger, string tipoReporte, string formato, int filas, int empresas, Guid accesoId);

    [LoggerMessage(Level = LogLevel.Error, Message = "No se pudo registrar el acceso al reporte {TipoReporte} {Formato} ({Excepcion}): no se entregan datos.")]
    private static partial void LogNoRegistrado(ILogger logger, string tipoReporte, string formato, string excepcion);
}
