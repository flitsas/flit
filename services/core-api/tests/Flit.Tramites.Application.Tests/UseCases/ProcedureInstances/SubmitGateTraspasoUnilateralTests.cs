using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10592 (R12) — gate de envío al OT del traspaso unilateral. A diferencia del bilateral, valida
/// identidad SOLO de la arrendadora (parte que transfiere, vía su representante legal — D3); el locatario
/// participa de forma DOCUMENTAL y NO entra al gate de identidad. Exige además documentos obligatorios
/// completos, FUR generado y organismo seleccionado, sin firma de compraventa ni impronta (la tipología
/// unilateral no incluye impronta en su checklist).
/// </summary>
public sealed class SubmitGateTraspasoUnilateralTests
{
    // DocTipos obligatorios del checklist unilateral (TramiteTipologiaCatalog) + FUR (exigido por el gate).
    private static readonly string[] MandatoryDocs =
        ["paz_salvo_locatario", "doc_locatario", "contrato_leasing", "declaracion_arrendadora", "fur"];

    private static ProcedureInstance CompleteUnilateral()
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000300",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = TramiteModalidadEntradaCodes.TraspasoUnilateral,
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoTraspasoUnilateral,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var tipo in MandatoryDocs)
            AddAttachment(instance, tipo);

        // Solo la arrendadora valida identidad. El locatario NO lleva biométrica (documental).
        AddBio(instance, BiometricRules.ParteArrendadora);

        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });

        return instance;
    }

    private static void AddAttachment(ProcedureInstance instance, string tipo) =>
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            Mimetype = "application/pdf",
            StoragePath = $"{instance.Id:D}/{tipo}",
            Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
        });

    private static void AddBio(ProcedureInstance instance, string parte) =>
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = parte,
            Status = BiometricEstados.Aprobado,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });

    // Set de partes con identidad aprobada, derivado de las validaciones biométricas de la instancia
    // (paridad con SubmitGateTraspasoTests: el gate recibe el set PER-PERSONA que resuelve el handler).
    private static HashSet<string> Aprobadas(ProcedureInstance instance) =>
        instance.BiometricValidations
            .Where(b => b.Status == BiometricEstados.Aprobado && b.PartyRole is not null)
            .Select(b => b.PartyRole!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Gate_UnilateralCompleto_NoBloquea()
    {
        // AC1: arrendadora aprobada-vigente + docs completos + FUR + organismo → lista vacía (puede preparar).
        var instance = CompleteUnilateral();
        SubmitGate.Evaluate(instance, Aprobadas(instance)).Should().BeEmpty();
    }

    [Fact]
    public void Gate_IdentidadArrendadoraPendiente_Bloquea()
    {
        // AC2: sin identidad aprobada de la arrendadora → identidad_no_aprobada.
        var instance = CompleteUnilateral();
        var sinArrendadora = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        SubmitGate.Evaluate(instance, sinArrendadora).Should().Contain(SubmitGate.IdentidadNoAprobada);
    }

    [Fact]
    public void Gate_DocumentosIncompletos_Bloquea()
    {
        // AC: falta un documento obligatorio del checklist unilateral → documentos_incompletos.
        var instance = CompleteUnilateral();
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "contrato_leasing"));

        SubmitGate.Evaluate(instance, Aprobadas(instance)).Should().Contain(SubmitGate.DocumentosIncompletos);
    }

    [Fact]
    public void Gate_SinFur_Bloquea()
    {
        // Falta el FUR → fur_requerido.
        var instance = CompleteUnilateral();
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "fur"));

        SubmitGate.Evaluate(instance, Aprobadas(instance)).Should().Contain(SubmitGate.FurRequerido);
    }

    [Fact]
    public void Gate_SinOrganismo_Bloquea()
    {
        // Falta el organismo de tránsito → organismo_requerido.
        var instance = CompleteUnilateral();
        instance.FieldValues.Clear();

        SubmitGate.Evaluate(instance, Aprobadas(instance)).Should().Contain(SubmitGate.OrganismoRequerido);
    }

    [Fact]
    public void Gate_LocatarioSinBiometrica_NoBloquea()
    {
        // AC3: el locatario es DOCUMENTAL. Con la arrendadora en verde y sin ninguna biométrica del
        // locatario, el gate no reporta identidad_no_aprobada ni ningún otro error.
        var instance = CompleteUnilateral();

        // Solo la arrendadora está aprobada; el locatario no tiene validación biométrica alguna.
        var soloArrendadora = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BiometricRules.ParteArrendadora,
        };

        var errores = SubmitGate.Evaluate(instance, soloArrendadora);

        errores.Should().BeEmpty();
        errores.Should().NotContain(SubmitGate.IdentidadNoAprobada);
    }

    [Fact]
    public void Gate_Unilateral_NoExigeFirmaCompraventa()
    {
        // El unilateral NO es compraventa directa: sin firmas de compraventa el gate igual pasa.
        var instance = CompleteUnilateral();
        instance.Signatures.Clear();

        SubmitGate.Evaluate(instance, Aprobadas(instance))
            .Should().NotContain(SubmitGate.FirmaCompraventaRequerida);
    }

    [Fact]
    public void Gate_Unilateral_NoExigeImpronta()
    {
        // La tipología unilateral no incluye impronta en su checklist → el gate no la exige.
        var instance = CompleteUnilateral();

        SubmitGate.Evaluate(instance, Aprobadas(instance))
            .Should().NotContain(SubmitGate.ImprontaRequerida);
    }
}
