using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Domain.Vehiculos;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// Punto 5 (ajustes 2026-11-11) — diccionario único de rótulos de negocio (<see cref="RotulosDiagnostico"/>). Cubre el AC5 (cada
/// código de cada enum o catálogo tiene rótulo), la tabla aprobada de alertas (decisión 5.2), el AC4 (código sin rótulo →
/// «Novedad sin clasificar» y log técnico sin PII) y los campos <c>*_etiqueta</c> que la API suma junto a cada código visible.
/// </summary>
/// <example>
/// <code>
/// RotulosDiagnostico.DeAlerta("SOAT_NO_VIGENTE", "RUNT");            // "SOAT no vigente"
/// RotulosDiagnostico.DeAlerta("FUENTE_PENDIENTE", "RUNT_PROPIETARIO"); // "Consulta pendiente: RUNT propietarios"
/// RotulosDiagnostico.DeEstadoCelda("CODIGO_NUEVO");                  // "Novedad sin clasificar" (y deja log)
/// </code>
/// </example>
public sealed class RotulosDiagnosticoTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // ---- AC5: cobertura de todos los códigos ----

    [Fact]
    public void Cobertura_cada_regla_del_catalogo_v1_y_v2_tiene_rotulo_de_alerta()
    {
        var codigos = CatalogoReglasDictamen.Historico.Select(f => f.Codigo).Distinct().ToList();
        codigos.Should().Contain(["SIMIT_SIN_VERIFICAR", "SOAT_MAQUINARIA", "FUENTE_PENDIENTE"]);
        SinRotulo(RotulosDiagnostico.Alertas, codigos).Should().BeEmpty();
    }

    [Fact]
    public void Cobertura_cada_fuente_tiene_rotulo() =>
        SinRotulo(RotulosDiagnostico.Fuentes, Enum.GetValues<Fuente>().Select(CodigosDeEnum.Codigo)).Should().BeEmpty();

    [Fact]
    public void Cobertura_cada_estado_de_lote_tiene_rotulo() =>
        SinRotulo(RotulosDiagnostico.EstadosLote, Enum.GetValues<EstadoDiagnostico>().Select(CodigosDeEnum.Codigo)).Should().BeEmpty();

    [Fact]
    public void Cobertura_cada_estado_de_celda_tiene_rotulo() =>
        SinRotulo(RotulosDiagnostico.EstadosCelda, Enum.GetValues<EstadoCelda>().Select(CodigosDeEnum.Codigo)).Should().BeEmpty();

    [Fact]
    public void Cobertura_cada_tipo_de_identificador_tiene_rotulo_incluido_motocarro()
    {
        SinRotulo(RotulosDiagnostico.TiposIdentificador, Enum.GetValues<TipoIdentificador>().Select(CodigosDeEnum.Codigo)).Should().BeEmpty();
        RotulosDiagnostico.DeTipoIdentificador("PLACA_MOTOCARRO").Should().Be("Placa de motocarro");
    }

    [Fact]
    public void Cobertura_cada_motivo_na_tiene_rotulo() =>
        SinRotulo(RotulosDiagnostico.MotivosNa, Enum.GetValues<MotivoNa>().Select(CodigosDeEnum.Codigo)).Should().BeEmpty();

    [Fact]
    public void Cobertura_cada_tipo_de_error_conocido_tiene_rotulo()
    {
        var codigos = ClasificadorErrores.Codigos.Concat(["BLOQUEADA_POR_RUNT", "PERSONAS_CON_ERROR", "SIN_IDENTIFICADOR", "FUENTE_NO_DISPONIBLE"]);
        SinRotulo(RotulosDiagnostico.TiposError, codigos).Should().BeEmpty();
    }

    [Fact]
    public void Cobertura_cada_tipo_de_tramite_y_familia_del_catalogo_tiene_rotulo()
    {
        SinRotulo(RotulosDiagnostico.TiposTramite, CatalogoReglasDictamen.TiposTramite).Should().BeEmpty();
        SinRotulo(RotulosDiagnostico.Familias, [ResolucionFuentesObligatorias.FamiliaTraspaso]).Should().BeEmpty();
    }

    [Fact]
    public void Cobertura_cada_veredicto_tiene_rotulo() =>
        SinRotulo(RotulosDiagnostico.Veredictos, ["VIABLE", "BLOQUEADO"]).Should().BeEmpty();

    // ---- Decisión 5.2: tabla aprobada ----

    [Theory]
    [InlineData("SOAT_NO_VIGENTE", "SOAT no vigente")]
    [InlineData("RTM_NO_VIGENTE", "Revisión técnico-mecánica no vigente")]
    [InlineData("ESTADO_VEHICULO", "Vehículo no activo en el RUNT")]
    [InlineData("SIMIT_VEHICULO", "Multas del vehículo")]
    [InlineData("SIMIT_PROPIETARIO", "Multas del propietario")]
    [InlineData("SIMIT_LOCATARIO", "Multas del locatario")]
    [InlineData("SIMIT_SIN_VERIFICAR", "No se pudo verificar multas")]
    [InlineData("SIMIT_LICENCIA", "Licencia suspendida o cancelada (SIMIT)")]
    [InlineData("LICENCIA_CONDUCTOR", "Licencia de conducción con novedad")]
    [InlineData("SIMIT_ACUERDO_PAGO", "Acuerdo de pago vigente")]
    [InlineData("LIMITACION", "Limitación a la propiedad")]
    [InlineData("GRAVAMEN", "Gravamen registrado")]
    [InlineData("GARANTIA_MOBILIARIA", "Garantía mobiliaria registrada")]
    [InlineData("PRENDA", "Prenda registrada")]
    [InlineData("INFRACCION_RUNT", "Infracciones registradas en el RUNT")]
    [InlineData("IMPUESTO", "Impuesto vehicular pendiente")]
    [InlineData("SOAT_MAQUINARIA", "SOAT de maquinaria no vigente")]
    [InlineData("AJUSTE_FLIT", "Ajuste FLIT")] // Cierre BE: alertamiento redactado por soporte (B7); antes salía «Novedad sin clasificar»
    public void Tabla_aprobada_de_rotulos_de_alertas(string codigo, string rotulo) =>
        RotulosDiagnostico.DeAlerta(codigo, "RUNT").Should().Be(rotulo);

    [Fact]
    public void Fuente_pendiente_lleva_el_nombre_de_la_fuente()
    {
        RotulosDiagnostico.DeAlerta("FUENTE_PENDIENTE", "RUNT_PROPIETARIO").Should().Be("Consulta pendiente: RUNT propietarios");
        RotulosDiagnostico.DeAlerta("FUENTE_PENDIENTE", "IMPUESTO_DEPARTAMENTAL").Should().Be("Consulta pendiente: Impuesto departamental");
    }

    // ---- AC4: fallback + log técnico ----

    [Fact]
    public void Codigo_sin_rotulo_da_novedad_sin_clasificar_y_deja_log_sin_el_valor_crudo_en_pantalla()
    {
        var log = new LogEnMemoria();
        var anterior = RotulosDiagnostico.Log;
        RotulosDiagnostico.Log = log;
        try
        {
            RotulosDiagnostico.DeAlerta("CODIGO_P5_DESCONOCIDO", "RUNT").Should().Be(RotulosDiagnostico.SinClasificar);
            RotulosDiagnostico.DeEstadoCelda("ESTADO_P5_DESCONOCIDO").Should().Be("Novedad sin clasificar");
            RotulosDiagnostico.DeFuente("FUENTE_P5_DESCONOCIDA").Should().Be("Novedad sin clasificar");
        }
        finally
        {
            RotulosDiagnostico.Log = anterior;
        }

        log.Mensajes.Should().Contain(m => m.Contains("CODIGO_P5_DESCONOCIDO") && m.Contains("alerta"));
        log.Mensajes.Should().Contain(m => m.Contains("ESTADO_P5_DESCONOCIDO") && m.Contains("estado_celda"));
        log.Mensajes.Should().Contain(m => m.Contains("FUENTE_P5_DESCONOCIDA") && m.Contains("fuente"));
        log.Niveles.Should().OnlyContain(n => n == LogLevel.Warning);
    }

    [Fact]
    public void Estado_de_fuente_externa_conocido_se_traduce_y_el_desconocido_pasa_tal_cual()
    {
        RotulosDiagnostico.DeEstadoFuente("NO VIGENTE").Should().Be("No vigente");
        RotulosDiagnostico.DeEstadoFuente(" vigente ").Should().Be("Vigente");
        RotulosDiagnostico.DeEstadoFuente("PENDIENTE").Should().Be("Pendiente");
        RotulosDiagnostico.DeEstadoFuente("ACUERDO DE PAGO").Should().Be("Acuerdo de pago");
        RotulosDiagnostico.DeEstadoFuente("EN_TRAMITE").Should().Be("En trámite");
        RotulosDiagnostico.DeEstadoFuente("Texto del organismo").Should().Be("Texto del organismo");
    }

    // ---- API: *_etiqueta junto al código ----

    [Fact]
    public void Celda_de_la_grilla_lleva_estado_etiqueta_y_motivo_na_etiqueta()
    {
        var na = Json(new CeldaGrillaDto(Guid.CreateVersion7(), "SIMIT_LOCATARIO", "NA", "SIN_DOCUMENTO_LOCATARIO", null, null, false, 1));
        na["estado"]!.GetValue<string>().Should().Be("NA");
        na["estado_etiqueta"]!.GetValue<string>().Should().Be("No aplica");
        na["motivo_na"]!.GetValue<string>().Should().Be("SIN_DOCUMENTO_LOCATARIO");
        na["motivo_na_etiqueta"]!.GetValue<string>().Should().Be("El vehículo está sin el documento del locatario: no hay a quién consultar.");

        var curso = Json(new CeldaGrillaDto(Guid.CreateVersion7(), "RUNT", "EN_CURSO", null, null, null, false, 2));
        curso["estado_etiqueta"]!.GetValue<string>().Should().Be("En curso");
        curso.ContainsKey("motivo_na_etiqueta").Should().BeTrue();
        curso["motivo_na_etiqueta"].Should().BeNull();
    }

    [Fact]
    public void Fila_y_grilla_llevan_tipo_identificador_etiqueta_y_estado_etiqueta()
    {
        var fila = new FilaVehiculoDto(Guid.CreateVersion7(), 1, "123ABC", "PLACA_MOTOCARRO", "123ABC", null, false, [], null, [], false);
        Json(fila)["tipo_identificador_etiqueta"]!.GetValue<string>().Should().Be("Placa de motocarro");

        var grilla = new GrillaDto(Guid.CreateVersion7(), "Lote", "CREADO", 0, 0, new AlcanceDto([], [], []), new AvanceLoteDto(1, 0), [fila], null, 0, []);
        Json(grilla)["estado_etiqueta"]!.GetValue<string>().Should().Be("Sin lanzar");
    }

    [Fact]
    public void Listado_de_diagnosticos_lleva_estado_etiqueta()
    {
        var item = new DiagnosticoListadoDto(Guid.CreateVersion7(), "Lote", DateTimeOffset.UnixEpoch, "SIMPLE", null, "EN_PROCESO",
            new UsuarioCreadorDto(Guid.CreateVersion7(), "Ana Prueba"), false);
        Json(item)["estado_etiqueta"]!.GetValue<string>().Should().Be("En proceso");
    }

    [Fact]
    public void Grilla_y_lista_del_backoffice_llevan_las_mismas_etiquetas()
    {
        var celda = Json(new CeldaGrillaBackofficeDto(Guid.CreateVersion7(), "IMPUESTO_DEPARTAMENTAL", "NA", "SERVICIO_PUBLICO", null, null, false, 1, null, null));
        celda["estado_etiqueta"]!.GetValue<string>().Should().Be("No aplica");
        celda["motivo_na_etiqueta"]!.GetValue<string>().Should().Be("El vehículo es de servicio público: esta consulta no aplica.");

        var fila = Json(new FilaVehiculoBackofficeDto(Guid.CreateVersion7(), 1, "ABC123", "PLACA_CARRO", "ABC123", null, true, [], null, []));
        fila["tipo_identificador_etiqueta"]!.GetValue<string>().Should().Be("Placa de carro");

        var lote = Json(new DiagnosticoBackofficeDto(Guid.CreateVersion7(), "Lote", "INCOMPLETO", DateTimeOffset.UnixEpoch, 1, 1));
        lote["estado_etiqueta"]!.GetValue<string>().Should().Be("Incompleto");

        var grilla = Json(new GrillaBackofficeDto(Guid.CreateVersion7(), Guid.CreateVersion7(), "Lote", "COMPLETADO", 0, 0,
            new AlcanceDto([], [], []), new AvanceLoteDto(0, 0), [], null));
        grilla["estado_etiqueta"]!.GetValue<string>().Should().Be("Completado");
    }

    [Fact]
    public void Definicion_de_fuentes_obligatorias_lleva_tipo_tramite_etiqueta_y_familia_etiqueta()
    {
        var porTipo = Json(FuentesObligatoriasBackofficeEndpoints.Definicion(null, "TRASPASO_UNILATERAL", [Fuente.RuntPropietario],
            new Dictionary<string, string>(StringComparer.Ordinal)));
        porTipo["tipo_tramite_etiqueta"]!.GetValue<string>().Should().Be("Traspaso unilateral");
        porTipo["familia_etiqueta"].Should().BeNull();
        porTipo["tipo_tramite_codigo"]!.GetValue<string>().Should().Be("TRASPASO_UNILATERAL");

        var publicado = Json(FuentesObligatoriasBackofficeEndpoints.Definicion(null, "TRASPASO_STANDARD", [],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["TRASPASO_STANDARD"] = "Traspaso estándar" }));
        publicado["tipo_tramite_etiqueta"]!.GetValue<string>().Should().Be("Traspaso estándar", "manda el nombre que publica Trámites");

        var porFamilia = Json(FuentesObligatoriasBackofficeEndpoints.Definicion("TRASPASO", null, [Fuente.SimitPropietario],
            new Dictionary<string, string>(StringComparer.Ordinal)));
        porFamilia["familia_etiqueta"]!.GetValue<string>().Should().Be("Traspaso");
        porFamilia["tipo_tramite_etiqueta"].Should().BeNull();
    }

    // ---- AC6: errores de la API en lenguaje de negocio ----

    [Fact]
    public void Los_rechazos_de_fuentes_obligatorias_no_nombran_codigos()
    {
        foreach (var codigo in new[]
                 {
                     RechazoGuardadoFuentes.DefinicionAmbigua, RechazoGuardadoFuentes.FuenteRuntNoConfigurable,
                     RechazoGuardadoFuentes.SimitProhibidoEnUnilateral, RechazoGuardadoFuentes.PisoNoDispensable,
                 })
        {
            var mensaje = FuentesObligatoriasBackofficeEndpoints.Mensaje(codigo);
            mensaje.Should().NotMatchRegex("[A-Z]{3,}_[A-Z_]+", $"el mensaje de {codigo} se lee en lenguaje de negocio");
        }
    }

    /// <summary>Nombres propios que se leen igual que su código (la fuente RUNT, el VIN); cualquier otro rótulo igual al código falla.</summary>
    private static readonly HashSet<string> NombresPropios = new(StringComparer.Ordinal) { "RUNT", "VIN" };

    private static List<string> SinRotulo(IReadOnlyDictionary<string, string> diccionario, IEnumerable<string> codigos) =>
        [.. codigos.Where(c => !diccionario.TryGetValue(c, out var r) || string.IsNullOrWhiteSpace(r) || (r == c && !NombresPropios.Contains(c)))];

    private static JsonObject Json(object dto) => JsonSerializer.SerializeToNode(dto, dto.GetType(), Web)!.AsObject();

    private sealed class LogEnMemoria : ILogger
    {
        private readonly List<(LogLevel Nivel, string Mensaje)> entradas = [];

        public IReadOnlyList<string> Mensajes
        {
            get
            {
                lock (entradas)
                    return [.. entradas.Select(e => e.Mensaje)];
            }
        }

        public IReadOnlyList<LogLevel> Niveles
        {
            get
            {
                lock (entradas)
                    return [.. entradas.Select(e => e.Nivel)];
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (entradas)
                entradas.Add((logLevel, formatter(state, exception)));
        }
    }
}
