using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Un documento generado (FUR / compraventa), referenciado al adjunto persistido.</summary>
public sealed record FurDocumentDto(Guid AttachmentId, string Tipo, string Filename, string Sha256);

public sealed record GenerarFurResult(IReadOnlyList<FurDocumentDto> Documents);

/// <summary>
/// Genera el FUR (y, en traspaso, el contrato de compraventa) con los datos reales de la instancia.
/// <para><b>Gating biométrica</b> (paridad Johan): traspaso requiere AMBAS partes aprobadas;
/// matrícula_inicial requiere comprador aprobada (Parte == "comprador"). Si falta → <c>biometria_gate</c> (409).</para>
/// El documento se genera vía el generador MOCK (sin librería PDF) y se persiste como ADJUNTO
/// (<c>IAttachmentStorage</c> + fila en procedure_instance_attachments, tipo 'fur' / 'compraventa').
/// Idempotente: re-generar reemplaza los adjuntos FUR/compraventa previos. Registra un evento
/// <c>fur_generado</c> en la bitácora de la instancia.
/// </summary>
public sealed class GenerarFurHandler(
    IProcedureInstanceRepository repo,
    IFurDocumentGenerator generator,
    IIdentityCertificateGenerator identityGenerator,
    IAttachmentStorage storage)
{
    public async Task<(GenerarFurResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithFurGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var esTraspaso = string.Equals(codigo, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase);

        // Gating biométrica: traspaso → ambas partes; matrícula → comprador (Parte == "comprador").
        if (!BiometriaGateOk(instance, esTraspaso))
            return (null, "biometria_gate");

        var fv = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        // Gating organismo de tránsito: requiere transit_office_code no vacío en field_values.
        if (string.IsNullOrWhiteSpace(Get(fv, "transit_office_code")))
            return (null, "organismo_requerido");

        var data = AssembleData(instance, codigo, esTraspaso, fv);

        var now = DateTimeOffset.UtcNow;
        var docs = new List<FurDocumentDto>(2);

        // FUR siempre + certificado de validación de identidad. Compraventa solo en traspaso.
        var generated = new List<GeneratedDocument>
        {
            generator.GenerateFur(data),
            identityGenerator.GenerateIdentityCertificate(AssembleIdentityData(instance)),
        };
        if (esTraspaso)
            generated.Add(generator.GenerateCompraventa(data));

        foreach (var doc in generated)
        {
            // Idempotencia: re-generar reemplaza el adjunto previo del mismo tipo.
            foreach (var prev in instance.Attachments.Where(a =>
                         string.Equals(a.Tipo, doc.Tipo, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                storage.Delete(prev.StoragePath);
                instance.Attachments.Remove(prev);
                repo.RemoveAttachment(prev);
            }

            var stored = await storage.SaveAsync(id, doc.Tipo, doc.Filename, new MemoryStream(doc.Content), ct);
            var attachment = new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                Tipo = doc.Tipo,
                Filename = doc.Filename,
                Mimetype = doc.Mimetype,
                SizeBytes = stored.SizeBytes,
                Sha256 = stored.Sha256,
                StoragePath = stored.StoragePath,
                Source = "system",
                UploadedAt = now,
            };
            instance.Attachments.Add(attachment);
            repo.Add(attachment);

            docs.Add(new FurDocumentDto(attachment.Id, doc.Tipo, doc.Filename, stored.Sha256));
        }

        // Bitácora: evento append-only de generación del FUR.
        var evento = new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = "fur_generado",
            Payload = JsonSerializer.Serialize(new
            {
                documentos = docs.Select(d => new { d.Tipo, d.Filename, d.Sha256 }),
                generado_at = now,
            }),
            CreatedAt = now,
        };
        instance.Events.Add(evento);
        repo.Add(evento);

        await repo.SaveChangesAsync(ct);

        return (new GenerarFurResult(docs), null);
    }

    /// <summary>
    /// Gating biométrica: traspaso requiere comprador + vendedor aprobados; matrícula requiere la
    /// parte única (comprador) aprobada.
    /// </summary>
    private static bool BiometriaGateOk(ProcedureInstance instance, bool esTraspaso)
    {
        bool Aprobada(string? parte) => instance.BiometricValidations.Any(v =>
            string.Equals(v.Parte, parte, StringComparison.OrdinalIgnoreCase)
            && v.Estado == BiometricEstados.Aprobado);

        return esTraspaso
            ? Aprobada("comprador") && Aprobada("vendedor")
            : Aprobada("comprador");
    }

    private static FurDocumentData AssembleData(
        ProcedureInstance instance, string? codigo, bool esTraspaso, Dictionary<string, string?> fv)
    {
        var partes = new List<DocumentParte>(2);
        AddParte(partes, instance, "comprador");
        if (esTraspaso)
            AddParte(partes, instance, "vendedor");

        var sellos = instance.Signatures
            .Where(s => s.Estado == SignatureEstados.Firmada)
            .Select(s => $"{s.Parte}/{s.DocTipo}: {s.Sha256 ?? "-"} ({s.FirmadoAt:O})")
            .ToList();

        var vehiculo = new VehiculoDatos(
            Marca: Get(fv, "vehicle_brand"),
            Linea: Get(fv, "vehicle_line"),
            Modelo: Get(fv, "vehicle_year"),
            Color: Get(fv, "vehicle_color"),
            Clase: Get(fv, "vehicle_class"),
            Combustible: Get(fv, "vehicle_fuel"),
            Cilindraje: Get(fv, "vehicle_engine_displacement"),
            Vin: Get(fv, "vin"),
            Placa: Get(fv, "plate"),
            // HU #10256 — datos ampliados desde RUNT/Verifik (field_values)
            NumeroMotor: Get(fv, "vehicle_engine_number"),
            NumeroChasis: Get(fv, "vehicle_chassis"),
            NumeroSerie: Get(fv, "vehicle_series"),
            TipoCarroceria: Get(fv, "vehicle_body_type"),
            TipoServicio: Get(fv, "vehicle_service"),
            Capacidad: Get(fv, "vehicle_passengers"),
            PesoBruto: Get(fv, "vehicle_weight"),
            NumeroEjes: Get(fv, "vehicle_axles"));

        var organismo = new OrganismoTransito(
            Codigo: Get(fv, "transit_office_code"),
            Nombre: Get(fv, "transit_office_name"),
            Ciudad: Get(fv, "transit_office_city"));

        return new FurDocumentData(
            ProcedureInstanceId: instance.Id,
            ReferenceNumber: instance.ReferenceNumber,
            Modalidad: instance.ModalidadEntrada,
            TipologiaCodigo: codigo,
            Vehiculo: vehiculo,
            Organismo: organismo,
            Partes: partes,
            ValorVenta: instance.Commercial?.ValorVenta,
            Causal: instance.Commercial?.Causal,
            SellosFirma: sellos);
    }

    /// <summary>
    /// Datos del certificado de identidad: comprador (de actores) + resultado de la biométrica del
    /// comprador. Score real si existe; el resultado refleja el estado aprobado de la validación.
    /// </summary>
    private static IdentityCertificateData AssembleIdentityData(ProcedureInstance instance)
    {
        var comprador = instance.Actors.FirstOrDefault(x =>
            string.Equals(x.ActorType, "comprador", StringComparison.OrdinalIgnoreCase));

        var bio = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.Parte, "comprador", StringComparison.OrdinalIgnoreCase)
            && v.Estado == BiometricEstados.Aprobado);

        var nombre = comprador?.FullName ?? bio?.Nombre ?? "-";
        var documento = comprador?.DocumentNumber ?? bio?.Documento ?? "-";
        // La biométrica del comprador ya está aprobada (gate previo) → score real o 95 por defecto.
        var score = bio?.Score ?? 95;

        return new IdentityCertificateData(
            ProcedureInstanceId: instance.Id,
            ReferenceNumber: instance.ReferenceNumber,
            CompradorNombre: nombre,
            CompradorDocumento: documento,
            Score: score,
            Resultado: "APROBADO");
    }

    private static void AddParte(List<DocumentParte> partes, ProcedureInstance instance, string rol)
    {
        var a = instance.Actors.FirstOrDefault(x =>
            string.Equals(x.ActorType, rol, StringComparison.OrdinalIgnoreCase));
        partes.Add(new DocumentParte(rol, a?.FullName, a?.DocumentNumber, a?.Email));
    }

    private static string? Get(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) ? v : null;
}
