using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Estimacion;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Carga;

/// <summary>Lo que el análisis toma de la cabecera: su alcance y, de la copia de la configuración, el límite y si muestra el estimado.</summary>
internal sealed record ParametrosAnalisis(IReadOnlyList<Fuente> Alcance, int LimiteVehiculosLote, bool MostrarEstimado);

/// <summary>
/// Análisis local del archivo de carga (HU #13749, F02-H4, F02-C4/C7/C8/C10/C11/C13): sin estado, sin base y sin fuentes
/// externas. Por fila (<see cref="ValidadorFilaVehiculo"/>, extraída al dominio en HU #13751): <see cref="ValidadorIdentificador"/> (la regla de Trámites, HU #13740) y, aparte, la regla de la
/// carga para entradas demasiado largas (decisión del usuario 2026-10-21): si el identificador limpio (mayúsculas, solo
/// A-Z y 0-9, antes de recortar) pasa de 8 caracteres y el validador lo tomó como placa, o de 17 y lo tomó como VIN, la fila
/// es errónea; nunca se acepta el valor recortado. Después, la forma del documento del locatario
/// (<see cref="ValidadorDocumentoPersona"/>). Los identificadores válidos repetidos se consolidan en la primera fila y se
/// cuentan como duplicados. Avisa sin válidos y si los válidos superan el límite del lote (en ambos casos no se puede
/// continuar). El estimado (<see cref="EstimadorConsultas"/>, HU #13747) solo si la configuración lo muestra antes de
/// lanzar. El documento del locatario sale siempre enmascarado.
/// </summary>
/// <example>
/// <code>
/// var resumen = AnalizadorCarga.Analizar(FormatoCarga.Leer("carga.csv", bytes), new ParametrosAnalisis([Fuente.Runt], 500, false));
/// // resumen.TotalFilas == resumen.Validos + resumen.Erroneos + resumen.Duplicados
/// </code>
/// </example>
internal static class AnalizadorCarga
{
    public const string CodigoIdentificadorRequerido = ValidadorFilaVehiculo.CodigoIdentificadorRequerido;
    public const string CodigoIdentificadorInvalido = ValidadorFilaVehiculo.CodigoIdentificadorInvalido;
    public const string CodigoIdentificadorDemasiadoLargo = ValidadorFilaVehiculo.CodigoIdentificadorDemasiadoLargo;
    public const string CodigoSinValidos = "SIN_VALIDOS";
    public const string CodigoLimiteSuperado = "LIMITE_SUPERADO";
    public const string CodigoColumnaRequerida = "COLUMNA_REQUERIDA";

    public static ResumenValidacionDto Analizar(TablaCarga tabla, ParametrosAnalisis parametros)
    {
        ArgumentNullException.ThrowIfNull(tabla);
        ArgumentNullException.ThrowIfNull(parametros);
        var (columnaIdentificador, columnaTipo, columnaDocumento) = Columnas(tabla);

        var filas = new List<FilaEnCurso>();
        var porIdentificador = new Dictionary<string, FilaEnCurso>(StringComparer.Ordinal);
        var duplicados = 0;
        foreach (var leida in tabla.Filas)
        {
            var fila = Analizar(leida, columnaIdentificador, columnaTipo, columnaDocumento);
            if (fila.Valido is not null && porIdentificador.TryGetValue(fila.Dto.IdentificadorNorm, out var primera))
            {
                primera.Duplicadas.Add(leida.Numero);
                duplicados++;
                continue;
            }

            if (fila.Valido is not null)
                porIdentificador[fila.Dto.IdentificadorNorm] = fila;
            filas.Add(fila);
        }

        var resultado = filas.Select(f => f.Dto with { FilasDuplicadas = f.Duplicadas }).ToList();
        var validos = resultado.Count(f => f.Valida);
        var superaLimite = validos > parametros.LimiteVehiculosLote;
        var avisos = new List<AvisoCargaDto>();
        if (validos == 0)
            avisos.Add(new AvisoCargaDto(CodigoSinValidos, "El archivo no tiene ningún registro válido: corrija los errores o suba otro archivo para continuar."));
        if (superaLimite)
        {
            avisos.Add(new AvisoCargaDto(CodigoLimiteSuperado,
                $"El archivo tiene {validos} vehículos válidos y el límite por lote es {parametros.LimiteVehiculosLote}: divida el archivo o retire vehículos para continuar."));
        }

        var estimado = parametros.MostrarEstimado
            ? EstimadorConsultas.Estimar(parametros.Alcance, filas.Where(f => f.Dto.Valida).Select(f => new VehiculoAEstimar(true, f.ConDocumento)))
                .Select(e => new EstimadoFuenteDto(CodigosDeEnum.Codigo(e.Fuente), e.Consultas)).ToList()
            : null;

        return new ResumenValidacionDto(
            tabla.Filas.Count, validos, resultado.Count - validos, duplicados, PuedeContinuar: validos > 0 && !superaLimite,
            parametros.LimiteVehiculosLote, superaLimite, avisos, resultado, estimado);
    }

    /// <summary>
    /// Posición de las columnas de la plantilla en el encabezado (sin distinguir mayúsculas ni espacios de los extremos); sin
    /// <see cref="PlantillaCarga.ColumnaIdentificador"/> es 422 <see cref="CodigoColumnaRequerida"/>. La usan el análisis y la
    /// confirmación del lote desde el archivo (HU #13751).
    /// </summary>
    internal static (int Identificador, int? Tipo, int? Documento) Columnas(TablaCarga tabla)
    {
        ArgumentNullException.ThrowIfNull(tabla);
        var identificador = Indice(tabla.Encabezados, PlantillaCarga.ColumnaIdentificador)
            ?? throw new Plataforma.ReglaException(CodigoColumnaRequerida,
                $"El archivo no tiene la columna {PlantillaCarga.ColumnaIdentificador} en la primera fila: use la plantilla.")
            {
                Campo = PlantillaCarga.ColumnaIdentificador,
            };
        return (identificador, Indice(tabla.Encabezados, PlantillaCarga.ColumnaTipoDocumento), Indice(tabla.Encabezados, PlantillaCarga.ColumnaDocumento));
    }

    /// <summary>Celda de una columna de la fila; null si la columna no existe o la fila es más corta.</summary>
    internal static string? Celda(FilaLeida fila, int? columna) =>
        columna is { } c && c < fila.Celdas.Count ? fila.Celdas[c] : null;

    /// <summary>Documento de persona enmascarado (como Comparendos, RN-011): <c>****</c> y los 4 últimos caracteres.</summary>
    public static string Enmascarar(string numero)
    {
        ArgumentNullException.ThrowIfNull(numero);
        return numero.Length <= 4 ? "****" : $"****{numero[^4..]}";
    }

    private static FilaEnCurso Analizar(FilaLeida leida, int columnaIdentificador, int? columnaTipo, int? columnaDocumento)
    {
        var original = Celda(leida, columnaIdentificador) ?? string.Empty;
        var tipoCrudo = Celda(leida, columnaTipo);
        var documentoCrudo = Celda(leida, columnaDocumento);

        // La regla de la fila es la del dominio (HU #13751): la misma de validar-filas, la confirmación y la captura Express.
        var fila = ValidadorFilaVehiculo.Validar(original, tipoCrudo, documentoCrudo);
        var documento = fila.Documento;
        var errores = fila.Errores.Select(e => new ErrorFilaDto(Columna(e.Campo), e.Codigo, e.Mensaje)).ToList();
        var numeroVisible = documento.Numero ?? (string.IsNullOrWhiteSpace(documentoCrudo) ? null : documentoCrudo.Trim());
        var tipoVisible = documento.Tipo ?? (string.IsNullOrWhiteSpace(tipoCrudo) ? null : tipoCrudo.Trim().ToUpperInvariant());
        var tipo = fila.Tipo;
        var dto = new FilaResumenDto(
            leida.Numero, original, fila.Normalizado, tipo is { } c ? CodigosDeEnum.Codigo(c) : null, tipoVisible,
            numeroVisible is null ? null : Enmascarar(numeroVisible), fila.EsValida, [], errores);
        return new FilaEnCurso(dto, tipo is null ? null : fila.Normalizado, documento.Presente);
    }

    /// <summary>Columna de la plantilla de cada campo de la fila.</summary>
    private static string Columna(CampoFilaVehiculo campo) => campo switch
    {
        CampoFilaVehiculo.TipoDocumento => PlantillaCarga.ColumnaTipoDocumento,
        CampoFilaVehiculo.Documento => PlantillaCarga.ColumnaDocumento,
        _ => PlantillaCarga.ColumnaIdentificador,
    };

    /// <summary>Columna por nombre, sin distinguir mayúsculas ni espacios de los extremos; null si no está.</summary>
    private static int? Indice(IReadOnlyList<string?> encabezados, string nombre)
    {
        for (var i = 0; i < encabezados.Count; i++)
        {
            if (string.Equals(encabezados[i]?.Trim(), nombre, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return null;
    }

    /// <summary>Fila en análisis: <see cref="Valido"/> es el identificador normalizado si es placa o VIN (clave de duplicados).</summary>
    private sealed record FilaEnCurso(FilaResumenDto Dto, string? Valido, bool ConDocumento)
    {
        public List<int> Duplicadas { get; } = [];
    }
}
