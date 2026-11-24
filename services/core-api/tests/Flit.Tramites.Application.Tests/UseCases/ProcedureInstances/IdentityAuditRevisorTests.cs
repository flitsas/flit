using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>Quién aprobó/rechazó una validación manual: nombre, correo y rol solo para el Super Admin; sin PII para los demás.</summary>
public sealed class IdentityAuditRevisorTests
{
    private static readonly Guid Revisor = Guid.Parse("019f1111-2222-7333-8444-555566667777");
    private static readonly Guid ValidationId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    private static IdentityAuditResponse Respuesta(string stage, string? detail)
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetBiometricByIdAsync(ValidationId, Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstanceBiometricValidation { Id = ValidationId, TenantId = TenantId });
        repo.ListIdentityAuditByValidationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([new IdentityValidationAuditEvent
            {
                OccurredAt = DateTimeOffset.Parse("2026-11-24T10:00:00Z"), Stage = stage, Outcome = "approved",
                Message = "El usuario revisó", Detail = detail,
            }]);
        var (res, _) = new GetIdentityAuditByValidationHandler(repo).HandleAsync(TenantId, ValidationId).GetAwaiter().GetResult();
        return res!;
    }

    private static IReviewerDirectory Directorio(ReviewerInfo? info)
    {
        var d = Substitute.For<IReviewerDirectory>();
        d.ResolveAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyDictionary<Guid, ReviewerInfo>)(info is null
                ? new Dictionary<Guid, ReviewerInfo>()
                : new Dictionary<Guid, ReviewerInfo> { [Revisor] = info }));
        return d;
    }

    [Fact]
    public async Task SuperAdmin_recibe_revisor_con_nombre_correo_y_rol_en_manual_aprobado()
    {
        var res = Respuesta(IdentityValidationAuditStages.ManualAprobado, $"usuario={Revisor}; origen=manual");

        var enriquecida = await IdentityAuditRevisor.AplicarAsync(
            res, Directorio(new ReviewerInfo("Ana Revisora", "ana@flit.co", "Super Admin")), TestContext.Current.CancellationToken);

        enriquecida.Events.Single().Revisor.Should().Be(new ReviewerInfo("Ana Revisora", "ana@flit.co", "Super Admin"));
    }

    [Fact]
    public async Task Usuario_inexistente_deja_name_email_y_role_en_null_sin_mostrar_el_guid()
    {
        var res = Respuesta(IdentityValidationAuditStages.ManualRechazado, $"usuario={Revisor}; motivo=foto_borrosa");

        var enriquecida = await IdentityAuditRevisor.AplicarAsync(res, Directorio(null), TestContext.Current.CancellationToken);

        enriquecida.Events.Single().Revisor.Should().Be(new ReviewerInfo(null, null, null));
        JsonSerializer.Serialize(enriquecida).Should().NotContain(Revisor.ToString());
    }

    [Fact]
    public void No_SuperAdmin_recibe_revisor_null_y_sin_PII_ni_guid()
    {
        var res = Respuesta(IdentityValidationAuditStages.ManualAprobado, $"usuario={Revisor}; origen=manual");

        // Sin enriquecer (el endpoint solo enriquece a Super Admin) y con el saneamiento de cliente.
        var paraCliente = IdentityAuditParaCliente.Aplicar(res.Events);

        paraCliente.Should().OnlyContain(e => e.Revisor == null);
        var json = JsonSerializer.Serialize(paraCliente);
        json.Should().NotContain(Revisor.ToString()).And.NotContain("ana@flit.co");
    }

    [Fact]
    public void El_guid_interno_nunca_se_serializa_aunque_se_haya_extraido()
    {
        var res = Respuesta(IdentityValidationAuditStages.ManualAprobado, $"usuario={Revisor}");

        res.Events.Single().RevisorId.Should().Be(Revisor);
        var json = JsonSerializer.Serialize(res);
        json.Should().NotContain("RevisorId").And.NotContain(Revisor.ToString());
    }

    [Theory]
    [InlineData(IdentityValidationAuditStages.ManualAprobado, "usuario=019f1111-2222-7333-8444-555566667777; x=1", true)]
    [InlineData(IdentityValidationAuditStages.ManualRechazado, "motivo=a; usuario=019f1111-2222-7333-8444-555566667777", true)]
    [InlineData(IdentityValidationAuditStages.ManualActivado, "usuario=019f1111-2222-7333-8444-555566667777", false)]
    [InlineData(IdentityValidationAuditStages.ManualAprobado, "usuario=no-es-guid", false)]
    [InlineData(IdentityValidationAuditStages.ManualAprobado, null, false)]
    public void ExtraerUsuario_solo_en_aprobado_o_rechazado_con_guid_valido(string stage, string? detail, bool espera)
        => IdentityAuditRevisor.ExtraerUsuario(stage, detail).HasValue.Should().Be(espera);
}
