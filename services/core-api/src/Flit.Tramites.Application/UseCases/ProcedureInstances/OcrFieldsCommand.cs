using Flit.Tramites.Application.UseCases.Certifications;
using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Campos extraídos por el OCR de un documento, tal como los devuelve el analizador.</summary>
public sealed record PersistOcrFieldsRequest(string Tipo, IReadOnlyDictionary<string, string?> Fields);

/// <summary>
/// Resultado de persistir el OCR: cuántas llaves se escribieron y cuáles se omitieron y por qué.
/// El detalle de omisiones es deliberado — sin él, "el certificado sigue vacío" es indepurable.
/// </summary>
public sealed record PersistOcrFieldsResult(
    int Persistidos,
    IReadOnlyList<string> OmitidosPorPrecedencia,
    IReadOnlyList<string> IgnoradosFueraDeAlcance);

/// <summary>
/// HU #10975 (Feature #10972) — persiste en <c>field_values</c> los campos que el OCR semántico ya
/// extrae de un documento cargado en el wizard y que hasta ahora se descartaban.
///
/// <para><b>Por qué un caso de uso propio y no <see cref="PatchFieldValuesHandler"/>:</b> aquel marca
/// todo como <c>Source = "user"</c> y no permite expresar la precedencia entre fuentes. Aquí el origen
/// es <c>"ocr"</c> y esa distinción es justo la que gobierna la regla de abajo.</para>
///
/// <para><b>Regla de precedencia (decisión de diseño).</b> El RUNT es fuente OFICIAL; el OCR es fuente
/// de RESPALDO. Por tanto: (1) una llave escrita por consulta NO se pisa con OCR; (2) el OCR solo
/// escribe llaves ausentes o previamente escritas por OCR; (3) si el RUNT llega después, SÍ sobrescribe
/// (la consulta es más fresca y más autoritativa). Esto evita que un PDF viejo cargado a mano
/// contradiga al RUNT dentro del mismo documento.</para>
///
/// <para><b>Alcance por tipo de documento:</b> un OCR de <c>soat</c> solo puede escribir llaves de SOAT.
/// Sin esta whitelist, el analizador de un documento cualquiera podría reescribir el vehículo entero.</para>
///
/// <para><b>Estado:</b> <c>borrador</c> y <c>subsanacion</c>, mismo criterio que
/// <see cref="PatchFieldValuesHandler"/> y que el trigger <c>trg_field_value_immutable</c>.</para>
///
/// <para><b>Bug #13194 — soporte del SOAT en <c>asignado</c>.</b> Con la compañía exigiendo SOAT vigente y
/// un RUNT que no lo reporta, «Enviar al OT» pide cargar el PDF: el adjunto ya se podía subir en
/// <c>asignado</c>, pero su lectura no tenía dónde quedar y el gestor no tenía salida. Ahí se admite SOLO
/// el OCR de tipo <c>soat</c> y SOLO <c>soat_estado</c> + <c>soat_vencimiento</c> (lo que el gate necesita
/// y lo único que el trigger deja escribir en ese estado); el resto del payload se ignora. Exige un
/// adjunto de SOAT no histórico en el trámite (sin PDF no hay soporte que leer). Precedencia: el OCR pisa
/// un <c>unknown</c> de la consulta («el RUNT no lo reporta»), nunca un <c>vencido</c> ni un <c>vigente</c>
/// afirmados por el RUNT. Que la lectura cuente como soporte lo sigue decidiendo
/// <c>ValidateSoatViaRuntHandler</c> (adjunto + fecha legible no vencida). El PATCH de esas llaves sigue
/// rechazado (<see cref="PatchFieldValuesHandler.ClaveDeSistemaError"/>).</para>
/// </summary>
public sealed class PersistOcrFieldsHandler(
    IProcedureInstanceRepository repo,
    Certifications.ICertificationIngestionService? certificationIngestion = null)
{
    /// <summary>Origen de los valores escritos por esta ruta. Lo consume la regla de precedencia.</summary>
    public const string OcrSource = "ocr";

    /// <summary>
    /// Mapa por tipo de documento: clave del JSON del OCR → clave de <c>field_values</c>.
    /// Es la whitelist: lo que no está aquí NO se escribe, venga como venga en el payload.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Whitelist =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // HU #10975 / #10976 — el prompt de SOAT ya extraía todo esto; solo faltaba persistirlo.
            ["soat"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["numero_poliza"] = "soat_poliza",
                ["aseguradora"] = "soat_aseguradora",
                ["fecha_inicio"] = "soat_vigencia",
                ["fecha_expedicion"] = "soat_expedicion", // HU #10976 (prompt v2)
                ["fecha_vencimiento"] = "soat_vencimiento",
                ["estado_poliza"] = SoatGate.FieldKey,
            },
            // HU #10977 — prompt de RTM nuevo.
            ["rtm"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["numero_certificado"] = "rtm_numero",
                ["cda_expide"] = "rtm_entidad",
                ["fecha_expedicion"] = "rtm_expedicion",
                ["fecha_vigencia"] = "rtm_vigencia",
                ["fecha_vencimiento"] = "rtm_vencimiento",
                ["estado"] = "rtm_estado",
            },
            // HU #12776 — fecha de expedición del certificado de Cámara de Comercio. Es lo único que
            // se persiste de este prompt: el resto de lo que extrae (razón social, NIT, representante)
            // ya lo tiene el trámite por el RUES y por la captura del actor, y escribirlo aquí sería
            // dejar que un PDF escaneado compitiera con la fuente oficial.
            //
            // Una llave POR ROL, como los códigos de adjunto: con una sola, en un traspaso entre dos
            // sociedades la fecha del comprador pisaría la del vendedor y la alerta de vigencia se
            // calcularía sobre el documento equivocado.
            [CamaraComercioAttachmentTipo.Vendedor] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fecha_expedicion"] = CamaraComercioFieldKeys.Expedicion("vendedor"),
            },
            [CamaraComercioAttachmentTipo.Comprador] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fecha_expedicion"] = CamaraComercioFieldKeys.Expedicion("comprador"),
            },
            [CamaraComercioAttachmentTipo.Locatario] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fecha_expedicion"] = CamaraComercioFieldKeys.Expedicion("locatario"),
            },
        };

    /// <summary>
    /// Bug #13194 — en <c>asignado</c> no hay adjunto de SOAT vigente en el trámite: la lectura no tiene
    /// documento que la respalde y no se registra.
    /// </summary>
    public const string SoporteSoatRequeridoError = "soporte_soat_requerido";

    /// <summary>Tipo de OCR admitido en <c>asignado</c> (Bug #13194).</summary>
    private const string SoatTipo = "soat";

    private const string SoatVencimientoKey = "soat_vencimiento";

    /// <summary>
    /// Bug #13194 — únicas llaves que el OCR del SOAT escribe en <c>asignado</c>. Espejo de la allowlist del
    /// trigger <c>trg_field_value_immutable</c> para ese estado (DDL 129-BUG13194).
    /// </summary>
    private static readonly HashSet<string> LlavesSoatEnAsignado =
        new(StringComparer.OrdinalIgnoreCase) { SoatGate.FieldKey, SoatVencimientoKey };

    /// <summary>¿El tipo de documento tiene campos persistibles por OCR?</summary>
    public static bool SoportaPersistencia(string? tipo) =>
        !string.IsNullOrWhiteSpace(tipo) && Whitelist.ContainsKey(tipo);

    public async Task<(PersistOcrFieldsResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        PersistOcrFieldsRequest request,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Tipo))
            return (null, "invalid_request");

        if (!Whitelist.TryGetValue(request.Tipo, out var mapa))
            return (null, "tipo_no_soportado");

        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // Misma puerta que PatchFieldValuesHandler: fuera de borrador/subsanación activa el trigger
        // de la BD rechazaría la escritura. Excepción (Bug #13194): el soporte del SOAT en 'asignado'.
        var soporteSoatEnAsignado = false;
        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
        {
            if (!string.Equals(instance.Status, TramiteEstado.Asignado, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(request.Tipo.Trim(), SoatTipo, StringComparison.OrdinalIgnoreCase))
            {
                return (null, "not_draft");
            }

            if (!await TieneAdjuntoSoatVigenteAsync(instance.Id, tenantId, ct))
                return (null, SoporteSoatRequeridoError);

            soporteSoatEnAsignado = true;
        }

        // Bug #13194 — se decide UNA vez, antes de escribir: si el RUNT afirmó el SOAT (vigente o vencido),
        // el PDF no registra nada en 'asignado'; si no lo reportó, el OCR reemplaza lo de la consulta.
        var runtAfirmoSoat = soporteSoatEnAsignado && !RuntNoReportaSoat(instance);

        var now = DateTimeOffset.UtcNow;
        var persistidos = 0;
        var omitidos = new List<string>();
        var ignorados = new List<string>();

        foreach (var (ocrKey, rawValue) in request.Fields)
        {
            if (!mapa.TryGetValue(ocrKey, out var fieldKey))
            {
                // Fuera de la whitelist del tipo: se descarta sin tocar nada. No es un error del
                // usuario (el OCR devuelve más campos de los que nos interesan), pero se reporta.
                ignorados.Add(ocrKey);
                continue;
            }

            // Bug #13194 — en 'asignado' solo entran estado y vencimiento; lo demás se reporta ignorado.
            if (soporteSoatEnAsignado && !LlavesSoatEnAsignado.Contains(fieldKey))
            {
                ignorados.Add(ocrKey);
                continue;
            }

            if (runtAfirmoSoat)
            {
                omitidos.Add(fieldKey);
                continue;
            }

            var value = Normalizar(fieldKey, rawValue);
            if (string.IsNullOrWhiteSpace(value))
                continue; // valor ausente ⇒ NO se escribe la llave ⇒ celda en blanco (regla HU #10856).

            var existing = instance.FieldValues.FirstOrDefault(f =>
                string.Equals(f.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                // Precedencia: el OCR solo puede pisar lo que él mismo escribió. Un valor de consulta
                // (fuente oficial) o digitado por el usuario manda sobre lo que diga un PDF. Se
                // comprueba por lista blanca de origen y no por lista negra: cualquier fuente futura
                // queda protegida por defecto, que es el lado seguro del error. Excepción (Bug #13194): en
                // 'asignado' ya se comprobó arriba que el RUNT no afirmó el SOAT (runtAfirmoSoat).
                if (!string.Equals(existing.Source, OcrSource, StringComparison.OrdinalIgnoreCase)
                    && !soporteSoatEnAsignado)
                {
                    omitidos.Add(fieldKey);
                    continue;
                }

                existing.ValueText = value;
                existing.Source = OcrSource;
                existing.UpdatedAt = now;
                persistidos++;
                continue;
            }

            var fieldValue = new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = instance.Id,
                // Valor "loose": derivado de un documento, no atado a un form_field del tipo de trámite.
                FormFieldId = null,
                FieldKey = fieldKey,
                ValueText = value,
                Source = OcrSource,
                CreatedAt = now,
            };
            instance.FieldValues.Add(fieldValue);
            // PK store-generated (uuidv7) con Id ya seteado: Added explícito para forzar INSERT.
            repo.Add(fieldValue);
            persistidos++;
        }

        if (persistidos > 0)
            await repo.SaveChangesAsync(ct);

        // HU #11304 — lo que el OCR extrajo también entra al almacén canónico, con procedencia `ocr`.
        // Importa más de lo que parece: hoy `soat_poliza` y `soat_vigencia` SOLO existen en los
        // trámites que pasaron por aquí, así que el OCR es la única fuente real de esas dos celdas en
        // buena parte del ambiente. La precedencia hace que una consulta posterior lo mejore sin
        // borrarlo, y que un OCR posterior no pise un dato de la fuente oficial.
        if (persistidos > 0)
            await IngestCertificationsAsync(id, tenantId, request.Tipo!, instance, now, ct);

        return (new PersistOcrFieldsResult(persistidos, omitidos, ignorados), null);
    }

    /// <summary>
    /// Bug #13194 — en <c>asignado</c> el OCR solo puede reemplazar lo que dejó la consulta si el RUNT NO
    /// reportó el SOAT (<c>soat_estado=unknown</c> de origen <c>consultation</c>) o si aún no hay estado. Un
    /// <c>vencido</c> o un <c>vigente</c> afirmados por el RUNT mandan sobre el PDF.
    /// </summary>
    private static bool RuntNoReportaSoat(ProcedureInstance instance)
    {
        var estado = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, SoatGate.FieldKey, StringComparison.OrdinalIgnoreCase));
        return estado is null
            || !string.Equals(estado.Source, "consultation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(estado.ValueText, SoatGate.Unknown, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(estado.ValueText);
    }

    /// <summary>¿El trámite tiene un adjunto de SOAT (<c>soat</c>/<c>soat_manual</c>) no histórico?</summary>
    private async Task<bool> TieneAdjuntoSoatVigenteAsync(Guid instanceId, Guid tenantId, CancellationToken ct)
    {
        var conAdjuntos = await repo.GetByIdWithAttachmentsAsync(instanceId, tenantId, ct);
        return conAdjuntos?.Attachments.Any(a =>
            !a.IsHistorico && AttachmentRules.IsSoatEvidenceTipo(a.Tipo)) == true;
    }

    /// <summary>
    /// Traslada al almacén canónico lo que acaba de quedar en <c>field_values</c> por esta ruta.
    /// </summary>
    /// <remarks>
    /// Se lee de la instancia y no del request para que la fuente sea exactamente lo persistido: si un
    /// campo se omitió por precedencia, aquí tampoco entra. Best-effort — el OCR ya respondió.
    /// </remarks>
    private async Task IngestCertificationsAsync(
        Guid instanceId, Guid tenantId, string tipo, ProcedureInstance instance,
        DateTimeOffset now, CancellationToken ct)
    {
        if (certificationIngestion is null)
            return;

        string? Valor(string key) => instance.FieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, key, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;

        var bundle = string.Equals(tipo, "soat", StringComparison.OrdinalIgnoreCase)
            ? CertificationBundle.ForVehicle(
                [CertificationFactory.Soat(
                    Valor("soat_poliza"), Valor("soat_aseguradora"), Valor("soat_expedicion"),
                    Valor("soat_vigencia"), Valor("soat_vencimiento"), Valor(SoatGate.FieldKey))],
                [])
            : CertificationBundle.ForVehicle(
                [],
                [CertificationFactory.Rtm(
                    Valor("rtm_numero"), Valor("rtm_entidad"), Valor("rtm_expedicion"),
                    Valor("rtm_vigencia"), Valor("rtm_vencimiento"), Valor("rtm_estado"))]);

        if (!bundle.HasAnyValue)
            return;

        var provenance = new CertificationProvenance(
            CertificationSourceKind.Ocr, OcrSource, now, MapperVersion: "ocr-v1");

        try
        {
            await certificationIngestion.IngestAsync(instanceId, tenantId, bundle, provenance, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Silencio acotado: ver RunConsultationHandler.IngestCertificationsAsync.
        }
    }

    /// <summary>
    /// Normaliza el valor según la llave destino. Hoy solo <c>soat_estado</c> lo necesita: es el gate de
    /// aprobación del OT (HU #10804) y el frontend compara su valor de forma ESTRICTA contra
    /// <c>"vigente"</c> en minúscula, así que el texto crudo del OCR ("vigente"/"vencida"/…) debe pasar
    /// por el vocabulario del gate. El resto de llaves se escriben tal cual las leyó el OCR.
    /// </summary>
    private static string? Normalizar(string fieldKey, string? value) =>
        string.Equals(fieldKey, SoatGate.FieldKey, StringComparison.OrdinalIgnoreCase)
            ? SoatGate.Normalize(value)
            : value?.Trim();
}
