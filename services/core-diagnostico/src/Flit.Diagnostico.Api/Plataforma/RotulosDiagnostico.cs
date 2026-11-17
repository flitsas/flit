using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Diccionario único código → rótulo de negocio del servicio (ajustes 2026-11-11, punto 5, decisión 5.1 = a). Lo leen la API
/// (los campos <c>*_etiqueta</c> que viajan junto a cada código), el PDF, el Excel y el índice del ZIP: un rótulo se cambia aquí
/// y cambia en todas las superficies. Los códigos se siguen guardando y viajando igual; esto es solo presentación.
/// <para>
/// Un código propio (regla, fuente, estado, tipo de identificador, motivo, tipo de error, trámite) sin rótulo devuelve
/// <see cref="SinClasificar"/> y deja un aviso en el log técnico con la categoría y el código (nunca datos de personas ni
/// del vehículo). Los estados que entregan las fuentes externas (<see cref="DeEstadoFuente"/>) son texto del organismo: el
/// conocido se pasa a tipo oración y el resto se muestra tal cual, sin log.
/// </para>
/// </summary>
/// <example>
/// <code>
/// RotulosDiagnostico.DeAlerta("SIMIT_PROPIETARIO", "SIMIT_PROPIETARIO"); // "Multas del propietario"
/// RotulosDiagnostico.DeAlerta("FUENTE_PENDIENTE", "RUNT");               // "Consulta pendiente: RUNT"
/// RotulosDiagnostico.DeEstadoLote("CREADO");                             // "Sin lanzar"
/// RotulosDiagnostico.DeEstadoFuente("NO VIGENTE");                       // "No vigente"
/// </code>
/// </example>
internal static partial class RotulosDiagnostico
{
    /// <summary>Texto neutro de un código que todavía no tiene rótulo (AC4). Nunca se muestra el código.</summary>
    public const string SinClasificar = "Novedad sin clasificar";

    /// <summary>Estado del vehículo en el Excel y el índice del ZIP: todas sus fuentes completas o N/A.</summary>
    public const string VehiculoTerminado = "Terminado";

    /// <summary>Estado del vehículo en el Excel y el índice del ZIP: alguna fuente sigue pendiente.</summary>
    public const string VehiculoEnProceso = "En proceso";

    /// <summary>Estado del vehículo en el Excel y el índice del ZIP: alguna fuente quedó en error.</summary>
    public const string VehiculoConError = "Con error";

    /// <summary>Aviso del dictamen en el PDF mientras el texto definitivo no está (decisión 5.2: no nombra la IA).</summary>
    public const string AnalisisEnPreparacion = "Análisis en preparación";

    private const string PrefijoFuentePendiente = "Consulta pendiente";

    private static ILogger _log = NullLogger.Instance;

    /// <summary>
    /// Log técnico de los códigos sin rótulo. <see cref="Program"/> lo conecta al arrancar (<see cref="Conectar"/>) y lo suelta al
    /// detenerse; sin conectar, no escribe.
    /// </summary>
    public static ILogger Log
    {
        get => Volatile.Read(ref _log);
        set => Volatile.Write(ref _log, value ?? NullLogger.Instance);
    }

    /// <summary>
    /// Conecta <paramref name="logger"/> y devuelve con qué soltarlo: al detenerse el host vuelve a <see cref="NullLogger"/> solo si
    /// el conectado sigue siendo el suyo (otro host del mismo proceso, p. ej. otra API de prueba, pudo conectar el propio después).
    /// Sin esto el estático queda apuntando a la fábrica de logs de un host ya cerrado.
    /// </summary>
    public static Action Conectar(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Log = logger;
        return () => Interlocked.CompareExchange(ref _log, NullLogger.Instance, logger);
    }

    /// <summary>Rótulo corto de cada regla del dictamen, v1 y v2 (decisión 5.2; el texto largo sigue siendo la plantilla del catálogo).</summary>
    public static IReadOnlyDictionary<string, string> Alertas { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["SOAT_NO_VIGENTE"] = "SOAT no vigente",
        ["SOAT_MAQUINARIA"] = "SOAT de maquinaria no vigente",
        ["RTM_NO_VIGENTE"] = "Revisión técnico-mecánica no vigente",
        ["ESTADO_VEHICULO"] = "Vehículo no activo en el RUNT",
        ["SIMIT_VEHICULO"] = "Multas del vehículo",
        ["SIMIT_PROPIETARIO"] = "Multas del propietario",
        ["SIMIT_LOCATARIO"] = "Multas del locatario",
        ["SIMIT_SIN_VERIFICAR"] = "No se pudo verificar multas",
        ["SIMIT_LICENCIA"] = "Licencia suspendida o cancelada (SIMIT)",
        ["SIMIT_ACUERDO_PAGO"] = "Acuerdo de pago vigente",
        ["LICENCIA_CONDUCTOR"] = "Licencia de conducción con novedad",
        ["LIMITACION"] = "Limitación a la propiedad",
        ["GRAVAMEN"] = "Gravamen registrado",
        ["GARANTIA_MOBILIARIA"] = "Garantía mobiliaria registrada",
        ["PRENDA"] = "Prenda registrada",
        ["INFRACCION_RUNT"] = "Infracciones registradas en el RUNT",
        ["IMPUESTO"] = "Impuesto vehicular pendiente",
        ["FUENTE_PENDIENTE"] = PrefijoFuentePendiente,
        // Alertamiento redactado por soporte en un ajuste FLIT (HU #14563 B7); no es una regla del catálogo.
        ["AJUSTE_FLIT"] = "Ajuste FLIT",
    };

    /// <summary>Nombre de cada fuente de consulta (pantalla, encabezados del Excel, hoja «Consultas»).</summary>
    public static IReadOnlyDictionary<string, string> Fuentes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RUNT"] = "RUNT",
        ["RUNT_PROPIETARIO"] = "RUNT propietarios",
        ["RUNT_LOCATARIO"] = "RUNT locatarios",
        ["SIMIT_VEHICULO"] = "SIMIT vehículo",
        ["SIMIT_PROPIETARIO"] = "SIMIT propietarios",
        ["SIMIT_LOCATARIO"] = "SIMIT locatarios",
        ["AVALUO"] = "Avalúo",
        ["IMPUESTO_DEPARTAMENTAL"] = "Impuesto departamental",
    };

    /// <summary>
    /// Título de la sección de cada fuente en el PDF (sin el prefijo «RUNT —», «SIMIT —»; el PDF lo pinta en mayúsculas). Una
    /// fuente sin título cae a <see cref="DeFuente"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> TitulosSeccion { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RUNT"] = "Información del vehículo",
        ["RUNT_PROPIETARIO"] = "Propietarios",
        ["RUNT_LOCATARIO"] = "Locatarios",
        ["SIMIT_VEHICULO"] = "Comparendos del vehículo",
        ["SIMIT_PROPIETARIO"] = "Comparendos de los propietarios",
        ["SIMIT_LOCATARIO"] = "Comparendos de los locatarios",
        ["AVALUO"] = "Avalúo comercial",
        ["IMPUESTO_DEPARTAMENTAL"] = "Impuesto vehicular departamental",
    };

    /// <summary>Estado del lote (<see cref="EstadoDiagnostico"/>), los mismos del chip del listado.</summary>
    public static IReadOnlyDictionary<string, string> EstadosLote { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CREADO"] = "Sin lanzar",
        ["EN_PROCESO"] = "En proceso",
        ["COMPLETADO"] = "Completado",
        ["INCOMPLETO"] = "Incompleto",
    };

    /// <summary>Estado de una celda del semáforo (<see cref="EstadoCelda"/>).</summary>
    public static IReadOnlyDictionary<string, string> EstadosCelda { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PENDIENTE"] = "Pendiente",
        ["EN_CURSO"] = "En curso",
        ["COMPLETADO"] = "Completado",
        ["ERROR"] = "Error",
        ["NA"] = "No aplica",
    };

    /// <summary>Clase del identificador del vehículo (<see cref="TipoIdentificador"/>).</summary>
    public static IReadOnlyDictionary<string, string> TiposIdentificador { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PLACA_CARRO"] = "Placa de carro",
        ["PLACA_MOTO"] = "Placa de moto",
        ["PLACA_REMOLQUE"] = "Placa de remolque",
        ["PLACA_MAQUINARIA"] = "Placa de maquinaria",
        ["PLACA_MOTOCARRO"] = "Placa de motocarro",
        ["VIN"] = "VIN",
    };

    /// <summary>Motivo de una celda N/A (<see cref="MotivoNa"/>): el mismo texto del detalle de celda y de la leyenda del PDF.</summary>
    public static IReadOnlyDictionary<string, string> MotivosNa { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["SERVICIO_PUBLICO"] = "El vehículo es de servicio público: esta consulta no aplica.",
        ["DEPARTAMENTO_NO_SOPORTADO"] = "El departamento del vehículo todavía no está soportado.",
        ["SIN_DOCUMENTO_LOCATARIO"] = "El vehículo está sin el documento del locatario: no hay a quién consultar.",
    };

    /// <summary>Tipo de error de una celda o persona (los de <see cref="ClasificadorErrores"/> y los propios del orquestador).</summary>
    public static IReadOnlyDictionary<string, string> TiposError { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TIMEOUT"] = "La fuente no respondió a tiempo.",
        ["RED"] = "No hubo conexión con la fuente.",
        ["HTTP_5XX"] = "La fuente no estaba disponible.",
        ["FUENTE_NO_DISPONIBLE"] = "La fuente no estaba disponible.",
        ["RESPUESTA_INVALIDA"] = "La fuente respondió con datos que no se pudieron leer.",
        ["ESTADO_NO_OK"] = "La fuente rechazó la consulta.",
        ["AUTENTICACION"] = "El proveedor rechazó las credenciales de FLIT.",
        ["LIMITE_PROVEEDOR"] = "El proveedor limitó las consultas por ahora.",
        ["NO_CONECTADO"] = "Esta fuente no está conectada en este ambiente.",
        ["PERSONAS_CON_ERROR"] = "Algunas personas no se pudieron consultar.",
        ["BLOQUEADA_POR_RUNT"] = "Depende del RUNT del vehículo, que también falló.",
        ["SIN_PLACA"] = "El vehículo no tiene placa según el RUNT.",
        ["SIN_IDENTIFICADOR"] = "El vehículo no tiene ni placa ni VIN según el RUNT.",
        ["NO_CONFIRMADO"] = "El impuesto no quedó confirmado.",
        ["MODELO_NO_ACEPTADO"] = "El modelo del vehículo no es aceptado para consultar el impuesto.",
        ["DOCUMENTO_NO_COINCIDE"] = "El documento del propietario no coincide con el del impuesto.",
        ["DOCUMENTO_NO_HOMOLOGADO"] = "El tipo de documento del propietario no está homologado para el impuesto.",
        ["VEHICULO_NO_ENCONTRADO"] = "El vehículo no aparece en el impuesto.",
        ["VEHICULO_NO_CONSULTABLE"] = "El vehículo no se puede consultar en el impuesto.",
        ["REQUIERE_ACTUALIZACION"] = "El impuesto pide actualizar los datos del vehículo antes de consultar.",
        ["SIN_DOCUMENTO"] = "Falta el documento del propietario para consultar el impuesto.",
        ["SIN_MODELO"] = "Falta el modelo del vehículo para consultar el impuesto.",
        ["ORGANISMO_INCOMPLETO"] = "El organismo de tránsito no tiene municipio para consultar el impuesto.",
        ["SIN_NOMBRE_PROPIETARIO"] = "Falta el nombre del propietario para consultar el impuesto.",
    };

    /// <summary>
    /// Respaldo del nombre de los tipos de trámite del catálogo del dictamen. Manda el nombre que publica Trámites
    /// (<c>ListarTiposTramite</c>, decisión 5.3); este se usa si el catálogo no responde o el tipo ya no está publicado.
    /// </summary>
    public static IReadOnlyDictionary<string, string> TiposTramite { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TRASPASO_STANDARD"] = "Traspaso",
        ["TRASPASO_UNILATERAL"] = "Traspaso unilateral",
        ["TRASPASO_TRANSFERENCIA_DE_DOMINIO"] = "Transferencia de dominio",
    };

    /// <summary>Familias de trámite de las fuentes obligatorias.</summary>
    public static IReadOnlyDictionary<string, string> Familias { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TRASPASO"] = "Traspaso",
    };

    /// <summary>Veredicto del dictamen en palabra (el color no lo sustituye).</summary>
    public static IReadOnlyDictionary<string, string> Veredictos { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["VIABLE"] = "Viable",
        ["BLOQUEADO"] = "Bloqueado",
    };

    /// <summary>Nombre de cada fuente del avalúo (PDF y Excel del lote).</summary>
    public static IReadOnlyDictionary<string, string> FuentesAvaluo { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fasecolda"] = "Fasecolda",
        ["mercado_libre"] = "Mercado Libre",
        ["base_gravable"] = "Base gravable",
    };

    /// <summary>
    /// Estados que entregan las fuentes en mayúsculas (SOAT, RTM, pólizas, solicitudes, garantías, licencias, comparendos, acuerdos),
    /// con la clave normalizada (mayúsculas, guion bajo como espacio). Solo presentación del PDF.
    /// </summary>
    public static IReadOnlyDictionary<string, string> EstadosFuente { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["VIGENTE"] = "Vigente",
        ["NO VIGENTE"] = "No vigente",
        ["NO REQUIERE"] = "No requiere",
        ["VENCIDO"] = "Vencido",
        ["VENCIDA"] = "Vencida",
        ["ACTIVO"] = "Activo",
        ["ACTIVA"] = "Activa",
        ["INACTIVO"] = "Inactivo",
        ["INACTIVA"] = "Inactiva",
        ["SUSPENDIDO"] = "Suspendido",
        ["SUSPENDIDA"] = "Suspendida",
        ["CANCELADO"] = "Cancelado",
        ["CANCELADA"] = "Cancelada",
        ["PENDIENTE"] = "Pendiente",
        ["PAGADO"] = "Pagado",
        ["PAGADA"] = "Pagada",
        ["INSCRITO"] = "Inscrito",
        ["INSCRITA"] = "Inscrita",
        ["REGISTRADO"] = "Registrado",
        ["REGISTRADA"] = "Registrada",
        ["APROBADO"] = "Aprobado",
        ["APROBADA"] = "Aprobada",
        ["RECHAZADO"] = "Rechazado",
        ["RECHAZADA"] = "Rechazada",
        ["AUTORIZADO"] = "Autorizado",
        ["AUTORIZADA"] = "Autorizada",
        ["EN TRAMITE"] = "En trámite",
        ["EN TRÁMITE"] = "En trámite",
        ["FINALIZADO"] = "Finalizado",
        ["FINALIZADA"] = "Finalizada",
        ["ACUERDO DE PAGO"] = "Acuerdo de pago",
        ["AL DIA"] = "Al día",
        ["AL DÍA"] = "Al día",
        ["CUMPLIDO"] = "Cumplido",
        ["INCUMPLIDO"] = "Incumplido",
        ["COBRO COACTIVO"] = "Cobro coactivo",
        ["LEVANTADA"] = "Levantada",
        ["LEVANTADO"] = "Levantado",
    };

    /// <summary>Rótulo de la regla; <c>FUENTE_PENDIENTE</c> suma el nombre de la fuente que falta.</summary>
    public static string DeAlerta(string? codigo, string? fuente)
    {
        var rotulo = Buscar(Alertas, "alerta", codigo);
        if (codigo != "FUENTE_PENDIENTE")
            return rotulo;
        return fuente is not null && Fuentes.TryGetValue(fuente, out var nombre)
            ? $"{PrefijoFuentePendiente}: {nombre}"
            : Desconocido("fuente", fuente, PrefijoFuentePendiente);
    }

    public static string DeFuente(string? codigo) => Buscar(Fuentes, "fuente", codigo);

    public static string DeFuente(Fuente fuente) => DeFuente(CodigosDeEnum.Codigo(fuente));

    /// <summary>Título de la sección del PDF; sin título propio, el nombre de la fuente (nunca el código).</summary>
    public static string DeTituloSeccion(string? codigo) =>
        codigo is not null && TitulosSeccion.TryGetValue(codigo, out var titulo) ? titulo : DeFuente(codigo);

    public static string DeEstadoLote(string? codigo) => Buscar(EstadosLote, "estado_lote", codigo);

    public static string DeEstadoLote(EstadoDiagnostico estado) => DeEstadoLote(CodigosDeEnum.Codigo(estado));

    public static string DeEstadoCelda(string? codigo) => Buscar(EstadosCelda, "estado_celda", codigo);

    public static string DeTipoIdentificador(string? codigo) => Buscar(TiposIdentificador, "tipo_identificador", codigo);

    /// <summary>Texto del motivo de N/A; null si la celda no tiene motivo.</summary>
    public static string? DeMotivoNa(string? codigo) => codigo is null ? null : Buscar(MotivosNa, "motivo_na", codigo);

    public static string DeMotivoNa(MotivoNa motivo) => Buscar(MotivosNa, "motivo_na", CodigosDeEnum.Codigo(motivo));

    public static string DeTipoError(string? codigo) => Buscar(TiposError, "tipo_error", codigo);

    /// <summary>
    /// Nombre del tipo de trámite: el publicado por Trámites si viene, si no el respaldo de <see cref="TiposTramite"/>; null si
    /// no hay código.
    /// </summary>
    public static string? DeTipoTramite(string? codigo, string? nombrePublicado = null)
    {
        if (codigo is null)
            return null;
        return string.IsNullOrWhiteSpace(nombrePublicado) ? Buscar(TiposTramite, "tipo_tramite", codigo) : nombrePublicado.Trim();
    }

    /// <summary>Nombre de la familia; null si no hay código.</summary>
    public static string? DeFamilia(string? codigo) => codigo is null ? null : Buscar(Familias, "familia", codigo);

    public static string DeVeredicto(string? codigo) => Buscar(Veredictos, "veredicto", codigo);

    /// <summary>Nombre de la fuente del avalúo; una que no está en la tabla se muestra tal cual (viene del proveedor).</summary>
    public static string DeFuenteAvaluo(string fuente) => FuentesAvaluo.GetValueOrDefault(fuente, fuente);

    /// <summary>
    /// Estado que entregó una fuente externa, en tipo oración si es uno conocido; si no, el texto del organismo tal cual (sin log:
    /// no es un código nuestro). Vacío o null devuelve <see cref="string.Empty"/>; quien pinta decide el «No hay información».
    /// </summary>
    public static string DeEstadoFuente(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;
        var limpio = valor.Trim();
        return EstadosFuente.GetValueOrDefault(limpio.Replace('_', ' ').ToUpperInvariant(), limpio);
    }

    private static string Buscar(IReadOnlyDictionary<string, string> diccionario, string categoria, string? codigo) =>
        codigo is not null && diccionario.TryGetValue(codigo, out var rotulo) ? rotulo : Desconocido(categoria, codigo, SinClasificar);

    private static string Desconocido(string categoria, string? codigo, string generico)
    {
        // El código es un valor de catálogo propio (mayúsculas y guion bajo), no un dato de personas: se registra recortado.
        var seguro = codigo is null ? "(nulo)" : codigo.Length > 64 ? codigo[..64] : codigo;
        try
        {
            SinRotulo(Log, categoria, seguro);
        }
        catch (Exception ex) when (EsLoggerCerrado(ex))
        {
            // El logger de un host que se está cerrando (p. ej. otra API de prueba): el rótulo se devuelve igual. Logger.Log envuelve
            // el fallo del proveedor en AggregateException, así que se mira también dentro.
        }

        return generico;
    }

    private static bool EsLoggerCerrado(Exception ex) => ex switch
    {
        ObjectDisposedException => true,
        AggregateException agregada => agregada.Flatten().InnerExceptions.All(i => i is ObjectDisposedException),
        _ => false,
    };

    [LoggerMessage(EventId = 126850, Level = LogLevel.Warning, Message = "Código sin rótulo de negocio: categoría {Categoria}, código {Codigo}")]
    private static partial void SinRotulo(ILogger logger, string categoria, string codigo);
}
