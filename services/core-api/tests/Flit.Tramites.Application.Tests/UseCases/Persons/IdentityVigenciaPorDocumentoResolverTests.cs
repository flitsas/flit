using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Persons;

/// <summary>
/// HU #11751 (ADR-0050) — <see cref="IdentityVigenciaPorDocumentoResolver"/> y
/// <see cref="GetIdentityVigenciaPorDocumentoHandler"/>: normalización del documento (Trim + mayúsculas
/// invariantes, ADR-0039), los cuatro estados de vigencia, y que el tenant se use TAL CUAL se lo pasa
/// el caller (la ruta admin), sin depender de ningún header ni claim adicional.
///
/// <para>Uso de ejemplo:
/// <c>new GetIdentityVigenciaPorDocumentoHandler(new IdentityVigenciaPorDocumentoResolver(repo))
///     .HandleAsync(tenantId, "cc", " 900123456 ", ct)</c> ⇒ <c>DocumentType == "CC"</c>,
/// <c>DocumentNumber == "900123456"</c>.</para>
/// </summary>
public sealed class IdentityVigenciaPorDocumentoResolverTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    // ── documento requerido ────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_SinDocumento_DevuelveDocumentoRequerido()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new GetIdentityVigenciaPorDocumentoHandler(new IdentityVigenciaPorDocumentoResolver(_repo));

        var (result, error) = await handler.HandleAsync(Guid.NewGuid(), null, "  ", ct);

        result.Should().BeNull();
        error.Should().Be("documento_requerido");
        await _repo.DidNotReceive().ListBiometricValidationsByPersonAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── normalización (ADR-0039) ───────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_NormalizaTrimYMayusculas_AntesDeConsultar()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        _repo.ListBiometricValidationsByPersonAsync(
                tenantId, "CC", "900123456", 0, 1, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)[], 0, false));

        var handler = new GetIdentityVigenciaPorDocumentoHandler(new IdentityVigenciaPorDocumentoResolver(_repo));
        var (result, error) = await handler.HandleAsync(tenantId, "  cc ", " 900123456 ", ct);

        error.Should().BeNull();
        result!.DocumentType.Should().Be("CC");
        result.DocumentNumber.Should().Be("900123456");
        await _repo.Received(1).ListBiometricValidationsByPersonAsync(
            tenantId, "CC", "900123456", 0, 1, Arg.Any<CancellationToken>());
    }

    // ── derivación de tenant: se usa EXACTAMENTE el tenant recibido ────────

    [Fact]
    public async Task HandleAsync_UsaElTenantRecibido_SinDependerDeOtraFuente()
    {
        // No hay X-Tenant-Id ni claim en juego: el único tenant posible es el argumento de HandleAsync
        // (que la ruta admin ya resolvió vía CompanyOwnTenantFilter). Si el handler consultara OTRO
        // tenant, este test lo detecta: el repo devuelve datos SOLO para tenantA.
        var ct = TestContext.Current.CancellationToken;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        // HandleAsync clasifica con DateTimeOffset.UtcNow (no recibe reloj): la ventana debe ser
        // relativa a la hora real, no al `Now` congelado — con fecha fija el test caducaba solo.
        var hoy = DateTimeOffset.UtcNow;
        var aprobada = Aprobada(hoy.AddDays(-1), hoy.AddDays(29));

        _repo.ListBiometricValidationsByPersonAsync(
                tenantA, "CC", "900123456", 0, 1, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)[aprobada], 1, false));
        _repo.ListBiometricValidationsByPersonAsync(
                tenantB, "CC", "900123456", 0, 1, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)[], 0, false));

        var handler = new GetIdentityVigenciaPorDocumentoHandler(new IdentityVigenciaPorDocumentoResolver(_repo));
        var (result, _) = await handler.HandleAsync(tenantA, "CC", "900123456", ct);

        result!.Status.Should().Be(IdentityVigenciaEstados.AprobadaVigente);
    }

    // ── los cuatro estados, vía el resolver directo ────────────────────────

    [Fact]
    public async Task ResolveAsync_SinFilas_SinValidacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        _repo.ListBiometricValidationsByPersonAsync(
                tenantId, "CC", "123", 0, 1, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)[], 0, false));

        var resolver = new IdentityVigenciaPorDocumentoResolver(_repo);
        var result = await resolver.ResolveAsync(tenantId, "CC", "123", Now, ct);

        result.Status.Should().Be(IdentityVigenciaEstados.SinValidacion);
        result.ValidUntil.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_EnProceso_EnCurso()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var v = new ProcedureInstanceBiometricValidation
        {
            Status = BiometricEstados.EnProceso,
            DocumentType = "CC",
            DocumentNumber = "123",
        };
        _repo.ListBiometricValidationsByPersonAsync(
                tenantId, "CC", "123", 0, 1, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)[v], 1, true));

        var resolver = new IdentityVigenciaPorDocumentoResolver(_repo);
        var result = await resolver.ResolveAsync(tenantId, "CC", "123", Now, ct);

        result.Status.Should().Be(IdentityVigenciaEstados.EnCurso);
    }

    [Fact]
    public async Task ResolveAsync_AprobadaVigente_ExponeValidUntilYCertificado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var v = Aprobada(Now.AddDays(-5), Now.AddDays(25));
        v.CertificateHash = "hash-abc";
        _repo.ListBiometricValidationsByPersonAsync(
                tenantId, "CC", "123", 0, 1, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)[v], 1, false));

        var resolver = new IdentityVigenciaPorDocumentoResolver(_repo);
        var result = await resolver.ResolveAsync(tenantId, "CC", "123", Now, ct);

        result.Status.Should().Be(IdentityVigenciaEstados.AprobadaVigente);
        result.CertificateHash.Should().Be("hash-abc");
        result.ValidUntil.Should().Be(v.ValidUntil);
    }

    [Fact]
    public async Task ResolveAsync_AprobadaFueraDeVentana_Vencida()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var v = Aprobada(Now.AddDays(-40), Now.AddDays(-10));
        _repo.ListBiometricValidationsByPersonAsync(
                tenantId, "CC", "123", 0, 1, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)[v], 1, false));

        var resolver = new IdentityVigenciaPorDocumentoResolver(_repo);
        var result = await resolver.ResolveAsync(tenantId, "CC", "123", Now, ct);

        result.Status.Should().Be(IdentityVigenciaEstados.Vencida);
    }

    // ── Variante del mandatario (HU #13130b, HU #13247): SOLO su validación propia, sin ventana de 30 días ─────────

    private static readonly Guid Signer = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ProcedureInstanceBiometricValidation Propia(
        string status, DateTimeOffset createdAt, string tipo = "CC", string numero = "123", Guid? signer = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Status = status,
            PartyRole = BiometricRules.ParteMandatario,
            MandateSignerId = signer ?? Signer,
            DocumentType = tipo,
            DocumentNumber = numero,
            ValidatedAt = status == BiometricEstados.Aprobado ? createdAt.AddMinutes(5) : null,
            CreatedAt = createdAt,
            CertificateHash = status == BiometricEstados.Aprobado ? "hash-abc" : null,
        };

    private void SeedPropias(params ProcedureInstanceBiometricValidation[] rowsNewestFirst) =>
        _repo.ListMandatarioValidationsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ProcedureInstanceBiometricValidation>)rowsNewestFirst);

    [Fact]
    public async Task ResolveMandatarioAsync_PropiaAprobadaHace40Dias_AprobadaVigenteSinFechaDeFin()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedPropias(Propia(BiometricEstados.Aprobado, Now.AddDays(-40)));

        var result = await new IdentityVigenciaPorDocumentoResolver(_repo)
            .ResolveMandatarioAsync(Signer, "CC", "123", Now, ct);

        result.Status.Should().Be(IdentityVigenciaEstados.AprobadaVigente);
        result.CertificateHash.Should().Be("hash-abc");
        result.ValidUntil.Should().BeNull();
    }

    [Fact]
    public async Task ResolveMandatarioAsync_PropiaAprobada_TraeLaRubricaDeKyverum_ParaEstamparlaEnElMandato()
    {
        var ct = TestContext.Current.CancellationToken;
        var propia = Propia(BiometricEstados.Aprobado, Now.AddDays(-2));
        propia.SignatureImagePath = "identity/rubrica.png";
        SeedPropias(propia);

        var result = await new IdentityVigenciaPorDocumentoResolver(_repo)
            .ResolveMandatarioAsync(Signer, "CC", "123", Now, ct);

        result.SignatureImagePath.Should().Be("identity/rubrica.png");
    }

    [Fact]
    public async Task ResolveMandatarioAsync_EnCurso_NoTraeRubrica()
    {
        var ct = TestContext.Current.CancellationToken;
        var propia = Propia(BiometricEstados.EnProceso, Now.AddHours(-1));
        propia.SignatureImagePath = "identity/rubrica.png";
        SeedPropias(propia);

        var result = await new IdentityVigenciaPorDocumentoResolver(_repo)
            .ResolveMandatarioAsync(Signer, "CC", "123", Now, ct);

        result.SignatureImagePath.Should().BeNull();
    }

    [Fact]
    public async Task ResolveMandatarioAsync_SoloHayAprobacionDeOtroRol_SinValidacion_YNoConsultaPorDocumento()
    {
        // HU #13247 AC2/AC5 — comprador, vendedor o prevalidación aprobados con la misma cédula no cuentan: el repositorio
        // solo devuelve filas con party_role mandatario + la ficha; sin ninguna, el estado es sin validación.
        var ct = TestContext.Current.CancellationToken;
        SeedPropias();

        var result = await new IdentityVigenciaPorDocumentoResolver(_repo)
            .ResolveMandatarioAsync(Signer, "CC", "123", Now, ct);

        result.Should().Be(IdentityVigenciaResult.SinValidacion);
        await _repo.DidNotReceive().ListBiometricValidationsByPersonAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().ListLatestBiometricValidationsByPersonsAsync(
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyCollection<(string DocumentTypeNorm, string DocumentNumberNorm)>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveMandatarioAsync_PropiaAprobadaDeUnDocumentoAnterior_NoCuenta()
    {
        // HU #13247 AC3 — tras editar el tipo o el número de documento, la validación del documento anterior no cuenta.
        var ct = TestContext.Current.CancellationToken;
        SeedPropias(Propia(BiometricEstados.Aprobado, Now.AddDays(-3), numero: "999"));

        var result = await new IdentityVigenciaPorDocumentoResolver(_repo)
            .ResolveMandatarioAsync(Signer, "CC", "123", Now, ct);

        result.Status.Should().Be(IdentityVigenciaEstados.SinValidacion);
    }

    [Fact]
    public async Task ResolveMandatarioAsync_DocumentoSeCompara_ConLaRegla_TrimYMayusculas()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedPropias(Propia(BiometricEstados.Aprobado, Now.AddDays(-3), tipo: "cc", numero: " ab123 "));

        var result = await new IdentityVigenciaPorDocumentoResolver(_repo)
            .ResolveMandatarioAsync(Signer, " CC", "AB123", Now, ct);

        result.Status.Should().Be(IdentityVigenciaEstados.AprobadaVigente);
    }

    [Fact]
    public async Task ResolveMandatarioAsync_UnaValidacionMasRecienteEnCurso_LaAprobadaAnteriorDejaDeContar()
    {
        // HU #13246 AC3 / HU #13247 AC3 — reenviar, cambiar de forma de firma o de documento lanza una nueva: manda la última.
        var ct = TestContext.Current.CancellationToken;
        SeedPropias(
            Propia(BiometricEstados.EnProceso, Now.AddHours(-1)),
            Propia(BiometricEstados.Aprobado, Now.AddDays(-60)));

        var result = await new IdentityVigenciaPorDocumentoResolver(_repo)
            .ResolveMandatarioAsync(Signer, "CC", "123", Now, ct);

        result.Status.Should().Be(IdentityVigenciaEstados.EnCurso);
    }

    [Theory]
    [InlineData(BiometricEstados.Rechazado, IdentityVigenciaEstados.SinValidacion)]
    [InlineData(BiometricEstados.ErrorEnvio, IdentityVigenciaEstados.SinValidacion)]
    [InlineData(BiometricEstados.Expirado, IdentityVigenciaEstados.Vencida)]
    [InlineData(BiometricEstados.PendienteEnvio, IdentityVigenciaEstados.EnCurso)]
    public async Task ResolveMandatarioAsync_SinAprobacion_ClasificaLaMasReciente(string estado, string esperado)
    {
        var ct = TestContext.Current.CancellationToken;
        SeedPropias(Propia(estado, Now.AddHours(-1)));

        (await new IdentityVigenciaPorDocumentoResolver(_repo).ResolveMandatarioAsync(Signer, "CC", "123", Now, ct))
            .Status.Should().Be(esperado);
    }

    [Fact]
    public async Task ResolveMandatariosAsync_CadaFichaSoloVeLasSuyas_YFichaSinDocumentoQuedaSinValidacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var otra = Guid.NewGuid();
        var sinDoc = Guid.NewGuid();
        SeedPropias(
            Propia(BiometricEstados.Aprobado, Now.AddDays(-40), signer: Signer),
            Propia(BiometricEstados.Aprobado, Now.AddDays(-2), numero: "456", signer: otra));

        var result = await new IdentityVigenciaPorDocumentoResolver(_repo).ResolveMandatariosAsync(
            [
                new IdentityVigenciaPorDocumentoResolver.MandatarioIdentityRef(Signer, "CC", "123"),
                new IdentityVigenciaPorDocumentoResolver.MandatarioIdentityRef(otra, "CC", "123"),
                new IdentityVigenciaPorDocumentoResolver.MandatarioIdentityRef(sinDoc, "CC", " "),
            ],
            Now,
            ct);

        result[Signer].Status.Should().Be(IdentityVigenciaEstados.AprobadaVigente);
        result[otra].Status.Should().Be(IdentityVigenciaEstados.SinValidacion); // su aprobación es de otro documento
        result[sinDoc].Status.Should().Be(IdentityVigenciaEstados.SinValidacion);
    }

    [Fact]
    public async Task ResolveManyBatchedAsync_ElTramiteNoCambia_LaVentanaDe30DiasSigueVigente()
    {
        // HU #13247 AC7 — la identidad de comprador/vendedor/prevalidación y BiometricRules.VigenciaDias no se tocan.
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var v = Aprobada(Now.AddDays(-40), Now.AddDays(-10));
        _repo.ListLatestBiometricValidationsByPersonsAsync(
                tenantId,
                Arg.Any<IReadOnlyCollection<(string DocumentTypeNorm, string DocumentNumberNorm)>>(),
                Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ProcedureInstanceBiometricValidation>)[v]);
        var key = Flit.Tramites.Domain.Identity.DocumentCanonicalNormalization.IdentidadKey(tenantId, "CC", "123");

        (await new IdentityVigenciaPorDocumentoResolver(_repo).ResolveManyBatchedAsync(tenantId, [("CC", "123")], Now, ct))[key]
            .Status.Should().Be(IdentityVigenciaEstados.Vencida);
        BiometricRules.VigenciaDias.Should().Be(30);
    }

    // ── ResolveManyAsync (HU #11752) ────────────────────────────────────────

    [Fact]
    public async Task ResolveManyAsync_ResuelveCadaDocumentoUnaSolaVez_YDeduplicaRepetidos()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        _repo.ListBiometricValidationsByPersonAsync(
                tenantId, "CC", "111", 0, 1, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)[], 0, false));
        _repo.ListBiometricValidationsByPersonAsync(
                tenantId, "NIT", "900", 0, 1, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)[Aprobada(Now.AddDays(-1), Now.AddDays(29))], 1, false));

        var resolver = new IdentityVigenciaPorDocumentoResolver(_repo);
        var documents = new (string, string)[] { ("CC", "111"), ("NIT", "900"), ("cc", " 111 ") };

        var result = await resolver.ResolveManyAsync(tenantId, documents, Now, ct);

        result.Should().HaveCount(2);
        await _repo.Received(1).ListBiometricValidationsByPersonAsync(
            tenantId, "CC", "111", 0, 1, Arg.Any<CancellationToken>());
    }

    private static ProcedureInstanceBiometricValidation Aprobada(DateTimeOffset validatedAt, DateTimeOffset validUntil) =>
        new()
        {
            Status = BiometricEstados.Aprobado,
            DocumentType = "CC",
            DocumentNumber = "123",
            ValidatedAt = validatedAt,
            ValidUntil = validUntil,
        };
}
