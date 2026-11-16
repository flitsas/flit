using System.Text;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 2 (diseño §5.4) — <see cref="MapeoBorradorTramite"/> arma la solicitud del rpc desde el vehículo: placa y
/// VIN, tipo resuelto, OT del RUNT (nombre tal cual y código de la réplica si casó con uno solo), propietario y locatario
/// descifrados SOLO aquí (ADR-0076), datos del vehículo de <c>datos_consolidados.vehiculo</c> como texto, <c>runt_consultado_en</c>,
/// <c>external_ref</c> = id del vehículo, <c>usuario_id</c> = quien lanza e <c>intento_id</c> = id del ítem. Documentos ficticios.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~MapeoBorradorTramiteTests
/// </code>
/// </example>
public sealed class MapeoBorradorTramiteTests
{
    private static readonly Guid Usuario = Guid.Parse("0199f3a1-1111-7000-8000-000000000011");
    private static readonly Guid Item = Guid.Parse("0199f3a1-2222-7000-8000-000000000022");
    private static readonly DateTimeOffset Runt = new(2026, 11, 11, 15, 30, 0, TimeSpan.Zero);
    private readonly ProtectorInverso _protector = new();

    [Fact]
    public void Arma_la_solicitud_completa_con_documentos_descifrados()
    {
        var vehiculo = Vehiculo();

        var s = MapeoBorradorTramite.Armar(vehiculo, "TRASPASO_STANDARD", Usuario, Item, "05001", _protector);

        s.ExternalRef.Should().Be(vehiculo.Id.ToString());
        s.UsuarioId.Should().Be(Usuario.ToString());
        s.TipoTramiteCodigo.Should().Be("TRASPASO_STANDARD");
        s.Placa.Should().Be("TST401");
        s.Vin.Should().BeEmpty();
        s.OrganismoNombreRunt.Should().Be("STRIA TTO MEDELLIN");
        s.OrganismoCodigo.Should().Be("05001");
        s.Propietario.TipoDocumento.Should().Be("C");
        s.Propietario.NumeroDocumento.Should().Be("99999123");
        s.Locatario.TipoDocumento.Should().Be("N");
        s.Locatario.NumeroDocumento.Should().Be("99999456");
        s.Vehiculo.Clase.Should().Be("AUTOMOVIL");
        s.Vehiculo.Servicio.Should().Be("PARTICULAR");
        s.Vehiculo.Marca.Should().Be("MARCA FICTICIA");
        s.Vehiculo.Linea.Should().Be("LINEA X");
        s.Vehiculo.Modelo.Should().Be("2020", "el modelo numérico viaja como texto");
        s.Vehiculo.Combustible.Should().Be("GASOLINA");
        s.Vehiculo.Cilindraje.Should().Be("1600");
        s.Vehiculo.Color.Should().BeEmpty("el RUNT de Diagnóstico no guarda el color");
        s.DiagnosticoId.Should().Be(vehiculo.DiagnosticoId.ToString());
        s.RuntConsultadoEn.ToDateTimeOffset().Should().Be(Runt);
        s.IntentoId.Should().Be(Item.ToString());
    }

    [Fact]
    public void Sin_locatario_ni_código_de_organismo_ni_datos_del_RUNT_no_inventa_valores()
    {
        var vehiculo = Vehiculo();
        vehiculo.LocatarioDocTipo = null;
        vehiculo.LocatarioDocCifrado = null;
        vehiculo.LocatarioDocOrigen = null;
        vehiculo.DatosConsolidados = "{}";
        vehiculo.RuntConsultadoEn = null;
        vehiculo.Servicio = null;
        vehiculo.OrganismoNombre = null;

        var s = MapeoBorradorTramite.Armar(vehiculo, "TRASPASO_STANDARD", Usuario, Item, null, _protector);

        s.Locatario.Should().BeNull();
        s.OrganismoCodigo.Should().BeEmpty();
        s.OrganismoNombreRunt.Should().BeEmpty();
        s.RuntConsultadoEn.Should().BeNull();
        s.Vehiculo.Should().NotBeNull();
        s.Vehiculo.Clase.Should().BeEmpty();
        s.Vehiculo.Servicio.Should().BeEmpty();
    }

    [Fact]
    public void Servicio_del_vehículo_toma_la_columna_si_el_consolidado_no_lo_trae()
    {
        var vehiculo = Vehiculo();
        vehiculo.DatosConsolidados = """{"vehiculo":{"clase":"MOTOCICLETA"}}""";
        vehiculo.Servicio = "PUBLICO";

        MapeoBorradorTramite.Armar(vehiculo, "T", Usuario, Item, null, _protector).Vehiculo.Servicio.Should().Be("PUBLICO");
    }

    [Fact]
    public void Vehículo_por_VIN_sin_placa_y_sin_propietario()
    {
        var vehiculo = Vehiculo();
        vehiculo.Placa = null;
        vehiculo.Vin = "TSTVIN00000000001";
        vehiculo.PropietarioDocTipo = null;
        vehiculo.PropietarioDocCifrado = null;

        var s = MapeoBorradorTramite.Armar(vehiculo, "T", Usuario, Item, null, _protector);

        s.Placa.Should().BeEmpty();
        s.Vin.Should().Be("TSTVIN00000000001");
        s.Propietario.Should().BeNull();
    }

    private VehiculoDiagnostico Vehiculo() => new()
    {
        TenantId = Guid.NewGuid(),
        DiagnosticoId = Guid.NewGuid(),
        IdentificadorOriginal = "TST401",
        IdentificadorNorm = "TST401",
        TipoIdentificador = TipoIdentificador.PlacaCarro,
        Placa = "TST401",
        Servicio = "PARTICULAR",
        OrganismoNombre = "STRIA TTO MEDELLIN",
        PropietarioDocTipo = "C",
        PropietarioDocCifrado = _protector.Proteger("99999123"),
        LocatarioDocTipo = "N",
        LocatarioDocCifrado = _protector.Proteger("99999456"),
        LocatarioDocOrigen = OrigenDocumento.Proveedor,
        DatosConsolidados = """
            {"vehiculo":{"placa":"TST401","clase":"AUTOMOVIL","servicio":"PARTICULAR","marca":"MARCA FICTICIA","linea":"LINEA X",
             "modelo":2020,"combustible":"GASOLINA","cilindraje":"1600","pasajeros":5}}
            """,
        RuntConsultadoEn = Runt,
    };

    /// <summary>Protector de prueba: invierte los bytes (no es cifrado; solo prueba que el mapeo descifra).</summary>
    private sealed class ProtectorInverso : IProtectorDocumentoPersona
    {
        public byte[] Proteger(string numero) => [.. Encoding.UTF8.GetBytes(numero).Reverse()];

        public string Desproteger(byte[] cifrado) => Encoding.UTF8.GetString([.. cifrado.Reverse()]);
    }
}
