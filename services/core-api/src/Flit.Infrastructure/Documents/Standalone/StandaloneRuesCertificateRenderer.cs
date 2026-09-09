using System.Text.Json;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Tramites.Application.Documents;

namespace Flit.Infrastructure.Documents.Standalone;

/// <summary>
/// Adaptador del puerto <see cref="IStandaloneRuesCertificateRenderer"/> (Feature #12201,
/// ADR-0056-generacion-documental-standalone): traduce los campos <c>rues_*</c> del snapshot al
/// contrato del generador del expediente y delega en <see cref="IRuesCertificateGenerator"/>.
///
/// <para><b>Ni <c>RuesCertificateData</c> ni <c>RuesCertificatePdfGenerator</c> se modifican.</b> El
/// generador no usa <c>ProcedureInstanceId</c> en absoluto y usa <c>ReferenceNumber</c> solo para
/// nombrar el archivo, así que el flujo standalone le pasa <see cref="Guid.Empty"/> —el mismo
/// convenio «sin instancia» del preview de RUNT— y una referencia sintética. Consecuencia buscada:
/// el certificado standalone NO puede divergir del del expediente, porque es el mismo generador.</para>
/// </summary>
internal sealed class StandaloneRuesCertificateRenderer : IStandaloneRuesCertificateRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRuesCertificateGenerator _generator;

    public StandaloneRuesCertificateRenderer(IRuesCertificateGenerator generator)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    public RenderedStandaloneDocument Render(
        IReadOnlyDictionary<string, string?> ruesFields,
        string referenceNumber)
    {
        ArgumentNullException.ThrowIfNull(ruesFields);

        var data = new RuesCertificateData(
            // Sin trámite: el generador ignora este identificador (no lo lee en ninguna parte).
            Guid.Empty,
            referenceNumber,
            RazonSocial: Val(ruesFields, "rues_razon_social") ?? string.Empty,
            Nit: Val(ruesFields, "rues_nit") ?? string.Empty,
            Estado: Val(ruesFields, "rues_estado") ?? string.Empty,
            MatriculaMercantil: Val(ruesFields, "rues_matricula_mercantil"),
            CamaraComercio: Val(ruesFields, "rues_camara_comercio"),
            Sigla: Val(ruesFields, "rues_sigla"),
            FechaMatricula: Val(ruesFields, "rues_fecha_matricula"),
            UltimoAnoRenovado: Val(ruesFields, "rues_ultimo_ano_renovado"),
            FechaRenovacion: Val(ruesFields, "rues_fecha_renovacion"),
            Direccion: Val(ruesFields, "rues_direccion"),
            Municipio: Val(ruesFields, "rues_municipio"),
            Categoria: Val(ruesFields, "rues_categoria"),
            ActividadEconomica: Val(ruesFields, "rues_actividad_economica"),
            TipoOrganizacion: Val(ruesFields, "rues_tipo_organizacion"),
            TipoCompania: Val(ruesFields, "rues_tipo_compania"),
            Email: Val(ruesFields, "rues_email"),
            IdRm: Val(ruesFields, "rues_id_rm"),
            FechaActualizacion: Val(ruesFields, "rues_fecha_actualizacion"),
            RazonCancelacion: Val(ruesFields, "rues_razon_cancelacion"),
            RepresentacionLegal: Val(ruesFields, "rues_representacion_legal"),
            Actividades: ParseActividades(Val(ruesFields, "rues_actividades_json")),
            CamaraCiudad: Val(ruesFields, "rues_camara_ciudad"),
            CamaraDepartamento: Val(ruesFields, "rues_camara_departamento"));

        var doc = _generator.GenerateRuesCertificate(data);

        return new RenderedStandaloneDocument(doc.Filename, doc.Mimetype, doc.Content);
    }

    private static string? Val(IReadOnlyDictionary<string, string?> fields, string key) =>
        fields.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    /// <summary>
    /// Actividades económicas persistidas como JSON compacto (mismo formato que en el expediente).
    /// Ausente o ilegible ⇒ <c>null</c>: la sección se pinta vacía, no se rompe la generación.
    /// </summary>
    private static List<RuesActividad>? ParseActividades(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<RuesActividadDto>>(json, JsonOptions);
            return items?.Select(a => new RuesActividad(a.Codigo, a.Nombre, a.Descripcion)).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RuesActividadDto(string? Codigo, string? Nombre, string? Descripcion);
}
