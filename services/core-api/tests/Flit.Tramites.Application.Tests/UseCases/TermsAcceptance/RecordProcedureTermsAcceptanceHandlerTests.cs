using Flit.Tramites.Application.UseCases.TermsAcceptance;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.TermsAcceptance;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.TermsAcceptance;

/// <summary>
/// Epic #12543 §5 y RN-03: la aceptación queda con user_id, accepted_at UTC, tramite_type e IP;
/// se escribe la evidencia ANTES del reflejo en auditoría, y un fallo al persistir se propaga
/// (el endpoint no puede responder 201 sin fila).
/// </summary>
public sealed class RecordProcedureTermsAcceptanceHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakeRepo _repo = new();
    private readonly FakeAudit _audit = new();
    private readonly IProcedureTypeRepository _types = Substitute.For<IProcedureTypeRepository>();

    private RecordProcedureTermsAcceptanceHandler Handler(string url = ProcedureTermsOptions.DefaultUrl) =>
        new(_repo, _audit, _types, new ProcedureTermsOptions { Url = url });

    [Fact]
    public async Task Registra_UsuarioFechaUtcTipoEIp_YReflejaEnAuditoria()
    {
        _types.CodeExistsAsync("matricula_inicial", Arg.Any<CancellationToken>()).Returns(true);
        var antes = DateTimeOffset.UtcNow;

        var result = await Handler().HandleAsync(
            new RecordProcedureTermsAcceptanceCommand(Tenant, User, "matricula_inicial", "190.1.2.3", "Mozilla/5.0"),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RecordProcedureTermsAcceptanceOutcome.Recorded);
        var fila = _repo.Added.Should().ContainSingle().Subject;
        fila.UserId.Should().Be(User);
        fila.TenantId.Should().Be(Tenant);
        fila.ProcedureTypeCode.Should().Be("matricula_inicial");
        fila.ClientIp.Should().Be("190.1.2.3");
        fila.UserAgent.Should().Be("Mozilla/5.0");
        fila.TermsUrl.Should().Be(ProcedureTermsOptions.DefaultUrl);
        fila.AcceptedAt.Offset.Should().Be(TimeSpan.Zero, "accepted_at es UTC");
        fila.AcceptedAt.Should().BeOnOrAfter(antes).And.BeOnOrBefore(DateTimeOffset.UtcNow);

        _audit.Written.Should().ContainSingle().Which.Should().BeSameAs(fila);
        result.Acceptance.Should().BeSameAs(fila);
    }

    [Fact]
    public async Task RecortaElCode_YGuardaLaUrlConfigurada()
    {
        _types.CodeExistsAsync("TRASPASO_STANDARD", Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler("https://ejemplo.test/tyc").HandleAsync(
            new RecordProcedureTermsAcceptanceCommand(null, User, "  TRASPASO_STANDARD ", null, null),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RecordProcedureTermsAcceptanceOutcome.Recorded);
        _repo.Added.Single().ProcedureTypeCode.Should().Be("TRASPASO_STANDARD");
        _repo.Added.Single().TermsUrl.Should().Be("https://ejemplo.test/tyc");
        _repo.Added.Single().TenantId.Should().BeNull("el SuperAdmin puede aceptar sin acotar compañía");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SinCode_NoPersisteNada(string? code)
    {
        var result = await Handler().HandleAsync(
            new RecordProcedureTermsAcceptanceCommand(Tenant, User, code!, null, null),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RecordProcedureTermsAcceptanceOutcome.InvalidProcedureTypeCode);
        _repo.Added.Should().BeEmpty();
        _audit.Written.Should().BeEmpty();
        await _types.DidNotReceiveWithAnyArgs().CodeExistsAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeMasLargoQueLaColumna_NoPersisteNada()
    {
        var result = await Handler().HandleAsync(
            new RecordProcedureTermsAcceptanceCommand(Tenant, User, new string('x', 51), null, null),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RecordProcedureTermsAcceptanceOutcome.InvalidProcedureTypeCode);
        _repo.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task CodeQueNoExisteEnElCatalogo_NoPersisteNada()
    {
        _types.CodeExistsAsync("inventado", Arg.Any<CancellationToken>()).Returns(false);

        var result = await Handler().HandleAsync(
            new RecordProcedureTermsAcceptanceCommand(Tenant, User, "inventado", null, null),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RecordProcedureTermsAcceptanceOutcome.ProcedureTypeNotFound);
        _repo.Added.Should().BeEmpty();
        _audit.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task SiLaEvidenciaNoSeEscribe_ElFalloSePropaga_YNoSeAudita()
    {
        _types.CodeExistsAsync("matricula_inicial", Arg.Any<CancellationToken>()).Returns(true);
        _repo.FailWith = new InvalidOperationException("db caída");

        var act = () => Handler().HandleAsync(
            new RecordProcedureTermsAcceptanceCommand(Tenant, User, "matricula_inicial", null, null),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("db caída");
        _audit.Written.Should().BeEmpty("el reflejo va DESPUÉS de la evidencia");
    }

    private sealed class FakeRepo : IProcedureTermsAcceptanceRepository
    {
        public List<ProcedureTermsAcceptance> Added { get; } = [];
        public Exception? FailWith { get; set; }

        public Task AddAsync(ProcedureTermsAcceptance acceptance, CancellationToken ct = default)
        {
            if (FailWith is not null) throw FailWith;
            Added.Add(acceptance);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAudit : IProcedureTermsAcceptanceAuditWriter
    {
        public List<ProcedureTermsAcceptance> Written { get; } = [];

        public Task WriteAcceptedAsync(ProcedureTermsAcceptance acceptance, CancellationToken ct = default)
        {
            Written.Add(acceptance);
            return Task.CompletedTask;
        }
    }
}
