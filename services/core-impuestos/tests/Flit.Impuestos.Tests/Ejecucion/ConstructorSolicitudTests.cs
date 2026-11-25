using System.Text.Json;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Catalogos;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Ejecucion;

/// <summary>
/// HU #14694 (M09; F2a-CF4, F2a-CF5, F2a-CF8, F5a-CF3) — la solicitud al API sale del RUNT y del <c>params_schema</c> del
/// departamento (guía §4 y §5): campos exigidos por el esquema; Cundinamarca (25) exige correo y celular o teléfono del RUNT, sin
/// contacto de respaldo (DA-9a=d), o queda «Datos incompletos»; NIT sin dígito de verificación; nunca los campos del contrato
/// anterior; departamento fuera de la configuración contractual ⇒ «Departamento no habilitado»; <c>callbackUrl</c> solo con
/// secreto, en <c>LISTO</c> y sin modo respaldo de firma.
/// </summary>
/// <example>
/// <code>
/// var r = ConstructorSolicitud.Construir("ABC123", datos, secretaria, copia, fuentes, callbackUrl: null);
/// </code>
/// </example>
public sealed class ConstructorSolicitudTests
{
    private static readonly SecretariaHomologada Medellin = new("05", "MEDELLIN");
    private static readonly SecretariaHomologada Chia = new("25", "CHIA");
    private static readonly CopiaConfiguracion Todos = new(500, null, 2);

    private static readonly IReadOnlyList<FuenteApi> Fuentes =
    [
        new() { DepartamentoCodigo = "05", Nombre = "Antioquia", Activa = true, ParamsSchema = EntornoEjecucion.Esquema05 },
        new() { DepartamentoCodigo = "25", Nombre = "Cundinamarca", Activa = true, ParamsSchema = EntornoEjecucion.Esquema25 },
        new() { DepartamentoCodigo = "41", Nombre = "Huila", Activa = false, ParamsSchema = "{}" },
        new() { DepartamentoCodigo = "08", Nombre = "Atlántico", Activa = true, ParamsSchema = "{\"properties\":{\"detail\":{\"required\":[\"secretaryDepartmentCode\",\"vehicleBrand\"]}}}" },
    ];

    [Fact]
    public void Antioquia_lleva_los_campos_que_exige_su_esquema_y_ningun_dato_de_contacto()
    {
        var r = ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Antioquia(), Medellin, Todos, Fuentes, callbackUrl: null);

        var lista = r.Should().BeOfType<Construccion.Lista>().Subject;
        lista.Solicitud.DocumentType.Should().Be("CC");
        lista.Solicitud.DocumentNumber.Should().Be("71706228");
        lista.Solicitud.PlateNumber.Should().Be("ABC123");
        lista.Solicitud.CallbackUrl.Should().BeNull();
        lista.Solicitud.Detail.Should().Contain("secretaryDepartmentCode", "05").And.Contain("secretaryCity", "MEDELLIN")
            .And.Contain("vehicleModel", "2019").And.Contain("OwnerName", "PEDRO PEREZ");
        lista.Solicitud.Detail.Keys.Should().NotContain(["OwnerEmail", "OwnerCellPhone", "OwnerPhone", "OwnerAddressComplete"],
            "el contacto y la dirección solo viajan si el departamento los exige (DA-7, Ley 1581)");
        lista.Datos.DepartamentoCodigo.Should().Be("05");
        lista.Datos.DepartamentoNombre.Should().Be("Antioquia");
        lista.Datos.Ciudad.Should().Be("MEDELLIN");
    }

    [Fact]
    public void Cundinamarca_sin_correo_queda_datos_incompletos_OwnerEmail()
    {
        var r = ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Cundinamarca(conCorreo: false), Chia, Todos, Fuentes, null);

        r.Should().BeOfType<Construccion.Incompleta>().Which.Campo.Should().Be("OwnerEmail");
    }

    [Fact]
    public void Cundinamarca_sin_celular_ni_telefono_queda_datos_incompletos_OwnerPhone()
    {
        var r = ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Cundinamarca(conTelefono: false), Chia, Todos, Fuentes, null);

        r.Should().BeOfType<Construccion.Incompleta>().Which.Campo.Should().Be("OwnerPhone");
    }

    [Fact]
    public void Cundinamarca_con_correo_y_celular_los_envia_aunque_el_esquema_no_los_pida()
    {
        var r = ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Cundinamarca(), Chia, Todos, Fuentes, null);

        var detalle = r.Should().BeOfType<Construccion.Lista>().Subject.Solicitud.Detail;
        detalle.Should().Contain("OwnerEmail", "pedro@correo.example").And.Contain("OwnerCellPhone", "3001234567");
        detalle.Should().NotContainKey("OwnerPhone");
    }

    [Fact]
    public void Un_telefono_fijo_va_como_OwnerPhone()
    {
        var datos = RuntDePrueba.Cundinamarca() with { Telefono = "6018765432" };

        var detalle = ConstructorSolicitud.Construir("ABC123", datos, Chia, Todos, Fuentes, null).Should().BeOfType<Construccion.Lista>().Subject.Solicitud.Detail;

        detalle.Should().Contain("OwnerPhone", "6018765432").And.NotContainKey("OwnerCellPhone");
    }

    [Theory]
    [InlineData("900123456-7", "900123456")]
    [InlineData("900.123.456-7", "900123456")]
    [InlineData("900123456", "900123456")]
    public void El_NIT_va_sin_digito_de_verificacion(string numero, string esperado)
    {
        var datos = RuntDePrueba.Antioquia() with { TipoDocumento = "NIT", NumeroDocumento = numero };

        var lista = ConstructorSolicitud.Construir("ABC123", datos, Medellin, Todos, Fuentes, null).Should().BeOfType<Construccion.Lista>().Subject;

        lista.Solicitud.DocumentType.Should().Be("NIT");
        lista.Solicitud.DocumentNumber.Should().Be(esperado);
    }

    [Fact]
    public void Un_NIT_de_diez_digitos_sin_guion_es_ambiguo_y_queda_datos_incompletos_documentNumber()
    {
        var datos = RuntDePrueba.Antioquia() with { TipoDocumento = "NIT", NumeroDocumento = "9001234567" };

        ConstructorSolicitud.Construir("ABC123", datos, Medellin, Todos, Fuentes, null)
            .Should().BeOfType<Construccion.Incompleta>().Which.Campo.Should().Be("documentNumber");
    }

    [Fact]
    public void Sin_tipo_de_documento_sin_nombre_exigido_o_sin_secretaria_homologada_queda_datos_incompletos_con_el_campo()
    {
        ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Antioquia() with { TipoDocumento = null }, Medellin, Todos, Fuentes, null)
            .Should().BeOfType<Construccion.Incompleta>().Which.Campo.Should().Be("documentType");
        ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Antioquia() with { NombrePropietario = " " }, Medellin, Todos, Fuentes, null)
            .Should().BeOfType<Construccion.Incompleta>().Which.Campo.Should().Be("OwnerName");
        ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Antioquia() with { Modelo = null }, Medellin, Todos, Fuentes, null)
            .Should().BeOfType<Construccion.Incompleta>().Which.Campo.Should().Be("vehicleModel");
        ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Antioquia(), secretaria: null, Todos, Fuentes, null)
            .Should().BeOfType<Construccion.Incompleta>().Which.Campo.Should().Be("secretaryDepartmentCode");
    }

    [Fact]
    public void Datos_incompletos_conserva_el_documento_y_el_nombre_que_si_llegaron()
    {
        var r = ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Cundinamarca(conCorreo: false), Chia, Todos, Fuentes, null);

        var datos = r.Should().BeOfType<Construccion.Incompleta>().Subject.Datos;
        datos.DocumentoTipo.Should().Be("CC");
        datos.DocumentoNumero.Should().Be("71706228");
        datos.NombrePropietario.Should().Be("PEDRO PEREZ");
        datos.DepartamentoCodigo.Should().Be("25");
    }

    [Fact]
    public void Un_campo_exigido_que_el_RUNT_si_trae_se_agrega_desde_el_esquema()
    {
        var r = ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Antioquia(), new SecretariaHomologada("08", "BARRANQUILLA"), Todos, Fuentes, null);

        r.Should().BeOfType<Construccion.Lista>().Which.Solicitud.Detail.Should().Contain("vehicleBrand", "CHEVROLET");
    }

    [Fact]
    public void Departamento_fuera_de_la_configuracion_contractual_no_esta_habilitado()
    {
        var soloAntioquia = new CopiaConfiguracion(500, ["05"], 2);

        var r = ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Cundinamarca(), Chia, soloAntioquia, Fuentes, null);

        var no = r.Should().BeOfType<Construccion.NoHabilitado>().Subject;
        no.Datos.DepartamentoCodigo.Should().Be("25");
        no.Datos.DepartamentoNombre.Should().Be("Cundinamarca");
    }

    [Fact]
    public void Sin_lista_contractual_un_departamento_inactivo_o_sin_fuente_no_esta_habilitado()
    {
        ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Antioquia(), new SecretariaHomologada("41", "NEIVA"), Todos, Fuentes, null)
            .Should().BeOfType<Construccion.NoHabilitado>();
        ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Antioquia(), new SecretariaHomologada("99", "MITU"), Todos, Fuentes, null)
            .Should().BeOfType<Construccion.NoHabilitado>();
    }

    [Fact]
    public void Nunca_lleva_los_campos_del_contrato_anterior()
    {
        var r = ConstructorSolicitud.Construir("ABC123", RuntDePrueba.Cundinamarca(), Chia, Todos, Fuentes, "https://x.prueba/avisos/t");

        var solicitud = r.Should().BeOfType<Construccion.Lista>().Subject.Solicitud;
        solicitud.Detail.Keys.Should().NotIntersectWith(SolicitudConsulta.CamposProhibidos);
        var json = JsonSerializer.Serialize(solicitud);
        foreach (var prohibido in SolicitudConsulta.CamposProhibidos)
            json.Should().NotContain(prohibido);
        solicitud.CallbackUrl.Should().Be("https://x.prueba/avisos/t");
    }

    [Theory]
    [InlineData(EstadoAprovisionamiento.Listo, false, true, true, true)]
    [InlineData(EstadoAprovisionamiento.ListoSinWebhook, false, true, true, false)]
    [InlineData(EstadoAprovisionamiento.Listo, true, true, true, false)]
    [InlineData(EstadoAprovisionamiento.Listo, false, false, true, false)]
    [InlineData(EstadoAprovisionamiento.Listo, false, true, false, false)]
    [InlineData(EstadoAprovisionamiento.Suspendido, false, true, true, false)]
    public void El_callbackUrl_va_solo_con_secreto_en_LISTO_sin_modo_respaldo_y_con_la_url_configurada(
        EstadoAprovisionamiento estado, bool modoRespaldo, bool tieneSecreto, bool urlConfigurada, bool esperado) =>
        ConstructorSolicitud.ConCallback(estado, modoRespaldo, tieneSecreto, urlConfigurada).Should().Be(esperado);

    [Fact]
    public void El_validador_lee_los_requeridos_de_detail_y_el_primero_que_falta()
    {
        var requeridos = ValidadorParamsSchema.RequeridosDetalle(EntornoEjecucion.Esquema05);

        requeridos.Should().Equal("secretaryDepartmentCode", "vehicleModel", "secretaryCity", "OwnerName");
        ValidadorParamsSchema.RequeridosDetalle("{}").Should().BeEmpty();
        ValidadorParamsSchema.RequeridosDetalle("no es json").Should().BeEmpty();
        ValidadorParamsSchema.PrimerFaltante(requeridos, new Dictionary<string, object?> { ["secretaryDepartmentCode"] = "05", ["vehicleModel"] = "2019", ["secretaryCity"] = "" })
            .Should().Be("secretaryCity");
    }
}
