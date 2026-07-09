using System.Globalization;
using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Un documento generado (FUR / compraventa), referenciado al adjunto persistido.</summary>
public sealed record FurDocumentDto(Guid AttachmentId, string Tipo, string Filename, string Sha256);

public sealed record GenerarFurResult(IReadOnlyList<FurDocumentDto> Documents);

/// <summary>
/// Genera el FUR (y, en traspaso, el contrato de compraventa) con los datos reales de la instancia.
/// <para><b>Validación de identidad</b> (HU #10463): ya NO bloquea la generación. Se resuelve la identidad
/// vigente PER-PERSONA (documento del actor, HU #10350) de las partes requeridas (traspaso: comprador +
/// vendedor; matrícula: comprador); si no está aprobada+vigente, el FUR se genera con el sello "NO FIRMADO"
/// en el espacio de firma y NO se emite el certificado de identidad. La RADICACIÓN sí sigue exigiendo
/// identidad (SubmitGate/#10459).</para>
/// El FUR/compraventa se generan vía <c>IFurDocumentGenerator</c> y se persisten como ADJUNTO
/// (<c>IAttachmentStorage</c> + fila en procedure_instance_attachments, tipo 'fur' / 'compraventa').
/// El <b>certificado de identidad</b> es el PDF REAL de Kyverum, descargado <i>best-effort</i>: si la
/// descarga falla se registra un warning y se OMITE (no bloquea el FUR ni emite mock).
/// Idempotente: re-generar reemplaza los adjuntos previos. Registra un evento <c>fur_generado</c>.
/// </summary>
public sealed class GenerarFurHandler(
    IProcedureInstanceRepository repo,
    IFurDocumentGenerator generator,
    IKyverumCertificateClient certClient,
    IRuesCertificateGenerator ruesGenerator,
    IProcedureInstancePrendaRepository prendaRepo,
    IAttachmentStorage storage,
    ILogger<GenerarFurHandler> logger)
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

        // HU #10463 — la validación de identidad ya NO bloquea la GENERACIÓN del FUR/consolidado.
        // Gating PER-PERSONA (HU #10350): se referencia la identidad vigente de la persona (documento del
        // actor), no una fila propia del trámite. Si falta, el FUR se genera con el sello "NO FIRMADO" y sin
        // certificado (no se declara "APROBADO" en falso). La RADICACIÓN sí sigue exigiendo identidad (#10459).
        var identidadAprobada = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
            repo, instance, DateTimeOffset.UtcNow, ct);
        var identidadValidada = BiometriaGateOk(identidadAprobada, esTraspaso);

        var fv = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        // Gating organismo de tránsito: requiere transit_office_code no vacío en field_values.
        if (string.IsNullOrWhiteSpace(Get(fv, "transit_office_code")))
            return (null, "organismo_requerido");

        // HU #10488 — sello de identidad (texto) por parte para el espacio de firma del FUR. Solo cuando la
        // identidad está validada (si no, el mapper pinta "NO FIRMADO"). Se resuelve la validación aprobada+
        // vigente de cada parte (fila propia o identidad referenciada por documento, HU #10350).
        var sellosIdentidad = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (identidadValidada)
        {
            var roles = esTraspaso ? new[] { "comprador", "vendedor" } : new[] { "comprador" };
            foreach (var role in roles)
            {
                var val = await ResolveApprovedValidationAsync(instance, role, DateTimeOffset.UtcNow, ct);
                if (val is not null)
                    sellosIdentidad[role] = BuildIdentidadSello(val);
            }
        }

        // HU #10601 — prenda vigente: marca el gravamen en el FUR cuando la decisión implica prenda
        // (solicitar/registrar). sin_prenda/omitir/levantar no marcan gravamen.
        var prendaVigente = await prendaRepo.GetVigenteAsync(id, tenantId, ct);
        var tienePrenda = prendaVigente is not null && PrendaDecision.ImplicaGravamen(prendaVigente.Decision);
        var acreedorPrenda = tienePrenda ? prendaVigente!.AcreedorNombre : null;

        var data = AssembleData(instance, codigo, esTraspaso, fv, identidadValidada, sellosIdentidad, tienePrenda, acreedorPrenda);

        var now = DateTimeOffset.UtcNow;
        var docs = new List<FurDocumentDto>(3);

        // FUR siempre. Compraventa solo en traspaso.
        var generated = new List<GeneratedDocument> { generator.GenerateFur(data) };
        if (esTraspaso)
            generated.Add(generator.GenerateCompraventa(data));

        if (identidadValidada)
        {
            // Certificado de identidad: PDF REAL de Kyverum (best-effort). Si falla, warning + omitir (sin mock).
            var certificado = await TryDownloadIdentityCertificateAsync(instance, ct);
            if (certificado is not null)
                generated.Add(certificado);
        }
        else
        {
            // Sin validación de identidad, retirar cualquier certificado previo (regeneración): el
            // consolidado no debe incluir un certificado de identidad obsoleto (#10463 AC5).
            foreach (var prev in instance.Attachments
                         .Where(a => string.Equals(a.Tipo, "certificado_identidad", StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                storage.Delete(prev.StoragePath);
                instance.Attachments.Remove(prev);
                repo.RemoveAttachment(prev);
            }
        }

        // HU #10589 — Certificado RUES: si el trámite tiene un actor persona jurídica (NIT), generar el
        // certificado RUES (PDF, Source=system) desde los datos del actor para que se fusione en el
        // consolidado. Independiente de la biométrica (una persona jurídica no valida identidad biométrica).
        var certificadoRues = TryGenerateRuesCertificate(instance);
        if (certificadoRues is not null)
        {
            generated.Add(certificadoRues);
        }
        else
        {
            // Sin actor NIT (o dejó de haberlo en una regeneración): retirar cualquier certificado RUES previo.
            foreach (var prev in instance.Attachments
                         .Where(a => string.Equals(a.Tipo, "certificado_rues", StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                storage.Delete(prev.StoragePath);
                instance.Attachments.Remove(prev);
                repo.RemoveAttachment(prev);
            }
        }

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
    /// Gating biométrica PER-PERSONA (documento del actor): traspaso requiere comprador + vendedor con
    /// identidad vigente aprobada; matrícula requiere el comprador. Se referencia la validación vigente de
    /// la persona (HU #10350), no una fila propia del trámite; el set lo resuelve el handler con el repo.
    /// </summary>
    private static bool BiometriaGateOk(IReadOnlySet<string> identidadAprobadaPartes, bool esTraspaso) =>
        esTraspaso
            ? identidadAprobadaPartes.Contains("comprador") && identidadAprobadaPartes.Contains("vendedor")
            : identidadAprobadaPartes.Contains("comprador");

    private static FurDocumentData AssembleData(
        ProcedureInstance instance, string? codigo, bool esTraspaso, Dictionary<string, string?> fv,
        bool identidadValidada, IReadOnlyDictionary<string, string> sellosIdentidad,
        bool tienePrenda, string? acreedorPrenda)
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
            SellosFirma: sellos,
            FechaTramite: ParseFechaTramite(Get(fv, "fur_processing_date")),
            // A4/B4 (HU #10673, ADR-0029) — anexa a las observaciones manuales el texto automático de las
            // transformaciones de color/combustible declaradas (diff snapshot RUNT vs efectivo).
            Observaciones: FurTransformationObservations.Compose(
                Get(fv, "fur_observations"),
                Get(fv, "vehicle_color_runt"), Get(fv, "vehicle_color"),
                Get(fv, "vehicle_fuel_runt"), Get(fv, "vehicle_fuel")),
            IdentidadValidada: identidadValidada,
            SellosIdentidad: sellosIdentidad,
            TienePrenda: tienePrenda,
            AcreedorPrenda: acreedorPrenda);
    }

    /// <summary>Huso horario de Colombia (UTC-5) para presentar las fechas del sello de identidad.</summary>
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    /// <summary>
    /// Resuelve la validación biométrica APROBADA+VIGENTE de una parte para el sello del FUR: primero la fila
    /// propia del trámite; si no, la identidad vigente REFERENCIADA por documento del actor (HU #10350, sin
    /// clonar). Devuelve null si la parte no tiene identidad aprobada vigente.
    /// </summary>
    private async Task<ProcedureInstanceBiometricValidation?> ResolveApprovedValidationAsync(
        ProcedureInstance instance, string role, DateTimeOffset now, CancellationToken ct)
    {
        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));

        // Fila propia aprobada+vigente del rol Y del documento del actor actual. El filtro por documento es
        // PARIDAD EXACTA con el gate (IdentityApprovalResolver.HasLocalVigente): sin él, una fila propia con
        // documento desfasado (documento editado tras validar) haría que el gate aprobara por la identidad
        // referenciada mientras el sello estamparía otro documento/certificado — inconsistencia en el FUR.
        var own = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.PartyRole, role, StringComparison.OrdinalIgnoreCase)
            && BiometricRules.EsAprobadaVigente(v, now)
            && BiometricRules.DocumentoCoincide(v, actor?.DocumentType, actor?.DocumentNumber));
        if (own is not null)
            return own;

        // Identidad vigente REFERENCIADA por documento del actor (HU #10350, sin clonar).
        if (actor is not null && !string.IsNullOrWhiteSpace(actor.DocumentType) && !string.IsNullOrWhiteSpace(actor.DocumentNumber))
            return await repo.FindVigenteApprovedByDocumentAsync(
                instance.TenantId, actor.DocumentType.Trim(), actor.DocumentNumber.Trim(), now, ct);

        return null;
    }

    /// <summary>
    /// Sello de texto de la validación biométrica (HU #10488): documento, uuid de la validación, serie/hash del
    /// certificado (firmaSerie) y fechas de aprobación/vencimiento (día calendario Colombia). Multilínea: el
    /// overlay del FUR lo pinta línea a línea en el espacio de firma.
    /// </summary>
    private static string BuildIdentidadSello(ProcedureInstanceBiometricValidation v)
    {
        var doc = $"{v.DocumentType} {v.DocumentNumber}".Trim();
        var uuid = string.IsNullOrWhiteSpace(v.KyverumVerificationId) ? v.Id.ToString("D") : v.KyverumVerificationId!;
        var firma = string.IsNullOrWhiteSpace(v.CertificateHash) ? "-" : v.CertificateHash!;
        var aprob = v.ValidatedAt is { } va
            ? va.ToOffset(ColombiaOffset).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "-";
        var vence = v.ValidUntil is { } vu
            ? vu.ToOffset(ColombiaOffset).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "-";
        return $"Validación biométrica {doc}\nUUID {uuid}\nFirma {firma}\nAprob {aprob} · Vence {vence}";
    }

    /// <summary>
    /// HU #10589 — Genera el certificado RUES del primer actor persona jurídica (DocumentType = NIT)
    /// del trámite, o <c>null</c> si no hay ninguno. Autocontenido: usa la razón social (FullName) y el
    /// NIT del actor; el estado en RUES es "ACTIVA" (mock hasta el proveedor real).
    /// </summary>
    private GeneratedDocument? TryGenerateRuesCertificate(ProcedureInstance instance)
    {
        var juridico = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.DocumentType, "NIT", StringComparison.OrdinalIgnoreCase));
        if (juridico is null)
            return null;

        var data = new RuesCertificateData(
            instance.Id,
            instance.ReferenceNumber,
            juridico.FullName,
            juridico.DocumentNumber,
            "ACTIVA");

        return ruesGenerator.GenerateRuesCertificate(data);
    }

    /// <summary>
    /// Descarga best-effort el certificado (PDF) de la validación de identidad del COMPRADOR desde Kyverum.
    /// Devuelve null (sin bloquear el FUR) si no hay validación Kyverum con id, si Kyverum no tiene
    /// certificado, o si la descarga falla — en los dos últimos casos registra un warning.
    /// </summary>
    private async Task<GeneratedDocument?> TryDownloadIdentityCertificateAsync(
        ProcedureInstance instance, CancellationToken ct)
    {
        static bool EsKyverumConId(ProcedureInstanceBiometricValidation v) =>
            v.Status == BiometricEstados.Aprobado
            && string.Equals(v.Provider, BiometricProviders.Kyverum, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(v.KyverumVerificationId);

        var bio = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.PartyRole, "comprador", StringComparison.OrdinalIgnoreCase) && EsKyverumConId(v));

        // Sin fila propia (identidad REFERENCIADA de otro trámite de la persona): se busca la validación
        // vigente del comprador por documento para tomar su certificado Kyverum (HU #10350, sin clonar).
        if (bio is null)
        {
            var actor = instance.Actors.FirstOrDefault(a =>
                string.Equals(a.ActorType, "comprador", StringComparison.OrdinalIgnoreCase));
            if (actor is not null && !string.IsNullOrWhiteSpace(actor.DocumentType) && !string.IsNullOrWhiteSpace(actor.DocumentNumber))
            {
                var source = await repo.FindVigenteApprovedByDocumentAsync(
                    instance.TenantId, actor.DocumentType.Trim(), actor.DocumentNumber.Trim(), DateTimeOffset.UtcNow, ct);
                if (source is not null && EsKyverumConId(source))
                    bio = source;
            }
        }

        if (bio is null)
            return null; // provider mock o sin id de Kyverum: no hay certificado externo que descargar.

        try
        {
            var cert = await certClient.DownloadCertificateAsync(bio.KyverumVerificationId!, ct);
            if (cert is null)
            {
                GenerarFurLog.CertificadoNoDisponible(logger, bio.Id, instance.Id);
                return null;
            }

            var safeRef = instance.ReferenceNumber.Replace('/', '-');
            return new GeneratedDocument(
                "certificado_identidad", $"certificado_identidad_{safeRef}.pdf", cert.ContentType, cert.Content);
        }
        catch (KyverumCertificateException ex)
        {
            GenerarFurLog.CertificadoDescargaFallo(logger, ex, instance.Id);
            return null;
        }
    }

    private static void AddParte(List<DocumentParte> partes, ProcedureInstance instance, string rol)
    {
        var a = instance.Actors.FirstOrDefault(x =>
            string.Equals(x.ActorType, rol, StringComparison.OrdinalIgnoreCase));
        var (ciudad, direccion) = ParseActorMetadata(a?.Metadata);
        partes.Add(new DocumentParte(
            rol,
            a?.FullName,
            a?.DocumentNumber,
            a?.Email,
            string.IsNullOrWhiteSpace(a?.DocumentType) ? null : a.DocumentType.Trim(),
            string.IsNullOrWhiteSpace(a?.Phone) ? null : a.Phone.Trim(),
            direccion,
            ciudad));
    }

    private static readonly JsonSerializerOptions ActorMetadataJson = new(JsonSerializerDefaults.Web);

    private static (string? Ciudad, string? Direccion) ParseActorMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata) || metadata == "{}")
            return (null, null);
        try
        {
            var m = JsonSerializer.Deserialize<ActorMetadataDto>(metadata, ActorMetadataJson);
            return (m?.Ciudad, m?.Direccion);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private sealed record ActorMetadataDto(string? Ciudad, string? Direccion);

    private static DateTime? ParseFechaTramite(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
    }

    private static string? Get(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) ? v : null;
}

/// <summary>Logging source-generated (CA1848) de la generación del FUR. No incluye PII ni secretos.</summary>
internal static partial class GenerarFurLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Kyverum no tiene certificado para la validación {ValidationId} (instancia {InstanceId}); se omite del expediente.")]
    public static partial void CertificadoNoDisponible(ILogger logger, Guid validationId, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo descargar el certificado de identidad de Kyverum (instancia {InstanceId}); se omite del expediente sin bloquear el FUR.")]
    public static partial void CertificadoDescargaFallo(ILogger logger, Exception ex, Guid instanceId);
}
