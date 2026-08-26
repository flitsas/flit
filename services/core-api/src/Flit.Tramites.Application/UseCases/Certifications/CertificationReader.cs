using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Domain.Certifications.Normalization;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.Certifications;

/// <summary>
/// Resuelve lo certificado para los documentos, <b>sin llamar a ningún proveedor</b>
/// (HU #11305, ADR-0041).
/// </summary>
/// <remarks>
/// Orden: tabla canónica → respaldo sobre <c>field_values</c> → nada. El respaldo existe solo por los
/// trámites anteriores al despliegue: sus datos siguen en las llaves sueltas y en el snapshot
/// congelado, y el expediente debe seguir saliendo igual. Los trámites nuevos no lo tocan.
/// </remarks>
public sealed class CertificationReader(ICertificationRepository repository) : ICertificationReader
{
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    /// <summary>
    /// Procedencia que se declara cuando el dato viene del respaldo. No se disfraza de consulta: el
    /// certificado dice de dónde salió realmente.
    /// </summary>
    private const string LegacyProvider = "field_values";

    /// <summary>Las 23 llaves <c>rues_*</c> del certificado, más el NIT.</summary>
    private static readonly string[] RuesFieldKeys =
    [
        "rues_nit", "rues_razon_social", "rues_estado", "rues_matricula_mercantil",
        "rues_camara_comercio", "rues_sigla", "rues_fecha_matricula", "rues_ultimo_ano_renovado",
        "rues_fecha_renovacion", "rues_direccion", "rues_municipio", "rues_categoria",
        "rues_actividad_economica", "rues_tipo_organizacion", "rues_tipo_compania", "rues_email",
        "rues_id_rm", "rues_fecha_actualizacion", "rues_razon_cancelacion",
        "rues_representacion_legal", "rues_actividades_json", "rues_camara_ciudad",
        "rues_camara_departamento",
    ];

    public async Task<CertificationView> ForDocumentsAsync(
        Guid instanceId,
        Guid tenantId,
        IReadOnlyDictionary<string, string?> fieldValues,
        CancellationToken cancellationToken)
    {
        var snapshot = await repository.LoadAsync(tenantId, instanceId, cancellationToken);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ColombiaOffset).DateTime);

        var (soat, soatFrom) = ResolveSoat(snapshot, fieldValues, today);
        var (rtm, rtmFrom) = ResolveRtm(snapshot, fieldValues, today);

        return new CertificationView(
            soat, soatFrom,
            rtm, rtmFrom,
            ResolveVehicle(fieldValues),
            ResolveMerchants(snapshot, fieldValues));
    }

    // ── SOAT / RTM ────────────────────────────────────────────────────────────────────────────────

    private static (SoatCertification?, CertificationProvenance?) ResolveSoat(
        CertificationSnapshot snapshot, IReadOnlyDictionary<string, string?> fv, DateOnly today)
    {
        var stored = snapshot.SoatPolicies.FirstOrDefault(p => p.IsCurrent)
            ?? Pick(snapshot.SoatPolicies, p => p.Certification, SoatSelection.PickCurrent, today);

        if (stored is not null)
            return (stored.Certification, stored.Provenance);

        var legacy = CertificationFactory.Soat(
            Get(fv, "soat_poliza"),
            Get(fv, "soat_aseguradora"),
            Get(fv, "soat_expedicion"),
            Get(fv, "soat_vigencia"),
            Get(fv, "soat_vencimiento"),
            Get(fv, SoatGate.FieldKey));

        return legacy.HasAnyValue ? (legacy, LegacyProvenance(fv)) : (null, null);
    }

    private static (RtmCertification?, CertificationProvenance?) ResolveRtm(
        CertificationSnapshot snapshot, IReadOnlyDictionary<string, string?> fv, DateOnly today)
    {
        var stored = snapshot.RtmInspections.FirstOrDefault(r => r.IsCurrent)
            ?? Pick(snapshot.RtmInspections, r => r.Certification, RtmSelection.PickCurrent, today);

        if (stored is not null)
            return (stored.Certification, stored.Provenance);

        var legacy = CertificationFactory.Rtm(
            Get(fv, "rtm_numero"),
            Get(fv, "rtm_entidad"),
            Get(fv, "rtm_expedicion"),
            Get(fv, "rtm_vigencia"),
            Get(fv, "rtm_vencimiento"),
            Get(fv, "rtm_estado"));

        return legacy.HasAnyValue ? (legacy, LegacyProvenance(fv)) : (null, null);
    }

    /// <summary>
    /// Si ninguna fila trae la bandera de vigente —por ejemplo tras un traslado desde el almacén
    /// anterior—, se aplica el mismo criterio de selección por fecha en vez de tomar la primera.
    /// </summary>
    private static TStored? Pick<TStored, TCertification>(
        IReadOnlyList<TStored> stored,
        Func<TStored, TCertification> certification,
        Func<IReadOnlyList<TCertification>, DateOnly, TCertification?> pick,
        DateOnly today)
        where TStored : class
        where TCertification : class
    {
        if (stored.Count == 0)
            return null;

        var chosen = pick([.. stored.Select(certification)], today);
        return chosen is null ? null : stored.FirstOrDefault(s => ReferenceEquals(certification(s), chosen));
    }

    private static VehicleRegistrationFacts ResolveVehicle(IReadOnlyDictionary<string, string?> fv) =>
        CertificationFactory.Vehicle(Get(fv, RtmCertificado.FieldKeyFechaMatricula));

    // ── Registro mercantil ────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, MerchantCertificationView> ResolveMerchants(
        CertificationSnapshot snapshot, IReadOnlyDictionary<string, string?> fv)
    {
        var result = new Dictionary<string, MerchantCertificationView>(StringComparer.OrdinalIgnoreCase);

        // 1) Tabla canónica: una fila por NIT.
        foreach (var stored in snapshot.MerchantRegistrations)
        {
            var nit = stored.Registration.Nit?.Trim();
            if (string.IsNullOrEmpty(nit))
                continue;

            result[nit] = new MerchantCertificationView(
                stored.Registration,
                Project(stored.Registration, LegacyFieldsFor(nit, fv)),
                stored.Provenance);
        }

        // 2) Respaldo: snapshot congelado + llaves de instancia, para los NITs que la tabla no tiene.
        foreach (var (nit, fields) in LegacyMerchantsFor(fv))
        {
            if (result.ContainsKey(nit))
                continue;

            result[nit] = new MerchantCertificationView(null, fields, LegacyProvenance(fv));
        }

        return result;
    }

    /// <summary>
    /// Vuelca lo modelado en el canónico sobre la forma <c>rues_*</c> que consume el generador, y
    /// completa el resto con lo que hubiera guardado antes. Lo canónico manda sobre el respaldo.
    /// </summary>
    private static Dictionary<string, string?> Project(
        MerchantRegistration registration, IReadOnlyDictionary<string, string?> fallback)
    {
        var fields = new Dictionary<string, string?>(fallback, StringComparer.OrdinalIgnoreCase);

        Set(fields, "rues_nit", registration.Nit);
        Set(fields, "rues_razon_social", registration.BusinessName.ToDocumentText());
        Set(fields, "rues_matricula_mercantil", registration.RegistrationNumber.ToDocumentText());
        Set(fields, "rues_estado", registration.Status.ToDocumentText());
        Set(fields, "rues_fecha_matricula", registration.RegisteredOn.ToDocumentText());
        Set(fields, "rues_fecha_renovacion", registration.RenewedOn.ToDocumentText());
        Set(fields, "rues_camara_comercio", registration.ChamberOfCommerce.ToDocumentText());
        Set(fields, "rues_categoria", registration.Category.ToDocumentText());
        Set(fields, "rues_direccion", registration.Address.ToDocumentText());
        Set(fields, "rues_municipio", registration.City.ToDocumentText());

        return fields;
    }

    private static void Set(Dictionary<string, string?> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields[key] = value;
    }

    /// <summary>
    /// Registros mercantiles guardados antes del despliegue: primero el snapshot congelado al
    /// registrar el trámite, y si no, las llaves <c>rues_*</c> de la instancia —que solo sirven a UNA
    /// compañía, la del último lookup—.
    /// </summary>
    private static IEnumerable<(string Nit, IReadOnlyDictionary<string, string?> Fields)> LegacyMerchantsFor(
        IReadOnlyDictionary<string, string?> fv)
    {
        foreach (var nit in RuesSnapshots.Nits(Get(fv, RuesSnapshots.FieldKey)))
        {
            var fields = RuesSnapshots.Read(Get(fv, RuesSnapshots.FieldKey), nit);
            if (fields is not null)
                yield return (nit, fields);
        }

        var nitInstancia = Get(fv, "rues_nit")?.Trim();
        if (!string.IsNullOrEmpty(nitInstancia))
            yield return (nitInstancia, InstanceRuesFields(fv));
    }

    private static IReadOnlyDictionary<string, string?> LegacyFieldsFor(
        string nit, IReadOnlyDictionary<string, string?> fv)
    {
        var snapshot = RuesSnapshots.Read(Get(fv, RuesSnapshots.FieldKey), nit);
        if (snapshot is not null)
            return snapshot;

        return string.Equals(Get(fv, "rues_nit")?.Trim(), nit, StringComparison.OrdinalIgnoreCase)
            ? InstanceRuesFields(fv)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> InstanceRuesFields(IReadOnlyDictionary<string, string?> fv)
    {
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in RuesFieldKeys)
        {
            var value = Get(fv, key);
            if (!string.IsNullOrWhiteSpace(value))
                fields[key] = value;
        }

        return fields;
    }

    // ── Utilidades ────────────────────────────────────────────────────────────────────────────────

    private static CertificationProvenance LegacyProvenance(IReadOnlyDictionary<string, string?> fv)
    {
        // La fecha que el certificado declara ya se venía guardando al ejecutar la consulta; se
        // reutiliza para no inventar una nueva y hacer creer que se consultó al generar el PDF.
        var raw = Get(fv, "runt_consulta_fecha");
        var fecha = ColombianCertificateDate.Parse(raw).Value;

        var observedAt = fecha is { } day
            ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), ColombiaOffset)
            : DateTimeOffset.MinValue;

        return new CertificationProvenance(
            CertificationSourceKind.System, LegacyProvider, observedAt,
            MapperVersion: CertificationProvenance.LegacyProviderKey);
    }

    private static string? Get(IReadOnlyDictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
