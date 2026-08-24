using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10459 — el gate de radicación de traspaso pasó de "siempre permite" a bloquear cuando falta
/// algo (documentos, biométricas, firma de compraventa, FUR u organismo). Además el flag demo
/// <c>TRAMITES_DEMO_RELAX_DOCS</c> ya no afloja la validación documental (fue retirado del código).
/// </summary>
public sealed class SubmitGateTraspasoTests
{
    private static readonly string[] MandatoryDocs =
        ["compraventa", "impronta", "soat", "rtm", "paz_salvo", "cedulas", "fur"];

    private static ProcedureInstance CompleteTraspaso()
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard ?? "traspaso"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000200",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var tipo in MandatoryDocs)
            AddAttachment(instance, tipo);

        AddBio(instance, "comprador");
        AddBio(instance, "vendedor");
        AddFirma(instance, "comprador");
        AddFirma(instance, "vendedor");

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
    // (HU #10350: el gate ahora recibe el set PER-PERSONA que el handler resuelve con el repo).
    private static HashSet<string> Aprobadas(ProcedureInstance instance) =>
        instance.BiometricValidations
            .Where(b => b.Status == BiometricEstados.Aprobado && b.PartyRole is not null)
            .Select(b => b.PartyRole!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void AddFirma(ProcedureInstance instance, string parte) =>
        instance.Signatures.Add(new ProcedureInstanceSignature
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Parte = parte,
            DocTipo = SignatureDocTipos.Compraventa,
            Estado = SignatureEstados.Firmada,
            SolicitadoAt = DateTimeOffset.UtcNow,
            FirmadoAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    [Fact]
    public void Gate_TraspasoCompleto_NoBloquea()
    {
        // AC1: todo completo → lista vacía (puede radicar).
        var instance = CompleteTraspaso();
        SubmitGate.Evaluate(instance, Aprobadas(instance)).Should().BeEmpty();
    }

    [Fact]
    public void Gate_SinFirmaCompraventa_NoBloquea()
    {
        // B12 (HU #10661, ADR-0028) AC1/AC4: la firma de compraventa YA NO bloquea el traspaso.
        // Sin firmas, si el resto del gate está OK, la lista de errores queda vacía (puede radicar)
        // y en particular NO aparece firma_compraventa_requerida.
        var instance = CompleteTraspaso();
        instance.Signatures.Clear();

        var errors = SubmitGate.Evaluate(instance, Aprobadas(instance));
        errors.Should().NotContain(SubmitGate.FirmaCompraventaRequerida);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Gate_SinBiometriaVendedor_Bloquea()
    {
        // AC2: falta una biométrica → identidad_no_aprobada.
        var instance = CompleteTraspaso();
        instance.BiometricValidations.Remove(
            instance.BiometricValidations.First(b => b.PartyRole == "vendedor"));

        SubmitGate.Evaluate(instance, Aprobadas(instance)).Should().Contain(SubmitGate.IdentidadNoAprobada);
    }

    [Fact]
    public void Gate_SinOrganismo_Bloquea()
    {
        // AC2: falta organismo → organismo_requerido.
        var instance = CompleteTraspaso();
        instance.FieldValues.Clear();

        SubmitGate.Evaluate(instance, Aprobadas(instance)).Should().Contain(SubmitGate.OrganismoRequerido);
    }

    [Fact]
    public void Gate_SinFur_Bloquea()
    {
        // AC2: falta FUR → fur_requerido.
        var instance = CompleteTraspaso();
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "fur"));

        SubmitGate.Evaluate(instance, Aprobadas(instance)).Should().Contain(SubmitGate.FurRequerido);
    }

    [Fact]
    public void Gate_TodoSatisfechoSalvoImpronta_Bloquea()
    {
        // Integración impronta↔trámites: aunque el checklist ya no la exige (Obligatorio: false),
        // el gate de radicación SIGUE exigiendo el adjunto tipo "impronta" (cargado o generado).
        var instance = CompleteTraspaso();
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "impronta"));

        SubmitGate.Evaluate(instance, Aprobadas(instance)).Should().Contain(SubmitGate.ImprontaRequerida);
    }

    [Fact]
    public void Gate_ConFlagDemoActivo_SigueExigiendoDocumentos()
    {
        // AC3: TRAMITES_DEMO_RELAX_DOCS=true ya NO afloja el gate documental (flag retirado).
        var previo = Environment.GetEnvironmentVariable("TRAMITES_DEMO_RELAX_DOCS");
        try
        {
            Environment.SetEnvironmentVariable("TRAMITES_DEMO_RELAX_DOCS", "true");

            var instance = CompleteTraspaso();
            // Quitar un documento obligatorio (compraventa) → debe seguir bloqueando pese al flag.
            instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "compraventa"));

            SubmitGate.Evaluate(instance, Aprobadas(instance)).Should().Contain(SubmitGate.DocumentosIncompletos);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRAMITES_DEMO_RELAX_DOCS", previo);
        }
    }
}
