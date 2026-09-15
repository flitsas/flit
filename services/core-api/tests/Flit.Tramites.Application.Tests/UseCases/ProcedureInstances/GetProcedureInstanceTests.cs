using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.RevocationRequests;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class GetProcedureInstanceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetProcedureInstanceHandler _sut;

    public GetProcedureInstanceTests()
    {
        _sut = new GetProcedureInstanceHandler(_repo);
    }

    [Fact]
    public async Task HandleAsync_NotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct)
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_Exists_ReturnsMappedDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
            FieldValues =
            {
                new ProcedureInstanceFieldValue
                {
                    Id = Guid.NewGuid(),
                    FormFieldId = Guid.NewGuid(),
                    FieldKey = "plate",
                    ValueText = "ABC123",
                    Source = "user"
                }
            },
            StatusHistory =
            {
                new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ToStatus = TramiteEstado.Borrador,
                    ChangedAt = DateTimeOffset.UtcNow
                }
            }
        };

        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.ReferenceNumber.Should().Be("TRM-2026-000001");
        result.FieldValues.Should().ContainSingle(f => f.FieldKey == "plate");
        result.StatusHistory.Should().ContainSingle(h => h.ToStatus == TramiteEstado.Borrador);
    }

    // Bug #12526 — la Línea de tiempo del trámite mostraba gestor/correo/empresa fijos en vacío aunque
    // el backend tuviera el dato de quién ejecutó cada transición (changed_by).
    [Fact]
    public async Task HandleAsync_StatusHistoryConChangedBy_ResuelveGestorCorreoYCompania()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var ejecutor = Guid.NewGuid();

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000007",
            Status = TramiteEstado.Entregado,
            CreatedAt = DateTimeOffset.UtcNow,
            StatusHistory =
            {
                new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    FromStatus = TramiteEstado.Preparado,
                    ToStatus = TramiteEstado.Entregado,
                    ChangedAt = DateTimeOffset.UtcNow,
                    ChangedBy = ejecutor,
                },
            },
        };

        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [ejecutor] = "Laura Restrepo" });
        _repo.GetUserEmailsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [ejecutor] = "laura.restrepo@renting.com" });
        _repo.GetUserCompaniasAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [ejecutor] = "Renting Colombia S.A.S" });

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        var entry = result!.StatusHistory.Should().ContainSingle().Subject;
        entry.ChangedByName.Should().Be("Laura Restrepo");
        entry.ChangedByEmail.Should().Be("laura.restrepo@renting.com");
        entry.ChangedByCompania.Should().Be("Renting Colombia S.A.S");
    }

    [Fact]
    public async Task HandleAsync_StatusHistorySinChangedBy_NoConsultaUsuarios()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000008",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
            StatusHistory =
            {
                new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    ToStatus = TramiteEstado.Borrador,
                    ChangedAt = DateTimeOffset.UtcNow,
                    ChangedBy = null,
                },
            },
        };

        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        var entry = result!.StatusHistory.Should().ContainSingle().Subject;
        entry.ChangedByName.Should().BeNull();
        entry.ChangedByEmail.Should().BeNull();
        entry.ChangedByCompania.Should().BeNull();
        await _repo.DidNotReceive().GetUserEmailsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetUserCompaniasAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    // HU #10871 — el detalle de instancia expone motivo+items de la observación de subsanación,
    // RECORTADOS del metadata jsonb (sin fieldSnapshot ni ot_tenant_id/approver_tenant_id).
    [Fact]
    public async Task HandleAsync_SubsanacionEntry_ExposesMotivoAndItemsWithoutSensitiveKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var otTenantId = Guid.NewGuid();
        var approverTenantId = Guid.NewGuid();

        // Shape real que persiste OtClientProcedureRepository.BuildStatusHistoryMetadata para una
        // transición a 'subsanacion': auditoría cross-tenant + checklist híbrido + snapshot de campos.
        var rawMetadata = $$"""
            {
              "ot_tenant_id": "{{otTenantId}}",
              "approver_tenant_id": "{{approverTenantId}}",
              "source": "ot_portal",
              "motivo": "Documento ilegible",
              "items": [
                { "campo": "cedula_comprador", "detalle": "La foto está borrosa" },
                { "campo": "runt", "detalle": "Falta el sello" }
              ],
              "fieldSnapshot": { "plate": "ABC123" }
            }
            """;

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000002",
            Status = TramiteEstado.Rechazado,
            SubsanacionActiva = true,
            CreatedAt = DateTimeOffset.UtcNow,
            StatusHistory =
            {
                new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    FromStatus = TramiteEstado.Entregado,
                    ToStatus = TramiteEstado.Rechazado,
                    ChangedAt = DateTimeOffset.UtcNow,
                    Reason = "Documento ilegible",
                    Metadata = rawMetadata,
                }
            }
        };

        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        var entry = result!.StatusHistory.Should().ContainSingle(h => h.ToStatus == TramiteEstado.Rechazado).Subject;
        entry.Metadata.Should().NotBeNullOrWhiteSpace();
        entry.Metadata.Should().Contain("Documento ilegible");
        entry.Metadata.Should().Contain("cedula_comprador");
        // El encoder por defecto de System.Text.Json escapa no-ASCII (á → á): se valida
        // deserializando en vez de comparar el string crudo.
        var parsed = JsonSerializer.Deserialize<JsonElement>(entry.Metadata!);
        parsed.GetProperty("items")[0].GetProperty("detalle").GetString().Should().Be("La foto está borrosa");
        entry.Metadata.Should().NotContain("fieldSnapshot");
        entry.Metadata.Should().NotContain("ABC123");
        entry.Metadata.Should().NotContain(otTenantId.ToString());
        entry.Metadata.Should().NotContain(approverTenantId.ToString());
        entry.Metadata.Should().NotContain("ot_tenant_id");
        entry.Metadata.Should().NotContain("approver_tenant_id");
        entry.Metadata.Should().NotContain("ot_portal");
    }

    // Transiciones sin checklist (p. ej. aprobar/rechazar) no traen observación: Metadata queda null,
    // sin regresión del contrato para consumidores que no lo esperan.
    [Fact]
    public async Task HandleAsync_EntryWithoutObservation_MetadataIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000003",
            Status = TramiteEstado.Aprobado,
            CreatedAt = DateTimeOffset.UtcNow,
            StatusHistory =
            {
                new ProcedureInstanceStatusHistory
                {
                    Id = Guid.NewGuid(),
                    FromStatus = TramiteEstado.Entregado,
                    ToStatus = TramiteEstado.Aprobado,
                    ChangedAt = DateTimeOffset.UtcNow,
                    Metadata = """{"ot_tenant_id":"11111111-1111-1111-1111-111111111111","approver_tenant_id":"11111111-1111-1111-1111-111111111111","source":"ot_portal"}""",
                }
            }
        };

        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        var entry = result!.StatusHistory.Should().ContainSingle(h => h.ToStatus == TramiteEstado.Aprobado).Subject;
        entry.Metadata.Should().BeNull();
    }

    // ── Bug #12376, defectos 3/4 — eventos administrativos en el tracking ──────────────────────────

    [Fact]
    public async Task HandleAsync_ReasignarGestorEvent_ExponeNombresResueltos()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var ejecutor = Guid.NewGuid();
        var gestorAnterior = Guid.NewGuid();
        var gestorNuevo = Guid.NewGuid();
        var cuando = DateTimeOffset.UtcNow;

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000004",
            Status = TramiteEstado.Entregado,
            CreatedAt = DateTimeOffset.UtcNow,
            Events =
            {
                new ProcedureInstanceEvent
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Tipo = "reasignar_gestor_admin",
                    Payload = JsonSerializer.Serialize(new
                    {
                        previous_assigned_to_user_id = gestorAnterior,
                        new_assigned_to_user_id = gestorNuevo,
                    }),
                    CreatedAt = cuando,
                    CreatedBy = ejecutor,
                },
            },
        };

        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);
        _repo.GetUserDisplayNamesAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Contains(ejecutor) && ids.Contains(gestorAnterior) && ids.Contains(gestorNuevo)),
                ct)
            .Returns(new Dictionary<Guid, string>
            {
                [ejecutor] = "Ana Ejecutora",
                [gestorAnterior] = "Carlos Anterior",
                [gestorNuevo] = "Diana Nueva",
            });
        // Hallazgo posterior al Bug #12526: el mismo vacío de Correo/Empresa ocurría en la tarjeta de
        // "Reasignación de gestor" — correo/empresa son del gestor NUEVO, la misma persona ya resuelta
        // arriba como "Diana Nueva".
        _repo.GetUserEmailsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [gestorNuevo] = "diana.nueva@renting.com" });
        _repo.GetUserCompaniasAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [gestorNuevo] = "Renting Colombia S.A.S" });

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        var evento = result!.Events.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be("reasignar_gestor_admin");
        evento.CreatedByName.Should().Be("Ana Ejecutora");
        evento.PreviousAssignedToName.Should().Be("Carlos Anterior");
        evento.NewAssignedToName.Should().Be("Diana Nueva");
        evento.NewAssignedToEmail.Should().Be("diana.nueva@renting.com");
        evento.NewAssignedToCompania.Should().Be("Renting Colombia S.A.S");
        evento.CreatedAt.Should().Be(cuando);
    }

    [Fact]
    public async Task HandleAsync_ReenvioValidacionEvent_ExponeDetalleSinCorreoEnClaro()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var ejecutor = Guid.NewGuid();

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000005",
            Status = TramiteEstado.Entregado,
            CreatedAt = DateTimeOffset.UtcNow,
            Events =
            {
                new ProcedureInstanceEvent
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Tipo = "reenvio_validacion_admin",
                    Payload = JsonSerializer.Serialize(new
                    {
                        validation_id = Guid.NewGuid(),
                        party_role = "comprador",
                        email_actualizado = true,
                        correo_destino = "nueva@dominio.com",
                        encolado = false,
                    }),
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = ejecutor,
                },
            },
        };

        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [ejecutor] = "Ana Ejecutora" });
        // Sin gestor propio del evento (el correo ya es el destino del reenvío): la empresa que aporta
        // información es la de quien lo ejecutó, ya nombrado en Rol.
        _repo.GetUserCompaniasAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [ejecutor] = "Renting Colombia S.A.S" });

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        var evento = result!.Events.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be("reenvio_validacion_admin");
        evento.CreatedByName.Should().Be("Ana Ejecutora");
        evento.PartyRole.Should().Be("comprador");
        evento.EmailActualizado.Should().BeTrue();
        evento.CorreoDestino.Should().Be("nueva@dominio.com");
        evento.CreatedByCompania.Should().Be("Renting Colombia S.A.S");
    }

    // Bug #12376 — solo reenvío/reasignación se exponen en Events; el resto de tipos (p.ej.
    // anular_admin) ya está cubierto por StatusHistory y NO debe duplicarse en el timeline.
    [Fact]
    public async Task HandleAsync_EventoNoRelevante_NoApareceEnEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000006",
            Status = TramiteEstado.Anulado,
            CreatedAt = DateTimeOffset.UtcNow,
            Events =
            {
                new ProcedureInstanceEvent
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Tipo = "anular_admin",
                    Payload = "{}",
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            },
        };

        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        result!.Events.Should().BeEmpty();
        await _repo.DidNotReceive().GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    // ── HU #12573 (Feature #12565) — RevocationEligibility (AC1-AC3 del botón "Solicitar revocatoria") ──

    private static ProcedureInstance AprobadoInstance(
        Guid tenantId, string? origin = null, bool isMigrated = false, Guid? transitOfficeId = null) => new()
    {
        ProcedureType = ProcedureTypeFixture.Matricula,
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000009",
        Status = TramiteEstado.Aprobado,
        Origin = origin,
        IsMigrated = isMigrated,
        TransitOfficeId = transitOfficeId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task HandleAsync_SinDependenciasDeRevocatoria_RevocationEligibilityEsNull()
    {
        // Construcción manual como el resto de tests de esta clase (1 solo arg): la degradación es
        // segura — el resto del contrato sigue funcionando igual.
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instance = AprobadoInstance(tenantId);
        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        result!.RevocationEligibility.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_TramiteNoAprobado_RevocationEligibilityEsNullAunConDependencias()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var revocationRepo = Substitute.For<IProcedureRevocationRequestRepository>();
        var calculator = Substitute.For<IBusinessDayCalculator>();
        var sut = new GetProcedureInstanceHandler(_repo, revocationRepo, calculator);

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000010",
            Status = TramiteEstado.Entregado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);

        var (result, error) = await sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        result!.RevocationEligibility.Should().BeNull();
        await revocationRepo.DidNotReceive().GetFirstApprovedAtAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AprobadoOrigenDashboardSinVentana_SourceSupportedYSinLimite()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var transitOfficeId = Guid.NewGuid();
        var revocationRepo = Substitute.For<IProcedureRevocationRequestRepository>();
        var calculator = Substitute.For<IBusinessDayCalculator>();
        var sut = new GetProcedureInstanceHandler(_repo, revocationRepo, calculator);

        var instance = AprobadoInstance(tenantId, origin: null, isMigrated: false, transitOfficeId: transitOfficeId);
        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);
        revocationRepo.GetFirstApprovedAtAsync(tenantId, instance.Id, ct).Returns(DateTimeOffset.UtcNow.AddDays(-1));
        revocationRepo.GetRevocationWindowBusinessDaysAsync(transitOfficeId, ct).Returns((int?)null);

        var (result, error) = await sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        var eligibility = result!.RevocationEligibility.Should().NotBeNull().And.Subject as ProcedureInstanceRevocationEligibilityDto;
        eligibility!.SourceSupported.Should().BeTrue();
        eligibility.WindowExpiresAt.Should().BeNull();
        eligibility.WindowExpired.Should().BeFalse();
        calculator.DidNotReceive().AddBusinessDays(Arg.Any<DateTimeOffset>(), Arg.Any<int>());
    }

    [Fact]
    public async Task HandleAsync_AprobadoOrigenIct_SourceSupportedEsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var revocationRepo = Substitute.For<IProcedureRevocationRequestRepository>();
        var calculator = Substitute.For<IBusinessDayCalculator>();
        var sut = new GetProcedureInstanceHandler(_repo, revocationRepo, calculator);

        var instance = AprobadoInstance(tenantId, origin: "ict");
        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);
        revocationRepo.GetFirstApprovedAtAsync(tenantId, instance.Id, ct).Returns(DateTimeOffset.UtcNow.AddDays(-1));

        var (result, error) = await sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        result!.RevocationEligibility!.SourceSupported.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_VentanaVencida_WindowExpiredEsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var transitOfficeId = Guid.NewGuid();
        var revocationRepo = Substitute.For<IProcedureRevocationRequestRepository>();
        var calculator = Substitute.For<IBusinessDayCalculator>();
        var sut = new GetProcedureInstanceHandler(_repo, revocationRepo, calculator);

        var instance = AprobadoInstance(tenantId, transitOfficeId: transitOfficeId);
        var approvedAt = DateTimeOffset.UtcNow.AddDays(-30);
        var vencida = DateTimeOffset.UtcNow.AddDays(-1);
        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);
        revocationRepo.GetFirstApprovedAtAsync(tenantId, instance.Id, ct).Returns(approvedAt);
        revocationRepo.GetRevocationWindowBusinessDaysAsync(transitOfficeId, ct).Returns(5);
        calculator.AddBusinessDays(approvedAt, 5).Returns(vencida);

        var (result, error) = await sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        var eligibility = result!.RevocationEligibility!;
        eligibility.WindowExpiresAt.Should().Be(vencida);
        eligibility.WindowExpired.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_VentanaVigente_WindowExpiredEsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var transitOfficeId = Guid.NewGuid();
        var revocationRepo = Substitute.For<IProcedureRevocationRequestRepository>();
        var calculator = Substitute.For<IBusinessDayCalculator>();
        var sut = new GetProcedureInstanceHandler(_repo, revocationRepo, calculator);

        var instance = AprobadoInstance(tenantId, transitOfficeId: transitOfficeId);
        var approvedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var vigente = DateTimeOffset.UtcNow.AddDays(10);
        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);
        revocationRepo.GetFirstApprovedAtAsync(tenantId, instance.Id, ct).Returns(approvedAt);
        revocationRepo.GetRevocationWindowBusinessDaysAsync(transitOfficeId, ct).Returns(5);
        calculator.AddBusinessDays(approvedAt, 5).Returns(vigente);

        var (result, error) = await sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        var eligibility = result!.RevocationEligibility!;
        eligibility.WindowExpiresAt.Should().Be(vigente);
        eligibility.WindowExpired.Should().BeFalse();
    }

    // ── HU #12575 (Feature #12565) — evento revocatoria_solicitada en el whitelist de Events (AC2) ──

    [Fact]
    public async Task HandleAsync_RevocatoriaSolicitadaEvent_ExponeAttemptNumberReasonYCorreo()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var solicitante = Guid.NewGuid();
        var cuando = DateTimeOffset.UtcNow;

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000011",
            Status = TramiteEstado.Aprobado,
            CreatedAt = DateTimeOffset.UtcNow,
            Events =
            {
                new ProcedureInstanceEvent
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Tipo = "revocatoria_solicitada",
                    Payload = JsonSerializer.Serialize(new
                    {
                        revocation_request_id = Guid.NewGuid(),
                        attempt_number = 1,
                        reason = "Placa entregada con datos incorrectos",
                        support_document_id = Guid.NewGuid(),
                    }),
                    CreatedAt = cuando,
                    CreatedBy = solicitante,
                },
            },
        };

        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [solicitante] = "Ana Administradora" });
        _repo.GetUserEmailsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [solicitante] = "ana.administradora@renting.com" });
        _repo.GetUserCompaniasAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns(new Dictionary<Guid, string> { [solicitante] = "Renting Colombia S.A.S" });

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        var evento = result!.Events.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be("revocatoria_solicitada");
        evento.CreatedByName.Should().Be("Ana Administradora");
        evento.CreatedByEmail.Should().Be("ana.administradora@renting.com");
        evento.CreatedByCompania.Should().Be("Renting Colombia S.A.S");
        evento.RevocationAttemptNumber.Should().Be(1);
        evento.RevocationReason.Should().Be("Placa entregada con datos incorrectos");
        evento.CreatedAt.Should().Be(cuando);
    }

    // ── HU #12575 (Feature #12565) — ActiveRevocationRequest (AC1 del badge secundario) ──────────────

    [Fact]
    public async Task HandleAsync_SinDependenciasDeRevocatoria_ActiveRevocationRequestEsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var instance = AprobadoInstance(tenantId);
        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        result!.ActiveRevocationRequest.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_TramiteNoAprobado_ActiveRevocationRequestEsNullAunConDependencias()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var revocationRepo = Substitute.For<IProcedureRevocationRequestRepository>();
        var calculator = Substitute.For<IBusinessDayCalculator>();
        var sut = new GetProcedureInstanceHandler(_repo, revocationRepo, calculator);

        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000012",
            Status = TramiteEstado.Entregado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);

        var (result, error) = await sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        result!.ActiveRevocationRequest.Should().BeNull();
        await revocationRepo.DidNotReceive().FindActiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AprobadoSinSolicitudActiva_ActiveRevocationRequestEsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var revocationRepo = Substitute.For<IProcedureRevocationRequestRepository>();
        var calculator = Substitute.For<IBusinessDayCalculator>();
        var sut = new GetProcedureInstanceHandler(_repo, revocationRepo, calculator);

        var instance = AprobadoInstance(tenantId);
        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);
        revocationRepo.FindActiveAsync(tenantId, instance.Id, ct).Returns((ProcedureRevocationRequest?)null);

        var (result, error) = await sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        result!.ActiveRevocationRequest.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_AprobadoConSolicitudActiva_ExponeStatusAttemptNumberYRequestedAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var revocationRepo = Substitute.For<IProcedureRevocationRequestRepository>();
        var calculator = Substitute.For<IBusinessDayCalculator>();
        var sut = new GetProcedureInstanceHandler(_repo, revocationRepo, calculator);

        var instance = AprobadoInstance(tenantId);
        var requestedAt = DateTimeOffset.UtcNow.AddDays(-1);
        _repo.GetByIdWithDetailsAsync(instance.Id, tenantId, ct).Returns(instance);
        revocationRepo.FindActiveAsync(tenantId, instance.Id, ct).Returns(new ProcedureRevocationRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instance.Id,
            AttemptNumber = 2,
            Status = ProcedureRevocationRequestStatus.EnRevision,
            RequestedBy = Guid.NewGuid(),
            RequestedAt = requestedAt,
        });

        var (result, error) = await sut.HandleAsync(instance.Id, tenantId, ct);

        error.Should().BeNull();
        var active = result!.ActiveRevocationRequest.Should().NotBeNull().And.Subject
            as ProcedureInstanceActiveRevocationRequestDto;
        active!.Status.Should().Be(ProcedureRevocationRequestStatus.EnRevision);
        active.AttemptNumber.Should().Be(2);
        active.RequestedAt.Should().Be(requestedAt);
    }
}
