using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Datos de demostración para previsualizar plantillas de mandato (Plataforma → Mandatos).
/// Usa marcadores visibles (no datos inventados de personas) para mandante/mandatario.
/// </summary>
public static class MandatoPreviewSample
{
    // Marcadores en mayúsculas: el generador los pinta en negrita vía MandatoKeywords.
    public const string PhRlNombre = "[ACÁ VA EL NOMBRE DEL REPRESENTANTE LEGAL]";
    public const string PhRlDocumento = "[ACÁ VA EL DOCUMENTO DEL REPRESENTANTE LEGAL]";
    public const string PhRazonSocial = "[ACÁ VA LA RAZÓN SOCIAL DEL MANDANTE]";
    public const string PhNit = "[ACÁ VA EL NIT DEL MANDANTE]";
    public const string PhPnNombre = "[ACÁ VA EL NOMBRE DEL MANDANTE]";
    public const string PhPnDocumento = "[ACÁ VA EL DOCUMENTO DEL MANDANTE]";
    public const string PhMandatarioNombre = "[ACÁ VA EL NOMBRE DEL MANDATARIO]";
    public const string PhMandatarioDocumento = "[ACÁ VA LA CÉDULA DEL MANDATARIO]";
    public const string PhPlaca = "[ACÁ VA LA PLACA]";
    public const string PhCamara = "[ACÁ VA LA CIUDAD DE LA CÁMARA]";

    public static MandatoData Build(string templateCode)
    {
        var normalized = (templateCode ?? string.Empty).Trim().ToLowerInvariant();
        var (officeCode, officeName, city) = normalized switch
        {
            MandatoTemplateResolver.Sabaneta => ("5631000", "STRIA MOVILIDAD SABANETA", "Sabaneta"),
            MandatoTemplateResolver.Bello => ("5088000", "STRIA MOVILIDAD BELLO", "Bello"),
            _ => ("05001000", "SECRETARIA DE MOVILIDAD DE MEDELLIN", "Medellín"),
        };

        // Preview en PJ para mostrar el bloque completo (RL + razón social + NIT) con marcadores.
        var mandante = new DocumentParte(
            "vendedor",
            PhRazonSocial,
            PhNit,
            null,
            "NIT",
            null,
            EsJuridica: true,
            RepresentanteLegalNombre: PhRlNombre,
            RepresentanteLegalTipoDoc: "CC",
            RepresentanteLegalDocumento: PhRlDocumento);

        var tramite = new FurDocumentData(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "PREV-MANDATO",
            Modalidad: "traspaso",
            TipologiaCodigo: "traspaso_standard",
            Vehiculo: new VehiculoDatos(
                null, null, null, null, null, null, null, null, PhPlaca),
            Organismo: new OrganismoTransito(officeCode, officeName, city),
            Partes: [mandante],
            ValorVenta: null,
            Causal: null,
            SellosFirma: [],
            FechaTramite: DateTime.UtcNow);

        var firmante = new MandatarioFirmante(PhMandatarioNombre, PhMandatarioDocumento);

        return normalized switch
        {
            MandatoTemplateResolver.Sabaneta => new MandatoData(
                tramite,
                MandatoTemplateResolver.Sabaneta,
                "UNION TEMPORAL SERVICIOS ESPECIALIZADOS DE TRANSITO Y TRANSPORTE DE SABANETA SETSA",
                "900273813-7",
                firmante,
                MandatoFamilia.OrganismoTransito,
                PhCamara,
                "UT-SETSA",
                ModoFirmaMandatario: MandatarioFirmaModo.SinBloque),

            MandatoTemplateResolver.Bello => new MandatoData(
                tramite,
                MandatoTemplateResolver.Bello,
                "UNION TEMPORAL MOVILIDAD AVANZADA DE BELLO MAB",
                "901783814-6",
                firmante,
                MandatoFamilia.OrganismoTransito,
                PhCamara,
                null,
                ModoFirmaMandatario: MandatarioFirmaModo.Manual),

            _ => new MandatoData(
                tramite,
                MandatoTemplateResolver.Generico,
                null,
                null,
                firmante,
                MandatoFamilia.Individuo,
                PhCamara,
                null,
                ModoFirmaMandatario: MandatarioFirmaModo.Estampada),
        };
    }
}
