using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// La exención de firma de la matrícula sin placa (canal gestor/OT de FLIT 2) debe verse igual en el
/// listado (<c>canSubmit</c> de la fila) y en el wizard, y el canal Quipux se lee UNA vez por OT dentro de
/// una misma llamada de listado.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>var fila = (await new ListProcedureInstancesHandler(repo, canal).HandleAsync(tenant, ct)).Single();</c>
/// ⇒ <c>fila.CanSubmit == wizard.CanSubmit</c>. Datos ficticios.
/// </remarks>
public sealed class ListaVsWizardFirmaExentaTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ITransitOfficeQuipuxCanalReader _canal = Substitute.For<ITransitOfficeQuipuxCanalReader>();
    private readonly Guid _ot = Guid.NewGuid();

    private ProcedureInstance Tramite(Guid tenantId)
    {
        var tipo = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "MATRICULA_NUEVA",
            Name = "Matrícula inicial",
            Family = "MATRICULAS",
            GateProfile = ProcedureTypeFixture.Matricula.GateProfile,
            Steps = ProcedureTypeFixture.Matricula.Steps,
            ExternalRefs = """{"quipux":{"familia":"MATRICULA"}}""",
        };
        var i = new ProcedureInstance
        {
            ProcedureType = tipo,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = tipo.Id,
            ReferenceNumber = "TRM-2026-000777",
            Status = TramiteEstado.Borrador,
            TransitOfficeId = _ot,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        i.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        i.PreflightSnapshots.Add(new ProcedureInstancePreflightSnapshot
        {
            Id = Guid.NewGuid(), Overall = "green", Checks = "[]", CreatedAt = DateTimeOffset.UtcNow,
        });
        i.Actors.Add(new ProcedureInstanceActor
        {
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "9000000777",
            FullName = "Persona comprador",
            Email = "c@example.test",
            Phone = "3001234567",
            Metadata = ActorMetadataReader.Serialize("Bogotá", "Calle 1 # 2-3", null),
        });
        foreach (var t in new[] { "factura", "aduana", "impronta" })
            i.Attachments.Add(new ProcedureInstanceAttachment { Id = Guid.NewGuid(), Tipo = t, Filename = $"{t}.pdf" });
        return i;
    }

    private void PrepararListado(Guid tenantId, params ProcedureInstance[] instancias)
    {
        _repo.ListWithSummaryGraphAsync(tenantId, ListProcedureInstancesHandler.MaxItems, Arg.Any<CancellationToken>())
            .Returns(instancias);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, bool>());
        foreach (var i in instancias)
            _repo.GetByIdWithWizardGraphAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>()).Returns(i);
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
    }

    [Theory]
    [InlineData(false, true)]  // sin canal Quipux: gestor/OT, exenta ⇒ la fila puede enviarse
    [InlineData(true, false)]  // canal Quipux activo: como antes, la firma bloquea
    public async Task ListadoYWizard_CoincidenEnCanSubmit_SegunElCanal(bool quipuxActivo, bool canSubmitEsperado)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var i = Tramite(tenant);
        PrepararListado(tenant, i);
        _canal.GetAsync(_ot, Arg.Any<CancellationToken>())
            .Returns(new TransitOfficeQuipuxCanal("11001", quipuxActivo, quipuxActivo, quipuxActivo));

        var fila = (await new ListProcedureInstancesHandler(_repo, _canal).HandleAsync(tenant, ct)).Should().ContainSingle().Subject;
        var (wizard, _) = await new GetWizardStateHandler(_repo, quipuxCanalReader: _canal).HandleAsync(i.Id, tenant, ct);

        fila.CanSubmit.Should().Be(canSubmitEsperado);
        wizard!.CanSubmit.Should().Be(fila.CanSubmit);
        wizard.FirmaExentaPorPreasignacion.Should().Be(canSubmitEsperado);
    }

    [Fact]
    public async Task Listado_LeeElCanalUnaSolaVezPorOt()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var a = Tramite(tenant);
        var b = Tramite(tenant);
        var c = Tramite(tenant);
        PrepararListado(tenant, a, b, c);
        _canal.GetAsync(_ot, Arg.Any<CancellationToken>())
            .Returns(new TransitOfficeQuipuxCanal("11001", false, false, false));

        var filas = await new ListProcedureInstancesHandler(_repo, _canal).HandleAsync(tenant, ct);

        filas.Should().HaveCount(3).And.OnlyContain(f => f.CanSubmit);
        await _canal.Received(1).GetAsync(_ot, Arg.Any<CancellationToken>());
    }
}
