using System.Text.Json.Serialization;

namespace Flit.Diagnostico.Api.Modulos.Estadisticas;

/// <summary>
/// Respuesta de E1–E3 (HU #14579, BE-1, contrato-api §1, OpenAPI <c>EstadisticasDto</c>). Solo conteos, sumas y porcentajes: sin
/// placa, VIN, documento ni usuario en ninguna profundidad (SP-16). Cohorte = lotes con <c>creado_en</c> en el rango y visibles para
/// quien consulta; saneamientos por <c>solicitado_en</c> y derivaciones por <c>creada_en</c> (criterio S1).
/// </summary>
public sealed record EstadisticasDto(
    [property: JsonPropertyName("ambito")] AmbitoEstadisticasDto Ambito,
    [property: JsonPropertyName("periodo")] PeriodoEstadisticasDto Periodo,
    [property: JsonPropertyName("generado_en")] DateTimeOffset GeneradoEn,
    [property: JsonPropertyName("resumen")] ResumenEstadisticasDto Resumen,
    [property: JsonPropertyName("serie")] IReadOnlyList<CubetaEstadisticasDto> Serie,
    [property: JsonPropertyName("veredictos")] VeredictosDto Veredictos,
    [property: JsonPropertyName("tipos_analisis")] TiposAnalisisDto TiposAnalisis,
    [property: JsonPropertyName("fuentes")] IReadOnlyList<FuenteEstadisticasDto> Fuentes,
    [property: JsonPropertyName("saneamientos")] IReadOnlyList<EstadoCantidadDto> Saneamientos,
    [property: JsonPropertyName("derivaciones")] DerivacionesEstadisticasDto Derivaciones,
    [property: JsonPropertyName("top_empresas")] IReadOnlyList<TopEmpresaDto> TopEmpresas);

/// <summary><c>EMPRESA</c> (E1, E3) con id y nombre, o <c>GLOBAL</c> (E2) con los dos en null.</summary>
public sealed record AmbitoEstadisticasDto(
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("empresa_id")] Guid? EmpresaId,
    [property: JsonPropertyName("empresa_nombre")] string? EmpresaNombre);

/// <summary>Rango pedido (AAAA-MM-DD) y granularidad de la serie (<c>DIA</c>, <c>SEMANA</c> o <c>MES</c>).</summary>
public sealed record PeriodoEstadisticasDto(
    [property: JsonPropertyName("desde")] DateOnly Desde,
    [property: JsonPropertyName("hasta")] DateOnly Hasta,
    [property: JsonPropertyName("granularidad")] string Granularidad);

/// <summary>Totales de la cohorte (contrato-api §1, tabla <c>resumen</c>).</summary>
public sealed record ResumenEstadisticasDto(
    [property: JsonPropertyName("diagnosticos")] int Diagnosticos,
    [property: JsonPropertyName("diagnosticos_express")] int DiagnosticosExpress,
    [property: JsonPropertyName("diagnosticos_masivos")] int DiagnosticosMasivos,
    [property: JsonPropertyName("vehiculos")] int Vehiculos,
    [property: JsonPropertyName("vehiculos_unicos")] int VehiculosUnicos,
    [property: JsonPropertyName("consultas")] int Consultas,
    [property: JsonPropertyName("consultas_exitosas")] int ConsultasExitosas,
    [property: JsonPropertyName("consultas_con_error")] int ConsultasConError,
    [property: JsonPropertyName("consultas_no_aplica")] int ConsultasNoAplica,
    [property: JsonPropertyName("consultas_pendientes")] int ConsultasPendientes,
    [property: JsonPropertyName("tasa_exito")] double? TasaExito,
    [property: JsonPropertyName("tiempo_promedio_consulta_ms")] long? TiempoPromedioConsultaMs,
    [property: JsonPropertyName("saneamientos_solicitados")] int SaneamientosSolicitados,
    [property: JsonPropertyName("tramites_creados")] int TramitesCreados);

/// <summary>Una cubeta de la serie; <see cref="Inicio"/> es la fecha de la cubeta.</summary>
public sealed record CubetaEstadisticasDto(
    [property: JsonPropertyName("inicio")] DateOnly Inicio,
    [property: JsonPropertyName("diagnosticos")] int Diagnosticos,
    [property: JsonPropertyName("express")] int Express,
    [property: JsonPropertyName("masivos")] int Masivos,
    [property: JsonPropertyName("vehiculos")] int Vehiculos);

/// <summary>Dictamen vigente de los vehículos de la cohorte.</summary>
public sealed record VeredictosDto(
    [property: JsonPropertyName("viables")] int Viables,
    [property: JsonPropertyName("bloqueados")] int Bloqueados,
    [property: JsonPropertyName("sin_dictamen")] int SinDictamen);

/// <summary>Lotes de la cohorte por tipo de análisis.</summary>
public sealed record TiposAnalisisDto(
    [property: JsonPropertyName("simple")] int Simple,
    [property: JsonPropertyName("con_analisis")] int ConAnalisis);

/// <summary>Una fuente con al menos una celda en la cohorte; el tiempo excluye los intentos MANUAL.</summary>
public sealed record FuenteEstadisticasDto(
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("consultas")] int Consultas,
    [property: JsonPropertyName("exitosas")] int Exitosas,
    [property: JsonPropertyName("con_error")] int ConError,
    [property: JsonPropertyName("no_aplica")] int NoAplica,
    [property: JsonPropertyName("pendientes")] int Pendientes,
    [property: JsonPropertyName("tasa_exito")] double? TasaExito,
    [property: JsonPropertyName("tiempo_promedio_ms")] long? TiempoPromedioMs);

/// <summary>Conteo de un estado de saneamiento.</summary>
public sealed record EstadoCantidadDto(
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("cantidad")] int Cantidad);

/// <summary>Derivaciones de las operaciones del rango; <see cref="EnCurso"/> = ítems PENDIENTE o ENVIADO.</summary>
public sealed record DerivacionesEstadisticasDto(
    [property: JsonPropertyName("creadas")] int Creadas,
    [property: JsonPropertyName("omitidas")] int Omitidas,
    [property: JsonPropertyName("fallidas")] int Fallidas,
    [property: JsonPropertyName("en_curso")] int EnCurso);

/// <summary>Una compañía del top de E2: id y nombre de Identidad (nunca null), diagnósticos y vehículos del rango.</summary>
public sealed record TopEmpresaDto(
    [property: JsonPropertyName("empresa_id")] Guid EmpresaId,
    [property: JsonPropertyName("empresa_nombre")] string EmpresaNombre,
    [property: JsonPropertyName("diagnosticos")] int Diagnosticos,
    [property: JsonPropertyName("vehiculos")] int Vehiculos);
