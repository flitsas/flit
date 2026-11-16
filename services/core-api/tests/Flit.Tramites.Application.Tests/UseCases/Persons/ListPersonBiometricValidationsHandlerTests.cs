using System.Text.Json;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Persons;

/// <summary>HU #11272 — historial multi-validación por persona.</summary>
public sealed class ListPersonBiometricValidationsHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private ListPersonBiometricValidationsHandler Handler() => new(_repo);

    [Fact]
    public async Task DevuelveHistorial_OrdenadoYAllTerminal()
    {
        var tenantId = Guid.NewGuid();
        var older = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DocumentType = "CC",
            DocumentNumber = "123",
            Name = "Ana",
            Email = "a@b.co",
            Status = BiometricEstados.Rechazado,
            TokenHash = "a",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        };
        var newer = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DocumentType = "CC",
            DocumentNumber = "123",
            Name = "Ana",
            Email = "a@b.co",
            Status = BiometricEstados.Aprobado,
            TokenHash = "b",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            ValidatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repo.ListBiometricValidationsByPersonAsync(
                tenantId, "CC", "123", 0, 20, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)[newer, older], 2, false));

        _repo.ListLinkedProceduresByIdentityDocumentsAsync(
                tenantId, Arg.Any<IReadOnlyCollection<(string, string)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, IReadOnlyList<LinkedProcedureSummary>>());

        var (result, error) = await Handler().HandleAsync(
            tenantId, " cc ", " 123 ", page: 1, pageSize: 20, ct: TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Total.Should().Be(2);
        result.Validations.Should().HaveCount(2);
        result.Validations[0].Id.Should().Be(newer.Id);
        result.AllTerminal.Should().BeTrue();
    }

    [Fact]
    public async Task DocumentoVacio_ReturnsError()
    {
        var (result, error) = await Handler().HandleAsync(
            Guid.NewGuid(), "  ", null, ct: TestContext.Current.CancellationToken);
        result.Should().BeNull();
        error.Should().Be("documento_requerido");
    }
    // ── Ajuste 2026-11-10 (punto 4): id de Kyverum Verify en el detalle por persona ─────────────────

    /// <summary>Mismas opciones que <c>Results.Ok</c> en Minimal APIs (JsonSerializerDefaults.Web, sin ignorar nulls).</summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static ProcedureInstanceBiometricValidation Fila(Guid tenantId, string? kyverumId, int diasAtras) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        DocumentType = "CC",
        DocumentNumber = "123",
        Name = "Ana",
        Email = "ana.perez@correo.co",
        Status = BiometricEstados.Aprobado,
        Provider = BiometricProviders.Kyverum,
        KyverumVerificationId = kyverumId,
        TokenHash = Guid.NewGuid().ToString("N"),
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        ValidatedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-diasAtras),
    };

    private void Repo(Guid tenantId, params ProcedureInstanceBiometricValidation[] rows)
    {
        _repo.ListBiometricValidationsByPersonAsync(
                tenantId, "CC", "123", 0, 20, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceBiometricValidation>)rows, rows.Length, false));
        _repo.ListLinkedProceduresByIdentityDocumentsAsync(
                tenantId, Arg.Any<IReadOnlyCollection<(string, string)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, IReadOnlyList<LinkedProcedureSummary>>());
    }

    [Fact]
    public async Task Detalle_ExponeKyverumVerificationId_DeCadaValidacion_YNullSiNoHay()
    {
        var tenantId = Guid.NewGuid();
        var conId = Fila(tenantId, "kv-ver-0001", diasAtras: 0);
        var sinId = Fila(tenantId, null, diasAtras: 1); // mock / cancelada: la columna queda en null
        Repo(tenantId, conId, sinId);

        var (result, error) = await Handler().HandleAsync(
            tenantId, "CC", "123", page: 1, pageSize: 20, ct: TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Validations.Should().HaveCount(2);
        result.Validations[0].KyverumVerificationId.Should().Be("kv-ver-0001");
        result.Validations[1].KyverumVerificationId.Should().BeNull();
        // El resto del DTO compartido no cambia.
        result.Validations[0].Id.Should().Be(conId.Id);
        result.Validations[0].Status.Should().Be(BiometricEstados.Aprobado);
    }

    [Fact]
    public async Task Detalle_SerializaKyverumVerificationId_EnCamelCase_ConNullExplicito()
    {
        var tenantId = Guid.NewGuid();
        Repo(tenantId, Fila(tenantId, "kv-ver-0002", 0), Fila(tenantId, null, 1));

        var (result, _) = await Handler().HandleAsync(
            tenantId, "CC", "123", page: 1, pageSize: 20, ct: TestContext.Current.CancellationToken);

        var json = JsonSerializer.Serialize(result, WebJson);
        using var doc = JsonDocument.Parse(json);
        var validations = doc.RootElement.GetProperty("validations");
        validations[0].GetProperty("kyverumVerificationId").GetString().Should().Be("kv-ver-0002");
        validations[1].GetProperty("kyverumVerificationId").ValueKind.Should().Be(JsonValueKind.Null);
        // El contrato compartido sigue viajando completo (no se envuelve la validación).
        validations[0].GetProperty("id").GetGuid().Should().NotBeEmpty();
        validations[0].GetProperty("status").GetString().Should().Be(BiometricEstados.Aprobado);
    }

    [Fact]
    public async Task Red_HijaConservaKyverumVerificationId_YSigueEnmascarandoCorreo()
    {
        var cabeza = Guid.NewGuid();
        var hija = Guid.NewGuid();
        Repo(hija, Fila(hija, "kv-ver-0003", 0));
        var network = new NetworkListPersonIdentityValidationsHandler(Handler());

        var (result, error) = await network.HandleAsync(
            TenantScope.Group(cabeza, [hija], GroupKind.Concesion), hija, "CC", "123", 1, 20,
            ct: TestContext.Current.CancellationToken);

        error.Should().BeNull();
        var v = result!.Validations.Should().ContainSingle().Subject;
        v.KyverumVerificationId.Should().Be("kv-ver-0003");
        v.Email.Should().NotBe("ana.perez@correo.co", "la red ve el correo enmascarado");
        v.CaptureUrl.Should().BeNull();
    }
}
