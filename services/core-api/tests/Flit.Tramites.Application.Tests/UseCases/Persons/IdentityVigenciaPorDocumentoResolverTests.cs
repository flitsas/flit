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
        var aprobada = Aprobada(Now.AddDays(-1), Now.AddDays(29));

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
