using System.Globalization;
using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
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
    IRnmcCertificateGenerator rnmcGenerator,
    IProcedureInstancePrendaRepository prendaRepo,
    IAttachmentStorage storage,
    ILogger<GenerarFurHandler> logger,
    ISignatureVaultPolicy? vaultPolicy = null,
    IFurTemplateResolver? templateResolver = null)
{
    // ADR-0025 §4 / HU #10645 — baúl de firmas: cubre la identidad de un actor NIT y alimenta la
    // IMAGEN real de la firma en el FUR. Default seguro (NUNCA resuelve) en tests que no lo ejercitan.
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

    // HU #10920 (Feature #10918) — resuelve la plantilla de FUR según la clasificación del vehículo. Si no
    // se inyecta (tests), la plantilla es AUTOMOTOR (comportamiento previo intacto).
    private readonly IFurTemplateResolver? _templateResolver = templateResolver;

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
            repo, instance, DateTimeOffset.UtcNow, ct, _vaultPolicy);
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

        // HU #10645 (ADR-0025 §4) — imagen REAL de la firma del baúl por parte NIT cubierta: se descarga el
        // artefacto (best-effort) y se alimenta FurDocumentData.FirmaImagenes; el mapper la estampa en el
        // espacio de firma en vez del sello de texto. Si la descarga falla, NO rompe el FUR (cae al sello).
        var (firmaImagenes, firmaBaulMetadatos) = await ResolveVaultSignaturesAsync(instance, esTraspaso, ct);

        // HU #10920 — plantilla de FUR según la clasificación del vehículo (vehicle_class). Sin resolver → AUTOMOTOR.
        var templateFormat = _templateResolver is not null
            ? await _templateResolver.ResolveAsync(Get(fv, "vehicle_class"), ct)
            : FurTemplateFormat.Automotor;

        var data = AssembleData(instance, codigo, esTraspaso, fv, identidadValidada, sellosIdentidad, tienePrenda, acreedorPrenda, firmaImagenes, firmaBaulMetadatos, templateFormat);

        var now = DateTimeOffset.UtcNow;
        var docs = new List<FurDocumentDto>(3);

        // FUR siempre. Compraventa solo en traspaso.
        var generated = new List<GeneratedDocument> { generator.GenerateFur(data) };
        if (esTraspaso)
            generated.Add(generator.GenerateCompraventa(data));

        if (identidadValidada)
        {
            // Certificado de identidad: PDF REAL de Kyverum (best-effort) POR PARTE. Traspaso emite el del
            // comprador y el del vendedor; matrícula solo el del comprador (mismo patrón que los sellos).
            // Si falla la descarga de una parte, warning + omitir esa parte (sin mock).
            var rolesCert = esTraspaso
                ? new[] { BiometricRules.ParteComprador, BiometricRules.ParteVendedor }
                : new[] { BiometricRules.ParteComprador };
            foreach (var role in rolesCert)
            {
                var certificado = await TryDownloadIdentityCertificateAsync(instance, role, ct);
                if (certificado is not null)
                    generated.Add(certificado);
            }
        }
        else
        {
            // Sin validación de identidad, retirar cualquier certificado previo (regeneración): el
            // consolidado no debe incluir un certificado de identidad obsoleto (#10463 AC5). StartsWith
            // cubre ambas variantes por parte (certificado_identidad y certificado_identidad_vendedor).
            foreach (var prev in instance.Attachments
                         .Where(a => a.Tipo.StartsWith("certificado_identidad", StringComparison.OrdinalIgnoreCase))
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

        // HU #10762 — Certificado RNMC: si el preflight consultó el RNMC de alguna parte, emitir el
        // certificado (PDF, Source=system) con el resultado por parte para que se descargue del trámite y
        // se fusione en el consolidado. Best-effort: nunca bloquea el FUR.
        var (certificadoRnmc, aplicaRnmc) = TryGenerateRnmcCertificate(instance);
        if (certificadoRnmc is not null)
        {
            generated.Add(certificadoRnmc);
        }
        else if (!aplicaRnmc)
        {
            // Sin checks RNMC (o dejó de haberlos en una regeneración): retirar el certificado previo, para
            // que el consolidado no arrastre un resultado RNMC obsoleto. Solo cuando el RNMC NO aplica: si
            // aplicaba y la generación falló, se conserva el previo (borrarlo por un fallo transitorio
            // perdería un certificado válido).
            foreach (var prev in instance.Attachments
                         .Where(a => string.Equals(a.Tipo, "certificado_rnmc", StringComparison.OrdinalIgnoreCase))
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
        bool tienePrenda, string? acreedorPrenda,
        IReadOnlyDictionary<string, byte[]>? firmaImagenes,
        IReadOnlyDictionary<string, FirmaBaulMetadata>? firmaBaulMetadatos,
        FurTemplateFormat templateFormat)
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
            // A4/B4 (HU #10673, ADR-0029) — los CAMPOS del vehículo del FUR (color/combustible) llevan el
            // dato ORIGINAL del RUNT (snapshot *_runt); la transformación declarada viaja solo en las
            // observaciones. Fallback al efectivo si no hay snapshot (trámites previos a la feature).
            Color: RuntOrEffective(fv, "vehicle_color_runt", "vehicle_color"),
            Clase: Get(fv, "vehicle_class"),
            Combustible: RuntOrEffective(fv, "vehicle_fuel_runt", "vehicle_fuel"),
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
            FirmaImagenes: firmaImagenes,
            FirmaBaulMetadatos: firmaBaulMetadatos,
            IdentidadValidada: identidadValidada,
            SellosIdentidad: sellosIdentidad,
            TienePrenda: tienePrenda,
            AcreedorPrenda: acreedorPrenda,
            TemplateFormat: templateFormat);
    }

    /// <summary>
    /// HU #10645 (ADR-0025 §4) — resuelve la IMAGEN de la firma del baúl por parte con actor JURÍDICO (NIT)
    /// cubierto por una firma activa+vigente. Descarga el artefacto vía <see cref="IAttachmentStorage.OpenReadAsync"/>
    /// (best-effort) y lo mapea por rol ("comprador"/"vendedor") para <see cref="FurDocumentData.FirmaImagenes"/>;
    /// los metadatos del baúl (<see cref="FurDocumentData.FirmaBaulMetadatos"/>) se estampan a la derecha de la
    /// imagen. NUNCA rompe el FUR: cualquier fallo de lectura se registra como warning y la parte cae al sello
    /// de texto. Devuelve null en ambos diccionarios si ninguna parte tiene firma de baúl.
    /// </summary>
    private async Task<(IReadOnlyDictionary<string, byte[]>? Images, IReadOnlyDictionary<string, FirmaBaulMetadata>? Metadata)> ResolveVaultSignaturesAsync(
        ProcedureInstance instance, bool esTraspaso, CancellationToken ct)
    {
        var roles = esTraspaso ? new[] { "comprador", "vendedor" } : new[] { "comprador" };
        Dictionary<string, byte[]>? images = null;
        Dictionary<string, FirmaBaulMetadata>? metadata = null;

        foreach (var role in roles)
        {
            var actor = instance.Actors.FirstOrDefault(a =>
                string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));
            if (actor is null || !EsActorJuridico(actor.DocumentType) || string.IsNullOrWhiteSpace(actor.DocumentNumber))
                continue;

            var match = await _vaultPolicy.ResolveAsync(instance.TenantId, actor.DocumentNumber.Trim(), ct);
            if (match is null || string.IsNullOrWhiteSpace(match.StoragePath))
                continue;

            try
            {
                var stream = await storage.OpenReadAsync(match.StoragePath, ct);
                if (stream is null)
                    continue;

                await using (stream.ConfigureAwait(false))
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, ct);
                    if (ms.Length == 0)
                        continue;
                    (images ??= new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase))[role] = ms.ToArray();
                    (metadata ??= new Dictionary<string, FirmaBaulMetadata>(StringComparer.OrdinalIgnoreCase))[role] =
                        new FirmaBaulMetadata(
                            match.DocumentNumber,
                            match.FullName,
                            match.VigenciaDesde,
                            match.VigenciaHasta,
                            match.SignatureVaultId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort (ADR-0025 §4): si el artefacto de firma no se puede leer, se omite la imagen y la
                // parte cae al sello de texto. No se bloquea la generación del FUR.
                GenerarFurLog.FirmaBaulNoDisponible(logger, ex, instance.Id);
            }
        }

        return (images, metadata);
    }

    /// <summary>¿El actor es persona JURÍDICA (NIT/N)? Solo estos consumen el baúl de firmas (ADR-0025 §4).</summary>
    private static bool EsActorJuridico(string? documentType)
    {
        var t = documentType?.Trim();
        return string.Equals(t, "NIT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "N", StringComparison.OrdinalIgnoreCase);
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
        // Documento del SUJETO de identidad (HU #10688): el RL en PJ, el actor en PN.
        var subject = actor is null ? null : IdentitySubjectResolver.For(actor);

        // Fila propia aprobada+vigente del rol Y del documento del sujeto actual. El filtro por documento es
        // PARIDAD EXACTA con el gate (IdentityApprovalResolver.HasLocalVigente): sin él, una fila propia con
        // documento desfasado (documento editado tras validar) haría que el gate aprobara por la identidad
        // referenciada mientras el sello estamparía otro documento/certificado — inconsistencia en el FUR.
        var own = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.PartyRole, role, StringComparison.OrdinalIgnoreCase)
            && BiometricRules.EsAprobadaVigente(v, now)
            && BiometricRules.DocumentoCoincide(v, subject?.TipoDocumento, subject?.NumeroDocumento));
        if (own is not null)
            return own;

        // Identidad vigente REFERENCIADA por documento del sujeto (HU #10350, sin clonar).
        if (subject is not null && !string.IsNullOrWhiteSpace(subject.TipoDocumento) && !string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            return await repo.FindVigenteApprovedByDocumentAsync(
                instance.TenantId, subject.TipoDocumento.Trim(), subject.NumeroDocumento.Trim(), now, ct);

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
    /// HU #10762 — Genera el certificado RNMC desde el ÚLTIMO snapshot de preflight (los checks
    /// <c>rnmc_{rol}_*</c>), o <c>null</c> si no se ha corrido el preflight o no consultó el RNMC (p. ej.
    /// todas las partes son personas jurídicas: el RNMC solo aplica a naturales).
    /// <para>La fuente del dato es el SNAPSHOT, no field_values: la señal <c>rnmc_medida_pendiente</c> es un
    /// booleano sin el detalle por parte que exige el certificado.</para>
    /// <para>Best-effort: cualquier fallo se registra como warning y omite el certificado; nunca aborta el FUR.</para>
    /// </summary>
    /// <returns>
    /// <c>Aplica</c> distingue "el RNMC no aplica a este trámite" (⇒ el caller retira el certificado previo)
    /// de "aplicaba pero la generación falló" (⇒ se conserva el previo, que sigue siendo válido).
    /// </returns>
    private (GeneratedDocument? Doc, bool Aplica) TryGenerateRnmcCertificate(ProcedureInstance instance)
    {
        try
        {
            // FEATURE 05 — el resultado RNMC ya no vive en el snapshot del pre-vuelo, sino en el
            // field_value `rnmc_checks` que escribe la consulta RNMC dedicada del paso final.
            var fvRnmc = instance.FieldValues
                .FirstOrDefault(f => string.Equals(f.FieldKey, RunRnmcConsultHandler.FieldRnmcChecks, StringComparison.Ordinal));

            var checks = GetPreflightHandler.DeserializeChecks(fvRnmc?.ValueJson)
                .Where(c => c.Key.EndsWith("medidas_correctivas", StringComparison.Ordinal))
                .ToList();
            if (checks.Count == 0)
                return (null, false);

            var entradas = new List<RnmcCertificateEntry>(checks.Count);
            foreach (var check in checks)
            {
                var rol = RolFromCheckKey(check.Key);
                var actor = instance.Actors.FirstOrDefault(a =>
                    string.Equals(a.ActorType, rol, StringComparison.OrdinalIgnoreCase));
                entradas.Add(new RnmcCertificateEntry(
                    rol,
                    actor?.FullName ?? string.Empty,
                    actor?.DocumentNumber ?? string.Empty,
                    EstadoRnmc(check.Status),
                    check.Message));
            }

            var consultadoAt = fvRnmc?.UpdatedAt ?? fvRnmc?.CreatedAt ?? DateTimeOffset.UtcNow;
            var data = new RnmcCertificateData(
                instance.Id,
                instance.ReferenceNumber,
                consultadoAt,
                entradas);

            return (rnmcGenerator.GenerateRnmcCertificate(data), true);
        }
        catch (Exception ex)
        {
            GenerarFurLog.CertificadoRnmcFallo(logger, ex, instance.Id);
            return (null, true);
        }
    }

    /// <summary>
    /// Rol del segmento intermedio de la key del check: <c>rnmc_{rol}_{keyDelMapper}</c> (p. ej.
    /// <c>rnmc_comprador_medidas_correctivas</c>). Cubre también la key sin sufijo (<c>rnmc_{rol}</c>), que
    /// el preflight emite cuando el provider RNMC no está disponible.
    /// </summary>
    private static string RolFromCheckKey(string key)
    {
        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    /// <summary>
    /// Estado del check → texto del certificado. Deliberadamente NO expone el estado crudo del preflight
    /// ni el proveedor: el certificado es un documento de cara al usuario / al OT.
    /// </summary>
    private static string EstadoRnmc(string? status) => status switch
    {
        "ok" => "SIN MEDIDAS CORRECTIVAS",
        "warn" => "CON MEDIDAS CORRECTIVAS",
        "unknown" => "SIN DATOS",
        _ => "NO VERIFICABLE",
    };

    /// <summary>
    /// Descarga best-effort el certificado (PDF) de la validación de identidad de una PARTE
    /// (<paramref name="role"/> = comprador | vendedor) desde Kyverum. El adjunto del comprador conserva
    /// el tipo <c>certificado_identidad</c> (retrocompatible); el del vendedor usa
    /// <c>certificado_identidad_vendedor</c>, de modo que ambos coexistan en el expediente.
    /// Devuelve null (sin bloquear el FUR) si no hay validación Kyverum con id, si Kyverum no tiene
    /// certificado, o si la descarga falla — en los dos últimos casos registra un warning.
    /// </summary>
    private async Task<GeneratedDocument?> TryDownloadIdentityCertificateAsync(
        ProcedureInstance instance, string role, CancellationToken ct)
    {
        static bool EsKyverumConId(ProcedureInstanceBiometricValidation v) =>
            v.Status == BiometricEstados.Aprobado
            && string.Equals(v.Provider, BiometricProviders.Kyverum, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(v.KyverumVerificationId);

        var bio = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.PartyRole, role, StringComparison.OrdinalIgnoreCase) && EsKyverumConId(v));

        // Sin fila propia (identidad REFERENCIADA de otro trámite de la persona): se busca la validación
        // vigente de la parte por documento para tomar su certificado Kyverum (HU #10350, sin clonar).
        if (bio is null)
        {
            var actor = instance.Actors.FirstOrDefault(a =>
                string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));
            // Documento del SUJETO de identidad (HU #10688): el RL en PJ, el actor en PN.
            var subject = actor is null ? null : IdentitySubjectResolver.For(actor);
            if (subject is not null && !string.IsNullOrWhiteSpace(subject.TipoDocumento) && !string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            {
                var source = await repo.FindVigenteApprovedByDocumentAsync(
                    instance.TenantId, subject.TipoDocumento.Trim(), subject.NumeroDocumento.Trim(), DateTimeOffset.UtcNow, ct);
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

            // Comprador: certificado_identidad (retrocompatible). Otras partes: sufijo de rol.
            var tipo = string.Equals(role, BiometricRules.ParteComprador, StringComparison.OrdinalIgnoreCase)
                ? "certificado_identidad"
                : $"certificado_identidad_{role}";
            var safeRef = instance.ReferenceNumber.Replace('/', '-');
            return new GeneratedDocument(
                tipo, $"{tipo}_{safeRef}.pdf", cert.ContentType, cert.Content);
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
        // HU #10688 — persona jurídica (tipo juridical o documento NIT): la razón social no se trocea en el FUR.
        var esJuridica = ActorPersonTypes.IsJuridical(a?.PersonType)
            || string.Equals(a?.DocumentType, "NIT", StringComparison.OrdinalIgnoreCase);
        partes.Add(new DocumentParte(
            rol,
            a?.FullName,
            a?.DocumentNumber,
            a?.Email,
            string.IsNullOrWhiteSpace(a?.DocumentType) ? null : a.DocumentType.Trim(),
            string.IsNullOrWhiteSpace(a?.Phone) ? null : a.Phone.Trim(),
            direccion,
            ciudad,
            esJuridica));
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

    // Devuelve el snapshot RUNT si existe (no vacío); si no, cae al valor efectivo. Se usa para imprimir
    // el dato original del vehículo en el FUR aunque exista una transformación declarada en el efectivo.
    private static string? RuntOrEffective(Dictionary<string, string?> fv, string runtKey, string effectiveKey)
    {
        var runt = Get(fv, runtKey);
        return string.IsNullOrWhiteSpace(runt) ? Get(fv, effectiveKey) : runt;
    }
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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo leer el artefacto de la firma del baúl (instancia {InstanceId}); la parte cae al sello de texto sin bloquear el FUR.")]
    public static partial void FirmaBaulNoDisponible(ILogger logger, Exception ex, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo generar el certificado RNMC (instancia {InstanceId}); se omite del expediente sin bloquear el FUR.")]
    public static partial void CertificadoRnmcFallo(ILogger logger, Exception ex, Guid instanceId);
}
