using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
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
}
