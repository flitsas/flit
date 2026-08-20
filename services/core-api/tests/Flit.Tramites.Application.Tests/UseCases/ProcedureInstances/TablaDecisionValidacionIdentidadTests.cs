using System.Text.Json;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11664 — <b>tabla de decisión de la validación de identidad de una parte jurídica, comprador y
/// vendedor, afirmada en los tres niveles del recorrido.</b>
///
/// <para><b>Para qué.</b> El Feature reordenó quién decide qué: el disparador dejó de hacer
/// prechequeos propios y la decisión vive entera en la precedencia única; el gate de radicación y el
/// chip del listado pasaron a consultar el predicado único del baúl. Cada pieza tiene su suite; lo que
/// faltaba era la tabla <b>completa</b> recorrida de punta a punta, para que un cambio en cualquiera de
/// las piezas no pueda mover el comportamiento observable sin que algo se ponga rojo.</para>
///
/// <para><b>El sujeto de identidad es siempre el representante legal del trámite</b>, nunca el NIT: el
/// NIT no es validable biométricamente. Las siete filas se distinguen por lo que ese representante
/// tiene (firma de baúl vigente, validación de identidad vigente) y por el mecanismo de firma que el
/// gestor eligió.</para>
///
/// <list type="table">
///   <item><term>1</term><description>baúl vigente + VID vigente, mecanismo — / baúl → sin correo, aprobado por el baúl</description></item>
///   <item><term>2</term><description>baúl vigente + VID vigente, mecanismo identidad → sin correo, aprobado solo por la VID</description></item>
///   <item><term>3</term><description>baúl vigente, sin VID, mecanismo — / baúl → sin correo, aprobado por el baúl</description></item>
///   <item><term>4</term><description>baúl vigente, sin VID, mecanismo identidad → <b>correo al RL</b>, no aprobado</description></item>
///   <item><term>5</term><description>sin baúl + VID vigente, cualquier mecanismo → sin correo, aprobado por la VID</description></item>
///   <item><term>6</term><description>sin baúl, sin VID, cualquier mecanismo → <b>correo al RL</b>, no aprobado</description></item>
///   <item><term>7</term><description>empresa/RL fuera del directorio (RL digitado a mano) → <b>correo al representante</b>, no aprobado</description></item>
/// </list>
///
/// <para><b>Sobre la fila 7.</b> En estado del trámite es indistinguible de la 6, y eso <i>es</i> la
/// corrección: desde la HU #11662 el directorio de representantes ya no participa en la decisión de
/// envío —es fuente de datos, no de decisiones (HU #11663)—. Se conserva como fila propia porque es el
/// escenario que el negocio describe, y se acompaña del test de no regresión de más abajo, que sí
/// distingue un representante de otro dentro de la misma compañía.</para>
///
/// <para><b>Los tres niveles.</b> (1) <see cref="IdentitySendDecisionEvaluator.Evaluate"/>, la regla
/// pura; (2) la compuerta real <see cref="PutActorsHandler"/> contra un
/// <see cref="IniciarKyverumVerifyHandler"/> observable, que es lo que de verdad manda el correo;
/// (3) el gate de radicación, por su consumidor público —el asistente—, porque
/// <c>IdentityApprovalResolver</c> es <c>internal</c> y este ensamblado no tiene
/// <c>InternalsVisibleTo</c>.</para>
///
/// <para><b>Cableado que hay que respetar.</b> El handler de Kyverum se construye <b>con</b>
/// <see cref="ISignatureVaultPolicy"/>, como hace la inyección de dependencias de producción. Sin ese
/// argumento el baúl queda inerte y las filas 1-3 pasan por el motivo equivocado.</para>
///
/// <para><b>Lo que esta suite NO afirma.</b> La columna «Firma» de la tabla —qué sello se plasma en el
/// FUR— depende de la ruta documental (<c>FurCommand</c>), que este Feature no modifica.</para>
///
/// <para>Uso de ejemplo:
/// <c>[Theory] [MemberData(nameof(Tabla))] N1_Evaluador(fila, rol, mecanismo, baul, vid, correo, motivo, gate)</c>.</para>
/// </summary>
public sealed class TablaDecisionValidacionIdentidadTests
{
    private const string RlTipoDocumento = "CC";
    private const string RlDocumento = "1090123456";
    private const string RlEmail = "rep@empresa.com";
    private const string RlNombre = "Ana Representante";

    /// <summary>Documento de la contraparte natural del traspaso (ya validada) cuando se prueba al vendedor.</summary>
    private const string CompradorNaturalDocumento = "777";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly BiometricsProviderOptions _providerOptions = new() { Provider = BiometricProviders.Kyverum };
    private readonly IKyverumVerifyClient _kyverumClient = Substitute.For<IKyverumVerifyClient>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly ISignatureVaultPolicy _baul = Substitute.For<ISignatureVaultPolicy>();
    private readonly PutActorsHandler _put;

    public TablaDecisionValidacionIdentidadTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
        _baul.ResolveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SignatureVaultMatch?)null);

        // El baúl viaja al handler de Kyverum: es él quien evalúa la precedencia. Mismo cableado que la
        // inyección de dependencias de producción; omitirlo dejaría el baúl inerte (aprendido en #11662).
        var kyverum = new IniciarKyverumVerifyHandler(
            _repo,
            _kyverumClient,
            new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(),
            Substitute.For<IIdentityValidationAuditLog>(),
            _baul);

        _put = new PutActorsHandler(_repo, _catalogRepo, _providerOptions, kyverum, _consentRepo, _baul);

        _catalogRepo.GetProcedureEntityByCodeAsync("BUYER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = Guid.NewGuid(), Code = "BUYER", Name = "Comprador" });
        _catalogRepo.GetProcedureEntityByCodeAsync("OWNER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = Guid.NewGuid(), Code = "OWNER", Name = "Propietario" });
        _catalogRepo.GetProcedureEntityByCodeAsync("SELLER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = Guid.NewGuid(), Code = "SELLER", Name = "Vendedor" });

        _kyverumClient.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult("kyv_1", "https://capture/kyv_1", "whsec_x", "pending", "{}"));
    }

    /// <summary>
    /// Las 7 filas por los 2 roles. Las filas cuyo mecanismo la tabla deja abierto («—/baúl»,
    /// «cualquiera») se expanden en un caso por variante: es donde se esconden las regresiones.
    /// Columnas: fila, rol, mecanismo, baúl vigente, VID vigente, espera correo, motivo, gate aprueba.
    /// </summary>
    public static TheoryData<int, string, string?, bool, bool, bool, string, bool> Tabla()
    {
        var data = new TheoryData<int, string, string?, bool, bool, bool, string, bool>();
        foreach (var rol in new[] { "comprador", "vendedor" })
        {
            // 1 — baúl y VID vigentes, sin elección o eligiendo el baúl: manda el baúl.
            foreach (var mecanismo in new string?[] { null, MecanismoFirma.Baul })
                data.Add(1, rol, mecanismo, true, true, false, IdentitySendMotivo.CoberturaBaul, true);

            // 2 — con las dos vigentes pero eligiendo el sello de identidad, quien acredita es la VID.
            data.Add(2, rol, MecanismoFirma.Identidad, true, true, false, IdentitySendMotivo.IdentidadVigente, true);

            // 3 — solo baúl: sin elección manda la precedencia del baúl (HU #11031).
            foreach (var mecanismo in new string?[] { null, MecanismoFirma.Baul })
                data.Add(3, rol, mecanismo, true, false, false, IdentitySendMotivo.CoberturaBaul, true);

            // 4 — solo baúl PERO eligiendo identidad: la firma no se va a consumir ⇒ hay que validar.
            data.Add(4, rol, MecanismoFirma.Identidad, true, false, true, IdentitySendMotivo.CorrespondeEnviar, false);

            // 5 — sin baúl y con VID vigente: acredita la VID, elija lo que elija el gestor.
            foreach (var mecanismo in new string?[] { null, MecanismoFirma.Baul, MecanismoFirma.Identidad })
                data.Add(5, rol, mecanismo, false, true, false, IdentitySendMotivo.IdentidadVigente, true);

            // 6 — sin nada: correo al representante legal del trámite.
            foreach (var mecanismo in new string?[] { null, MecanismoFirma.Baul, MecanismoFirma.Identidad })
                data.Add(6, rol, mecanismo, false, false, true, IdentitySendMotivo.CorrespondeEnviar, false);

            // 7 — empresa/RL fuera del directorio (digitado a mano): mismo desenlace que la 6, y esa
            // indistinguibilidad es la corrección (el directorio no decide nada).
            data.Add(7, rol, null, false, false, true, IdentitySendMotivo.CorrespondeEnviar, false);
        }

        return data;
    }

    // ── Nivel 1 — la regla pura ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Tabla))]
    public void N1_Evaluador(
        int fila, string rol, string? mecanismo, bool baul, bool vid,
        bool esperaCorreo, string motivoEsperado, bool esperaAprobado)
    {
        // esperaAprobado lo afirma el nivel 3; viaja al mensaje de fallo.
        var caso = $"fila {fila} / {rol} / mecanismo {mecanismo ?? "—"} (gate {esperaAprobado})";
        // El evaluador recibe la cobertura del baúl CRUDA y vuelve a pasarla por el predicado único:
        // por eso la fila 4 —con firma vigente— decide enviar igual.
        var now = DateTimeOffset.UtcNow;
        var ctx = new IdentitySendDecisionContext(
            Guid.NewGuid(),
            RlTipoDocumento,
            RlDocumento,
            ActorJuridico(rol, mecanismo),
            HasBaulFirmaActivaVigente: baul,
            ValidationsForPerson: vid ? [VidVigente(now)] : [],
            now);

        var decision = IdentitySendDecisionEvaluator.Evaluate(ctx);

        decision.Motivo.Should().Be(motivoEsperado, caso);
        decision.Kind.Should().Be(
            esperaCorreo ? IdentitySendDecisionKind.Enviar : IdentitySendDecisionKind.NoEnviar, caso);
    }

    [Fact]
    public void N1_ElEvaluadorNoMiraElRol()
    {
        // Corolario de la tabla: el rol no es variable de la decisión —no viaja siquiera en el
        // contexto—, así que comprador y vendedor no pueden divergir por construcción. Se afirma
        // explícitamente para que nadie introduzca una asimetría por rol sin romper algo.
        var now = DateTimeOffset.UtcNow;
        IdentitySendDecision Decidir(string rol) => IdentitySendDecisionEvaluator.Evaluate(
            new IdentitySendDecisionContext(
                Guid.NewGuid(), RlTipoDocumento, RlDocumento,
                ActorJuridico(rol, MecanismoFirma.Identidad),
                HasBaulFirmaActivaVigente: true,
                ValidationsForPerson: [],
                now));

        Decidir("comprador").Should().BeEquivalentTo(
            Decidir("vendedor"), o => o.Excluding(d => d.ValidationId));
    }

    // ── Nivel 2 — la compuerta, extremo a extremo ─────────────────────────────

    [Theory]
    [MemberData(nameof(Tabla))]
    public async Task N2_Compuerta(
        int fila, string rol, string? mecanismo, bool baul, bool vid,
        bool esperaCorreo, string motivoEsperado, bool esperaAprobado)
    {
        // motivoEsperado / esperaAprobado los afirman los niveles 1 y 3; aquí viajan al mensaje de
        // fallo para que un caso rojo se lea como una fila de la tabla y no como un índice suelto.
        var caso = $"fila {fila} / {rol} / mecanismo {mecanismo ?? "—"} "
                   + $"(motivo {motivoEsperado}, gate {esperaAprobado})";
        var ct = TestContext.Current.CancellationToken;
        var esVendedor = rol == "vendedor";
        var (id, tenant, _instance) = NuevoTramite(esVendedor ? "traspaso" : "matricula_inicial");
        if (baul)
            ConFirmaDelBaul();
        if (vid)
            ConVidVigente(tenant);

        var (_, error) = await _put.HandleAsync(
            id, tenant, new PutActorsRequest([ActorInputJuridico(rol, mecanismo)]), ct);

        error.Should().BeNull(caso);
        await _kyverumClient.Received(esperaCorreo ? 1 : 0).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r =>
                r.Parte == rol && r.Documento == RlDocumento && r.Email == RlEmail),
            Arg.Any<CancellationToken>());
    }

    // ── Nivel 3 — el gate de radicación ───────────────────────────────────────

    [Theory]
    [MemberData(nameof(Tabla))]
    public async Task N3_GateDeRadicacion(
        int fila, string rol, string? mecanismo, bool baul, bool vid,
        bool esperaCorreo, string motivoEsperado, bool esperaAprobado)
    {
        // esperaCorreo / motivoEsperado son de los niveles 1 y 2; viajan al mensaje de fallo.
        var caso = $"fila {fila} / {rol} / mecanismo {mecanismo ?? "—"} "
                   + $"(correo {esperaCorreo}, motivo {motivoEsperado})";
        var ct = TestContext.Current.CancellationToken;
        var esVendedor = rol == "vendedor";
        var instance = esVendedor
            ? TraspasoConVendedorJuridico(mecanismo, vid)
            : MatriculaConCompradorJuridico(mecanismo, vid);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(instance);

        var handler = baul
            ? new GetWizardStateHandler(_repo, vaultPolicy: new StubBaul(FirmaDelBaul()))
            : new GetWizardStateHandler(_repo);

        var (result, _err) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        // Paso de Identidad: 4 en matrícula, 5 en traspaso (donde exige AMBAS partes; la contraparte
        // natural ya está aprobada, así que lo que se lee es el veredicto sobre el vendedor).
        var identidad = result!.Steps.Single(s => s.Index == (esVendedor ? 5 : 4));
        identidad.Status.Should().Be(esperaAprobado ? "complete" : "incomplete", caso);
    }

    // ── No regresión: el representante elegido manda sobre cualquier otro de la compañía ──

    [Fact]
    public async Task NoRegresion_ConDosRepresentantesAcreditados_ElCorreoVaAlQueEligioElGestor()
    {
        // El defecto que motivó el Feature entero. La compañía tenía acreditados a DOS representantes:
        // A, con firma de baúl vigente, y B, sin nada. El gestor elige a B en el trámite y, como la
        // compuerta preguntaba al directorio por LA COMPAÑÍA, veía «esta empresa ya tiene con qué
        // firmar» y a B no le llegaba nada: se quedaba sin identidad y sin forma de conseguirla.
        // La decisión se toma ahora por el documento de B, así que a B se le envía.
        const string DocumentoA = "1011111111";
        const string DocumentoB = "1022222222";
        var ct = TestContext.Current.CancellationToken;
        var (id, tenant, _) = NuevoTramite();

        // Solo el representante A tiene firma en el baúl (y ninguno tiene identidad vigente).
        _baul.ResolveAsync(Arg.Any<Guid>(), RlTipoDocumento, DocumentoA, Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "Alberto Representante", "sha", "path", "sha",
                new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), DocumentoA));

        var actor = new ActorInput(
            "comprador", "NIT", "900123456", "Empresa Compradora SAS", "contacto@empresa.com", "3001234567",
            PersonType: "juridical",
            RepresentanteLegal: new ActorRepresentanteLegal(
                RlTipoDocumento, DocumentoB, "Beatriz Representante", "beatriz@empresa.com", null));

        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);

        error.Should().BeNull();
        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Is<KyverumVerifyStartRequest>(r =>
                r.Documento == DocumentoB && r.Email == "beatriz@empresa.com"),
            Arg.Any<CancellationToken>());

        // Centinela: si alguien vuelve a resolver la cobertura por la COMPAÑÍA, la consulta saldría
        // con el NIT en vez de con el documento del representante elegido.
        await _baul.DidNotReceive().ResolveAsync(
            Arg.Any<Guid>(), "NIT", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (Guid Id, Guid Tenant, ProcedureInstance Instance) NuevoTramite(string modalidad = "matricula_inicial")
    {
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = modalidad,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return (id, tenant, instance);
    }

    private void ConFirmaDelBaul() =>
        _baul.ResolveAsync(Arg.Any<Guid>(), RlTipoDocumento, RlDocumento, Arg.Any<CancellationToken>())
            .Returns(FirmaDelBaul());

    private void ConVidVigente(Guid tenant) =>
        _repo.FindVigenteApprovedByDocumentAsync(
                tenant, RlTipoDocumento, RlDocumento, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(VidVigente(DateTimeOffset.UtcNow));

    private static SignatureVaultMatch FirmaDelBaul() => new(
        Guid.NewGuid(), RlNombre, "sig-hash", "vault/firma.png", "art-sha",
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        RlDocumento);

    /// <summary>Validación de identidad del representante, aprobada y dentro de vigencia.</summary>
    private static ProcedureInstanceBiometricValidation VidVigente(DateTimeOffset now, string? parte = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProcedureInstanceId = Guid.NewGuid(),
            PartyRole = parte,
            Name = RlNombre,
            DocumentType = RlTipoDocumento,
            DocumentNumber = RlDocumento,
            Email = RlEmail,
            Status = BiometricEstados.Aprobado,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "hash",
            ValidatedAt = now.AddDays(-1),
            ValidUntil = now.AddDays(29),
            ExpiresAt = now.AddHours(1),
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1),
        };

    private static ActorInput ActorInputJuridico(string rol, string? mecanismo) =>
        new(
            rol,
            "NIT",
            rol == "vendedor" ? "900987654" : "900123456",
            "Empresa SAS",
            "contacto@empresa.com",
            "3001234567",
            PersonType: "juridical",
            RepresentanteLegal: new ActorRepresentanteLegal(
                RlTipoDocumento, RlDocumento, RlNombre, RlEmail, null, mecanismo));

    private static ProcedureInstanceActor ActorJuridico(string rol, string? mecanismo)
    {
        var rl = new Dictionary<string, object?>
        {
            ["tipoDocumento"] = RlTipoDocumento,
            ["numeroDocumento"] = RlDocumento,
            ["nombreCompleto"] = RlNombre,
            ["email"] = RlEmail,
        };
        if (mecanismo is not null)
            rl["mecanismoFirma"] = mecanismo;

        return new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = rol,
            DocumentType = "NIT",
            DocumentNumber = rol == "vendedor" ? "900987654" : "900123456",
            FullName = "Empresa SAS",
            Email = "contacto@empresa.com",
            Phone = "3001234567",
            PersonType = ActorPersonTypes.Juridical,
            Metadata = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["ciudad"] = "Bogotá",
                ["direccion"] = "Calle 1 # 2-3",
                ["representanteLegal"] = rl,
            }),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Matrícula inicial completa salvo por la identidad, con el comprador jurídico bajo prueba.</summary>
    private static ProcedureInstance MatriculaConCompradorJuridico(string? mecanismo, bool vid)
    {
        var now = DateTimeOffset.UtcNow;
        var instance = BaseInstance("matricula_inicial", null, now);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "vin",
            ValueText = "1HGCM82633A004352",
            Source = "user",
        });
        instance.PreflightSnapshots.Add(Preflight(now));
        instance.Actors.Add(ActorJuridico("comprador", mecanismo));
        foreach (var tipo in new[] { "factura", "aduana", "impronta" })
            instance.Attachments.Add(Doc(tipo));
        if (vid)
            instance.BiometricValidations.Add(VidVigente(now, "comprador"));
        return instance;
    }

    /// <summary>
    /// Traspaso con los pasos de datos (1-4) completos y el comprador —persona natural— ya validado,
    /// para que el paso de Identidad (5) refleje únicamente el veredicto sobre el vendedor jurídico.
    /// </summary>
    private static ProcedureInstance TraspasoConVendedorJuridico(string? mecanismo, bool vid)
    {
        var now = DateTimeOffset.UtcNow;
        var instance = BaseInstance("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard, now);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "plate",
            ValueText = "ABC123",
            Source = "user",
        });
        instance.ChecklistEstado =
            "{\"contrato_compraventa\":true,\"impronta\":true,\"soat\":true,\"rtm\":true,\"paz_salvo\":true,\"cedulas\":true}";
        instance.PreflightSnapshots.Add(Preflight(now));
        instance.Commercial = new ProcedureInstanceCommercial
        {
            Id = Guid.NewGuid(),
            ValorVenta = 100m,
            CreatedAt = now,
        };
        instance.Actors.Add(ActorJuridico("vendedor", mecanismo));
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = CompradorNaturalDocumento,
            FullName = "Maria Compradora",
            Email = "maria@x.com",
            Phone = "3001234567",
            Metadata = ActorMetadataReader.Serialize("Bogotá", "Calle 1 # 2-3", null),
            CreatedAt = now,
        });
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            ProcedureInstanceId = instance.Id,
            PartyRole = "comprador",
            Name = "Maria Compradora",
            DocumentType = "CC",
            DocumentNumber = CompradorNaturalDocumento,
            Email = "maria@x.com",
            Status = BiometricEstados.Aprobado,
            Provider = BiometricProviders.Kyverum,
            TokenHash = "hash-comprador",
            ValidatedAt = now.AddDays(-1),
            ValidUntil = now.AddDays(29),
            ExpiresAt = now.AddHours(1),
            CreatedAt = now.AddDays(-1),
        });
        if (vid)
            instance.BiometricValidations.Add(VidVigente(now, "vendedor"));
        return instance;
    }

    private static ProcedureInstance BaseInstance(string modalidad, string? tipologia, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = modalidad,
            TipologiaCodigo = tipologia,
            CreatedAt = now,
        };

    private static ProcedureInstancePreflightSnapshot Preflight(DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), Overall = "green", Checks = "[]", CreatedAt = now };

    private static ProcedureInstanceAttachment Doc(string tipo) =>
        new()
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            StoragePath = $"x/{tipo}",
            UploadedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Baúl que resuelve la firma del representante legal del trámite y de nadie más.</summary>
    private sealed class StubBaul(SignatureVaultMatch match) : ISignatureVaultPolicy
    {
        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                string.Equals(documentNumber, RlDocumento, StringComparison.Ordinal) ? match : null);
    }
}
