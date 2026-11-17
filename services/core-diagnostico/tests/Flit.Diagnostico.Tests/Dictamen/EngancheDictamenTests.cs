using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Dictamen;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// Enganche del dictamen al vehículo terminado (corte 5). La función del flanco, el pedido, los alertamientos y la
/// versión no abren Postgres: el cierre anidado no se simula con base.
/// </summary>
public sealed class EngancheDictamenTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 3, 30, 0, TimeSpan.Zero);

    private static readonly LecturasParaDictamen SinLecturas = new(null, null, null, null, null, null, null, null);

    [Fact]
    public void No_emite_sin_celdas_ni_si_ya_estaba_terminado_ni_si_queda_una_fuera()
    {
        EmisorDictamen.FlancoDeSubida([]).Should().BeFalse();
        EmisorDictamen.FlancoDeSubida(EstadoCelda.Completado, EstadoCelda.Completado, []).Should().BeFalse();
        EmisorDictamen.FlancoDeSubida(EstadoCelda.Na, EstadoCelda.Na, [EstadoCelda.Completado]).Should().BeFalse();
        EmisorDictamen.FlancoDeSubida(EstadoCelda.Pendiente, EstadoCelda.Completado, [EstadoCelda.Error]).Should().BeFalse();
        EmisorDictamen.FlancoDeSubida(EstadoCelda.EnCurso, EstadoCelda.Error, [EstadoCelda.Completado, EstadoCelda.Na]).Should().BeFalse();
    }

    [Fact]
    public void Emite_cuando_este_cierre_es_el_que_lo_termina()
    {
        // El «antes» es el estado anterior de esta celda más el actual de las hermanas. El «después» solo cambia esta.
        EmisorDictamen.FlancoDeSubida(EstadoCelda.EnCurso, EstadoCelda.Completado, [EstadoCelda.Completado, EstadoCelda.Na]).Should().BeTrue();
        EmisorDictamen.FlancoDeSubida(EstadoCelda.Pendiente, EstadoCelda.Na, [EstadoCelda.Completado]).Should().BeTrue();
        EmisorDictamen.FlancoDeSubida(EstadoCelda.Error, EstadoCelda.Completado, []).Should().BeTrue();
    }

    [Fact]
    public void Simple_tipo_null_o_fuera_de_la_familia_no_arman_pedido()
    {
        EmisorDictamen.ArmarPedido(ModoDiagnostico.Simple, "TRASPASO_STANDARD", Ahora, SinLecturas).Should().BeNull();
        EmisorDictamen.ArmarPedido(ModoDiagnostico.ConAnalisis, null, Ahora, SinLecturas).Should().BeNull();
        EmisorDictamen.ArmarPedido(ModoDiagnostico.ConAnalisis, "   ", Ahora, SinLecturas).Should().BeNull();
        EmisorDictamen.ArmarPedido(ModoDiagnostico.ConAnalisis, "MATRICULA_INICIAL", Ahora, SinLecturas).Should().BeNull();
    }

    [Fact]
    public void El_pedido_trae_el_tipo_la_fecha_de_Colombia_y_la_celda_que_corresponde()
    {
        var lecturas = new LecturasParaDictamen(
            new LecturaCelda<DetalleRuntReporte>(EstadoCelda.Completado, null),
            new LecturaCelda<VehiculoRunt>(EstadoCelda.Completado, Vehiculo()),
            new LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>(EstadoCelda.Na, null),
            null,
            null,
            null,
            null,
            null);

        var pedido = EmisorDictamen.ArmarPedido(ModoDiagnostico.ConAnalisis, "TRASPASO_UNILATERAL", Ahora, lecturas);

        pedido.Should().NotBeNull();
        pedido!.TipoTramiteCodigo.Should().Be("TRASPASO_UNILATERAL");
        pedido.Hoy.Should().Be(new DateOnly(2026, 11, 3), "03:30 UTC es las 22:30 del día anterior en Colombia");
        pedido.RuntDetalle!.Estado.Should().Be(EstadoCelda.Completado);
        pedido.RuntDetalle.Lectura.Should().BeNull("sin cifra legible la lectura queda null y el estado se conserva");
        pedido.RuntNormalizado!.Lectura.Should().BeSameAs(lecturas.RuntNormalizado!.Lectura);
        pedido.SimitVehiculo!.Estado.Should().Be(EstadoCelda.Na);
        pedido.Impuesto.Should().BeNull();
    }

    [Fact]
    public void Alertamientos_copian_la_plantilla_o_el_texto_del_modelo_y_sin_reglas_el_veredicto_es_viable()
    {
        var tipo = "TRASPASO_STANDARD";
        var reglas = new ReglaEvaluada[]
        {
            new("SOAT_NO_VIGENTE", "RUNT", "BLOQUEA", null),
            new("IMPUESTO", "IMPUESTO_DEPARTAMENTAL", "ADVIERTE", 150_000),
        };
        var bloqueado = new DictamenEvaluado("BLOQUEADO", reglas);
        var pendiente = EmisorDictamen.AlertamientosJson(tipo, bloqueado, "PENDIENTE", "no se copia");
        var completada = EmisorDictamen.AlertamientosJson(tipo, bloqueado, "COMPLETADA", "El modelo explicó el bloqueo.");

        Claves(pendiente).Should().Equal("SOAT_NO_VIGENTE", "IMPUESTO");
        Texto(pendiente, 0).Should().Be(Plantilla(tipo, "SOAT_NO_VIGENTE"));
        Texto(pendiente, 1).Should().Be(Plantilla(tipo, "IMPUESTO"));
        Efecto(pendiente, 1).Should().Be("ADVIERTE");
        Fuente(pendiente, 1).Should().Be("IMPUESTO_DEPARTAMENTAL");
        Texto(completada, 0).Should().Be("El modelo explicó el bloqueo.");
        Texto(completada, 1).Should().Be("El modelo explicó el bloqueo.");

        var vacio = new DictamenEvaluado("VIABLE", []);
        var fila = EmisorDictamen.Materializar(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, tipo, vacio,
            new ResultadoRedaccion("Redacción IA pendiente.", "PLANTILLA", "PENDIENTE", 0, "no-configurado", null), Ahora);

        fila.Veredicto.Should().Be("VIABLE");
        fila.Alertamientos.Should().Be("[]");
        fila.CatalogoVersion.Should().Be(CatalogoReglasDictamen.Version);
        fila.AnalisisOverride.Should().BeFalse();
        fila.Vigente.Should().BeTrue();
    }

    [Fact]
    public void La_version_nace_en_1_y_la_siguiente_apaga_la_vigente()
    {
        var primera = EmisorDictamen.SiguienteVersion(null);
        primera.Anterior.Should().BeNull();
        primera.Nueva.Should().Be(new VersionEmitida(1, true));

        var segunda = EmisorDictamen.SiguienteVersion(3);
        segunda.Anterior.Should().Be(new VersionEmitida(3, false));
        segunda.Nueva.Should().Be(new VersionEmitida(4, true));
    }

    [Fact]
    public void El_pedido_al_redactor_lleva_la_suspension_y_el_bien_sin_documentos_ni_nombres()
    {
        var bien = EmisorDictamen.BienDe(Detalle(), placaVehiculo: "OTRA1", servicioVehiculo: "Publico", identificadorNorm: "VIN123");
        var pedido = EmisorDictamen.PedidoAlRedactor(
            new DictamenEvaluado("VIABLE", [new ReglaEvaluada("ESTADO_VEHICULO", "RUNT", "ADVIERTE", null)]), bien, redaccionSuspendida: true);

        pedido.RedaccionSuspendida.Should().BeTrue();
        pedido.Bien.Should().Be(new DatosBien("ABC123", "AUTOMOVIL", "CHEVROLET", "ONIX", "2020", "Particular"));
        typeof(DatosBien).GetProperties().Select(p => p.Name).Should().Equal("Placa", "Clase", "Marca", "Linea", "Modelo", "Servicio");

        var sinDetalle = EmisorDictamen.BienDe(null, null, "Particular", "VIN123");
        sinDetalle.Should().Be(new DatosBien("VIN123", null, null, null, null, "Particular"));

        var fallo = new ResultadoRedaccion("Redacción IA pendiente.", "PLANTILLA", "PENDIENTE", 1, "claude", TipoFalloRedactor.Timeout, TimeSpan.FromSeconds(15));
        EmisorDictamen.DisponibleDesde(fallo, Ahora).Should().Be(Ahora.AddSeconds(15));
        var sinEspera = fallo with { TipoFallo = TipoFalloRedactor.Red, ReintentarEn = null };
        EmisorDictamen.DisponibleDesde(sinEspera, Ahora).Should().Be(Ahora);
        var suspension = fallo with { TipoFallo = null, ReintentarEn = TimeSpan.FromMinutes(5) };
        EmisorDictamen.DisponibleDesde(suspension, Ahora).Should().BeNull();
    }

    private static VehiculoRunt Vehiculo() =>
        new(null, null, null, null, null, [], [], "INTEMPO", null);

    private static DetalleRuntReporte Detalle() => new(
        new DetalleRuntVehiculo("ABC123", null, "Particular", null, "AUTOMOVIL", "2020", null, null),
        new DetalleRuntGeneral("CHEVROLET", "ONIX", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, "ACTIVO"),
        new DetalleRuntTecnico(null, null, null, null),
        [],
        [],
        [],
        [],
        [],
        []);

    private static string Plantilla(string tipo, string codigo) =>
        CatalogoReglasDictamen.Filas.Single(f => f.TipoTramiteCodigo == tipo && f.Codigo == codigo).PlantillaTexto;

    private static JsonElement Item(string json, int indice)
    {
        using var documento = JsonDocument.Parse(json);
        var item = documento.RootElement[indice];
        item.EnumerateObject().Select(p => p.Name).Should().Equal("codigo", "texto", "efecto", "fuente");
        return item.Clone();
    }

    private static string[] Claves(string json)
    {
        using var documento = JsonDocument.Parse(json);
        return documento.RootElement.EnumerateArray().Select(e => e.GetProperty("codigo").GetString()!).ToArray();
    }

    private static string? Texto(string json, int indice) => Item(json, indice).GetProperty("texto").GetString();

    private static string? Efecto(string json, int indice) => Item(json, indice).GetProperty("efecto").GetString();

    private static string? Fuente(string json, int indice) => Item(json, indice).GetProperty("fuente").GetString();
}
