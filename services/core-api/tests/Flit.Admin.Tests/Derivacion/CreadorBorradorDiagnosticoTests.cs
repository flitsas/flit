using Flit.Api.Derivacion;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Derivacion;

/// <summary>
/// ADR-0084 / diseño §3 y §9 — orquestación del rpc <c>CrearBorradorDesdeDiagnostico</c> con dobles de la persistencia, del
/// autor y del resolutor de OT (sin base). Cubre: crea el borrador con origen, referencia, canal, autor y OT; siembra de
/// campos; idempotencia (incluida la carrera 23505); referencia de otro origen; usuario sin permiso; OT no habilitado; tipo
/// inválido; datos inválidos y traducción de los códigos del caso de uso.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>var r = await new CreadorBorradorDiagnostico(almacen, autor, oficinas).CrearAsync(empresa, request, ct);</c>
/// </remarks>
public sealed class CreadorBorradorDiagnosticoTests
{
    private static readonly Guid Empresa = Guid.Parse("0b0e0000-0000-4000-8000-000000000001");
    private static readonly Guid Usuario = Guid.Parse("0b0e0000-0000-4000-8000-0000000000aa");
    private static readonly Guid Vehiculo = Guid.Parse("0b0e0000-0000-7000-8000-0000000000f1");
    private static readonly ResolvedTransitOffice Oficina = new(Guid.Parse("0b0e0000-0000-4000-8000-0000000000c1"), "05001000", "Secretaria de prueba", "05001", "Municipio de prueba");

    private readonly AlmacenFalso _almacen = new();
    private readonly AutorFalso _autor = new();
    private readonly OficinasFalsas _oficinas = new();

    private CreadorBorradorDiagnostico Creador() => new(_almacen, _autor, _oficinas);

    private static CrearBorradorDesdeDiagnosticoRequest Solicitud(Action<CrearBorradorDesdeDiagnosticoRequest>? ajuste = null)
    {
        var r = new CrearBorradorDesdeDiagnosticoRequest
        {
            ExternalRef = Vehiculo.ToString(),
            UsuarioId = Usuario.ToString(),
            TipoTramiteCodigo = "TRASPASO_STANDARD",
            Placa = "ABC123",
            Vin = "9BWZZZ377VT004251",
            OrganismoNombreRunt = "SECRETARIA DE PRUEBA",
            Propietario = new DocumentoPersona { TipoDocumento = "cc", NumeroDocumento = "1000000001" },
            Vehiculo = new VehiculoBorrador
            {
                Clase = "AUTOMOVIL", Servicio = "PARTICULAR", Marca = "MARCA DE PRUEBA", Linea = "LINEA X", Modelo = "2020",
                Combustible = "GASOLINA", Cilindraje = "1600", Color = "BLANCO",
            },
            DiagnosticoId = Guid.NewGuid().ToString(),
            IntentoId = Guid.NewGuid().ToString(),
        };
        ajuste?.Invoke(r);
        return r;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Crea ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreaElBorrador_ConOrigenDiagnostico_ReferenciaDelVehiculo_CanalTramites_YElUsuarioComoAutor()
    {
        _oficinas.PorNombre = Oficina;

        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(), Ct);

        respuesta.ResultadoCase.Should().Be(CrearBorradorDesdeDiagnosticoResponse.ResultadoOneofCase.Creado);
        respuesta.Creado.YaExistia.Should().BeFalse();
        respuesta.Creado.ProcedureInstanceId.Should().Be(_almacen.IdCreado.ToString());
        respuesta.Creado.Numero.Should().Be("TR-0001");
        respuesta.Creado.Estado.Should().Be("borrador");

        var crear = _almacen.Creaciones.Should().ContainSingle().Which;
        crear.TenantId.Should().Be(Empresa);
        crear.CreatedByUserId.Should().Be(Usuario);
        crear.Origin.Should().Be("diagnostico");
        crear.ExternalRef.Should().Be(Vehiculo.ToString());
        crear.Channel.Should().Be(TramiteCanal.Tramites);
        crear.ChannelTransitOfficeId.Should().BeNull();
        crear.TransitOfficeId.Should().Be(Oficina.Id);
        crear.ProcedureTypeCode.Should().Be("TRASPASO_STANDARD");
        _autor.Consultas.Should().ContainSingle().Which.Should().Be((Empresa, Usuario, "TRASPASO"));
    }

    [Fact]
    public async Task SiembraPlacaVinPropietarioYFichaDelVehiculo_ConLasClavesCanonicas()
    {
        _oficinas.PorNombre = Oficina;

        await Creador().CrearAsync(Empresa, Solicitud(), Ct);

        var campos = _almacen.Campos.ToDictionary(c => c.FieldKey, c => c.ValueText);
        campos.Should().Contain("plate", "ABC123").And.Contain("vin", "9BWZZZ377VT004251")
            .And.Contain("owner_document_type", "CC").And.Contain("owner_document_number", "1000000001");
        campos.Keys.Should().NotContain(k => k.StartsWith("lessee_", StringComparison.Ordinal), "en traspaso el locatario no se siembra como atributo");

        var sistema = _almacen.Sistema.ToDictionary(c => c.FieldKey, c => c.ValueText);
        sistema.Should().Contain("vehicle_class", "AUTOMOVIL").And.Contain("vehicle_service", "PARTICULAR")
            .And.Contain("vehicle_brand", "MARCA DE PRUEBA").And.Contain("vehicle_line", "LINEA X")
            .And.Contain("vehicle_year", "2020").And.Contain("vehicle_fuel", "GASOLINA").And.Contain("vehicle_fuel_runt", "GASOLINA")
            .And.Contain("vehicle_engine_displacement", "1600").And.Contain("vehicle_color", "BLANCO").And.Contain("vehicle_color_runt", "BLANCO");
        sistema.Should().Contain("transit_office_id", Oficina.Id.ToString()).And.Contain("transit_office_code", Oficina.Code);
        _almacen.SoloFaltantes.Should().AllBeEquivalentTo(false);
    }

    [Fact]
    public async Task EnUnTipoNoTraspaso_ElLocatarioSeSiembraComoEnIct()
    {
        _almacen.Tipo = new TipoParaDerivar("MATRICULA_LEASING", "MATRICULAS");
        _oficinas.PorNombre = Oficina;

        await Creador().CrearAsync(Empresa, Solicitud(r =>
        {
            r.TipoTramiteCodigo = "MATRICULA_LEASING";
            r.Locatario = new DocumentoPersona { TipoDocumento = "nit", NumeroDocumento = "900000001" };
        }), Ct);

        var campos = _almacen.Campos.ToDictionary(c => c.FieldKey, c => c.ValueText);
        campos.Should().Contain("lessee_document_type", "NIT").And.Contain("lessee_document_number", "900000001");
    }

    [Fact]
    public async Task UnFalloDeSiembra_VaAAdvertencias_YNoDeshaceElBorrador()
    {
        _oficinas.PorNombre = Oficina;
        _almacen.ErrorCampos = "invalid_value";

        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(), Ct);

        respuesta.Creado.Should().NotBeNull();
        respuesta.Creado.Advertencias.Should().Contain("seed_warning:invalid_value");
    }

    [Fact]
    public async Task SinNombreQueCase_ResuelveElOtPorCodigo()
    {
        _oficinas.PorCodigo = Oficina;

        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(r => r.OrganismoCodigo = "05001000"), Ct);

        respuesta.Creado.Should().NotBeNull();
        _almacen.Creaciones.Single().TransitOfficeId.Should().Be(Oficina.Id);
        _oficinas.CodigosConsultados.Should().ContainSingle().Which.Should().Be("05001000");
    }

    // ── Idempotencia ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConLaMismaReferenciaYOrigenDiagnostico_DevuelveElMismoTramite_SinCrearOtro()
    {
        var existente = Guid.NewGuid();
        _almacen.Existente = new TramiteExistente(existente, "TR-0099", "radicado", "diagnostico", Vehiculo.ToString());

        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(), Ct);

        respuesta.Creado.YaExistia.Should().BeTrue();
        respuesta.Creado.ProcedureInstanceId.Should().Be(existente.ToString());
        respuesta.Creado.Numero.Should().Be("TR-0099");
        respuesta.Creado.Estado.Should().Be("radicado");
        _almacen.Creaciones.Should().BeEmpty();
        _almacen.Campos.Should().BeEmpty("fuera de borrador no se reescribe nada");
    }

    [Fact]
    public async Task SiElExistenteSigueEnBorrador_CompletaSoloLosCamposFaltantes()
    {
        _oficinas.PorNombre = Oficina;
        _almacen.Existente = new TramiteExistente(Guid.NewGuid(), "TR-0100", "borrador", "diagnostico", Vehiculo.ToString());

        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(), Ct);

        respuesta.Creado.YaExistia.Should().BeTrue();
        _almacen.Creaciones.Should().BeEmpty();
        _almacen.Campos.Should().NotBeEmpty();
        _almacen.SoloFaltantes.Should().AllBeEquivalentTo(true);
    }

    // Revisión de seguridad 2026-11-11: retomar un borrador existente no siembra campos a nombre de un autor que ya no puede
    // crear el tipo; se devuelve el mismo trámite (idempotencia intacta) sin tocarlo.
    [Fact]
    public async Task SiElExistenteSigueEnBorrador_YElAutorYaNoPuedeCrear_DevuelveElMismoSinSembrar()
    {
        _oficinas.PorNombre = Oficina;
        var existente = Guid.NewGuid();
        _almacen.Existente = new TramiteExistente(existente, "TR-0103", "borrador", "diagnostico", Vehiculo.ToString());
        _autor.Puede = false;

        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(), Ct);

        respuesta.Creado.YaExistia.Should().BeTrue();
        respuesta.Creado.ProcedureInstanceId.Should().Be(existente.ToString());
        _autor.Consultas.Should().ContainSingle().Which.Should().Be((Empresa, Usuario, "TRASPASO"), "se revalida al autor antes de sembrar");
        _almacen.Campos.Should().BeEmpty("sin permiso no se siembran campos a su nombre");
        _almacen.Sistema.Should().BeEmpty();
        _almacen.Creaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task UnaCarreraQueChocaConElIndiceUnico_SeRespondeComoYaExistia()
    {
        _oficinas.PorNombre = Oficina;
        _almacen.ChocarConReferencia = true;
        var ganador = Guid.NewGuid();
        _almacen.ExistenteTrasChoque = new TramiteExistente(ganador, "TR-0101", "borrador", "diagnostico", Vehiculo.ToString());

        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(), Ct);

        respuesta.Creado.YaExistia.Should().BeTrue();
        respuesta.Creado.ProcedureInstanceId.Should().Be(ganador.ToString());
    }

    [Theory]
    [InlineData("ict")]
    [InlineData("persona_natural")]
    [InlineData(null)]
    public async Task ConLaMismaReferenciaDeOtroOrigen_SeRechaza_SinCrearNada(string? origen)
    {
        _almacen.Existente = new TramiteExistente(Guid.NewGuid(), "TR-0102", "borrador", origen, Vehiculo.ToString());

        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(), Ct);

        respuesta.Rechazo.Codigo.Should().Be("REFERENCIA_DE_OTRO_ORIGEN");
        _almacen.Creaciones.Should().BeEmpty();
    }

    // ── Rechazos ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UsuarioSinPermiso_InactivoODeOtraCompania_SeRechaza_SinCrear()
    {
        _oficinas.PorNombre = Oficina;
        _autor.Puede = false;

        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(), Ct);

        respuesta.Rechazo.Codigo.Should().Be("USUARIO_SIN_PERMISO");
        respuesta.Rechazo.Motivo.Should().NotBeNullOrWhiteSpace();
        _almacen.Creaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task OtNoHabilitado_PorNombreNiPorCodigo_SeRechaza_NuncaSeCreaSinOt()
    {
        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(r => r.OrganismoCodigo = "99999000"), Ct);

        respuesta.Rechazo.Codigo.Should().Be("OT_NO_HABILITADO");
        _almacen.Creaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task TipoInexistenteONoPublicado_SeRechazaConTipoNoDisponible()
    {
        _almacen.Tipo = null;

        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(r => r.TipoTramiteCodigo = "NO_EXISTE"), Ct);

        respuesta.Rechazo.Codigo.Should().Be("TIPO_NO_DISPONIBLE");
        _autor.Consultas.Should().BeEmpty();
        _almacen.Creaciones.Should().BeEmpty();
    }

    [Theory]
    [InlineData("no-es-uuid", "0b0e0000-0000-4000-8000-0000000000aa", "ABC123", "", "TRASPASO_STANDARD")]
    [InlineData("0b0e0000-0000-7000-8000-0000000000f1", "", "ABC123", "", "TRASPASO_STANDARD")]
    [InlineData("0b0e0000-0000-7000-8000-0000000000f1", "no-es-uuid", "ABC123", "", "TRASPASO_STANDARD")]
    [InlineData("0b0e0000-0000-7000-8000-0000000000f1", "0b0e0000-0000-4000-8000-0000000000aa", "", "  ", "TRASPASO_STANDARD")]
    [InlineData("0b0e0000-0000-7000-8000-0000000000f1", "0b0e0000-0000-4000-8000-0000000000aa", "ABC123", "", "")]
    public async Task DatosIncompletos_SeRechazanConDatosInvalidos_SinTocarLaBase(string externalRef, string usuario, string placa, string vin, string tipo)
    {
        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(r =>
        {
            r.ExternalRef = externalRef;
            r.UsuarioId = usuario;
            r.Placa = placa;
            r.Vin = vin;
            r.TipoTramiteCodigo = tipo;
        }), Ct);

        respuesta.Rechazo.Codigo.Should().Be("DATOS_INVALIDOS");
        _almacen.Lecturas.Should().Be(0);
    }

    [Theory]
    [InlineData("not_found", "TIPO_NO_DISPONIBLE")]
    [InlineData("not_published", "TIPO_NO_DISPONIBLE")]
    [InlineData("procedure_type_not_enabled", "TIPO_NO_DISPONIBLE")]
    [InlineData("procedure_family_blocked", "FAMILIA_BLOQUEADA")]
    [InlineData("family_permission_denied", "USUARIO_SIN_PERMISO")]
    [InlineData("ot_not_permitted", "OT_NO_HABILITADO")]
    [InlineData("OT_NOT_AUTHORIZED_FOR_TYPE", "OT_NO_HABILITADO")]
    [InlineData("COMPANY_RULE_VIOLATION", "REGLA_COMPANIA")]
    [InlineData("DUPLICATE_ACTIVE_PROCEDURE", "TRAMITE_ACTIVO_DUPLICADO")]
    [InlineData("tenant_inactive", "EMPRESA_INACTIVA")]
    [InlineData("network_inactive", "EMPRESA_INACTIVA")]
    [InlineData("invalid_reference", "DATOS_INVALIDOS")]
    public async Task UnErrorDeNegocioDelCasoDeUso_SeTraduceAlCodigoEstable(string error, string codigo)
    {
        _oficinas.PorNombre = Oficina;
        _almacen.ErrorCrear = error;

        var respuesta = await Creador().CrearAsync(Empresa, Solicitud(), Ct);

        respuesta.Rechazo.Codigo.Should().Be(codigo);
        respuesta.Rechazo.Motivo.Should().NotContain(error, "el motivo es lenguaje claro, no el código técnico");
    }

    // ── Por referencia ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PorReferencias_SoloDevuelveLosDeOrigenDiagnostico()
    {
        _almacen.PorReferencias =
        [
            new TramiteExistente(Guid.NewGuid(), "TR-1", "borrador", "diagnostico", "a"),
            new TramiteExistente(Guid.NewGuid(), "TR-2", "borrador", "ict", "b"),
        ];

        var respuesta = await Creador().PorReferenciasAsync(Empresa, ["a", "b", "c"], Ct);

        respuesta.Tramites.Should().ContainSingle().Which.ExternalRef.Should().Be("a");
    }

    // ── Dobles ────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class AlmacenFalso : IAlmacenBorradorDiagnostico
    {
        public Guid IdCreado { get; } = Guid.NewGuid();
        public TipoParaDerivar? Tipo { get; set; } = new("TRASPASO_STANDARD", "TRASPASO");
        public TramiteExistente? Existente { get; set; }
        public TramiteExistente? ExistenteTrasChoque { get; set; }
        public bool ChocarConReferencia { get; set; }
        public string? ErrorCrear { get; set; }
        public string? ErrorCampos { get; set; }
        public IReadOnlyList<TramiteExistente> PorReferencias { get; set; } = [];
        public List<CreateProcedureInstanceRequest> Creaciones { get; } = [];
        public List<FieldValueInput> Campos { get; } = [];
        public List<FieldValueInput> Sistema { get; } = [];
        public List<bool> SoloFaltantes { get; } = [];
        public int Lecturas { get; private set; }

        public Task<TramiteExistente?> BuscarPorReferenciaAsync(Guid tenantId, string externalRef, CancellationToken ct)
        {
            Lecturas++;
            return Task.FromResult(Creaciones.Count > 0 && ChocarConReferencia ? ExistenteTrasChoque : Existente);
        }

        public Task<IReadOnlyList<TramiteExistente>> BuscarPorReferenciasAsync(Guid tenantId, IReadOnlyCollection<string> externalRefs, CancellationToken ct)
        {
            Lecturas++;
            return Task.FromResult(PorReferencias);
        }

        public Task<TipoParaDerivar?> TipoPublicadoAsync(string codigo, CancellationToken ct)
        {
            Lecturas++;
            return Task.FromResult(Tipo);
        }

        public Task<CreacionBorrador> CrearAsync(CreateProcedureInstanceRequest request, CancellationToken ct)
        {
            Creaciones.Add(request);
            if (ChocarConReferencia)
                return Task.FromResult(new CreacionBorrador(null, null, ReferenciaDuplicada: true));
            if (ErrorCrear is not null)
                return Task.FromResult(new CreacionBorrador(null, ErrorCrear));
            return Task.FromResult(new CreacionBorrador(
                new ProcedureInstanceSummary(IdCreado, "TR-0001", "borrador", Guid.NewGuid(), request.TenantId, DateTimeOffset.UtcNow, null), null));
        }

        public Task<string?> SembrarCamposAsync(Guid id, Guid tenantId, Guid autor, IReadOnlyList<FieldValueInput> campos, bool soloFaltantes, CancellationToken ct)
        {
            Campos.AddRange(campos);
            SoloFaltantes.Add(soloFaltantes);
            return Task.FromResult(ErrorCampos);
        }

        public Task<string?> SembrarSistemaAsync(Guid id, Guid tenantId, IReadOnlyList<FieldValueInput> campos, bool soloFaltantes, CancellationToken ct)
        {
            Sistema.AddRange(campos);
            SoloFaltantes.Add(soloFaltantes);
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class AutorFalso : IAutorDerivacion
    {
        public bool Puede { get; set; } = true;
        public List<(Guid, Guid, string)> Consultas { get; } = [];

        public Task<bool> PuedeCrearAsync(Guid tenantId, Guid usuarioId, string familia, CancellationToken ct)
        {
            Consultas.Add((tenantId, usuarioId, familia));
            return Task.FromResult(Puede);
        }
    }

    private sealed class OficinasFalsas : ITransitOfficeResolver
    {
        public ResolvedTransitOffice? PorNombre { get; set; }
        public ResolvedTransitOffice? PorCodigo { get; set; }
        public List<string> CodigosConsultados { get; } = [];

        public Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(Guid tenantId, string transitOfficeName, CancellationToken cancellationToken = default) =>
            Task.FromResult(PorNombre);

        public Task<ResolvedTransitOffice?> ResolveEnabledByIdAsync(Guid tenantId, Guid transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResolvedTransitOffice?>(null);

        public Task<ResolvedTransitOffice?> ResolveEnabledByCodeAsync(Guid tenantId, string transitOfficeCode, CancellationToken cancellationToken = default)
        {
            CodigosConsultados.Add(transitOfficeCode);
            return Task.FromResult(PorCodigo);
        }
    }
}
