using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #13154 (Feature #13117, Épica #13090) — el tipo de mandato fijado por el Super Admin para una compañía en
/// un organismo se comprueba de punta a punta: PUT real contra la base local → prelación/evaluador de F4
/// (<see cref="MandateSignerEvaluator"/>, #13144) y gate de radicación (<see cref="TramiteLifecycleService"/>) con la
/// política del ambiente (warn/block) → datos con los que sale el contrato (<see cref="MandatoFirmaModoResolver"/>
/// y la entidad citada). Compañía C, organismo O; C no tiene mandatarios, así que «Persona natural» no tiene a nadie.
/// <para>Uso de ejemplo: el Super Admin deja a C en <c>open</c> y C radica sin mandatario persona: no se bloquea y
/// el contrato sale con líneas abiertas (<see cref="MandatarioFirmaModo.Manual"/>).</para>
/// </summary>
public sealed class CompanyRuleTypeEffectHttpTests(WebApplicationFactory<Program> factory)
    : CompanyRulesHttpTestBase(factory)
{
    private const string Entidad = "UNION TEMPORAL F5 DE PRUEBA";

    private async Task<JsonElement> PutRuleAsync(object body)
    {
        AuthenticateSuperAdmin();
        var response = await Client.PutAsJsonAsync(RuleUrl(Company), body, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private ProcedureInstance NewInstance(string status = TramiteEstado.Preparado)
    {
        var id = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = Company,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013154",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(), TenantId = Company, ProcedureInstanceId = id,
            FieldKey = "plate", ValueText = "ABC123", Source = "consultation",
        });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(), TenantId = Company, ProcedureInstanceId = id,
            FieldKey = "transit_office_id", ValueText = Office.ToString(), Source = "user",
        });
        return instance;
    }

    /// <summary>
    /// Deja al comprador con identidad aprobada y vigente (documento ficticio). Misma idea que
    /// <c>FirmaFixture.Firmar</c> de los tests de trámites: este proyecto no referencia ese ensamblado.
    /// </summary>
    private static void FirmarComprador(ProcedureInstance instance)
    {
        const string documento = "9000000001";
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            ActorType = BiometricRules.ParteComprador,
            PersonType = "natural",
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = "Persona comprador",
            Email = "comprador@example.test",
        });
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = BiometricRules.ParteComprador,
            Status = BiometricEstados.Aprobado,
            Name = "Persona comprador",
            DocumentType = "CC",
            DocumentNumber = documento,
            Email = "comprador@example.test",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            ValidatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>Evalúa con servicios NUEVOS (scope propio): si hubiera caché o estado retenido, se vería.</summary>
    private async Task<MandateSignerEvaluacion> EvaluateAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MandateSignerEvaluator>()
            .EvaluateAsync(NewInstance(), ct: Ct);
    }

    private async Task<MandateOtConfig> ConfigAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var config = await scope.ServiceProvider.GetRequiredService<IMandateRequirementPolicy>()
            .ResolveByOfficeIdAsync(Office, Company, Ct);
        return config!;
    }

    private async Task<TramiteTransitionOutcome> RadicarAsync(TramiteValidationMode modo)
    {
        using var scope = Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var instance = NewInstance();

        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var typeRepo = Substitute.For<IProcedureTypeRepository>();
        typeRepo.GetByIdAsync(instance.ProcedureTypeId, Arg.Any<CancellationToken>()).Returns(new ProcedureType
        {
            Id = instance.ProcedureTypeId, Code = "X", Name = "X", Family = "matriculas",
            PublicationStatus = PublicationStatus.Published, WizardEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        var grant = Substitute.For<ITransitOfficeGrantGate>();
        grant.IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var operable = Substitute.For<IOtOperabilityGate>();
        operable.IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        // Bug #13194: la firma corre antes que el gate del mandatario. Sin comprador firmado
        // la transición devuelve firma_pendiente y estos AC nunca llegan a evaluar el tipo.
        FirmarComprador(instance);

        var sut = new TramiteLifecycleService(
            repo, typeRepo, grant, operable, NullOtRuleGate.Instance,
            Substitute.For<ITramiteTransitionRecorder>(), Substitute.For<ITramiteTransitionPublisher>(),
            vaultPolicy: sp.GetService<ISignatureVaultPolicy>(),
            mandatePolicy: sp.GetRequiredService<IMandateRequirementPolicy>(),
            mandateDirectory: sp.GetRequiredService<IMandateSignerDirectory>(),
            validationPolicy: new TramiteValidationPolicy(
                TramiteValidationMode.Block, TramiteValidationMode.Block, TramiteValidationMode.Block, modo),
            personalizedDocumentResolver: sp.GetRequiredService<IPersonalizedDocumentResolver>());

        return await sut.TransitionAsync(
            new TramiteTransitionCommand(instance.Id, instance.TenantId, TramiteEstado.Entregado, null, null), Ct);
    }

    [Fact]
    public async Task AC1_PersonaJuridica_NoBloquea_ElContratoCitaALaEntidad()
    {
        await PutRuleAsync(new
        {
            assignmentMode = "institutional",
            institutionalMandataryName = Entidad,
            institutionalMandataryNit = "900123456-7",
        });

        var evaluacion = await EvaluateAsync();
        evaluacion.Estado.Should().Be(MandateSignerEstado.NoAplica);
        evaluacion.Motivo.Should().Be(MandateSignerEstados.MotivoModoSinFirmante);
        evaluacion.CodigoDeError.Should().BeNull();

        var block = await RadicarAsync(TramiteValidationMode.Block);
        block.Success.Should().BeTrue("Persona jurídica no exige mandatario persona ni en block");

        var config = await ConfigAsync();
        config.InstitutionalMandataryName.Should().Be(Entidad, "el contrato cita a la entidad");
        MandatoFirmaModoResolver.Resolve(config.AssignmentMode, tieneConvenio: false, tieneEstampa: false)
            .Should().Be(MandatarioFirmaModo.SinBloque);
    }

    [Fact]
    public async Task AC2_MandatoAbierto_NoBloquea_ElContratoSaleConLineasAbiertas()
    {
        await PutRuleAsync(new { assignmentMode = "open" });

        var evaluacion = await EvaluateAsync();
        evaluacion.Estado.Should().Be(MandateSignerEstado.NoAplica);
        evaluacion.CodigoDeError.Should().BeNull();

        (await RadicarAsync(TramiteValidationMode.Block)).Success.Should().BeTrue();

        var config = await ConfigAsync();
        config.InstitutionalMandataryName.Should().BeNull("el mandato abierto no cita a nadie");
        MandatoFirmaModoResolver.Resolve(config.AssignmentMode, tieneConvenio: false, tieneEstampa: false)
            .Should().Be(MandatarioFirmaModo.Manual, "líneas abiertas para el mandatario");
    }

    [Fact]
    public async Task AC3_PersonaNatural_SinMandatarioValido_AplicaElResultadoDeF4SegunElPerfil()
    {
        await PutRuleAsync(new { assignmentMode = "signer" });

        var evaluacion = await EvaluateAsync();
        evaluacion.Estado.Should().Be(MandateSignerEstado.SinMandatario);
        evaluacion.CodigoDeError.Should().Be("mandatario_no_configurado");

        var block = await RadicarAsync(TramiteValidationMode.Block);
        block.Success.Should().BeFalse();
        block.ErrorCode.Should().Be(TramiteEstadoErrores.MandatarioNoConfigurado);

        var warn = await RadicarAsync(TramiteValidationMode.Warn);
        warn.Success.Should().BeTrue("en warn radica y deja el aviso");

        var off = await RadicarAsync(TramiteValidationMode.Off);
        off.Success.Should().BeTrue("en off no consulta");
    }

    [Fact]
    public async Task AC4_CambioInmediato_ElTipoNuevoSeUsaSinEsperarCacheNiReinicio()
    {
        var created = await PutRuleAsync(new { assignmentMode = "signer" });
        (await EvaluateAsync()).Estado.Should().Be(MandateSignerEstado.SinMandatario);
        (await RadicarAsync(TramiteValidationMode.Block)).Success.Should().BeFalse();

        var version = created.GetProperty("rowVersion").GetInt64();
        var changed = await Client.PutAsJsonAsync(
            RuleUrl(Company), new { assignmentMode = "open", rowVersion = version }, Ct);
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        // Justo después, sin reiniciar ni invalidar nada: el tipo nuevo manda.
        (await EvaluateAsync()).Estado.Should().Be(MandateSignerEstado.NoAplica);
        (await RadicarAsync(TramiteValidationMode.Block)).Success.Should().BeTrue();

        // Y la vuelta atrás también se ve al instante.
        var back = await Client.PutAsJsonAsync(
            RuleUrl(Company),
            new
            {
                assignmentMode = "signer",
                rowVersion = (await changed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("rowVersion").GetInt64(),
            },
            Ct);
        back.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EvaluateAsync()).Estado.Should().Be(MandateSignerEstado.SinMandatario);
    }

    [Fact]
    public async Task AC5_SuperAdminDeOtroTenant_SinRestriccion_Responde200_ConCompaniaConGrantHabilitado()
    {
        // El Super Admin pertenece al tenant de la plataforma, no a C ni al organismo O.
        SuperAdminTenantId.Should().NotBe(Company);
        SuperAdminTenantId.Should().NotBe(OtTenantId);

        var body = await PutRuleAsync(new { assignmentMode = "open" });

        body.GetProperty("companyTenantId").GetGuid().Should().Be(Company);
        (await ReadRuleAsync(Company)).Exists.Should().BeTrue();
    }

    [Fact]
    public async Task AC6_CompaniaSinGrant_Responde404_YNoCreaLaRegla()
    {
        AuthenticateSuperAdmin();

        var response = await Client.PutAsJsonAsync(RuleUrl(CompanyWithoutGrant), new { assignmentMode = "open" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadRuleAsync(CompanyWithoutGrant)).Exists.Should().BeFalse();
    }

    [Theory]
    [InlineData("desconocido")]
    [InlineData("persona_rl")]
    public async Task AC7_ModoInvalido_Responde400_AssignmentModeInvalido(string modo)
    {
        AuthenticateSuperAdmin();

        var response = await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = modo }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("assignment_mode_invalido");
        (await ReadRuleAsync(Company)).Exists.Should().BeFalse();
    }
}
