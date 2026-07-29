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
/// <para><b>Estado:</b> solo <c>borrador</c> y <c>subsanacion</c>, mismo criterio que
/// <see cref="PatchFieldValuesHandler"/> y que el trigger <c>trg_field_value_immutable</c>.</para>
/// </summary>
public sealed class PersistOcrFieldsHandler(IProcedureInstanceRepository repo)
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
        };

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
        // de la BD rechazaría la escritura.
        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
        {
            return (null, "not_draft");
        }

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
                // queda protegida por defecto, que es el lado seguro del error.
                if (!string.Equals(existing.Source, OcrSource, StringComparison.OrdinalIgnoreCase))
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

        return (new PersistOcrFieldsResult(persistidos, omitidos, ignorados), null);
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
