using Flit.Tramites.Application.UseCases.Certifications;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #14153 AC1 — Trámites NO empieza a persistir el crudo del conductor ahora que <c>kyverum_runt_conductor</c> y
/// <c>verifik_conductor</c> llenan <see cref="ConsultationResult.RawPayload"/> (y core-api lo recibe siempre de core-consultas porque
/// pide <c>incluir_respuesta_cruda</c>). El único camino que escribe el crudo es la ingesta canónica
/// (<see cref="ICertificationIngestionService"/>), que <see cref="RunConsultationHandler"/> solo invoca con <c>Certifications</c>; los
/// mappers de conductor no producen bundle, así que la ingesta no se llama y <c>field_values</c> recibe exactamente lo de siempre.
/// <para>Uso de ejemplo: <c>await handler.HandleAsync(id, tenant, "RUNT_PERSONA", ct: ct)</c> con un resultado de conductor con crudo
/// → <c>ingesta.DidNotReceive().IngestAsync(...)</c>.</para>
/// </summary>
public sealed class RespuestaCrudaConductorTramitesTests
{
    private readonly IProcedureInstanceRepository _instanceRepo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    private readonly ICertificationIngestionService _ingesta = Substitute.For<ICertificationIngestionService>();

    [Theory]
    [InlineData("kyverum_runt_conductor")]
    [InlineData("verifik_conductor")]
    public async Task Resultado_de_conductor_con_crudo_no_llega_a_la_ingesta_y_field_values_no_cambia(string proveedor)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instancia = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000141",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _instanceRepo.GetByIdWithDetailsAsync(id, tenant, ct).Returns(instancia);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_PERSONA", ct)
            .Returns(new ConsultationTemplate { Id = Guid.NewGuid(), Code = "RUNT_PERSONA", ExternalRefs = $$"""{"provider":"{{proveedor}}"}""" });
        var conCrudo = new ConsultationResult(
            proveedor, "green",
            [new ConsultationCheck("conductor", "Persona en RUNT", "ok", proveedor, null)],
            [new HydratedField("person_full_name", "PERSONA FICTICIA UNO", null)],
            RawPayload: new RawProviderPayload(proveedor, RawProviderPayload.PersonSubject, "9999900141",
                """{"ok":true,"licencias":[{"numeroLicencia":"99999001"}]}""", DateTimeOffset.UtcNow));
        _registry.Resolve(proveedor).Returns(new Fijo(proveedor, conCrudo));
        var handler = new RunConsultationHandler(_instanceRepo, _catalogRepo, _registry,
            new ExternalQueryCacheService(Substitute.For<IExternalQueryCacheRepository>(), Substitute.For<IPersonDataConsentRepository>(), _catalogRepo),
            _ingesta);

        var (result, error) = await handler.HandleAsync(id, tenant, "RUNT_PERSONA", ct: ct);

        error.Should().BeNull();
        result.Should().BeSameAs(conCrudo);
        await _ingesta.DidNotReceiveWithAnyArgs().IngestAsync(default, default, default!, default!, default, ct);
        instancia.FieldValues.Should().ContainSingle(f => f.FieldKey == "person_full_name" && f.ValueText == "PERSONA FICTICIA UNO",
            "field_values recibe solo los campos hidratados, como hoy");
        instancia.FieldValues.Should().NotContain(f => (f.ValueText ?? string.Empty).Contains("numeroLicencia"), "el crudo no se escribe en field_values");
    }

    private sealed class Fijo(string key, ConsultationResult result) : IConsultationProvider
    {
        public string Key => key;

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct) => Task.FromResult(result);
    }
}
