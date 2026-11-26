using System.Collections;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Épica #14666 / HU #14698 (A1), ADR-0092 — criterio único «firmado por todas las partes» sobre el resolvedor del
/// listado. Un escenario Gherkin por prueba. Datos ficticios.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>evaluator.Evaluar(tramite, contexto, now)</c> ⇒ <c>FirmadoPorTodas</c> y
/// <c>TextoPendientes</c> («Vendedor: A · Comprador: B (rechazada)»).
/// </remarks>
public sealed class TramiteFirmaEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private readonly TramiteFirmaEvaluator _sut = new(Substitute.For<IProcedureInstanceRepository>());

    private static string Clave(string doc) => BiometricRules.IdentidadKey(Tenant, "CC", doc);

    private static TramiteFirmaContexto Ctx(params string[] documentosAprobados) =>
        TramiteFirmaContexto.Vacio with { IdentidadKeys = new HashSet<string>(documentosAprobados.Select(Clave)) };

    private static ProcedureInstance Tramite(ProcedureType tipo, params (string Rol, string Nombre, string Doc)[] actores)
    {
        var t = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureType = tipo,
            ProcedureTypeId = tipo.Id,
            ReferenceNumber = "FT9-0000999",
            Status = "preparado",
            CreatedAt = Now.AddDays(-2),
        };
        var ordinal = new Dictionary<string, int>();
        foreach (var (rol, nombre, doc) in actores)
        {
            ordinal[rol] = ordinal.GetValueOrDefault(rol) + 1;
            t.Actors.Add(new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                ProcedureInstanceId = t.Id,
                ActorType = rol,
                DocumentType = "CC",
                DocumentNumber = doc,
                FullName = nombre,
                PersonType = "natural",
                Ordinal = ordinal[rol],
            });
        }

        return t;
    }

    private static ProcedureInstanceBiometricValidation Validacion(string rol, string doc, string estado) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        PartyRole = rol,
        Status = estado,
        Name = "Persona de prueba",
        DocumentType = "CC",
        DocumentNumber = doc,
        TokenHash = Guid.NewGuid().ToString("N"),
        ExpiresAt = Now.AddDays(1),
        CreatedAt = Now.AddDays(-1),
    };

    [Fact]
    public void Traspaso_con_todas_las_partes_firmadas_es_si()
    {
        var t = Tramite(ProcedureTypeFixture.Traspaso,
            ("vendedor", "Ana Vendedora", "9000000001"), ("comprador", "Luis Comprador", "9000000002"));

        var r = _sut.Evaluar(t, Ctx("9000000001", "9000000002"), Now);

        r.FirmadoPorTodas.Should().BeTrue();
        r.Pendientes.Should().BeEmpty();
    }

    [Fact]
    public void Dos_vendedores_firmados_y_comprador_pendiente_es_no_con_el_comprador()
    {
        var t = Tramite(ProcedureTypeFixture.Traspaso,
            ("vendedor", "Ana Uno", "9000000001"), ("vendedor", "Beto Dos", "9000000003"),
            ("comprador", "Luis Comprador", "9000000002"));

        var r = _sut.Evaluar(t, Ctx("9000000001", "9000000003"), Now);

        r.FirmadoPorTodas.Should().BeFalse();
        r.Pendientes.Should().ContainSingle().Which.Texto.Should().Be("Comprador: Luis Comprador");
    }

    [Fact]
    public void Dos_compradores_solidarios_con_uno_aprobado_lista_solo_al_no_aprobado()
    {
        var t = Tramite(ProcedureTypeFixture.Traspaso,
            ("vendedor", "Ana Vendedora", "9000000001"),
            ("comprador", "Comprador Uno", "9000000002"), ("comprador", "Comprador Dos", "9000000004"));

        var r = _sut.Evaluar(t, Ctx("9000000001", "9000000002"), Now);

        r.FirmadoPorTodas.Should().BeFalse();
        r.Pendientes.Should().ContainSingle().Which.Nombre.Should().Be("Comprador Dos");
    }

    [Fact]
    public void Matricula_inicial_sin_vendedor_firmada_por_el_comprador_es_si()
    {
        var t = Tramite(ProcedureTypeFixture.Matricula, ("comprador", "Luis Comprador", "9000000002"));

        var r = _sut.Evaluar(t, Ctx("9000000002"), Now);

        r.FirmadoPorTodas.Should().BeTrue();
        r.Pendientes.Should().BeEmpty();
    }

    [Fact]
    public void Traspaso_unilateral_solo_exige_al_vendedor()
    {
        var t = Tramite(ProcedureTypeFixture.TraspasoUnilateral,
            ("vendedor", "Ana Vendedora", "9000000001"), ("comprador", "Locatario Sin Firma", "9000000002"));

        var firmado = _sut.Evaluar(t, Ctx("9000000001"), Now);
        var pendiente = _sut.Evaluar(t, Ctx(), Now);

        firmado.FirmadoPorTodas.Should().BeTrue();
        pendiente.Pendientes.Should().ContainSingle().Which.Rol.Should().Be("Vendedor");
    }

    [Fact]
    public void Parte_rechazada_cuenta_como_pendiente_y_se_marca_rechazada()
    {
        var t = Tramite(ProcedureTypeFixture.Traspaso,
            ("vendedor", "Ana Vendedora", "9000000001"), ("comprador", "Luis Comprador", "9000000002"));
        t.BiometricValidations.Add(Validacion("comprador", "9000000002", BiometricEstados.Rechazado));

        var r = _sut.Evaluar(t, Ctx("9000000001"), Now);

        r.FirmadoPorTodas.Should().BeFalse();
        var p = r.Pendientes.Should().ContainSingle().Subject;
        p.Rechazada.Should().BeTrue();
        p.Texto.Should().Be("Comprador: Luis Comprador (rechazada)");
    }

    [Fact]
    public void Resumen_dos_de_tres_y_estado_por_persona()
    {
        var t = Tramite(ProcedureTypeFixture.Traspaso,
            ("vendedor", "Ana Vendedora", "9000000001"),
            ("comprador", "Comprador Uno", "9000000002"), ("comprador", "Comprador Dos", "9000000004"));
        t.BiometricValidations.Add(Validacion("comprador", "9000000004", BiometricEstados.Rechazado));

        var r = _sut.Evaluar(t, Ctx("9000000001", "9000000002"), Now);

        r.TotalPartes.Should().Be(3);
        r.Firmaron.Should().Be(2);
        r.TextoResumen.Should().Be("2 de 3 · Faltan firmas");
        r.Partes.Select(p => (p.Rol, p.Ordinal, p.Estado)).Should().Equal(
            ("Vendedor", 1, "Firmó"), ("Comprador", 1, "Firmó"), ("Comprador", 2, "Rechazada"));
        r.Partes[0].Documento.Should().Be("9000000001");
    }

    [Fact]
    public void Persona_sin_validacion_es_sin_validar_y_completas_dice_firmas_completas()
    {
        var t = Tramite(ProcedureTypeFixture.Matricula, ("comprador", "Luis Comprador", "9000000002"));

        var sin = _sut.Evaluar(t, Ctx(), Now);
        var con = _sut.Evaluar(t, Ctx("9000000002"), Now);

        sin.Partes.Single().Estado.Should().Be("Sin validar");
        sin.TextoResumen.Should().Be("0 de 1 · Faltan firmas");
        con.TextoResumen.Should().Be("1 de 1 · Firmas completas");
        con.Partes.Single().Estado.Should().Be("Firmó");
    }

    [Fact]
    public void Firma_previa_vigente_de_otro_tramite_cuenta_como_firmada()
    {
        // La identidad vigente de la PERSONA (clave en lote) acredita sin fila propia en el trámite.
        var t = Tramite(ProcedureTypeFixture.Matricula, ("comprador", "Luis Comprador", "9000000002"));

        var r = _sut.Evaluar(t, Ctx("9000000002"), Now);

        t.BiometricValidations.Should().BeEmpty();
        r.FirmadoPorTodas.Should().BeTrue();
    }

    [Fact]
    public void Firma_de_bául_vigente_cuenta_como_firmada()
    {
        var t = Tramite(ProcedureTypeFixture.Matricula, ("comprador", "Empresa SAS", "9000000050"));
        t.Actors.Single().PersonType = "juridical";
        t.Actors.Single().DocumentType = "NIT";
        t.Actors.Single().Metadata = """{"representanteLegal":{"tipoDocumento":"CC","numeroDocumento":"9000000051","nombreCompleto":"Rep Legal"}}""";
        var ctx = TramiteFirmaContexto.Vacio with
        {
            FirmaBaul = new Dictionary<string, bool> { [Clave("9000000051")] = true },
        };

        var r = _sut.Evaluar(t, ctx, Now);

        r.FirmadoPorTodas.Should().BeTrue();
    }

    [Fact]
    public void Tramite_sin_partes_que_deban_firmar_es_si_con_lista_vacia()
    {
        var t = Tramite(ProcedureTypeFixture.Traspaso);

        var r = _sut.Evaluar(t, Ctx(), Now);

        // Sin actores no hay a quién pedirle firma: no se reporta pendiente alguno.
        r.FirmadoPorTodas.Should().BeTrue();
        r.Pendientes.Should().BeEmpty();
        // Y el texto no afirma «Firmas completas» donde nadie firmó.
        r.TextoResumen.Should().Be("Sin partes que firmen");
    }

    [Fact]
    public void Fallo_al_calcular_deja_a_la_persona_pendiente_sin_romper_el_reporte()
    {
        // Persona jurídica: el resolvedor consulta el baúl, que aquí lanza.
        var t = Tramite(ProcedureTypeFixture.Matricula, ("comprador", "Empresa SAS", "9000000050"));
        t.Actors.Single().PersonType = "juridical";
        t.Actors.Single().DocumentType = "NIT";
        t.Actors.Single().Metadata = """{"representanteLegal":{"tipoDocumento":"CC","numeroDocumento":"9000000051","nombreCompleto":"Rep Legal"}}""";
        var ctx = Ctx("9000000051") with { FirmaBaul = new ThrowingDictionary() };

        var r = _sut.Evaluar(t, ctx, Now);

        r.FirmadoPorTodas.Should().BeFalse();
        r.Pendientes.Should().ContainSingle().Which.Texto.Should().Be("Comprador: Empresa SAS");
    }

    [Fact]
    public void Formato_de_pendientes_une_vendedor_y_comprador_con_punto_medio()
    {
        var t = Tramite(ProcedureTypeFixture.Traspaso,
            ("vendedor", "Ana Vendedora", "9000000001"), ("comprador", "Luis Comprador", "9000000002"));

        var r = _sut.Evaluar(t, Ctx(), Now);

        r.TextoPendientes.Should().Be("Vendedor: Ana Vendedora · Comprador: Luis Comprador");
    }

    private sealed class ThrowingDictionary : IReadOnlyDictionary<string, bool>
    {
        public bool this[string key] => throw new InvalidOperationException("boom");
        public IEnumerable<string> Keys => throw new InvalidOperationException("boom");
        public IEnumerable<bool> Values => throw new InvalidOperationException("boom");
        public int Count => throw new InvalidOperationException("boom");
        public bool ContainsKey(string key) => throw new InvalidOperationException("boom");
        public bool TryGetValue(string key, out bool value) => throw new InvalidOperationException("boom");
        public IEnumerator<KeyValuePair<string, bool>> GetEnumerator() => throw new InvalidOperationException("boom");
        IEnumerator IEnumerable.GetEnumerator() => throw new InvalidOperationException("boom");
    }
}
