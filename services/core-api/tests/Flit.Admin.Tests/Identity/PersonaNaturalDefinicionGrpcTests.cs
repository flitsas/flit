using System.Text.Json;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Cliente = Flit.Tramites.Grpc.V1.PersonaNaturalService.PersonaNaturalServiceClient;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #14078 (Feature K, Épica #14021) — <c>ObtenerDefinicionTipoTramite</c> por la fachada gRPC, de punta a punta con el
/// servidor OIDC real de los tests. Cada prueba arma tipos, pasos, documentos y organismos propios (códigos únicos) y los
/// retira al terminar; requiere la base con las migraciones aplicadas.
/// </summary>
public sealed class PersonaNaturalDefinicionGrpcTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Scope = "platform.tramites.persona_natural";
    private const string Portal = "svc-personanatural";

    private const string PerfilTraspaso =
        """{"entryMode":"PLATE","requiresSeller":true,"requiresBuyer":true,"allowsMultipleBuyer":true,"requiresCommercialValue":true,"requiresBiometrics":true,"biometricActors":["OWNER","BUYER"],"requiresSignature":true}""";

    private const string PerfilUnTitular =
        """{"entryMode":"PLATE","requiresBuyer":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true}""";

    private const string PerfilPrenda =
        """{"entryMode":"PLATE","requiresBuyer":true,"hasPrendaGate":true}""";

    [Fact]
    public async Task Traspaso_DevuelveSusPasosCamposActoresDocumentosYFlags_DesdeLoQueFlit2YaTiene()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var d = (await cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoTraspaso, e.Organismo), cancellationToken: ct)).Definicion;

        d.Codigo.Should().Be(e.CodigoTraspaso);
        d.Familia.Should().Be("TRASPASO");
        d.Version.Should().Be(1);
        d.DisponiblePn.Should().BeTrue();
        d.MotivoNoDisponible.Should().BeEmpty();
        d.Huella.Should().HaveLength(64);
        d.Pasos.Select(p => p.Clave).Should().NotContain("oculto", "un paso inactivo no sale");
    }

    [Fact]
    public async Task Traspaso_LosPasosVanEnOrden_ConSusSectionTypes_YSoloLosCamposQueDigitaElCiudadano()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var d = (await cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoTraspaso, e.Organismo), cancellationToken: ct)).Definicion;

        d.Pasos.Select(p => p.Clave).Should().Equal("consulta", "actores", "datos");
        d.Pasos[0].SectionTypes.Should().Equal("vehicle_query");
        d.Pasos[1].SectionTypes.Should().Equal("actor_form", "biometric");
        var datos = d.Pasos[2];
        datos.SectionTypes.Should().Equal("generic_form");
        datos.Campos.Select(c => c.FieldKey).Should().Equal("km", "uso").And.NotContain("precargado", "los campos bloqueados no los digita el ciudadano");
        datos.Campos[0].Obligatorio.Should().BeTrue();
        datos.Campos[1].Opciones.Should().Equal("particular", "publico");
    }

    [Fact]
    public async Task Traspaso_LosActoresSalenDelPerfil_ConCopropietariosYBiometriaDeAmbasPartes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var d = (await cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoTraspaso, e.Organismo), cancellationToken: ct)).Definicion;

        d.Actores.Select(a => a.Rol).Should().Equal("vendedor", "comprador");
        d.Actores.Should().OnlyContain(a => a.Biometria && a.Firma && a.Min == 1 && a.PersonaNaturalPermitida);
        d.Actores.Single(a => a.Rol == "comprador").Max.Should().Be(4);
        d.Actores.Single(a => a.Rol == "vendedor").Max.Should().Be(1);
        d.Flags.EntryMode.Should().Be("PLATE");
        d.Flags.RequiereComercial.Should().BeTrue();
        d.Flags.RequiereBiometria.Should().BeTrue();
        d.Flags.PermiteTransformaciones.Should().BeTrue();
    }

    [Fact]
    public async Task LosDocumentos_SalenConLaPrecedenciaDelOrganismoSobreLaMatrizPorDefecto_ConSuInstruccionYSinBuzonesInformativos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var conOrganismo = (await cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoTraspaso, e.Organismo), cancellationToken: ct)).Definicion;
        var porDefecto = (await cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoTraspaso, null), cancellationToken: ct)).Definicion;

        porDefecto.Documentos.Select(x => x.DocCode).Should().Equal(e.DocA, e.DocB);
        conOrganismo.Documentos.Select(x => x.DocCode).Should().Equal(e.DocB, e.DocA);
        var a = conOrganismo.Documentos.Single(x => x.DocCode == e.DocA);
        a.Obligatorio.Should().BeTrue();
        a.Nombre.Should().Be("Documento A de prueba");
        a.InstruccionCargue.Should().Be("Suba el documento A legible.");
        conOrganismo.Documentos.Should().NotContain(x => x.DocCode == e.DocBuzon, "un buzón informativo no lo carga el ciudadano");
        conOrganismo.Documentos.Select(x => x.Orden).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task UnTipoDeUnTitularYUnoDePrenda_TienenSusPropiosActoresYCapacidades_SinCodigoPorTipo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var titular = (await cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoUnTitular, e.Organismo), cancellationToken: ct)).Definicion;
        var prenda = (await cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoPrenda, e.Organismo), cancellationToken: ct)).Definicion;

        titular.Actores.Should().ContainSingle().Which.Etiqueta.Should().Be("Titular");
        titular.Flags.PermiteTransformaciones.Should().BeFalse("la familia OTROS no acumula transformaciones");
        titular.DisponiblePn.Should().BeTrue();
        prenda.Flags.RequierePrenda.Should().BeTrue();
        prenda.DisponiblePn.Should().BeFalse("exige gravamen o acreedor prendario");
        prenda.MotivoNoDisponible.Should().Contain("prendario");
    }

    [Fact]
    public async Task ElPerfilCrudoNoSale_SoloLasCapacidadesConNombresDelContrato()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var d = (await cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoTraspaso, e.Organismo), cancellationToken: ct)).Definicion;

        var texto = Google.Protobuf.JsonFormatter.Default.Format(d);
        texto.Should().NotContainAny("requiresSeller", "allowsMultipleBuyer", "biometricActors", "validateOtOperability", "gate_profile");
    }

    [Fact]
    public async Task LaHuella_EsEstableYCambiaSiCambiaLaMatrizDocumental()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var uno = (await cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoTraspaso, e.Organismo), cancellationToken: ct)).Definicion;
        var dos = (await cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoTraspaso, e.Organismo), cancellationToken: ct)).Definicion;
        await e.HacerOpcionalAsync(e.DocA, ct);
        var tres = (await cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoTraspaso, e.Organismo), cancellationToken: ct)).Definicion;

        dos.Huella.Should().Be(uno.Huella);
        tres.Huella.Should().NotBe(uno.Huella);
        tres.Version.Should().Be(uno.Version, "la versión del tipo no cambió; por eso existe la huella");
    }

    [Fact]
    public async Task TipoDesconocido_TipoConWizardApagado_YOrganismoNoHabilitado_TienenSuPropioErrorYNadaParcial()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var desconocido = await Error(() => cliente.ObtenerDefinicionTipoTramiteAsync(Pedir("NO_EXISTE_" + Guid.NewGuid().ToString("N"), e.Organismo), cancellationToken: ct).ResponseAsync);
        var apagado = await Error(() => cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoApagado, e.Organismo), cancellationToken: ct).ResponseAsync);
        var sinGrant = await Error(() => cliente.ObtenerDefinicionTipoTramiteAsync(Pedir(e.CodigoTraspaso, e.OrganismoSinGrant), cancellationToken: ct).ResponseAsync);

        PlatformRpcErrors.Reason(desconocido).Should().Be("tipo_no_disponible");
        PlatformRpcErrors.Reason(apagado).Should().Be("tipo_no_disponible");
        PlatformRpcErrors.Reason(sinGrant).Should().Be("organismo_no_habilitado");
    }

    [Fact]
    public async Task ValidaLaEntrada_YElCandadoDeClienteYEmpresaVaPrimero()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var bueno = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);
        var ajeno = await ClienteAsync(host, Guid.NewGuid(), ct);

        (await Error(() => bueno.ObtenerDefinicionTipoTramiteAsync(new ObtenerDefinicionTipoTramiteRequest(), cancellationToken: ct).ResponseAsync)).StatusCode.Should().Be(StatusCode.InvalidArgument);
        (await Error(() => bueno.ObtenerDefinicionTipoTramiteAsync(Pedir("X", null, "no-guid"), cancellationToken: ct).ResponseAsync)).StatusCode.Should().Be(StatusCode.InvalidArgument);
        PlatformRpcErrors.Reason(await Error(() => ajeno.ObtenerDefinicionTipoTramiteAsync(Pedir("TRASPASO_STANDARD", null), cancellationToken: ct).ResponseAsync)).Should().Be("invalid_tenant");
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static ObtenerDefinicionTipoTramiteRequest Pedir(string codigo, Guid? organismo, string? organismoTexto = null) =>
        new() { Codigo = codigo, OrganismoId = organismoTexto ?? organismo?.ToString() ?? string.Empty };

    private static async Task<RpcException> Error(Func<Task> llamada) => (await llamada.Should().ThrowAsync<RpcException>()).Which;

    private WebApplicationFactory<Program> Host() =>
        OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("PersonaNatural:Fachada:Habilitada", "true");
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Secret", OidcServerTests.ServiceSecret);
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Scopes:0", Scope);
        });

    private static async Task<Cliente> ClienteAsync(WebApplicationFactory<Program> host, Guid empresa, CancellationToken ct)
    {
        using var http = host.CreateClient();
        http.DefaultRequestHeaders.Add("X-Flit-Domain", OidcServerTests.Hub);
        var response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Portal,
            ["client_secret"] = OidcServerTests.ServiceSecret,
            ["scope"] = Scope,
        }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = json.RootElement.GetProperty("access_token").GetString()!;
        var canal = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = host.Server.CreateHandler() });
        return new Cliente(canal.CreateCallInvoker().Intercept(metadata =>
        {
            metadata.Add("authorization", $"Bearer {token}");
            metadata.Add("x-flit-tenant-id", empresa.ToString());
            return metadata;
        }));
    }

    /// <summary>Tipos, pasos, documentos y organismos de una prueba, con códigos únicos; se retiran al terminar.</summary>
    private sealed class Escenario : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly FlitDbContext _db;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        private readonly List<Guid> _tipos = [];
        private readonly List<Guid> _documentos = [];

        public Guid Organismo { get; } = Guid.NewGuid();
        public Guid OrganismoSinGrant { get; } = Guid.NewGuid();
        public Guid TipoTraspaso { get; } = Guid.NewGuid();
        public string CodigoTraspaso => "PN_D_" + _sufijo + "_A";
        public string CodigoUnTitular => "PN_D_" + _sufijo + "_B";
        public string CodigoPrenda => "PN_D_" + _sufijo + "_C";
        public string CodigoApagado => "PN_D_" + _sufijo + "_D";
        public string DocA => "pn_doc_a_" + _sufijo.ToLowerInvariant();
        public string DocB => "pn_doc_b_" + _sufijo.ToLowerInvariant();
        public string DocBuzon => "pn_doc_buzon_" + _sufijo.ToLowerInvariant();
        private Guid _docAId;

        private Escenario(IServiceScope scope)
        {
            _scope = scope;
            _db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        }

        public static async Task<Escenario> CrearAsync(WebApplicationFactory<Program> host, CancellationToken ct)
        {
            var e = new Escenario(host.Services.CreateScope());
            var db = e._db;
            foreach (var (id, n) in new[] { (e.Organismo, "A"), (e.OrganismoSinGrant, "B") })
            {
                var code = $"PND{e._sufijo}{n}";
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, city_name, department_name)
                    VALUES ({id}, {code}, {"Organismo de prueba " + code}, '99', '999', 'Municipio de prueba', 'Departamento de prueba')
                    """, ct);
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.tenant_transit_office_grants (tenant_id, transit_office_id, is_enabled, source)
                VALUES ({PlatformTenants.PersonaNatural}, {e.Organismo}, true, 'CLIENT')
                """, ct);

            await e.TipoAsync(e.TipoTraspaso, e.CodigoTraspaso, "TRASPASO", PerfilTraspaso, wizard: true, ct);
            await e.TipoAsync(Guid.NewGuid(), e.CodigoUnTitular, "OTROS", PerfilUnTitular, wizard: true, ct);
            await e.TipoAsync(Guid.NewGuid(), e.CodigoPrenda, "OTROS", PerfilPrenda, wizard: true, ct);
            await e.TipoAsync(Guid.NewGuid(), e.CodigoApagado, "OTROS", PerfilUnTitular, wizard: false, ct);

            // Pasos: uno inactivo (no sale) y campos bloqueados (no los digita el ciudadano).
            e.Paso(e.TipoTraspaso, "consulta", 1, true, [("vehicle_query", [])]);
            e.Paso(e.TipoTraspaso, "actores", 2, true, [("actor_form", []), ("biometric", [])]);
            e.Paso(e.TipoTraspaso, "oculto", 3, false, [("generic_form", [])]);
            e.Paso(e.TipoTraspaso, "datos", 4, true,
            [
                ("generic_form", [
                    new FormField { FieldKey = "uso", Label = "Uso", FieldType = "select", Options = """["particular","publico"]""", SortOrder = 2 },
                    new FormField { FieldKey = "km", Label = "Kilometraje", FieldType = "number", IsRequired = true, SortOrder = 1 },
                    new FormField { FieldKey = "precargado", Label = "Precargado", FieldType = "text", IsLocked = true, SortOrder = 3 },
                ]),
            ]);

            e._docAId = await e.DocumentoAsync(e.DocA, "Documento A de prueba", "Suba el documento A legible.", mandatory: true, orden: 1, dummy: false, ct);
            await e.DocumentoAsync(e.DocB, "Documento B de prueba", null, mandatory: false, orden: 2, dummy: false, ct);
            await e.DocumentoAsync(e.DocBuzon, "Buzón informativo", null, mandatory: false, orden: 3, dummy: true, ct);
            // El organismo pone el B antes que el A.
            var docB = await db.DocumentTypes.Where(d => d.Code == e.DocB).Select(d => d.Id).SingleAsync(ct);
            db.DocumentOrderOverrides.Add(new DocumentOrderOverride
            {
                Id = Guid.NewGuid(), ProcedureTypeId = e.TipoTraspaso, DocumentTypeId = docB, ScopeType = DocumentOrderScope.Ot, ScopeRefId = e.Organismo, SortOrder = 0, CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return e;
        }

        private async Task TipoAsync(Guid id, string code, string familia, string perfil, bool wizard, CancellationToken ct)
        {
            _tipos.Add(id);
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, wizard_enabled, created_at)
                VALUES ({id}, {code}, {"Tipo de prueba " + code}, {familia}, 1, {perfil}::jsonb, 'published', now(), true, {wizard}, now())
                """, ct);
        }

        private void Paso(Guid tipo, string clave, short orden, bool activo, (string Tipo, FormField[] Campos)[] secciones)
        {
            var paso = new ProcedureStep { Id = Guid.NewGuid(), ProcedureTypeId = tipo, Code = clave, Title = "Paso " + clave, SortOrder = orden, IsActive = activo, CreatedAt = DateTimeOffset.UtcNow };
            short n = 0;
            foreach (var (sectionType, campos) in secciones)
            {
                var s = new ProcedureSection { Id = Guid.NewGuid(), ProcedureStepId = paso.Id, Code = $"{clave}-{n}", Title = "Sección", SortOrder = ++n, Layout = "single", SectionType = sectionType, CreatedAt = DateTimeOffset.UtcNow };
                foreach (var c in campos)
                {
                    c.Id = Guid.NewGuid();
                    c.ProcedureSectionId = s.Id;
                    c.CreatedAt = DateTimeOffset.UtcNow;
                    s.FormFields.Add(c);
                }
                paso.Sections.Add(s);
            }
            _db.ProcedureSteps.Add(paso);
            _db.SaveChanges();
        }

        private async Task<Guid> DocumentoAsync(string code, string nombre, string? instruccion, bool mandatory, short orden, bool dummy, CancellationToken ct)
        {
            var doc = new DocumentType
            {
                Id = Guid.NewGuid(), Code = code, Name = nombre, UploadInstructions = instruccion, MimeTypesAllowed = ["application/pdf"], MaxSizeBytes = 1_000_000, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
            };
            _documentos.Add(doc.Id);
            _db.DocumentTypes.Add(doc);
            _db.ProcedureDocumentRequirements.Add(new ProcedureDocumentRequirement
            {
                Id = Guid.NewGuid(), ProcedureTypeId = TipoTraspaso, DocumentTypeId = doc.Id, IsMandatory = mandatory, DefaultSortOrder = orden, IsDummy = dummy, CreatedAt = DateTimeOffset.UtcNow,
            });
            await _db.SaveChangesAsync(ct);
            return doc.Id;
        }

        public Task<int> HacerOpcionalAsync(string code, CancellationToken ct) =>
            _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE tramites.procedure_document_requirements SET is_mandatory = false
                 WHERE procedure_type_id = {TipoTraspaso} AND document_type_id = {_docAId}
                """, ct);

        public async ValueTask DisposeAsync()
        {
            try
            {
                var orgs = new[] { Organismo, OrganismoSinGrant };
                var tipos = _tipos.ToArray();
                var docs = _documentos.ToArray();
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.document_order_overrides WHERE procedure_type_id = ANY({tipos})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_document_requirements WHERE procedure_type_id = ANY({tipos})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.document_types WHERE id = ANY({docs})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.form_fields WHERE procedure_section_id IN (SELECT s.id FROM tramites.procedure_sections s JOIN tramites.procedure_steps p ON p.id = s.procedure_step_id WHERE p.procedure_type_id = ANY({tipos}))");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_sections WHERE procedure_step_id IN (SELECT id FROM tramites.procedure_steps WHERE procedure_type_id = ANY({tipos}))");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_steps WHERE procedure_type_id = ANY({tipos})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_transit_office_grants WHERE transit_office_id = ANY({orgs})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_types WHERE id = ANY({tipos})");
                await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM catalogs.transit_offices WHERE id = ANY({orgs})");
            }
            finally
            {
                _scope.Dispose();
            }
        }
    }
}
