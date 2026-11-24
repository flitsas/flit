using Flit.Queries.Domain;
using FluentAssertions;
using Xunit;

namespace Flit.Queries.Core.Tests;

/// <summary>
/// La semántica que dos productos deben compartir: si divergiera, dos informes del mismo cliente se contradirían
/// sin que nada falle con un error.
/// </summary>
public sealed class MotorDeConsultasTests
{
    private sealed record Fila(string Numero, string Placa, string Estado, DateTimeOffset? Fecha);

    private sealed class Catalogo(string? rowNoun = null) : IQueryFieldCatalog
    {
        private static readonly string[] Ops =
            [QueryOperator.EsAlguno, QueryOperator.NoEsNinguno, QueryOperator.Contiene, QueryOperator.EstaVacio];

        public IReadOnlyList<QueryFieldDto> Fields { get; } =
        [
            new("numero", "No. comparendo", QueryFieldKind.Texto, "Identificación", Ops, [], null, true),
            new("placa", "Placa", QueryFieldKind.Texto, "Identificación", Ops, [], null, true),
            new("estado", "Estado", QueryFieldKind.Opcion, "Estado", Ops, [], null, true),
        ];

        public IReadOnlyList<QueryFieldOptionDto> DateFields { get; } = [new("imposicion", "Fecha de imposición")];

        public string DefaultDateField => "imposicion";

        public IReadOnlyList<string> SortFields { get; } = ["fecha"];

        public string DefaultSort => "fecha";

        public string Universo => "su empresa";

        public string RowNoun => rowNoun ?? "trámite";

        public bool IsIdentifier(string fieldId) => fieldId is "numero" or "placa";
    }

    private static readonly DateTimeOffset Desde = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Hasta = new(2026, 1, 31, 23, 59, 59, TimeSpan.Zero);

    private static QueryEngine<Fila> Motor(IQueryFieldCatalog catalogo) => new(
        catalogo,
        campo => campo switch
        {
            "numero" => f => [f.Numero],
            "placa" => f => [f.Placa],
            "estado" => f => [f.Estado],
            _ => _ => [],
        },
        (f, _) => f.Fecha);

    // ---- Rangos: «últimos 30 días» significa lo mismo en todos los productos ----

    [Fact]
    public void Ultimos30_IncluyeHoyYVeintinueveDiasAtras()
    {
        var hoy = new DateOnly(2026, 11, 24);
        QueryRangePreset.Resolve(new QueryDateFilter("x", QueryRangePreset.Ultimos30), hoy)
            .Should().Be((new DateOnly(2026, 10, 26), hoy));
    }

    [Theory]
    [InlineData(QueryRangePreset.Ultimos365, -364, 0)]
    [InlineData(QueryRangePreset.Proximos30, 0, 29)]
    [InlineData(QueryRangePreset.Proximos90, 0, 89)]
    public void PresetsDeReportes2_SonRelativosAHoy(string preset, int desde, int hasta)
    {
        var hoy = new DateOnly(2026, 11, 24);
        QueryRangePreset.IsKnown(preset).Should().BeTrue();
        QueryRangePreset.Resolve(new QueryDateFilter("x", preset), hoy).Should().Be((hoy.AddDays(desde), hoy.AddDays(hasta)));
    }

    [Fact]
    public void MesAnterior_EnEnero_EsDiciembreCompletoDelAnioAnterior()
    {
        QueryRangePreset.Resolve(new QueryDateFilter("x", QueryRangePreset.MesAnterior), new DateOnly(2026, 1, 15))
            .Should().Be((new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void Personalizado_ConFechasAlReves_LasOrdena()
    {
        // V1 mandaba el rango invertido y el reporte contaba todas las fechas: aquí se corrige, no se vacía.
        var filtro = new QueryDateFilter("x", QueryRangePreset.Personalizado, new DateOnly(2026, 3, 31), new DateOnly(2026, 3, 1));
        QueryRangePreset.Resolve(filtro, new DateOnly(2026, 11, 24))
            .Should().Be((new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));
    }

    [Fact]
    public void PresetDesconocido_CaeEnUltimos30()
    {
        var hoy = new DateOnly(2026, 11, 24);
        QueryRangePreset.Resolve(new QueryDateFilter("x", "inventado"), hoy)
            .Should().Be((hoy.AddDays(-29), hoy));
    }

    // ---- Normalización: una placa pegada desde Excel se encuentra igual ----

    [Theory]
    [InlineData("bdj-456", "BDJ456")]
    [InlineData(" ics 187 ", "ICS187")]
    [InlineData("1.234.567", "1234567")]
    public void Identificador_SeComparaSinSeparadoresNiMayusculas(string pegado, string guardado)
    {
        var predicado = Motor(new Catalogo()).BuildPredicate(new QueryCondition("placa", QueryOperator.EsAlguno, [pegado]));
        predicado(new Fila("1", guardado, "A", Desde)).Should().BeTrue();
    }

    [Fact]
    public void CampoNoIdentificador_ConservaLosSeparadores()
    {
        var predicado = Motor(new Catalogo()).BuildPredicate(new QueryCondition("estado", QueryOperator.EsAlguno, ["en-tiempo"]));
        predicado(new Fila("1", "X", "ENTIEMPO", Desde)).Should().BeFalse();
        predicado(new Fila("1", "X", "En-Tiempo", Desde)).Should().BeTrue();
    }

    [Fact]
    public void NoEsNinguno_YEstaVacio_HacenLoQueDicen()
    {
        var motor = Motor(new Catalogo());
        var fila = new Fila("1", "ABC123", "", Desde);

        motor.BuildPredicate(new QueryCondition("placa", QueryOperator.NoEsNinguno, ["abc-123"]))(fila).Should().BeFalse();
        motor.BuildPredicate(new QueryCondition("estado", QueryOperator.EstaVacio, []))(fila).Should().BeTrue();
        motor.BuildPredicate(new QueryCondition("placa", QueryOperator.Contiene, ["bc1"]))(fila).Should().BeTrue();
    }

    [Fact]
    public void Normalizer_DescartaOperadorQueElCampoNoAdmite_YRecortaValores()
    {
        var catalogo = new Catalogo();
        QueryNormalizer.Normalize(catalogo, new QueryCondition("placa", "mayor_que", ["1"])).Should().BeNull();
        QueryNormalizer.Normalize(catalogo, new QueryCondition("placa", QueryOperator.EsAlguno, [" a ", "A", ""]))!
            .Values.Should().Equal("a");
    }

    [Fact]
    public void Normalizer_SinFechaConocida_UsaLaDelCatalogoYUltimos30()
    {
        var def = QueryNormalizer.Normalize(new Catalogo(), new QueryDefinition(new QueryDateFilter("otra", "x"), [], []));
        def.Fechas.Campo.Should().Be("imposicion");
        def.Fechas.Preset.Should().Be(QueryRangePreset.Ultimos30);
    }

    // ---- Cobertura: explicar por qué falta lo que se pidió por nombre ----

    private static IReadOnlyList<QueryCoverageItemDto> Cobertura(
        IQueryFieldCatalog catalogo, IReadOnlyCollection<Fila> universo, params QueryCondition[] condiciones)
    {
        var motor = Motor(catalogo);
        var def = new QueryDefinition(new QueryDateFilter("imposicion", QueryRangePreset.Personalizado), condiciones, []);
        var conPredicado = condiciones.Select(c => (c, motor.BuildPredicate(c))).ToList();
        var matched = universo
            .Where(f => motor.InRange(f, "imposicion", Desde, Hasta) && conPredicado.All(p => p.Item2(f)))
            .ToList();
        return motor.BuildCoverage(def, universo, matched, conPredicado, Desde, Hasta);
    }

    [Fact]
    public void Cobertura_DistingueEncontradoNoExisteFueraDeRangoYFiltroCulpable()
    {
        Fila[] universo =
        [
            new("C1", "AAA111", "ACTIVO", Desde.AddDays(3)),
            new("C2", "BBB222", "ACTIVO", Desde.AddDays(-40)),
            new("C3", "CCC333", "INACTIVO", Desde.AddDays(5)),
        ];

        var items = Cobertura(
            new Catalogo("comparendo"),
            universo,
            new QueryCondition("numero", QueryOperator.EsAlguno, ["c1", "c2", "c3", "c9"]),
            new QueryCondition("estado", QueryOperator.EsAlguno, ["ACTIVO"]));

        items.Select(i => (i.Valor, i.Resultado)).Should().Equal(
            ("c1", QueryCoverageResult.Encontrado),
            ("c2", QueryCoverageResult.Excluido),
            ("c3", QueryCoverageResult.Excluido),
            ("c9", QueryCoverageResult.NoExiste));

        items[1].Motivo.Should().Be("Existe, pero su fecha de imposición queda fuera del rango.");
        items[2].Motivo.Should().Be("Existe, pero lo dejó fuera el filtro «Estado».");
        items[3].Motivo.Should().Be("No hay ningún comparendo con este valor en su empresa.");
    }

    [Fact]
    public void Cobertura_SoloRindeCuentasDeIdentificadores()
    {
        Cobertura(new Catalogo(), [new("C1", "A", "ACTIVO", Desde)],
                new QueryCondition("estado", QueryOperator.EsAlguno, ["INACTIVO"]))
            .Should().BeEmpty();
    }

    // ---- Trámites no cambia: sin declarar el sustantivo, el motor habla de «trámite» como antes ----

    [Fact]
    public void SinRowNoun_LosTextosSiguenHablandoDeTramites()
    {
        var items = Cobertura(new Catalogo(), [], new QueryCondition("numero", QueryOperator.EsAlguno, ["X"]));
        items.Single().Motivo.Should().Be("No hay ningún trámite con este valor en su empresa.");

        new SuperAdminQueryTooBroadException(25_000, 20_000).Message.Should().Be(
            // El número sale con la cultura del proceso (comportamiento previo a la extracción, no se cambia aquí).
            $"Esta consulta trae unos {25_000:N0} trámites, más de lo que se puede traer de una vez (máximo {20_000:N0}). "
            + "Acótala eligiendo una o varias compañías, o un rango de fecha más corto.");
    }

    [Fact]
    public void ConsultaDemasiadoAmplia_UsaElSustantivoYLaFormaDeAcotarDelModulo()
    {
        new SuperAdminQueryTooBroadException(48_936, 20_000, "comparendos", "un NIT o un rango de fecha más corto")
            .Message.Should().Contain($"{48_936:N0} comparendos").And.EndWith("Acótala eligiendo un NIT o un rango de fecha más corto.");
    }
}
