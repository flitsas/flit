using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace Flit.Admin.Tests.AdminOt;

/// <summary>
/// Épica #13216 (HU #13391, ADR-0070 adenda v4) — contrato HTTP de <c>POST /api/v1/admin/ot/consolidados/lotes</c>:
/// <c>OtModulePolicy</c> AND <c>consolidado-masivo.download</c>, tenant OT del claim (Super Admin: el dueño del
/// organismo de <c>?transitOfficeId</c>), origen <c>ot_bandeja</c> con el organismo, siempre
/// <c>consolidado_maestro</c>, y los códigos estables del motor (#13374) en la raíz del ProblemDetails.
/// Host real sin PostgreSQL: repositorios del lote y de la bandeja, cifrador y catálogo sustituidos; el resolver
/// <c>ot_bandeja</c> es el REAL (#13390) sobre el repositorio sustituido; <see cref="FlitDbContext"/> en memoria solo
/// para el perfil OT que lee <c>ResolveOtUserScopeAsync</c>.
/// <para>Uso de ejemplo: <c>POST /api/v1/admin/ot/consolidados/lotes { tipoDocumento: "consolidado_maestro",
/// confirmaEfectos: true, seleccion: { modo: "filtro", ids: [], excluidos: [], filtro: { condiciones: [...] } } }</c>
/// ⇒ <c>202 LoteConsolidados</c> en <c>en_cola</c>.</para>
/// </summary>
public sealed class ConsolidadoLoteOtEndpointTests : IClassFixture<ConsolidadoLoteOtEndpointTests.LoteOtFactory>
{
    private const string Ruta = "/api/v1/admin/ot/consolidados/lotes";
    private const string Permiso = "consolidado-masivo.download";

    internal static readonly Guid TenantOt = Guid.Parse("0e000000-0000-4000-8000-000000013391");
    internal static readonly Guid TenantOtAjeno = Guid.Parse("0e000000-0000-4000-8000-0000000133a2");
    private static readonly Guid TenantSuperAdmin = Guid.Parse("5a000000-0000-4000-8000-000000013391");
    private static readonly Guid TenantCompania = Guid.Parse("c0000000-0000-4000-8000-000000013391");
    internal static readonly Guid Organismo = Guid.Parse("07000000-0000-4000-8000-000000013391");
    internal static readonly Guid OrganismoAjeno = Guid.Parse("07000000-0000-4000-8000-0000000133a2");
    private static readonly Guid Cliente1 = Guid.Parse("c1000000-0000-4000-8000-000000013391");
    private static readonly Guid Cliente2 = Guid.Parse("c2000000-0000-4000-8000-000000013391");
    private static readonly Guid UsuarioId = Guid.Parse("13391000-0000-4000-8000-000000000001");

    private readonly LoteOtFactory _factory;

    public ConsolidadoLoteOtEndpointTests(LoteOtFactory factory)
    {
        _factory = factory;
        _factory.Reiniciar();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── AC1 — creación por el Admin OT ───────────────────────────────────────────────────

    [Fact]
    public async Task AC1_OtAdminConPermiso_FiltroDeLaBandeja_202_LoteOtBandejaDelTenantOtYSuOrganismo()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var excluido = Guid.NewGuid();
        _factory.Bandeja.ListAccessibleRefsAsync(TenantOt, Arg.Any<OtClientProcedureFilter?>(), null, Organismo, Arg.Any<CancellationToken>())
            .Returns([
                new OtClientProcedureRef(a, Cliente1, "FT1-1", "ABC123"),
                new OtClientProcedureRef(excluido, Cliente1, "FT1-2", null),
                new OtClientProcedureRef(b, Cliente2, "FT1-3", "XYZ987"),
            ]);

        var response = await Cliente(OtAdmin()).PostAsync(Ruta, Json(new
        {
            tipoDocumento = "consolidado_maestro",
            confirmaEfectos = true,
            seleccion = new
            {
                modo = "filtro",
                ids = Array.Empty<Guid>(),
                excluidos = new[] { excluido },
                filtro = new
                {
                    condiciones = new[] { new { fieldId = "placa", @operator = "es_alguno", values = new[] { "ABC123", "XYZ987" } } },
                    busqueda = "1020304050",
                    familia = "TRASPASO",
                    page = 1,
                    pageSize = 25,
                },
            },
        }), Ct);

        var cuerpo = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, cuerpo);
        response.Headers.Location!.ToString().Should().StartWith("/api/v1/consolidados/lotes/");
        using var json = JsonDocument.Parse(cuerpo);
        json.RootElement.GetProperty("estado").GetString().Should().Be("en_cola");
        json.RootElement.GetProperty("tipoDocumento").GetString().Should().Be("consolidado_maestro");
        json.RootElement.GetProperty("total").GetInt32().Should().Be(2);

        var nuevo = _factory.UltimoNuevo!;
        nuevo.Origen.Should().Be(ConsolidadoExportOrigin.OtBandeja);
        nuevo.TenantId.Should().Be(TenantOt);
        nuevo.OtTransitOfficeId.Should().Be(Organismo);
        nuevo.ScopeTenantId.Should().BeNull();
        nuevo.TipoDocumento.Should().Be(ConsolidadoExportDocumentType.ConsolidadoMaestro);
        nuevo.RolCodigo.Should().Be("ot_admin");
        nuevo.ModoSeleccion.Should().Be(ConsolidadoExportSelectionMode.Filtro);
        nuevo.Items.Select(i => (i.Id, i.TenantId)).Should().Equal((a, Cliente1), (b, Cliente2));
        nuevo.UserAgent.Should().Be("flit-tests/13391");
        await _factory.Bandeja.Received(1).ResolveTransitOfficeIdAsync(TenantOt, null, Arg.Any<CancellationToken>());
        await _factory.Bandeja.Received(1).ListAccessibleRefsAsync(TenantOt,
            Arg.Is<OtClientProcedureFilter?>(f => f!.Familia == "TRASPASO" && f.Condiciones!.Count == 1 && f.Busqueda == "1020304050"),
            null, Organismo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_SinTipoDocumento_UsaConsolidadoMaestro_YOtAdminConTransitOfficeIdLoIgnora()
    {
        var a = Guid.NewGuid();
        _factory.Bandeja.ListAccessibleRefsAsync(TenantOt, null, Arg.Any<IReadOnlyCollection<Guid>?>(), Organismo, Arg.Any<CancellationToken>())
            .Returns([new OtClientProcedureRef(a, Cliente1, "FT1-1", null)]);

        var response = await Cliente(OtAdmin()).PostAsync($"{Ruta}?transitOfficeId={OrganismoAjeno}", Json(new
        {
            confirmaEfectos = true,
            seleccion = new { modo = "ids", ids = new[] { a }, excluidos = Array.Empty<Guid>(), filtro = (object?)null },
        }), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        _factory.UltimoNuevo!.TipoDocumento.Should().Be(ConsolidadoExportDocumentType.ConsolidadoMaestro);
        _factory.UltimoNuevo.TenantId.Should().Be(TenantOt);
        _factory.UltimoNuevo.OtTransitOfficeId.Should().Be(Organismo);
        await _factory.Bandeja.Received(1).ResolveTransitOfficeIdAsync(TenantOt, null, Arg.Any<CancellationToken>());
        _factory.Catalogo.DidNotReceiveWithAnyArgs().Exists(default);
    }

    [Fact]
    public async Task AC1_TenantOtSinOrganismoResoluble_403_SinOrganismo_SinCrear()
    {
        _factory.Bandeja.ResolveTransitOfficeIdAsync(TenantOt, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var response = await Cliente(OtAdmin()).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("sin_organismo");
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    // ── AC2 — sin permiso ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_UsuarioOtSinRolOtAdminNiPermiso_403_SinLoteNiAuditoria()
    {
        var token = Token(TenantOt, "ot_operador", [], entityType: "TRANSIT_OFFICE");

        var response = await Cliente(token).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().ObtenerSettingsAsync(default);
    }

    [Fact]
    public async Task AC2_OtAdminConTokenEmitidoAntesDelGrant_403()
    {
        var response = await Cliente(OtAdmin(permisos: [])).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    [Fact]
    public async Task AC2_SinToken_401()
    {
        var response = await _factory.CreateClient().PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    // ── AC3 — usuario de compañía ────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_GestorDeCompaniaConElPermiso_403PorOtModulePolicy()
    {
        var response = await Cliente(Token(TenantCompania, "Radicador", [Permiso])).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
        await _factory.Bandeja.DidNotReceiveWithAnyArgs().ResolveTransitOfficeIdAsync(default, default, default);
    }

    // ── AC4 — Super Admin sin organismo ──────────────────────────────────────────────────

    [Fact]
    public async Task AC4_SuperAdminSinTransitOfficeId_400_TransitOfficeRequerido_SinLote()
    {
        var response = await Cliente(SuperAdmin()).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("transit_office_requerido");
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().ObtenerSettingsAsync(default);
    }

    [Fact]
    public async Task AC4_SuperAdminConTransitOfficeIdFueraDelCatalogo_400_SinLote()
    {
        _factory.Catalogo.Exists(Arg.Any<Guid>()).Returns(false);

        var response = await Cliente(SuperAdmin()).PostAsync($"{Ruta}?transitOfficeId={Organismo}", CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("transit_office_invalido");
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    // ── AC5 — Super Admin desde la bandeja de un OT (CF-10) ─────────────────────────────

    [Fact]
    public async Task AC5_SuperAdminConTransitOfficeId_202_TenantDelOtDuenoDelOrganismo_YRolSuperAdmin()
    {
        var a = Guid.NewGuid();
        _factory.Bandeja.ListAccessibleRefsAsync(TenantOtAjeno, null, Arg.Any<IReadOnlyCollection<Guid>?>(), OrganismoAjeno, Arg.Any<CancellationToken>())
            .Returns([new OtClientProcedureRef(a, Cliente2, "FT2-1", null)]);

        var response = await Cliente(SuperAdmin()).PostAsync($"{Ruta}?transitOfficeId={OrganismoAjeno}", CuerpoIds(a), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var nuevo = _factory.UltimoNuevo!;
        nuevo.TenantId.Should().Be(TenantOtAjeno, "el tenant OT dueño del organismo, no el del token del Super Admin");
        nuevo.TenantId.Should().NotBe(TenantSuperAdmin);
        nuevo.OtTransitOfficeId.Should().Be(OrganismoAjeno);
        nuevo.Origen.Should().Be(ConsolidadoExportOrigin.OtBandeja);
        nuevo.RolCodigo.Should().Be("SuperAdmin");
        nuevo.Items.Should().ContainSingle().Which.TenantId.Should().Be(Cliente2);
        await _factory.Bandeja.Received(1).ResolveTransitOfficeIdAsync(TenantOtAjeno, OrganismoAjeno, Arg.Any<CancellationToken>());
    }

    // ── AC6 — ID inyectado (CF-06) ───────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_IdsConDosDeOtroOrganismo_202_ConTotal3()
    {
        var deLaBandeja = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var ajenos = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _factory.Bandeja.ListAccessibleRefsAsync(TenantOt, null, Arg.Any<IReadOnlyCollection<Guid>?>(), Organismo, Arg.Any<CancellationToken>())
            .Returns(deLaBandeja.Select((id, i) => new OtClientProcedureRef(id, Cliente1, $"FT1-{i}", null)).ToList());

        var response = await Cliente(OtAdmin()).PostAsync(Ruta, CuerpoIds([.. deLaBandeja, .. ajenos]), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        (await Raiz(response)).GetProperty("total").GetInt32().Should().Be(3);
        _factory.UltimoNuevo!.Items.Select(i => i.Id).Should().Equal(deLaBandeja);
        await _factory.Bandeja.Received(1).ListAccessibleRefsAsync(TenantOt, null,
            Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids!.Count == 5), Organismo, Arg.Any<CancellationToken>());
    }

    // ── AC7 — validaciones de entrada ────────────────────────────────────────────────────

    [Theory]
    [InlineData("{\"tipoDocumento\":\"consolidado\",\"confirmaEfectos\":true,\"seleccion\":{\"modo\":\"ids\",\"ids\":[]}}", "tipo_no_permitido")]
    [InlineData("{\"tipoDocumento\":\"consolidado_maestro\",\"confirmaEfectos\":true,\"seleccion\":{\"modo\":\"filtro\",\"ids\":[],\"excluidos\":[],\"filtro\":{\"condiciones\":[{\"fieldId\":\"no_existe\",\"operator\":\"es_alguno\",\"values\":[\"x\"]}]}}}", "filtro_invalido")]
    [InlineData("{\"tipoDocumento\":\"consolidado_maestro\",\"confirmaEfectos\":true,\"seleccion\":{\"modo\":\"filtro\",\"ids\":[],\"excluidos\":[],\"filtro\":{\"familia\":\"NO_EXISTE\"}}}", "filtro_invalido")]
    [InlineData("{\"tipoDocumento\":\"consolidado_maestro\",\"seleccion\":{\"modo\":\"ids\",\"ids\":[]}}", "confirmacion_requerida")]
    [InlineData("{\"tipoDocumento\":\"consolidado_maestro\",\"confirmaEfectos\":false,\"seleccion\":{\"modo\":\"ids\",\"ids\":[]}}", "confirmacion_requerida")]
    [InlineData("{\"tipoDocumento\":\"consolidado_maestro\",\"confirmaEfectos\":true}", "seleccion_requerida")]
    [InlineData("{\"tipoDocumento\":\"consolidado_maestro\",\"confirmaEfectos\":true,\"seleccion\":{\"modo\":\"filtro\",\"ids\":[],\"excluidos\":[],\"filtro\":null}}", "seleccion_invalida")]
    public async Task AC7_CuerpoInvalido_400_ConCodigoEstable_SinLote(string cuerpo, string codigo)
    {
        var response = await Cliente(OtAdmin()).PostAsync(Ruta, new StringContent(cuerpo, Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
        (await Raiz(response)).GetProperty("error").GetString().Should().Be(codigo);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    [Fact]
    public async Task AC7_FiltroFueraDeCatalogo_NoLlegaALaBandeja()
    {
        var response = await Cliente(OtAdmin()).PostAsync(Ruta, new StringContent(
            "{\"tipoDocumento\":\"consolidado_maestro\",\"confirmaEfectos\":true,\"seleccion\":{\"modo\":\"filtro\",\"filtro\":{\"condiciones\":[{\"fieldId\":\"placa\",\"operator\":\"no_existe\",\"values\":[\"x\"]}]}}}",
            Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await _factory.Bandeja.DidNotReceiveWithAnyArgs().ListAccessibleRefsAsync(default, default, default, default, default);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    [Theory]
    [InlineData("ids")]
    [InlineData("excluidos")]
    public async Task AC7_MasDe10000IdsOExcluidos_422_SeleccionExcedeTope(string clave)
    {
        var muchos = Enumerable.Range(0, LoteSeleccionTopes.MaxIds + 1).Select(_ => Guid.NewGuid()).ToArray();
        object seleccion = clave == "ids"
            ? new { modo = "ids", ids = muchos, excluidos = Array.Empty<Guid>(), filtro = (object?)null }
            : new { modo = "filtro", ids = Array.Empty<Guid>(), excluidos = muchos, filtro = new { } };

        var response = await Cliente(OtAdmin()).PostAsync(Ruta, Json(new
        {
            tipoDocumento = "consolidado_maestro",
            confirmaEfectos = true,
            seleccion,
        }), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("seleccion_excede_tope");
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    // ── AC8 — lote activo ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC8_LoteActivoDeCualquierOrigen_409_ConLoteActivoIdEnLaRaiz()
    {
        var activo = Guid.NewGuid();
        _factory.Lotes.ObtenerLoteActivoIdAsync(UsuarioId, Arg.Any<CancellationToken>()).Returns(activo);

        var response = await Cliente(OtAdmin()).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var raiz = await Raiz(response);
        raiz.GetProperty("error").GetString().Should().Be("lote_activo");
        raiz.GetProperty("loteActivoId").GetGuid().Should().Be(activo);
        await _factory.Lotes.DidNotReceiveWithAnyArgs().CrearAsync(default!, default);
    }

    // ── AC9 — auditoría minimizada y atómica ─────────────────────────────────────────────

    [Fact]
    public async Task AC9_ResumenDelFiltro_LlevaElOrganismo_CondicionesContadas_YBusquedaSoloPresenteYLongitud()
    {
        _factory.Bandeja.ListAccessibleRefsAsync(TenantOt, Arg.Any<OtClientProcedureFilter?>(), null, Organismo, Arg.Any<CancellationToken>())
            .Returns([new OtClientProcedureRef(Guid.NewGuid(), Cliente1, "FT1-1", "ABC123")]);

        var response = await Cliente(OtAdmin()).PostAsync(Ruta, Json(new
        {
            tipoDocumento = "consolidado_maestro",
            confirmaEfectos = true,
            seleccion = new
            {
                modo = "filtro",
                ids = Array.Empty<Guid>(),
                excluidos = Array.Empty<Guid>(),
                filtro = new
                {
                    condiciones = new[] { new { fieldId = "placa", @operator = "es_alguno", values = new[] { "ABC123", "XYZ987", "QWE456" } } },
                    busqueda = "1020304050",
                },
            },
        }), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var resumen = _factory.UltimoNuevo!.ResumenFiltroJson!;
        resumen.Should().NotContain("ABC123").And.NotContain("1020304050", "sin placas ni documentos en la auditoría");
        var raiz = JsonDocument.Parse(resumen).RootElement;
        raiz.GetProperty("organismo").GetString().Should().Be(Organismo.ToString("D"));
        raiz.GetProperty("origenFiltro").GetString().Should().Be(ConsolidadoExportOrigin.OtBandeja);
        var condicion = raiz.GetProperty("filtro").GetProperty("condiciones")[0];
        condicion.GetProperty("campo").GetString().Should().Be("placa");
        condicion.GetProperty("operador").GetString().Should().Be("es_alguno");
        condicion.GetProperty("cantidad").GetInt32().Should().Be(3);
        var busqueda = raiz.GetProperty("filtro").GetProperty("busqueda");
        busqueda.GetProperty("presente").GetBoolean().Should().BeTrue();
        busqueda.GetProperty("longitud").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task AC9_AuditoriaNoRegistrada_503_LoteNoCreado()
    {
        _factory.ResultadoCrear = new CrearLoteResultado(CrearLoteEstado.NoCreado);

        var response = await Cliente(OtAdmin()).PostAsync(Ruta, CuerpoIds(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var raiz = await Raiz(response);
        raiz.GetProperty("error").GetString().Should().Be("lote_no_creado");
        raiz.GetProperty("detail").GetString().Should().Be(CrearLoteConsolidadosHandler.MensajeNoDisponible);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────

    private HttpClient Cliente(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("flit-tests/13391");
        return client;
    }

    private static StringContent CuerpoIds(params Guid[] ids) => Json(new
    {
        tipoDocumento = "consolidado_maestro",
        confirmaEfectos = true,
        seleccion = new { modo = "ids", ids, excluidos = Array.Empty<Guid>(), filtro = (object?)null },
    });

    private static StringContent Json(object cuerpo) =>
        new(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> Raiz(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string OtAdmin(string[]? permisos = null) =>
        Token(TenantOt, "ot_admin", permisos ?? [Permiso], entityType: "TRANSIT_OFFICE");

    /// <summary>Super Admin SIN el claim del permiso: pasa por el bypass de <c>RequirePermission</c>.</summary>
    private static string SuperAdmin() => Token(TenantSuperAdmin, "SuperAdmin", []);

    private static string Token(Guid tenantId, string role, string[] permisos, string? entityType = null)
    {
        var claims = new List<Claim>
        {
            new("sub", UsuarioId.ToString()),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
        };
        if (entityType is not null)
            claims.Add(new Claim("entity_type", entityType));
        claims.AddRange(permisos.Select(p => new Claim("permissions", p)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>
    /// Host con <see cref="IConsolidadoLoteRepository"/>, <see cref="IOtClientProcedureRepository"/>,
    /// <see cref="IConsolidadoLoteCipher"/> y <see cref="ITransitOfficeCatalog"/> sustituidos, y el
    /// <see cref="FlitDbContext"/> en memoria con el perfil del OT ajeno (Super Admin, AC5). El resolver
    /// <c>ot_bandeja</c> es el registrado por la infraestructura (#13390).
    /// </summary>
    public sealed class LoteOtFactory : WebApplicationFactory<Program>
    {
        private readonly string _baseEnMemoria = $"flit-13391-{Guid.NewGuid()}";

        public IConsolidadoLoteRepository Lotes { get; } = Substitute.For<IConsolidadoLoteRepository>();
        public IOtClientProcedureRepository Bandeja { get; } = Substitute.For<IOtClientProcedureRepository>();
        public IConsolidadoLoteCipher Cipher { get; } = Substitute.For<IConsolidadoLoteCipher>();
        public ITransitOfficeCatalog Catalogo { get; } = Substitute.For<ITransitOfficeCatalog>();
        public NuevoLoteConsolidados? UltimoNuevo { get; private set; }
        public CrearLoteResultado? ResultadoCrear { get; set; }

        public LoteOtFactory() => Reiniciar();

        public void Reiniciar()
        {
            Lotes.ClearSubstitute();
            Bandeja.ClearSubstitute();
            Catalogo.ClearSubstitute();
            UltimoNuevo = null;
            ResultadoCrear = null;

            Cipher.GenerarDekEnvuelta().Returns([1, 2, 3]);
            Catalogo.Exists(Arg.Any<Guid>()).Returns(true);
            Bandeja.ResolveTransitOfficeIdAsync(TenantOt, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(Organismo);
            Bandeja.ResolveTransitOfficeIdAsync(TenantOtAjeno, OrganismoAjeno, Arg.Any<CancellationToken>()).Returns(OrganismoAjeno);
            Bandeja.ListAccessibleRefsAsync(Arg.Any<Guid>(), Arg.Any<OtClientProcedureFilter?>(), Arg.Any<IReadOnlyCollection<Guid>?>(),
                    Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<OtClientProcedureRef>());
            Lotes.ObtenerSettingsAsync(Arg.Any<CancellationToken>()).Returns(new ConsolidadoExportSettings { IsActive = true });
            Lotes.ObtenerLoteActivoIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);
            Lotes.CrearAsync(Arg.Any<NuevoLoteConsolidados>(), Arg.Any<CancellationToken>()).Returns(ci =>
            {
                var nuevo = ci.Arg<NuevoLoteConsolidados>();
                UltimoNuevo = nuevo;
                return ResultadoCrear ?? new CrearLoteResultado(CrearLoteEstado.Creado, new ConsolidadoExportBatch
                {
                    Id = Guid.NewGuid(),
                    TenantId = nuevo.TenantId,
                    RequestedByUserId = nuevo.UsuarioId,
                    RequestedRoleCode = nuevo.RolCodigo,
                    Origin = nuevo.Origen,
                    OtTransitOfficeId = nuevo.OtTransitOfficeId,
                    DocumentType = nuevo.TipoDocumento,
                    SelectionMode = nuevo.ModoSeleccion,
                    Status = ConsolidadoExportStatus.EnCola,
                    TotalItems = nuevo.Items.Count,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Lotes);
                services.RemoveAll<IOtClientProcedureRepository>();
                services.AddScoped(_ => Bandeja);
                services.AddSingleton(Cipher);
                services.RemoveAll<ITransitOfficeCatalog>();
                services.AddScoped(_ => Catalogo);
                services.AddScoped<ITenantScopeResolver>(_ => new SingleScope());

                // ResolveOtUserScopeAsync lee admin.transit_office_profiles: base en memoria con el perfil del OT ajeno.
                var opciones = new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(_baseEnMemoria).Options;
                services.RemoveAll<FlitDbContext>();
                services.AddScoped(_ => new FlitDbContext(opciones));
                using var semilla = new FlitDbContext(opciones);
                if (!semilla.TransitOfficeProfiles.Any())
                {
                    semilla.TransitOfficeProfiles.Add(new TransitOfficeProfile
                    {
                        Id = Guid.NewGuid(),
                        TenantId = TenantOtAjeno,
                        TransitOfficeId = OrganismoAjeno,
                        OperationMode = "dashboard",
                        CreatedAt = DateTimeOffset.UtcNow,
                    });
                    semilla.SaveChanges();
                }
            });
        }
    }

    private sealed class SingleScope : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantScope.Single(tenantId));
    }
}
