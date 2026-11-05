using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13750 (F01-H4), escenarios 1 a 6 — reglas de la cabecera del diagnóstico, sin base ni HTTP: nombre de 1 a 150
/// caracteres, RUNT siempre, solo fuentes habilitadas y disponibles, cada fuente en su grupo, Con Análisis IA con un tipo del
/// catálogo y con las fuentes obligatorias de ese tipo. El catálogo es un doble del puerto <see cref="ICatalogoTiposTramite"/>
/// (solo en pruebas); el de D1 trae la lista de obligatorias vacía.
/// </summary>
/// <example>
/// <code>
/// var validador = new ValidadorCabecera(catalogo, f => f != Fuente.ImpuestoDepartamental);
/// var errores = await validador.ValidarAsync(new SolicitudCabecera("Lote", ModoDiagnostico.Simple, null, alcance), [Fuente.Runt], ct);
/// </code>
/// </example>
public sealed class ValidadorCabeceraTests
{
    private static readonly Fuente[] TodasHabilitadas = Enum.GetValues<Fuente>();

    private static Alcance Alcance(params Fuente[] porVehiculo) => new(porVehiculo, [], []);

    private static SolicitudCabecera Simple(string? nombre, Alcance alcance) => new(nombre, ModoDiagnostico.Simple, null, alcance);

    private static Task<IReadOnlyList<ErrorCabecera>> Validar(SolicitudCabecera s, IReadOnlyCollection<Fuente>? habilitadas = null, CatalogoDoble? catalogo = null) =>
        new ValidadorCabecera(catalogo ?? new CatalogoDoble(), f => f != Fuente.ImpuestoDepartamental)
            .ValidarAsync(s, habilitadas ?? TodasHabilitadas, TestContext.Current.CancellationToken);

    // Escenario 1 (F01-C1) — nombre de 0 y de 151 caracteres (o solo espacios): inválido.
    [Theory]
    [InlineData(0)]
    [InlineData(151)]
    [InlineData(-1)]
    public async Task Nombre_vacio_o_de_151_caracteres_es_invalido(int largo)
    {
        var nombre = largo < 0 ? "    " : new string('a', largo);

        var errores = await Validar(Simple(nombre, Alcance(Fuente.Runt)));

        errores.Select(e => (e.Codigo, e.Campo)).Should().Equal((ValidadorCabecera.CodigoNombreInvalido, ValidadorCabecera.CampoNombre));
    }

    // Escenario 1 (F01-C1) — 150 caracteres es el máximo válido; null también es inválido.
    [Fact]
    public async Task Nombre_de_150_caracteres_es_valido_y_null_no()
    {
        (await Validar(Simple(new string('a', 150), Alcance(Fuente.Runt)))).Should().BeEmpty();
        (await Validar(Simple(null, Alcance(Fuente.Runt)))).Select(e => e.Codigo).Should().Equal(ValidadorCabecera.CodigoNombreInvalido);
    }

    // Escenario 2 (F01-C2) — alcance sin RUNT.
    [Fact]
    public async Task Alcance_sin_RUNT_es_invalido()
    {
        var errores = await Validar(Simple("Lote", Alcance(Fuente.SimitVehiculo)));

        errores.Select(e => e.Codigo).Should().Equal(ValidadorCabecera.CodigoAlcanceSinRunt);
        errores[0].Campo.Should().Be(ValidadorCabecera.CampoAlcance);
    }

    // Escenario 3 (F01-C3) — fuente fuera de fuentes_habilitadas.
    [Fact]
    public async Task Fuente_fuera_de_habilitadas_es_invalida()
    {
        var errores = await Validar(Simple("Lote", Alcance(Fuente.Runt, Fuente.SimitVehiculo)), habilitadas: [Fuente.Runt]);

        errores.Select(e => e.Codigo).Should().Equal(ValidadorCabecera.CodigoFuenteNoHabilitada);
        errores[0].Mensaje.Should().Contain("SIMIT_VEHICULO");
    }

    // Escenario 3 (decisión de la ronda) — una fuente habilitada pero sin adaptador disponible (IMPUESTO_DEPARTAMENTAL sin C3).
    [Fact]
    public async Task Fuente_habilitada_sin_adaptador_disponible_es_invalida()
    {
        var errores = await Validar(Simple("Lote", Alcance(Fuente.Runt, Fuente.ImpuestoDepartamental)));

        errores.Select(e => e.Codigo).Should().Equal(ValidadorCabecera.CodigoFuenteNoDisponible);
    }

    // Escenario 4 (F01-C4) — RUNT_LOCATARIO dentro de porVehiculo; SIMIT_VEHICULO dentro de porPropietario.
    [Fact]
    public async Task Fuente_en_grupo_ajeno_es_invalida()
    {
        var enVehiculo = await Validar(Simple("Lote", Alcance(Fuente.Runt, Fuente.RuntLocatario)));
        var enPropietario = await Validar(Simple("Lote", new Alcance([Fuente.Runt], [Fuente.SimitVehiculo], [])));

        enVehiculo.Select(e => e.Codigo).Should().Equal(ValidadorCabecera.CodigoFuenteEnGrupoAjeno);
        enVehiculo[0].Campo.Should().Be("alcance.por_vehiculo");
        enPropietario.Select(e => e.Codigo).Should().Equal(ValidadorCabecera.CodigoFuenteEnGrupoAjeno);
        enPropietario[0].Campo.Should().Be("alcance.por_propietario");
    }

    // Contrato de los grupos (R-24): cada fuente pertenece a uno solo; el alcance en sus grupos es válido.
    [Fact]
    public async Task Cada_fuente_tiene_su_grupo_y_el_alcance_completo_en_sus_grupos_es_valido()
    {
        Enum.GetValues<Fuente>().GroupBy(GruposFuente.De).ToDictionary(g => g.Key, g => g.ToArray()).Should().BeEquivalentTo(
            new Dictionary<GrupoFuente, Fuente[]>
            {
                [GrupoFuente.PorVehiculo] = [Fuente.Runt, Fuente.SimitVehiculo, Fuente.Avaluo, Fuente.ImpuestoDepartamental],
                [GrupoFuente.PorPropietario] = [Fuente.RuntPropietario, Fuente.SimitPropietario],
                [GrupoFuente.PorLocatario] = [Fuente.RuntLocatario, Fuente.SimitLocatario],
            });

        var alcance = new Alcance([Fuente.Runt, Fuente.SimitVehiculo, Fuente.Avaluo, Fuente.Runt], [Fuente.RuntPropietario, Fuente.SimitPropietario], [Fuente.RuntLocatario, Fuente.SimitLocatario]);
        (await Validar(Simple("Lote", alcance))).Should().BeEmpty();
        alcance.Fuentes.Should().Equal(Fuente.Runt, Fuente.SimitVehiculo, Fuente.Avaluo, Fuente.RuntPropietario, Fuente.SimitPropietario, Fuente.RuntLocatario, Fuente.SimitLocatario);
        Domain.Diagnosticos.Alcance.Agrupar(alcance.Fuentes).Should().BeEquivalentTo(
            new Alcance([Fuente.Runt, Fuente.SimitVehiculo, Fuente.Avaluo], [Fuente.RuntPropietario, Fuente.SimitPropietario], [Fuente.RuntLocatario, Fuente.SimitLocatario]));
    }

    // Escenario 5 (F01-C5) — Con Análisis IA sin tipo de trámite (null o en blanco); Simple no consulta el catálogo.
    [Fact]
    public async Task Con_analisis_sin_tipo_es_invalido_y_simple_no_consulta_el_catalogo()
    {
        var catalogo = new CatalogoDoble();

        var sinTipo = await Validar(new SolicitudCabecera("Lote", ModoDiagnostico.ConAnalisis, null, Alcance(Fuente.Runt)), catalogo: catalogo);
        var enBlanco = await Validar(new SolicitudCabecera("Lote", ModoDiagnostico.ConAnalisis, "  ", Alcance(Fuente.Runt)), catalogo: catalogo);
        var simple = await Validar(Simple("Lote", Alcance(Fuente.Runt)), catalogo: catalogo);

        sinTipo.Select(e => e.Codigo).Should().Equal(ValidadorCabecera.CodigoTipoTramiteRequerido);
        sinTipo[0].Campo.Should().Be(ValidadorCabecera.CampoTipoTramite);
        enBlanco.Select(e => e.Codigo).Should().Equal(ValidadorCabecera.CodigoTipoTramiteRequerido);
        simple.Should().BeEmpty();
        catalogo.Llamadas.Should().Be(0, "sin tipo o en modo Simple no hace falta el catálogo");
    }

    // Escenario 5 (F01-C5) — tipo que no está en el catálogo de la etapa (lista blanca de traspasos).
    [Fact]
    public async Task Tipo_fuera_del_catalogo_es_invalido()
    {
        var errores = await Validar(new SolicitudCabecera("Lote", ModoDiagnostico.ConAnalisis, "MATRICULA_INICIAL", Alcance(Fuente.Runt)));

        errores.Select(e => e.Codigo).Should().Equal(ValidadorCabecera.CodigoTipoTramiteNoDisponible);
        errores[0].Campo.Should().Be(ValidadorCabecera.CampoTipoTramite);
    }

    // Escenario 6 (F01-C7) — el doble del puerto declara [SIMIT_VEHICULO] para el tipo: sin ella, inválido; con ella, válido.
    [Fact]
    public async Task Obligatorias_faltantes_del_tipo_son_invalidas()
    {
        var catalogo = new CatalogoDoble(new TipoTramite("TRASPASO_STANDARD", "TRASPASO", "TRASPASO", [Fuente.SimitVehiculo]));

        var sin = await Validar(new SolicitudCabecera("Lote", ModoDiagnostico.ConAnalisis, "TRASPASO_STANDARD", Alcance(Fuente.Runt)), catalogo: catalogo);
        var con = await Validar(new SolicitudCabecera("Lote", ModoDiagnostico.ConAnalisis, "TRASPASO_STANDARD", Alcance(Fuente.Runt, Fuente.SimitVehiculo)), catalogo: catalogo);

        sin.Select(e => e.Codigo).Should().Equal(ValidadorCabecera.CodigoFuentesObligatoriasFaltantes);
        sin[0].Mensaje.Should().Contain("SIMIT_VEHICULO");
        con.Should().BeEmpty();
    }

    // Escenario 6 (F01-C7) — con el catálogo de D1 (obligatorias vacías) no se restringe.
    [Fact]
    public async Task Catalogo_de_D1_con_obligatorias_vacias_no_restringe()
    {
        var errores = await Validar(new SolicitudCabecera("Lote", ModoDiagnostico.ConAnalisis, "TRASPASO_STANDARD", Alcance(Fuente.Runt)));

        errores.Should().BeEmpty();
    }

    // Conducta degradada de F01-H2 — si el catálogo no está disponible, la excepción sube (la API responde 503).
    [Fact]
    public async Task Catalogo_no_disponible_propaga_la_excepcion()
    {
        var catalogo = new CatalogoDoble { Caido = true };

        var validar = () => Validar(new SolicitudCabecera("Lote", ModoDiagnostico.ConAnalisis, "TRASPASO_STANDARD", Alcance(Fuente.Runt)), catalogo: catalogo);

        await validar.Should().ThrowAsync<CatalogoTiposTramiteNoDisponibleException>();
    }

    // Una regla ya incumplida no consulta el catálogo: el 422 no depende de que Trámites responda.
    [Fact]
    public async Task Con_errores_previos_no_consulta_el_catalogo()
    {
        var catalogo = new CatalogoDoble { Caido = true };

        var errores = await Validar(new SolicitudCabecera(string.Empty, ModoDiagnostico.ConAnalisis, "TRASPASO_STANDARD", Alcance(Fuente.Runt)), catalogo: catalogo);

        errores.Select(e => e.Codigo).Should().Equal(ValidadorCabecera.CodigoNombreInvalido);
        catalogo.Llamadas.Should().Be(0);
    }

    /// <summary>Doble del puerto del catálogo (solo pruebas): por defecto, los tres traspasos de D1 sin obligatorias.</summary>
    internal sealed class CatalogoDoble(params TipoTramite[] tipos) : ICatalogoTiposTramite
    {
        private readonly IReadOnlyList<TipoTramite> _tipos = tipos.Length > 0
            ? tipos
            :
            [
                new("TRASPASO_STANDARD", "TRASPASO", "TRASPASO", []),
                new("TRASPASO_TRANSFERENCIA_DE_DOMINIO", "TRANSFERENCIA DE DOMINIO", "TRASPASO", []),
                new("TRASPASO_UNILATERAL", "TRASPASO UNILATERAL", "TRASPASO", []),
            ];

        public bool Caido { get; init; }

        public int Llamadas { get; private set; }

        public Task<IReadOnlyList<TipoTramite>> ListarAsync(CancellationToken ct)
        {
            Llamadas++;
            return Caido ? throw new CatalogoTiposTramiteNoDisponibleException() : Task.FromResult(_tipos);
        }
    }
}
