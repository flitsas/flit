using System.Text;
using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.Certifications;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class FurHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IFurDocumentGenerator _generator = new MockFurDocumentGenerator();
    private readonly FakeCertClient _certClient = new();
    private readonly IRuesCertificateGenerator _ruesGenerator = Substitute.For<IRuesCertificateGenerator>();
    private readonly IRnmcCertificateGenerator _rnmcGenerator = Substitute.For<IRnmcCertificateGenerator>();
    private readonly IProcedureInstancePrendaRepository _prendaRepo = Substitute.For<IProcedureInstancePrendaRepository>();
    private readonly FakeStorage _storage = new();
    private readonly GenerarFurHandler _handler;

    /// <summary>Datos con los que el handler invocó al generador RNMC; null si no lo invocó.</summary>
    private RnmcCertificateData? CapturedRnmcData =>
        _rnmcGenerator.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IRnmcCertificateGenerator.GenerateRnmcCertificate))
            .Select(c => (RnmcCertificateData)c.GetArguments()[0]!)
            .LastOrDefault();

    public FurHandlerTests()
    {
        _ruesGenerator.GenerateRuesCertificate(Arg.Any<RuesCertificateData>())
            .Returns(ci =>
            {
                var d = ci.Arg<RuesCertificateData>();
                return new GeneratedDocument("certificado_rues", $"certificado_rues_{d.Nit}.pdf",
                    "application/pdf", Encoding.UTF8.GetBytes($"RUES {d.RazonSocial} {d.Nit} {d.Estado}"));
            });
        _rnmcGenerator.GenerateRnmcCertificate(Arg.Any<RnmcCertificateData>())
            .Returns(ci =>
            {
                var d = ci.Arg<RnmcCertificateData>();
                return new GeneratedDocument("certificado_rnmc", $"certificado_rnmc_{d.ReferenceNumber}.pdf",
                    "application/pdf", Encoding.UTF8.GetBytes($"%PDF RNMC {d.Entradas.Count}"));
            });
        // HU #11305 — el lector documental va en el handler por defecto, con el almacén canónico
        // VACÍO: así estas pruebas ejercitan el respaldo real sobre field_values, que es el camino de
        // los trámites anteriores al despliegue. Sin lector no habría certificaciones en absoluto.
        _handler = new GenerarFurHandler(_repo, _generator, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage,
            NullLogger<GenerarFurHandler>.Instance,
            certificationReader: new CertificationReader(new AlmacenCertificacionesVacio()));
    }

    /// <summary>
    /// Cliente de certificado Kyverum de prueba: por defecto devuelve un PDF; configurable para simular
    /// "sin certificado" (null / 404) o un fallo (excepción transitoria).
    /// </summary>
    private sealed class FakeCertClient : IKyverumCertificateClient
    {
        public bool ReturnNull { get; set; }
        public Exception? Throw { get; set; }
        public List<string> RequestedIds { get; } = [];

        public Task<KyverumCertificate?> DownloadCertificateAsync(string verificationId, CancellationToken ct = default)
        {
            RequestedIds.Add(verificationId);
            if (Throw is not null)
                throw Throw;
            if (ReturnNull)
                return Task.FromResult<KyverumCertificate?>(null);
            return Task.FromResult<KyverumCertificate?>(
                new KyverumCertificate(Encoding.UTF8.GetBytes("%PDF-1.4 fake"), "application/pdf", $"certificado_{verificationId}.pdf"));
        }
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public List<string> Deleted { get; } = [];

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_{Saved.Count}";
            Saved.Add(path);
            return new StoredFile(path, $"sha-{tipo}", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Deleted.Add(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string tipologia) =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For(tipologia ?? (tipologia == TramiteTipologiaCatalog.CodigoTraspasoStandard ? "traspaso" : "matricula_inicial")),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Setea el organismo de tránsito (transit_office_code) para satisfacer el gate.</summary>
    private static void WithOrganismo(ProcedureInstance instance) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });

    private static void WithField(ProcedureInstance instance, string key, string value) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = key,
            ValueText = value,
            Source = "user",
        });

    private sealed class FakeSoatRtmGenerator : ISoatRtmCertificateGenerator
    {
        public SoatRtmCertificateData? LastData { get; private set; }

        public GeneratedDocument GenerateSoatRtmCertificate(SoatRtmCertificateData data)
        {
            LastData = data;
            return new GeneratedDocument("certificado_soat_rtm", "certificado_soat_rtm.pdf", "application/pdf",
                Encoding.UTF8.GetBytes("SOAT_RTM"));
        }
    }

    private static ProcedureInstanceBiometricValidation Bio(string? parte) =>
        new()
        {
            Id = Guid.NewGuid(),
            PartyRole = parte,
            Status = BiometricEstados.Aprobado,
            // Provider kyverum + id ⇒ GenerarFurHandler descarga el certificado real de esa parte.
            Provider = BiometricProviders.Kyverum,
            KyverumVerificationId = $"kyv-{parte ?? "titular"}",
            Name = "X",
            DocumentType = "CC",
            DocumentNumber = "1",
            Email = "x@y.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Generar_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithFurGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Generar_Migrado_NoRegeneraYRetornaSoloLectura()
    {
        // Migración V1→V2: un trámite migrado es una foto de solo lectura. Aunque tenga organismo y
        // biométrica (que normalmente dispararían la generación), NO se regenera ni se sobreescriben
        // los PDFs históricos: se devuelve 'migrado_solo_lectura' sin tocar el storage.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.IsMigrated = true;
        instance.Status = TramiteEstado.Aprobado;
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio("comprador"));
        instance.BiometricValidations.Add(Bio("vendedor"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().Be("migrado_solo_lectura");
        result.Should().BeNull();
        _storage.Saved.Should().BeEmpty();   // no generó nada
        _storage.Deleted.Should().BeEmpty(); // no borró/sobreescribió los PDFs migrados
    }

    [Fact]
    public async Task Generar_Traspaso_WithoutBiometria_GeneratesFurNoFirmadoWithoutCertificate()
    {
        // HU #10463 AC1/AC5: sin validación (falta vendedor) el FUR se genera igual (NO biometria_gate)
        // y NO se emite el certificado de identidad.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio("comprador")); // falta vendedor -> identidad no válida
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        // FUR + compraventa (traspaso), SIN certificado_identidad.
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur", "compraventa"]);
        instance.Events.Should().ContainSingle(e => e.Tipo == "fur_generado");
    }

    [Fact]
    public async Task Generar_InvalidaConsolidadosPersistidos()
    {
        // HU #10860 (ADR-0032): el consolidado embebe el FUR/certificados que se acaban de regenerar,
        // así que regenerar el FUR debe invalidar ambos consolidados persistidos (maestro + wizard) para
        // que la próxima petición del consolidado los reconstruya con el FUR fresco.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.ConsolidadoWizardVigente = true;
        instance.ConsolidadoMaestroVigente = true;
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        instance.ConsolidadoWizardVigente.Should().BeFalse();
        instance.ConsolidadoMaestroVigente.Should().BeFalse();
    }

    [Fact]
    public async Task Generar_Traspaso_ConCompraventaDelUsuario_AutogeneraLaDelSistemaYConservaLaDelUsuario()
    {
        // ADR-0035 (supersede ADR-0031): la compraventa del sistema se genera SIEMPRE en traspaso, aunque
        // el usuario haya cargado la suya; la del usuario (Source=user) se conserva intacta y ambas coexisten.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        var userCompraventaId = Guid.NewGuid();
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = userCompraventaId,
            TenantId = tenant,
            ProcedureInstanceId = id,
            Tipo = "compraventa",
            Filename = "compraventa_usuario.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 3,
            Sha256 = "sha-user-cv",
            StoragePath = $"{id:D}/compraventa_user",
            Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        // El sistema SÍ genera su compraventa (ADR-0035).
        result!.Documents.Select(d => d.Tipo).Should().Contain("compraventa");
        // Y la del usuario sigue intacta: ambas coexisten como adjuntos del expediente.
        var compraventas = instance.Attachments.Where(a => a.Tipo == "compraventa").ToList();
        compraventas.Should().HaveCount(2);
        compraventas.Should().ContainSingle(a => a.Id == userCompraventaId && a.Source == "user");
        compraventas.Should().ContainSingle(a => a.Source == "system");
    }

    [Fact]
    public async Task Generar_ConVencimientosSoatRtm_GeneraCertificadoCombinado()
    {
        // HU #10856: con soat_vencimiento/rtm_vencimiento (RUNT) se emite UN certificado combinado
        // "Certificado de vigencia SOAT Y RTM" (Source=system, tipo certificado_soat_rtm).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        WithField(instance, "soat_vencimiento", "2027-01-15");
        WithField(instance, "soat_aseguradora", "La Previsora S.A.");
        WithField(instance, "rtm_vencimiento", "2027-03-20");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        var fakeSoatRtm = new FakeSoatRtmGenerator();
        var handler = new GenerarFurHandler(
            _repo, _generator, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage,
            NullLogger<GenerarFurHandler>.Instance, soatRtmGenerator: fakeSoatRtm,
            certificationReader: new CertificationReader(new AlmacenCertificacionesVacio()));

        var (result, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().Contain("certificado_soat_rtm");
        // Matrícula → sin bloque de avalúo, sin tabla RTM; entidad SOAT proyectada.
        fakeSoatRtm.LastData!.Avaluo.Should().BeNull();
        fakeSoatRtm.LastData!.Rtm.Should().BeNull();
        fakeSoatRtm.LastData!.Soat.Entidad.Should().Be("La Previsora S.A.");
    }

    // ── HU #11136 — la RTM solo aplica a vehículos con más de 5 años ─────────

    private async Task<FakeSoatRtmGenerator> GenerarTraspasoConFechaMatricula(string? fechaMatricula)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        WithField(instance, "soat_vencimiento", "2027-01-15");
        WithField(instance, "rtm_vencimiento", "2027-03-20");
        if (fechaMatricula is not null)
            WithField(instance, "vehicle_registration_date", fechaMatricula);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var fake = new FakeSoatRtmGenerator();
        var handler = new GenerarFurHandler(
            _repo, _generator, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage,
            NullLogger<GenerarFurHandler>.Instance, soatRtmGenerator: fake,
            certificationReader: new CertificationReader(new AlmacenCertificacionesVacio()));

        var (_, error) = await handler.HandleAsync(id, tenant, ct);
        error.Should().BeNull();
        return fake;
    }

    [Fact]
    public async Task Generar_TraspasoDeVehiculoAntiguo_IncluyeLaTablaDeRtm()
    {
        var fake = await GenerarTraspasoConFechaMatricula("15/03/2015");

        fake.LastData!.Rtm.Should().NotBeNull();
        fake.LastData.Rtm!.FechaVencimiento.Should().Be("2027/03/20",
            "HU #11305 — el certificado imprime el día normalizado; antes salía el crudo del proveedor");
    }

    [Fact]
    public async Task Generar_TraspasoDeVehiculoReciente_OmiteLaTablaDeRtmSinTocarElResto()
    {
        // Antes la tabla se pintaba en TODO traspaso, sin mirar la antigüedad del vehículo.
        var fake = await GenerarTraspasoConFechaMatricula("15/03/2025");

        fake.LastData!.Rtm.Should().BeNull();
        fake.LastData.Soat.FechaVencimiento.Should().Be("2027/01/15", "el bloque SOAT no cambia");
    }

    [Fact]
    public async Task Generar_TraspasoSinFechaDeMatricula_IncluyeLaTablaDeRtm()
    {
        // Fallo seguro: hay proveedores de RUNT que no reportan la fecha de matrícula.
        var fake = await GenerarTraspasoConFechaMatricula(null);

        fake.LastData!.Rtm.Should().NotBeNull();
    }

    [Fact]
    public async Task Generar_Matricula_WithoutBiometria_GeneratesOnlyFurNoCertificate()
    {
        // HU #10463 AC1/AC5: matrícula sin biométrica del comprador → FUR sin certificado.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance); // sin biométrica

        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur"]);
        instance.Events.Should().ContainSingle(e => e.Tipo == "fur_generado");
    }

    /// <summary>
    /// HU #11641 — la casilla de subtrámite se marca con la BANDERA declarada aunque no haya diff que
    /// calcular. Es el caso que dejaba el FUR mudo: si el RUNT no devolvió el valor original no hay
    /// snapshot contra el que comparar, pero el gestor declaró el cambio y el wizard se lo muestra
    /// como subtrámite activo. El formulario debe decir lo mismo que la pantalla.
    /// </summary>
    [Fact]
    public async Task Generar_TransformacionDeclaradaSinSnapshotRunt_MarcaLaCasilla()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        AddFieldValue(instance, "vehicle_color", "AZUL");   // sin vehicle_color_runt: no hay diff
        AddFieldValue(instance, "cambio_color", "true");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured!.Transformaciones.Color.Should().BeTrue(
            "el gestor declaró el cambio de color: sin snapshot RUNT no hay diff, pero el trámite lo incluye igual");
        capturing.Captured.Observaciones.Should().Be("Color nuevo(NUEVO COLOR: AZUL)",
            "si el gestor declaró la transformación, el párrafo 23 lleva el valor nuevo aunque no haya snapshot RUNT");
    }

    /// <summary>
    /// Sin bandera pero CON diff (trámites anteriores a que la bandera existiera) la casilla también
    /// se marca: son dos formas de enterarse de lo mismo.
    /// </summary>
    [Fact]
    public async Task Generar_TransformacionSoloPorDiff_MarcaLaCasilla()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        AddFieldValue(instance, "vehicle_body_type_runt", "ESTACAS");
        AddFieldValue(instance, "vehicle_body_type", "FURGON");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured!.Transformaciones.Carroceria.Should().BeTrue();
        capturing.Captured.Transformaciones.Color.Should().BeFalse("no se declaró ni difiere");
        capturing.Captured.Transformaciones.Combustible.Should().BeFalse();
    }

    /// <summary>Un trámite sin transformaciones no marca ninguna casilla de subtrámite.</summary>
    [Fact]
    public async Task Generar_SinTransformaciones_NoMarcaSubtramites()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        AddFieldValue(instance, "vehicle_color_runt", "AZUL");
        AddFieldValue(instance, "vehicle_color", "AZUL");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured!.Transformaciones.Should().Be(default(FurTransformacionesDeclaradas));
    }

    // ── Blindaje: nivel y desmonte ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("NIVEL_1", "BLINDAJE NIVEL 1.")]
    [InlineData("NIVEL_2", "BLINDAJE NIVEL 2.")]
    [InlineData("NIVEL_3", "BLINDAJE NIVEL 3.")]
    public async Task Generar_Blindaje_DeclaraElNivelYMarcaBlindadoSi(string opcion, string esperado)
    {
        var ct = TestContext.Current.CancellationToken;
        var capturing = await GenerarBlindaje(ct, (BlindajeOpciones.FieldKey, opcion));

        capturing.Captured!.Observaciones.Should().Be(esperado);
        capturing.Captured.Transformaciones.Blindaje.Should().BeTrue(
            "los tres niveles dejan el vehículo blindado");
    }

    [Fact]
    public async Task Generar_Desmonte_DeclaraElRetiroYMarcaBlindadoNo()
    {
        // Es el caso que la casilla sola no puede expresar: cae en NO igual que un vehículo que nunca
        // estuvo blindado, así que sin la observación el FUR no dice que hubo trámite.
        var ct = TestContext.Current.CancellationToken;
        var capturing = await GenerarBlindaje(ct, (BlindajeOpciones.FieldKey, "DESMONTE"));

        capturing.Captured!.Observaciones.Should().Be("DESMONTE DE BLINDAJE.");
        capturing.Captured.Transformaciones.Blindaje.Should().BeFalse(
            "el desmonte deja el vehículo SIN blindaje, aunque el trámite sea un blindaje");
    }

    [Fact]
    public async Task Generar_BlindajeSinOpcion_MarcaLaCasillaPeroNoInventaElTexto()
    {
        // Borrador abierto antes de que la opción existiera: el tipo basta para el SI, pero escribir
        // un nivel por defecto declararía ante el organismo un blindaje que nadie eligió.
        var ct = TestContext.Current.CancellationToken;
        var capturing = await GenerarBlindaje(ct);

        capturing.Captured!.Observaciones.Should().BeNull();
        capturing.Captured.Transformaciones.Blindaje.Should().BeTrue();
    }

    [Fact]
    public async Task Generar_NivelResidualEnUnTipoQueNoEsBlindaje_NoSeImprime()
    {
        // Mismo criterio que las otras tres capas (ADR-0050): un valor persistido que el tipo no lleva
        // no debe llegar al documento.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        instance.ProcedureType = ProcedureTypeFixture.CambioColor;
        WithOrganismo(instance);
        AddFieldValue(instance, BlindajeOpciones.FieldKey, "NIVEL_3");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured!.Observaciones.Should().NotContain("BLINDAJE NIVEL");
        capturing.Captured.Transformaciones.Blindaje.Should().BeFalse();
    }

    // ── Tipos prendarios: el FUR sale tan completo como el de un traspaso ────────────────────────

    /// <summary>Genera el FUR de un tipo prendario con la decisión de prenda vigente indicada.</summary>
    private async Task<CapturingFurGenerator> GenerarPrendario(
        ProcedureType tipo, string decision, CancellationToken ct, string? entidadLevantamiento = null)
    {
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        instance.ProcedureType = tipo;
        WithOrganismo(instance);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _prendaRepo.GetVigenteAsync(id, tenant, ct).Returns(new ProcedureInstancePrenda
        {
            Decision = decision,
            Estado = PrendaEstado.Vigente,
            AcreedorNombre = "BANCO XYZ S.A.",
            AcreedorDocumento = "890900608",
            LevantamientoEntidad = entidadLevantamiento,
        });

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured.Should().NotBeNull();
        return capturing;
    }

    [Fact]
    public async Task Levantamiento_FurDeclaraCasilla12_Numeral20_YParrafo23()
    {
        var ct = TestContext.Current.CancellationToken;
        var capturing = await GenerarPrendario(
            ProcedureTypeFixture.LevantamientoPrenda, PrendaDecision.Levantar, ct,
            entidadLevantamiento: "NOTARÍA 15 DE MEDELLÍN");

        var data = capturing.Captured!;
        // Casilla del numeral 3: la pone el tipo (tabla 1).
        FurNumeral3Marks.Resolve(data).Should().Contain(12);
        // Numeral 20 «A FAVOR DE»: sale del acreedor del gravamen que se levanta.
        data.AcreedorPrenda.Should().Be("BANCO XYZ S.A.");
        data.PrendaMarking.Should().Be(FurPrendaMarking.Levantamiento);
        // Párrafo 23: el bloque que nombra a quién se le levanta.
        // El acreedor lo nombra el numeral 20; el recuadro dice ante quién se levantó.
        data.Observaciones.Should().Be("Levantamiento de prenda ante NOTARÍA 15 DE MEDELLÍN");
    }

    [Fact]
    public async Task Inscripcion_FurDeclaraCasilla11_Numeral20_YParrafo23()
    {
        var ct = TestContext.Current.CancellationToken;
        var capturing = await GenerarPrendario(
            ProcedureTypeFixture.PrendaInscripcion, PrendaDecision.Registrar, ct);

        var data = capturing.Captured!;
        FurNumeral3Marks.Resolve(data).Should().Contain(11);
        data.AcreedorPrenda.Should().Be("BANCO XYZ S.A.");
        data.PrendaMarking.Should().Be(FurPrendaMarking.Constitucion);
        data.Observaciones.Should().Be("Inscripción de prenda a favor de BANCO XYZ S.A.");
    }

    /// <summary>Genera el FUR de un trámite del tipo <c>BLINDAJE</c> con los field_values indicados.</summary>
    private async Task<CapturingFurGenerator> GenerarBlindaje(
        CancellationToken ct, params (string Key, string Value)[] fieldValues)
    {
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        instance.ProcedureType = ProcedureTypeFixture.Blindaje;
        WithOrganismo(instance);
        foreach (var (key, value) in fieldValues)
            AddFieldValue(instance, key, value);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured.Should().NotBeNull();
        return capturing;
    }

    /// <summary>Generador FUR que captura el <see cref="FurDocumentData"/> ensamblado para aserciones.</summary>
    private sealed class CapturingFurGenerator : IFurDocumentGenerator
    {
        public FurDocumentData? Captured { get; private set; }

        public GeneratedDocument GenerateFur(FurDocumentData data)
        {
            Captured = data;
            return new GeneratedDocument("fur", "fur.pdf", "application/pdf", [1, 2, 3]);
        }

        public GeneratedDocument GenerateFurFillAll(FurTemplateFormat format = FurTemplateFormat.Automotor) =>
            new("fur", "fur_FILLALL.pdf", "application/pdf", [1, 2, 3]);

        public GeneratedDocument GenerateCompraventa(FurDocumentData data)
        {
            Captured = data;
            return new GeneratedDocument("compraventa", "cv.pdf", "application/pdf", [1, 2, 3]);
        }
    }

    private static void AddFieldValue(ProcedureInstance instance, string key, string value) =>
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = key,
            ValueText = value,
            Source = "user",
        });

    [Fact]
    public async Task Generar_PlacaEnColumna_SinFieldValue_LaPintaEnElFur()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.Plate = "ABC123";
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage,
            NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured!.Vehiculo.Placa.Should().Be("ABC123");
    }

    [Fact]
    public async Task Generar_MatriculaSinPlaca_DejaPlacaVacia()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage,
            NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured!.Vehiculo.Placa.Should().BeNull();
    }

    [Fact]
    public async Task Generar_ConTransformacion_FurUsaRuntEnCamposYNuevoEnObservaciones()
    {
        // A4/B4 (HU #10673, ADR-0029): el FUR imprime el color/combustible ORIGINAL del RUNT en los CAMPOS
        // del vehículo; la transformación declarada (valor nuevo) va SOLO en observaciones y sin flecha.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        AddFieldValue(instance, "vehicle_color_runt", "PLATA");
        AddFieldValue(instance, "vehicle_color", "NEGRO");
        AddFieldValue(instance, "cambio_color", "true");
        AddFieldValue(instance, "vehicle_fuel_runt", "GASOLINA");
        AddFieldValue(instance, "vehicle_fuel", "DIESEL");
        AddFieldValue(instance, "cambio_combustible", "true");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured.Should().NotBeNull();
        capturing.Captured!.Vehiculo.Color.Should().Be("PLATA");         // campo del FUR = RUNT original
        capturing.Captured.Vehiculo.Combustible.Should().Be("GASOLINA"); // campo del FUR = RUNT original
        capturing.Captured.Observaciones.Should().Be("Color nuevo(NUEVO COLOR: NEGRO) COMBUSTIBLE_NUEVO: DIESEL");
    }

    [Fact]
    public async Task Generar_SinSnapshotRunt_FurCaeAlEfectivo()
    {
        // Trámite previo a la feature (sin *_runt): los campos del FUR caen al valor efectivo.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        AddFieldValue(instance, "vehicle_color", "AZUL");
        AddFieldValue(instance, "vehicle_fuel", "GASOLINA");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured!.Vehiculo.Color.Should().Be("AZUL");
        capturing.Captured.Vehiculo.Combustible.Should().Be("GASOLINA");
        capturing.Captured.Observaciones.Should().BeNull(); // sin cambio declarado, sin texto automático
    }

    [Fact]
    public async Task Generar_ConTransformacionCarroceria_FurUsaRuntEnCamposYNuevoEnObservaciones()
    {
        // A4/B4 (HU #10673, ADR-0029): el FUR imprime la carrocería ORIGINAL del RUNT en el campo del
        // vehículo; la transformación declarada (valor nuevo) va SOLO en observaciones.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        AddFieldValue(instance, "vehicle_body_type_runt", "SEDAN");
        AddFieldValue(instance, "vehicle_body_type", "PICKUP");
        AddFieldValue(instance, "cambio_carroceria", "true");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured.Should().NotBeNull();
        capturing.Captured!.Vehiculo.TipoCarroceria.Should().Be("SEDAN");  // campo del FUR = RUNT original
        capturing.Captured.Observaciones.Should().Be("Carroceria nueva(NUEVA CARROCERIA: PICKUP)");
    }

    [Fact]
    public async Task Generar_SinSnapshotCarroceria_FurCaeAlEfectivo()
    {
        // Trámite previo a la feature (sin vehicle_body_type_runt): el campo del FUR cae al valor efectivo.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        AddFieldValue(instance, "vehicle_body_type", "PICKUP");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage, NullLogger<GenerarFurHandler>.Instance);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        capturing.Captured!.Vehiculo.TipoCarroceria.Should().Be("PICKUP");
        capturing.Captured.Observaciones.Should().BeNull();
    }

    private static ProcedureInstanceActor ActorJuridico(ProcedureInstance instance) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "EMPRESA DEMO S.A.S.",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Generar_ConActorPersonaJuridica_GeneraCertificadoRuesSystem()
    {
        // HU #10589 AC: un actor persona jurídica (NIT) genera el certificado RUES (Source=system)
        // que se incorpora al consolidado.
        // HU #10990 — el certificado exige DATOS DE REGISTRO del RUES para ese NIT: antes bastaba con
        // que el actor fuera NIT y el documento salía con la razón social y 19 casillas en blanco.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorJuridico(instance));
        AddFieldValue(instance, "rues_nit", "900123456");
        AddFieldValue(instance, "rues_razon_social", "EMPRESA DEMO S.A.S.");
        AddFieldValue(instance, "rues_matricula_mercantil", "MM-778899");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().Contain("certificado_rues");
        instance.Attachments.Should().Contain(a => a.Tipo == "certificado_rues" && a.Source == "system");
    }

    [Fact]
    public async Task Generar_ConActorJuridicoSinDatosDeRues_NoEmiteCertificadoEnBlanco()
    {
        // HU #10990 — cambio de comportamiento deliberado: un certificado sin datos de registro no
        // certifica nada. Sin rues_* del actor y sin resolutor inyectado (default nulo), se omite.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorJuridico(instance));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_rues");
    }

    // ── HU #10990 — certificado RUES resuelto POR ACTOR ──────────────────────

    /// <summary>
    /// Almacén canónico VACÍO: obliga al lector a caer en el respaldo sobre <c>field_values</c>, que
    /// es el camino de los trámites anteriores al despliegue de la HU #11302.
    /// </summary>
    private sealed class AlmacenCertificacionesVacio : ICertificationRepository
    {
        public Task<CertificationSnapshot> LoadAsync(Guid tenantId, Guid instanceId, CancellationToken ct) =>
            Task.FromResult(CertificationSnapshot.Empty);

        public Task<Guid?> SaveRawPayloadAsync(
            Guid tenantId, Guid instanceId, RawProviderPayload? payload, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);

        public Task UpsertSoatPoliciesAsync(
            Guid tenantId, Guid instanceId, IReadOnlyList<StoredSoatPolicy> policies, CancellationToken ct) =>
            Task.CompletedTask;

        public Task UpsertRtmInspectionsAsync(
            Guid tenantId, Guid instanceId, IReadOnlyList<StoredRtmInspection> inspections, CancellationToken ct) =>
            Task.CompletedTask;

        public Task UpsertMerchantRegistrationsAsync(
            Guid tenantId, Guid instanceId, IReadOnlyList<StoredMerchantRegistration> registrations, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<int> FreezeAsync(Guid tenantId, Guid instanceId, DateTimeOffset frozenAt, CancellationToken ct) =>
            Task.FromResult(0);
    }

    /// <summary>
    /// HU #11305 — handler con el lector documental real. No hay resolutor que inyectar: la consulta
    /// en vivo al RUES desapareció (D4), así que estas pruebas ya no pueden "simular el proveedor".
    /// Lo que se ejercita es el respaldo real sobre lo persistido.
    /// </summary>
    private GenerarFurHandler HandlerConRues() =>
        new(_repo, _generator, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage,
            NullLogger<GenerarFurHandler>.Instance,
            certificationReader: new CertificationReader(new AlmacenCertificacionesVacio()));

    // ── Bug #11146 — una parte firma de UNA sola manera ──────────────────────

    /// <summary>Baúl que siempre resuelve firma vigente para la persona consultada.</summary>
    private sealed class VaultConFirma : ISignatureVaultPolicy
    {
        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default) =>
            Task.FromResult<SignatureVaultMatch?>(new SignatureVaultMatch(
                Guid.NewGuid(), "REPRESENTANTE DEMO", "hash", "ruta", "sha",
                new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "52082029"));
    }

    private static ProcedureInstanceActor ActorJuridicoCon(string? mecanismo, string rol = "comprador")
    {
        var actor = ActorJuridico(Instance(Guid.NewGuid(), Guid.NewGuid(), TramiteTipologiaCatalog.CodigoTraspasoStandard));
        actor.ActorType = rol;
        // El sujeto de identidad de una persona JURÍDICA es su representante legal, y eso lo decide
        // `PersonType`: sin marcarlo, el sujeto sería el NIT y la validación no casaría.
        actor.PersonType = "juridical";
        actor.Metadata = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["representanteLegal"] = new Dictionary<string, object?>
            {
                ["tipoDocumento"] = "CC",
                ["numeroDocumento"] = "52082029",
                ["nombreCompleto"] = "REPRESENTANTE DEMO",
                // null = sin eleccion explicita, que es el caso normal.
                ["mecanismoFirma"] = mecanismo,
            },
        });
        return actor;
    }

    private Task<FurDocumentData> DatosDelDocumentoCon(string mecanismo) =>
        DatosDelDocumento(mecanismo, conBaul: true, rol: "comprador");

    /// <summary>
    /// Traspaso con comprador Y vendedor jurídicos, ambos con identidad aprobada y vigente. Es
    /// traspaso siempre porque el vendedor solo existe ahí, y ambos validan porque el gate del FUR
    /// exige las dos partes.
    /// </summary>
    private async Task<FurDocumentData> DatosDelDocumento(string? mecanismo, bool conBaul, string rol)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);

        foreach (var parte in new[] { "comprador", "vendedor" })
        {
            var actor = ActorJuridicoCon(mecanismo, parte);
            actor.TenantId = tenant;
            actor.ProcedureInstanceId = id;
            instance.Actors.Add(actor);

            // El sujeto de identidad de un actor jurídico es su REPRESENTANTE LEGAL: la validación va
            // con el documento del representante, o no casaría y el sello no se emitiría por motivos
            // ajenos a lo que se prueba. Aprobada Y vigente.
            var bio = Bio(parte);
            bio.DocumentType = "CC";
            bio.DocumentNumber = "52082029";
            bio.ValidatedAt = DateTimeOffset.UtcNow;
            bio.ValidUntil = DateTimeOffset.UtcNow.AddDays(20);
            instance.BiometricValidations.Add(bio);
        }

        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var capturing = new CapturingFurGenerator();
        var handler = new GenerarFurHandler(
            _repo, capturing, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage,
            NullLogger<GenerarFurHandler>.Instance,
            vaultPolicy: conBaul ? new VaultConFirma() : null);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);
        error.Should().BeNull();
        capturing.Captured.Should().NotBeNull();
        _ = rol;
        return capturing.Captured!;
    }

    [Fact]
    public async Task Generar_ConFirmaDelBaul_NoDejaSelloDeIdentidadEnLosDocumentos()
    {
        // Lo reportado: con firma de baúl E identidad vigente, la compraventa imprimía las dos estampas.
        // La exclusividad se decide en el ensamblado, así que la arrastran TODOS los documentos.
        var data = await DatosDelDocumentoCon(MecanismoFirma.Baul);

        (data.SellosIdentidad ?? new Dictionary<string, string>())
            .Should().NotContainKey("comprador", "con firma del baúl, esa ES la firma del documento");
    }

    [Fact]
    public async Task Generar_ConIdentidadElegida_NoApalancaImagenDelBaul()
    {
        var data = await DatosDelDocumentoCon(MecanismoFirma.Identidad);

        (data.FirmaImagenes ?? new Dictionary<string, byte[]>())
            .Should().NotContainKey("comprador", "se eligió el sello de identidad: no se toca el baúl");
        (data.SellosIdentidad ?? new Dictionary<string, string>())
            .Should().ContainKey("comprador");
    }

    [Theory]
    [InlineData("comprador")]
    [InlineData("vendedor")]
    public async Task Generar_SinBaulYSinEleccion_CONSERVA_ElSelloDeIdentidad(string rol)
    {
        // Regresion: al retirar el sello por el mero hecho de ser persona juridica, una parte con
        // identidad validada y SIN firma de baul se quedaba sin firma en los documentos. Sin eleccion
        // explicita manda la precedencia del baul, pero solo si la firma existe de verdad.
        var data = await DatosDelDocumento(mecanismo: null, conBaul: false, rol: rol);

        (data.SellosIdentidad ?? new Dictionary<string, string>())
            .Should().ContainKey(rol, "sin firma del baul, la identidad validada es la firma");
    }

    [Theory]
    [InlineData("comprador")]
    [InlineData("vendedor")]
    public async Task Generar_ConBaulExplicitoSinImagen_DejaLaFirmaEnBlanco(string rol)
    {
        // Elegido el baul a proposito, no se rellena con un sello que el negocio no eligio: la firma
        // queda en blanco y se nota.
        var data = await DatosDelDocumento(MecanismoFirma.Baul, conBaul: true, rol: rol);

        (data.SellosIdentidad ?? new Dictionary<string, string>()).Should().NotContainKey(rol);
    }

    private static ProcedureInstanceActor ActorJuridicoVendedor(ProcedureInstance instance) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "vendedor",
            DocumentType = "NIT",
            DocumentNumber = "800555444",
            FullName = "VENDEDORA S.A.S.",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Generar_TraspasoEntreDosPersonasJuridicasSinDatosPersistidos_NoEmiteCertificado()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.Actors.Add(ActorJuridico(instance));          // comprador, NIT 900123456
        instance.Actors.Add(ActorJuridicoVendedor(instance));  // vendedor,  NIT 800555444
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        // HU #11305 (D1/D4) — CONTRAPARTIDA ACEPTADA POR EL PO. Sin dato persistido ya no se emite
        // certificado: antes esta misma situación disparaba una consulta en vivo al RUES por cada NIT,
        // cobrada y repetida en cada regeneración del expediente. A cambio, generar el expediente
        // cuesta cero llamadas externas. Las compañías precargadas del directorio de representantes
        // legales pierden este anexo, y ese es el riesgo a medir tras el despliegue.
        var (result, error) = await HandlerConRues().HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        var tipos = result!.Documents.Select(d => d.Tipo).ToList();
        tipos.Should().NotContain("certificado_rues");
        tipos.Should().NotContain("certificado_rues_vendedor");
        tipos.Should().Contain("fur", "el expediente se genera igual");
    }

    [Fact]
    public async Task Generar_ConRuesEnFieldValuesDelMismoNit_EmiteElCertificadoDesdeElRespaldo()
    {
        // HU #11305 — respaldo para trámites anteriores al despliegue: las `rues_*` de instancia siguen
        // sirviendo cuando corresponden al NIT del actor.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorJuridico(instance));
        AddFieldValue(instance, "rues_nit", "900123456");
        AddFieldValue(instance, "rues_razon_social", "EMPRESA DEMO S.A.S.");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await HandlerConRues().HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().Contain("certificado_rues");
    }

    // ── HU #11133 — snapshot congelado al registrar ──────────────────────────

    [Fact]
    public async Task Generar_ConSnapshotDeAmbasCompanias_EmiteAmbosCertificadosSinLlamadasSalientes()
    {
        // El objetivo del negocio: regenerar el expediente no puede costar consultas al RUES. Con dos
        // personas jurídicas el camino anterior siempre pagaba al menos una, porque las llaves
        // `rues_*` son de instancia y solo pueden representar a una de las dos compañías.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.Actors.Add(ActorJuridico(instance));          // comprador, NIT 900123456
        instance.Actors.Add(ActorJuridicoVendedor(instance));  // vendedor,  NIT 800555444

        var snapshot = RuesSnapshots.Merge(
            null, "900123456",
            [new HydratedField("rues_razon_social", "EMPRESA DEMO S.A.S.", null)],
            DateTimeOffset.UtcNow);
        snapshot = RuesSnapshots.Merge(
            snapshot, "800555444",
            [new HydratedField("rues_razon_social", "VENDEDORA S.A.S.", null)],
            DateTimeOffset.UtcNow);
        AddFieldValue(instance, RuesSnapshots.FieldKey, snapshot!);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await HandlerConRues().HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        var tipos = result!.Documents.Select(d => d.Tipo).ToList();
        tipos.Should().Contain("certificado_rues");
        tipos.Should().Contain("certificado_rues_vendedor");
    }

    [Fact]
    public async Task Generar_ConSnapshot_TienePrecedenciaSobreLasLlavesDeInstancia()
    {
        // Las `rues_*` de instancia quedan como respaldo de trámites anteriores al snapshot; cuando
        // ambos existen manda el snapshot, que es el que se congeló al registrar ESTE trámite.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorJuridico(instance));
        AddFieldValue(instance, "rues_nit", "900123456");
        AddFieldValue(instance, "rues_razon_social", "NOMBRE DESACTUALIZADO");
        AddFieldValue(instance, RuesSnapshots.FieldKey, RuesSnapshots.Merge(
            null, "900123456",
            [new HydratedField("rues_razon_social", "NOMBRE DEL SNAPSHOT", null)],
            DateTimeOffset.UtcNow)!);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await HandlerConRues().HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        _ruesGenerator.Received().GenerateRuesCertificate(
            Arg.Is<RuesCertificateData>(d => d.RazonSocial == "NOMBRE DEL SNAPSHOT"));
    }

    [Fact]
    public async Task Generar_SinDatosDeRuesPersistidos_GeneraElFurIgualSinCertificado()
    {
        // HU #11305 — ya no hay proveedor que se pueda caer al generar: el expediente no hace llamadas
        // salientes. Sin dato persistido, el certificado sencillamente no se emite y el FUR sale igual.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorJuridico(instance));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await HandlerConRues().HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().Contain("fur");
        result.Documents.Select(d => d.Tipo).Should().NotContain("certificado_rues");
    }

    [Fact]
    public async Task Generar_ConRuesDeOtroNit_NoMezclaCompanias()
    {
        // HU #10990 — las rues_* son de INSTANCIA: en un traspaso PJ → PJ la segunda consulta pisaba a
        // la primera. Si no corresponden al NIT del actor, no se usan (y sin resolutor, no hay
        // certificado) en vez de imprimir la matrícula de otra compañía.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorJuridico(instance)); // NIT 900123456
        AddFieldValue(instance, "rues_nit", "800999888"); // ← otra compañía
        AddFieldValue(instance, "rues_razon_social", "OTRA EMPRESA S.A.S.");
        AddFieldValue(instance, "rues_matricula_mercantil", "MM-000000");
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_rues");
    }

    [Fact]
    public async Task Generar_SinActorPersonaJuridica_NoGeneraCertificadoRues()
    {
        // Sin actor NIT no se emite el certificado RUES.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_rues");
    }

    [Fact]
    public async Task Generar_Traspaso_BothAprobadas_GeneratesFurAndCompraventa()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio("comprador"));
        instance.BiometricValidations.Add(Bio("vendedor"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        // FUR + certificado de identidad del comprador + del vendedor + compraventa (traspaso).
        result!.Documents.Should().HaveCount(4);
        result.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(
            ["fur", "certificado_identidad", "certificado_identidad_vendedor", "compraventa"]);
        instance.Attachments.Should().HaveCount(4);
        // Se descarga el certificado de Kyverum de CADA parte (por su verificationId propio).
        _certClient.RequestedIds.Should().BeEquivalentTo(["kyv-comprador", "kyv-vendedor"]);
        instance.Events.Should().ContainSingle(e => e.Tipo == "fur_generado");
        _repo.Received(4).Add(Arg.Any<ProcedureInstanceAttachment>());
        _repo.Received(1).Add(Arg.Any<ProcedureInstanceEvent>());
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Generar_Matricula_CompradorAprobada_GeneratesOnlyFur()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio(parte: "comprador")); // matrícula = comprador
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        // Matrícula: FUR + certificado de identidad (sin compraventa).
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur", "certificado_identidad"]);
        _certClient.RequestedIds.Should().ContainSingle().Which.Should().Be("kyv-comprador");
    }

    [Fact]
    public async Task Generar_CertificadoDownloadFails_GeneratesFurWithoutCertificate()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio(parte: "comprador"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        // Kyverum caído: la descarga del certificado lanza. NO debe bloquear ni abortar el FUR.
        _certClient.Throw = new KyverumCertificateException("Kyverum no disponible.", transient: true);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        // Sin mock: solo el FUR; el certificado se omite tras el warning.
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur"]);
        instance.Attachments.Should().NotContain(a => a.Tipo == "certificado_identidad");
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Generar_CertificadoNotAvailable_GeneratesFurWithoutCertificate()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio(parte: "comprador"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        // Kyverum sin certificado para ese id (404 → null).
        _certClient.ReturnNull = true;

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur"]);
    }

    [Fact]
    public async Task Generar_MockProvider_SkipsCertificateDownload()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        // Validación mock (sin id Kyverum): no hay certificado externo que descargar.
        var bio = Bio(parte: "comprador");
        bio.Provider = BiometricProviders.Mock;
        bio.KyverumVerificationId = null;
        instance.BiometricValidations.Add(bio);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().BeEquivalentTo(["fur"]);
        _certClient.RequestedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Generar_Matricula_WithoutOrganismo_RejectsGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        instance.BiometricValidations.Add(Bio(parte: "comprador")); // biométrica ok, falta organismo
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().Be("organismo_requerido");
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Generar_Idempotent_ReplacesPreviousFur()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.BiometricValidations.Add(Bio(parte: "comprador"));
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Tipo = "fur",
            Filename = "old.txt",
            Mimetype = "text/plain",
            StoragePath = "old/fur",
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        _storage.Deleted.Should().Contain("old/fur");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "fur");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "certificado_identidad");
    }

    [Fact]
    public async Task Generar_InvalidaElConsolidadoMaestroCacheado()
    {
        // El consolidado maestro (#10701) cachea su copia con ConsolidadoMaestroVigente. Como (re)generar
        // el FUR SIEMPRE reemplaza el adjunto 'fur', hay que invalidar ese caché para que su próxima vista
        // lo refunda con el FUR nuevo; si no, seguiría sirviendo el consolidado con el FUR viejo.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.ConsolidadoMaestroVigente = true; // consolidado maestro previamente cacheado

        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        instance.ConsolidadoMaestroVigente.Should().BeFalse();
    }

    // ── HU #10762 · Certificado RNMC ──────────────────────────────────────

    private static ProcedureInstanceActor ActorNatural(ProcedureInstance instance, string rol, string nombre, string doc) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = rol,
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = nombre,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>
    /// Snapshot de preflight con los checks indicados. FEATURE 05 — el certificado RNMC ya no lee del
    /// snapshot sino del field_value <c>rnmc_checks</c>: este helper además lo siembra con los mismos
    /// checks (con <c>createdAt</c> como fecha de consulta), para que el certificado se genere igual.
    /// </summary>
    private static ProcedureInstancePreflightSnapshot Snapshot(
        ProcedureInstance instance, DateTimeOffset createdAt, params PreflightCheckDto[] checks)
    {
        var json = JsonSerializer.Serialize(checks);
        var existing = instance.FieldValues.FirstOrDefault(f => f.FieldKey == "rnmc_checks");
        if (existing is not null)
        {
            existing.ValueJson = json;
        }
        else
        {
            instance.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = instance.TenantId,
                ProcedureInstanceId = instance.Id,
                FieldKey = "rnmc_checks",
                ValueJson = json,
                Source = "system",
                CreatedAt = createdAt,
            });
        }

        return new ProcedureInstancePreflightSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Overall = "green",
            Checks = json,
            Provider = "verifik_rnmc",
            CreatedAt = createdAt,
        };
    }

    [Fact]
    public async Task Generar_SinSnapshotPreflight_NoGeneraCertificadoRnmc()
    {
        // AC1: sin preflight corrido no hay dato RNMC que certificar.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns((ProcedureInstancePreflightSnapshot?)null);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_rnmc");
        CapturedRnmcData.Should().BeNull();
    }

    [Fact]
    public async Task Generar_SnapshotSinChecksRnmc_NoGeneraCertificadoRnmc()
    {
        // AC1: preflight corrido pero sin consulta RNMC (p. ej. persona jurídica) → no se emite.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("runt_vehiculo", "RUNT", "ok", "verifik_runt", null)));

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_rnmc");
        CapturedRnmcData.Should().BeNull();
    }

    [Fact]
    public async Task Generar_ConChecksRnmc_GeneraCertificadoRnmcSystem()
    {
        // AC1/AC2: con checks rnmc_ se emite UN certificado_rnmc (Source=system) adjunto al trámite.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                "ok", "verifik_rnmc", null)));

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().ContainSingle(t => t == "certificado_rnmc");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "certificado_rnmc" && a.Source == "system");

        // Los datos del actor se toman de instance.Actors, no del check.
        var data = CapturedRnmcData!;
        data.Entradas.Should().ContainSingle();
        data.Entradas[0].Rol.Should().Be("comprador");
        data.Entradas[0].Nombre.Should().Be("DANIEL AMADO");
        data.Entradas[0].Documento.Should().Be("1193552679");
    }

    [Fact]
    public async Task Generar_ConChecksRnmc_MapeaEstadosPorParte()
    {
        // AC2: ok → SIN MEDIDAS CORRECTIVAS; warn → CON MEDIDAS CORRECTIVAS. El rol sale del segmento
        // intermedio de la key (rnmc_{rol}_{key}) y el detalle del Message del check.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoTraspasoStandard);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        instance.Actors.Add(ActorNatural(instance, "vendedor", "MARIA LOPEZ", "52123456"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                "ok", "verifik_rnmc", null),
            new PreflightCheckDto("rnmc_vendedor_medidas_correctivas", "Medidas correctivas (Policía)",
                "warn", "verifik_rnmc", "1 medida(s): Riña")));

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        var data = CapturedRnmcData!;
        data.Entradas.Should().HaveCount(2);

        var comprador = data.Entradas.Single(e => e.Rol == "comprador");
        comprador.Estado.Should().Be("SIN MEDIDAS CORRECTIVAS");
        comprador.Detalle.Should().BeNull();

        var vendedor = data.Entradas.Single(e => e.Rol == "vendedor");
        vendedor.Estado.Should().Be("CON MEDIDAS CORRECTIVAS");
        vendedor.Nombre.Should().Be("MARIA LOPEZ");
        vendedor.Detalle.Should().Be("1 medida(s): Riña");
    }

    [Theory]
    [InlineData("unknown", "SIN DATOS")]
    [InlineData("error", "NO VERIFICABLE")]
    public async Task Generar_ConCheckRnmcSinResultado_MapeaEstadoNoConcluyente(string status, string esperado)
    {
        // AC2: unknown → SIN DATOS; error → NO VERIFICABLE. Nunca se afirma "sin medidas" sin dato.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                status, "verifik_rnmc", null)));

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        CapturedRnmcData!.Entradas.Single().Estado.Should().Be(esperado);
    }

    [Fact]
    public async Task Generar_ConChecksRnmc_ConsultadoEnEsLaFechaDelSnapshot()
    {
        // AC2: la fecha de consulta del certificado es la de la corrida de preflight, NO la de generación
        // del FUR: certificar "consultado hoy" con datos de ayer sería falso.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        var consultadoEn = new DateTimeOffset(2026, 7, 10, 8, 15, 0, TimeSpan.Zero);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, consultadoEn,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                "ok", "verifik_rnmc", null)));

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        CapturedRnmcData!.ConsultadoEn.Should().Be(consultadoEn);
    }

    [Fact]
    public async Task Generar_ConChecksRnmc_NoFiltraElProveedorAlCertificado()
    {
        // AC2 (regla dura): el nombre del integrador (verifik) NUNCA llega al documento del usuario; la
        // fuente que se muestra es la entidad oficial y la pinta el generador como literal.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                "ok", "verifik_rnmc", null)));

        var (_, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        var serialized = JsonSerializer.Serialize(CapturedRnmcData!);
        serialized.Should().NotContainEquivalentOf("verifik");
    }

    [Fact]
    public async Task Generar_SinChecksRnmc_BorraElCertificadoRnmcPrevio()
    {
        // AC2: regeneración sin RNMC (p. ej. el actor pasó a persona jurídica) no puede dejar el
        // certificado obsoleto en el expediente.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Tipo = "certificado_rnmc",
            Filename = "certificado_rnmc_viejo.pdf",
            Mimetype = "application/pdf",
            StoragePath = "old/certificado_rnmc",
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns((ProcedureInstancePreflightSnapshot?)null);

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        _storage.Deleted.Should().Contain("old/certificado_rnmc");
        instance.Attachments.Should().NotContain(a => a.Tipo == "certificado_rnmc");
        result!.Documents.Select(d => d.Tipo).Should().NotContain("certificado_rnmc");
        _repo.Received(1).RemoveAttachment(Arg.Is<ProcedureInstanceAttachment>(a => a.Tipo == "certificado_rnmc"));
    }

    [Fact]
    public async Task Generar_GeneradorRnmcFalla_NoBloqueaElFur_YConservaElCertificadoPrevio()
    {
        // AC3: el certificado RNMC es best-effort — si el generador falla, el FUR se emite igual.
        // Y el certificado previo se CONSERVA: el RNMC sí aplicaba, así que el previo sigue siendo válido;
        // borrarlo por un fallo transitorio perdería un documento bueno (a diferencia de "ya no aplica").
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        WithOrganismo(instance);
        instance.Actors.Add(ActorNatural(instance, "comprador", "DANIEL AMADO", "1193552679"));
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Tipo = "certificado_rnmc",
            Filename = "certificado_rnmc_previo.pdf",
            Mimetype = "application/pdf",
            StoragePath = "old/certificado_rnmc",
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);
        _repo.GetLatestPreflightAsync(id, tenant, ct).Returns(Snapshot(
            instance, DateTimeOffset.UtcNow,
            new PreflightCheckDto("rnmc_comprador_medidas_correctivas", "Medidas correctivas (Policía)",
                "ok", "verifik_rnmc", null)));
        _rnmcGenerator.GenerateRnmcCertificate(Arg.Any<RnmcCertificateData>())
            .Returns(_ => throw new InvalidOperationException("QuestPDF caído"));

        var (result, error) = await _handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        result!.Documents.Select(d => d.Tipo).Should().Contain("fur").And.NotContain("certificado_rnmc");
        _storage.Deleted.Should().NotContain("old/certificado_rnmc");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "certificado_rnmc");
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    // ── HU #10936 · Escritura: persistir la usada + congelar tras entrega ──────────

    /// <summary>Resolutor de escrituras de prueba: cuenta invocaciones y devuelve lo configurado.</summary>
    private sealed class FakeDeedResolver : IProcedureDeedResolver
    {
        public int Calls { get; private set; }
        public IReadOnlyList<ResolvedDeedDocument> ToReturn { get; set; } = [];

        public Task<IReadOnlyList<ResolvedDeedDocument>> ResolveForActorsAsync(
            Guid tenantId, IEnumerable<ProcedureInstanceActor> actors, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(ToReturn);
        }
    }

    private static ProcedureInstanceAttachment EscrituraSistema(
        ProcedureInstance instance, Guid attachmentId, Guid deedId, string storagePath) =>
        new()
        {
            Id = attachmentId,
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = "escritura",
            Filename = "escritura.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 4,
            Sha256 = "sha-escritura",
            StoragePath = storagePath,
            Source = "system",
            SourceDeedId = deedId,
            UploadedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Generar_TramiteActivo_ResuelveEscritura_YPersisteSourceDeedId()
    {
        // HU #10936 — en estados previos a la entrega (borrador) se re-resuelve la escritura y el adjunto
        // de sistema queda con source_deed_id = deed elegido.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial); // Status = borrador
        WithOrganismo(instance);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var deedId = Guid.NewGuid();
        var resolver = new FakeDeedResolver
        {
            ToReturn = [new ResolvedDeedDocument(
                "escritura", "escritura.pdf", Encoding.UTF8.GetBytes("%PDF-ESC"), "900123456", "vendedor", deedId)],
        };
        var handler = new GenerarFurHandler(
            _repo, _generator, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage,
            NullLogger<GenerarFurHandler>.Instance, deedResolver: resolver);

        var (result, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        resolver.Calls.Should().Be(1); // trámite activo ⇒ se re-resuelve
        var esc = instance.Attachments.Single(a => a.Tipo == "escritura");
        esc.Source.Should().Be("system");
        esc.SourceDeedId.Should().Be(deedId);
        result!.Documents.Select(d => d.Tipo).Should().Contain("escritura");
    }

    [Fact]
    public async Task Generar_TramiteEntregado_CongelaEscritura_NoReResuelveNiReemplaza()
    {
        // HU #10936 — una vez ENTREGADO, la escritura utilizada queda fija: no se re-resuelve (el resolutor
        // no se invoca) ni se reemplaza el adjunto existente (mismo id, mismo source_deed_id, sin borrado).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, TramiteTipologiaCatalog.CodigoMatriculaInicial);
        instance.Status = TramiteEstado.Entregado;
        WithOrganismo(instance);
        var prevAttachmentId = Guid.NewGuid();
        var usedDeedId = Guid.NewGuid();
        instance.Attachments.Add(EscrituraSistema(instance, prevAttachmentId, usedDeedId, "old/escritura"));
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        // El resolutor devolvería OTRA escritura si se le llamara: no debe usarse (congelado).
        var resolver = new FakeDeedResolver
        {
            ToReturn = [new ResolvedDeedDocument(
                "escritura", "escritura.pdf", Encoding.UTF8.GetBytes("%PDF-NUEVA"), "900123456", "vendedor", Guid.NewGuid())],
        };
        var handler = new GenerarFurHandler(
            _repo, _generator, _certClient, _ruesGenerator, _rnmcGenerator, _prendaRepo, _storage,
            NullLogger<GenerarFurHandler>.Instance, deedResolver: resolver);

        var (_, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        resolver.Calls.Should().Be(0); // congelado ⇒ no se re-resuelve
        var esc = instance.Attachments.Single(a => a.Tipo == "escritura");
        esc.Id.Should().Be(prevAttachmentId);       // el adjunto previo se conserva
        esc.SourceDeedId.Should().Be(usedDeedId);   // con la escritura original
        _storage.Deleted.Should().NotContain("old/escritura"); // no se trató como huérfano
    }
}

public sealed class MockFurDocumentGeneratorTests
{
    private static FurDocumentData Data() =>
        new(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "TRM-2026-000001",
            Modalidad: "traspaso",
            TipologiaCodigo: "traspaso_standard",
            Vehiculo: new VehiculoDatos(
                Marca: "TOYOTA", Linea: "COROLLA", Modelo: "2024", Color: "ROJO",
                Clase: "AUTOMOVIL", Combustible: "GASOLINA", Cilindraje: "1800",
                Vin: "1HGCM82633A004352", Placa: "ABC123"),
            Organismo: new OrganismoTransito(Codigo: "11001000", Nombre: "SDM Bogotá", Ciudad: "Bogotá"),
            Partes: [new DocumentParte("comprador", "Juan", "123", "j@x.com")],
            ValorVenta: 50000m,
            Causal: "venta",
            SellosFirma: ["comprador/compraventa: abc (2026)"]);

    [Fact]
    public void GenerateFur_EmbedsRealData()
    {
        var doc = new MockFurDocumentGenerator().GenerateFur(Data());

        doc.Tipo.Should().Be("fur");
        doc.Mimetype.Should().Be("text/plain");
        var content = Encoding.UTF8.GetString(doc.Content);
        content.Should().Contain("TRM-2026-000001");
        content.Should().Contain("1HGCM82633A004352");
        content.Should().Contain("TOYOTA");
        content.Should().Contain("COROLLA");
        content.Should().Contain("SDM Bogotá");
        content.Should().Contain("11001000");
        content.Should().Contain("Juan");
        content.Should().Contain("MOCK FUR");
    }

    [Fact]
    public void GenerateCompraventa_ContainsValor()
    {
        var doc = new MockFurDocumentGenerator().GenerateCompraventa(Data());

        doc.Tipo.Should().Be("compraventa");
        Encoding.UTF8.GetString(doc.Content).Should().Contain("50000.00");
    }
}
