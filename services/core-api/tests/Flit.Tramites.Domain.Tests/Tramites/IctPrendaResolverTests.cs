using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Tramites;

/// <summary>
/// Bug #13445 (D3) — matriz conservadora de prenda para borradores ICT: cuerpo ICT (<c>ict_prenda_*</c>)
/// contra la señal RUNT (<c>runt_*</c>). Un caso por fila de la matriz, más acreedor distinto, varias
/// garantías, gravamen no prendario y familia OTROS.
/// <para>Uso de ejemplo: <c>IctPrendaResolver.Resolver(fieldValues, ProcedureFamily.Traspaso)</c> →
/// <c>Auto("levantar", acreedor del RUNT)</c>.</para>
/// </summary>
public sealed class IctPrendaResolverTests
{
    // Datos ficticios (nunca de producción).
    private const string DocRunt = "900000001";
    private const string NombreRunt = "BANCO RUNT DE PRUEBA";
    private const string DocCuerpo = "900000002";
    private const string NombreCuerpo = "FINANCIERA CUERPO DE PRUEBA";

    private static ProcedureInstanceFieldValue Fv(string clave, string? texto, string? json = null) =>
        new() { FieldKey = clave, ValueText = texto, ValueJson = json };

    private static string Garantia(string nombre, string documento) =>
        $$"""{"nombreAcreedor":"{{nombre}}","numeroDocumentoAcreedor":"{{documento}}"}""";

    /// <summary>Señal RUNT por nombre: positiva (garantía en el detalle), negativa (NO/NO y []) o desconocida.</summary>
    private static List<ProcedureInstanceFieldValue> SenalRunt(string senal) => senal switch
    {
        "positiva" =>
        [
            Fv(RuntGravamenSignal.PrendasKey, "SI"),
            Fv(RuntGravamenSignal.GravamenesKey, "SI"),
            Fv(RuntGravamenSignal.DetalleKey, null, $"[{Garantia(NombreRunt, DocRunt)}]"),
        ],
        "negativa" =>
        [
            Fv(RuntGravamenSignal.PrendasKey, "NO"),
            Fv(RuntGravamenSignal.GravamenesKey, "NO"),
            Fv(RuntGravamenSignal.DetalleKey, null, "[]"),
        ],
        "desconocida" => [],
        _ => throw new ArgumentOutOfRangeException(nameof(senal)),
    };

    /// <summary>Cuerpo ICT: operación (1/2/3 o null) con el acreedor del cuerpo cuando hay operación.</summary>
    private static List<ProcedureInstanceFieldValue> Cuerpo(string? operacion, string? doc = DocCuerpo)
    {
        if (operacion is null)
            return [];

        var lista = new List<ProcedureInstanceFieldValue>
        {
            Fv(IctPrendaResolver.OperacionKey, operacion),
            Fv(IctPrendaResolver.AcreedorNombreKey, NombreCuerpo),
        };
        if (doc is not null)
            lista.Add(Fv(IctPrendaResolver.AcreedorDocumentoKey, doc));
        return lista;
    }

    private static IctPrendaResolucion Resolver(string senal, string? operacion, ProcedureFamily familia, string? doc = DocCuerpo) =>
        IctPrendaResolver.Resolver([.. SenalRunt(senal), .. Cuerpo(operacion, doc)], familia);

    // ── La matriz, fila por fila ──────────────────────────────────────────────────────────────

    public static TheoryData<string, string?, ProcedureFamily, IctPrendaResolucionTipo, string?, string?, string?> Matriz => new()
    {
        // senal,        operacion, familia,                  tipo esperado,                          decisión,                   motivo,                                                  discrepancia
        { "positiva",    "1",  ProcedureFamily.Traspaso,   IctPrendaResolucionTipo.Auto,            PrendaDecision.Levantar,  null,                                                    IctPrendaResolver.MotivoAcreedorDistinto },
        { "positiva",    "1",  ProcedureFamily.Matriculas, IctPrendaResolucionTipo.PendienteGestor, null,                     IctPrendaResolver.MotivoMatriculaLevantarAtipico,        null },
        { "positiva",    "2",  ProcedureFamily.Traspaso,   IctPrendaResolucionTipo.PendienteGestor, null,                     IctPrendaResolver.MotivoTraspasoInscribirConPrendaRunt,  null },
        { "positiva",    "3",  ProcedureFamily.Traspaso,   IctPrendaResolucionTipo.PendienteGestor, null,                     IctPrendaResolver.MotivoTraspasoSinAccionConPrendaRunt,  null },
        { "positiva",    null, ProcedureFamily.Traspaso,   IctPrendaResolucionTipo.PendienteGestor, null,                     IctPrendaResolver.MotivoTraspasoSinAccionConPrendaRunt,  null },
        { "positiva",    "3",  ProcedureFamily.Matriculas, IctPrendaResolucionTipo.Auto,            PrendaDecision.Omitir,    null,                                                    null },
        { "positiva",    null, ProcedureFamily.Matriculas, IctPrendaResolucionTipo.Auto,            PrendaDecision.Omitir,    null,                                                    null },
        { "negativa",    "1",  ProcedureFamily.Traspaso,   IctPrendaResolucionTipo.Auto,            PrendaDecision.SinPrenda, null,                                                    IctPrendaResolver.MotivoLevantarSinPrendaRunt },
        { "negativa",    "1",  ProcedureFamily.Matriculas, IctPrendaResolucionTipo.Auto,            PrendaDecision.SinPrenda, null,                                                    IctPrendaResolver.MotivoLevantarSinPrendaRunt },
        { "negativa",    "2",  ProcedureFamily.Traspaso,   IctPrendaResolucionTipo.Auto,            PrendaDecision.Registrar, null,                                                    null },
        { "negativa",    "2",  ProcedureFamily.Matriculas, IctPrendaResolucionTipo.Auto,            PrendaDecision.Registrar, null,                                                    null },
        { "negativa",    "3",  ProcedureFamily.Traspaso,   IctPrendaResolucionTipo.Nada,            null,                     null,                                                    null },
        { "negativa",    null, ProcedureFamily.Matriculas, IctPrendaResolucionTipo.Nada,            null,                     null,                                                    null },
        { "desconocida", "1",  ProcedureFamily.Traspaso,   IctPrendaResolucionTipo.PendienteGestor, null,                     IctPrendaResolver.MotivoRuntDesconocido,                 null },
        { "desconocida", "2",  ProcedureFamily.Matriculas, IctPrendaResolucionTipo.PendienteGestor, null,                     IctPrendaResolver.MotivoRuntDesconocido,                 null },
        { "desconocida", "3",  ProcedureFamily.Traspaso,   IctPrendaResolucionTipo.PendienteGestor, null,                     IctPrendaResolver.MotivoRuntDesconocido,                 null },
        { "desconocida", null, ProcedureFamily.Traspaso,   IctPrendaResolucionTipo.Nada,            null,                     null,                                                    null },
        { "positiva",    "1",  ProcedureFamily.Otros,      IctPrendaResolucionTipo.Nada,            null,                     null,                                                    null },
        { "negativa",    "2",  ProcedureFamily.Otros,      IctPrendaResolucionTipo.Nada,            null,                     null,                                                    null },
    };

    [Theory]
    [MemberData(nameof(Matriz))]
    public void Resolver_CumpleLaMatrizConservadora(
        string senal, string? operacion, ProcedureFamily familia,
        IctPrendaResolucionTipo tipo, string? decision, string? motivo, string? discrepancia)
    {
        var r = Resolver(senal, operacion, familia);

        r.Tipo.Should().Be(tipo);
        r.Decision.Should().Be(decision);
        r.Motivo.Should().Be(motivo);
        r.Discrepancia.Should().Be(discrepancia);
    }

    // ── Acreedor ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LevantarEnTraspaso_UsaElAcreedorDelRuntAunqueElCuerpoTraigaOtro()
    {
        var r = Resolver("positiva", "1", ProcedureFamily.Traspaso);

        r.Tipo.Should().Be(IctPrendaResolucionTipo.Auto);
        r.AcreedorNombre.Should().Be(NombreRunt);
        r.AcreedorDocumento.Should().Be(DocRunt);
        r.Discrepancia.Should().Be(IctPrendaResolver.MotivoAcreedorDistinto);
    }

    [Theory]
    [InlineData(DocRunt)]
    [InlineData("900.000.001")]
    [InlineData("900.000.001-5")]
    public void LevantarEnTraspaso_ConElMismoAcreedorNormalizado_NoAvisaDiscrepancia(string docCuerpo)
    {
        // Puntos, guion y dígito de verificación no hacen distinto al acreedor.
        var r = Resolver("positiva", "1", ProcedureFamily.Traspaso, docCuerpo);

        r.Tipo.Should().Be(IctPrendaResolucionTipo.Auto);
        r.Decision.Should().Be(PrendaDecision.Levantar);
        r.Discrepancia.Should().BeNull();
    }

    [Fact]
    public void InscribirEnMatricula_ConElMismoAcreedorQueElRunt_RegistraConElDelCuerpo()
    {
        var r = Resolver("positiva", "2", ProcedureFamily.Matriculas, DocRunt);

        r.Tipo.Should().Be(IctPrendaResolucionTipo.Auto);
        r.Decision.Should().Be(PrendaDecision.Registrar);
        r.AcreedorNombre.Should().Be(NombreCuerpo);
        r.AcreedorDocumento.Should().Be(DocRunt);
    }

    [Fact]
    public void InscribirEnMatricula_ConOtroAcreedorQueElRunt_QuedaParaElGestorConDiscrepancia()
    {
        var r = Resolver("positiva", "2", ProcedureFamily.Matriculas, DocCuerpo);

        r.Tipo.Should().Be(IctPrendaResolucionTipo.PendienteGestor);
        r.Motivo.Should().Be(IctPrendaResolver.MotivoAcreedorDistinto);
        r.Discrepancia.Should().Be(IctPrendaResolver.MotivoAcreedorDistinto);
    }

    [Fact]
    public void InscribirEnMatricula_SinDocumentoEnElCuerpo_QuedaParaElGestor()
    {
        var r = Resolver("positiva", "2", ProcedureFamily.Matriculas, doc: null);

        r.Tipo.Should().Be(IctPrendaResolucionTipo.PendienteGestor);
        r.Motivo.Should().Be(IctPrendaResolver.MotivoAcreedorSinDocumento);
    }

    [Fact]
    public void InscribirConRuntNegativo_RegistraConElAcreedorDelCuerpo()
    {
        var r = Resolver("negativa", "2", ProcedureFamily.Traspaso);

        r.AcreedorNombre.Should().Be(NombreCuerpo);
        r.AcreedorDocumento.Should().Be(DocCuerpo);
    }

    // ── Casos que no se adivinan ──────────────────────────────────────────────────────────────

    [Fact]
    public void LevantarConVariasGarantiasEnElRunt_QuedaParaElGestor()
    {
        List<ProcedureInstanceFieldValue> fv =
        [
            Fv(RuntGravamenSignal.DetalleKey, null,
                $"[{Garantia(NombreRunt, DocRunt)},{Garantia("OTRO BANCO DE PRUEBA", "900000003")}]"),
            .. Cuerpo("1", DocRunt),
        ];

        var r = IctPrendaResolver.Resolver(fv, ProcedureFamily.Traspaso);

        r.Tipo.Should().Be(IctPrendaResolucionTipo.PendienteGestor);
        r.Motivo.Should().Be(IctPrendaResolver.MotivoVariasGarantias);
    }

    [Fact]
    public void GravamenSinPrenda_BanderaDeGravamenesSiYPrendasNo_QuedaParaElGestor()
    {
        // Embargo o medida cautelar: el RUNT reporta gravamen pero no prenda ni garantía.
        List<ProcedureInstanceFieldValue> fv =
        [
            Fv(RuntGravamenSignal.PrendasKey, "NO"),
            Fv(RuntGravamenSignal.GravamenesKey, "SI"),
            Fv(RuntGravamenSignal.DetalleKey, null, "[]"),
            .. Cuerpo("1"),
        ];

        var r = IctPrendaResolver.Resolver(fv, ProcedureFamily.Traspaso);

        r.Tipo.Should().Be(IctPrendaResolucionTipo.PendienteGestor);
        r.Motivo.Should().Be(IctPrendaResolver.MotivoGravamenNoPrendario);
    }

    [Fact]
    public void BanderaIlegible_NoSeTomaComoNegativa()
    {
        // «PENDIENTE» no es ni sí ni no: no se inventa que el vehículo no tiene prenda.
        List<ProcedureInstanceFieldValue> fv =
        [
            Fv(RuntGravamenSignal.PrendasKey, "PENDIENTE"),
            Fv(RuntGravamenSignal.GravamenesKey, "NO"),
            .. Cuerpo("2"),
        ];

        IctPrendaResolver.Resolver(fv, ProcedureFamily.Traspaso).Motivo
            .Should().Be(IctPrendaResolver.MotivoRuntDesconocido);
    }

    [Fact]
    public void OperacionDesconocida_SeTrataComoCuerpoVacio()
    {
        List<ProcedureInstanceFieldValue> fv = [.. SenalRunt("negativa"), Fv(IctPrendaResolver.OperacionKey, "9")];

        IctPrendaResolver.Resolver(fv, ProcedureFamily.Traspaso).Tipo.Should().Be(IctPrendaResolucionTipo.Nada);
    }

    [Fact]
    public void ToString_NoExponeElAcreedor()
    {
        var r = Resolver("positiva", "1", ProcedureFamily.Traspaso);

        r.ToString().Should().NotContain(DocRunt).And.NotContain(NombreRunt);
    }
}
