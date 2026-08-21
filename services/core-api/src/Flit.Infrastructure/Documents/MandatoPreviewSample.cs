using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Tramites.Catalog;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Datos de demostración para previsualizar plantillas de mandato (Plataforma → Mandatos).
/// Usa marcadores visibles (no datos inventados de personas) para mandante/mandatario.
/// </summary>
public static class MandatoPreviewSample
{
    // Marcadores en mayúsculas: salen en negrita POR CONSTRUCCIÓN, igual que cualquier valor real —
    // fluyen por los mismos accessors (PnNombre, RlNombre, Camara…) que interpolan cada párrafo del
    // generador (MandatoPdfGenerator.MandatoParrafoHandler), no por una lista de palabras clave.
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

    /// <summary>
    /// Ciudad del organismo para la cláusula de cierre. Va como marcador incluso cuando el organismo
    /// es real: el catálogo solo guarda el CÓDIGO DIVIPOLA de la ciudad (<c>city_code</c>), no su
    /// nombre, y el generador descarta el código a propósito (HU #11016) en vez de imprimirlo.
    /// </summary>
    public const string PhCiudadOrganismo = "[ACÁ VA LA CIUDAD DEL ORGANISMO]";

    // Datos de MUESTRA para el simulador: se leen como un contrato impreso, pero llevan la palabra
    // "MUESTRA" incrustada para que nadie confunda una simulación con un documento emitido, y no
    // corresponden a ninguna persona ni a ningún vehículo real.
    public const string MuestraRlNombre = "JUAN MUESTRA RAMÍREZ";
    public const string MuestraRlDocumento = "79000111";
    public const string MuestraRazonSocial = "COMERCIALIZADORA DE MUESTRA S.A.S.";
    public const string MuestraNit = "900111222-3";
    public const string MuestraPnNombre = "MARÍA MUESTRA GÓMEZ";
    public const string MuestraPnDocumento = "52000333";
    public const string MuestraMandatarioNombre = "CARLOS MUESTRA DÍAZ";
    public const string MuestraMandatarioDocumento = "1020000444";
    public const string MuestraPlaca = "MUE123";

    /// <summary>
    /// Muestra para previsualizar una redacción.
    /// </summary>
    /// <param name="templateCode">Redacción a mostrar.</param>
    /// <param name="esJuridica">
    /// Tipo de persona del MANDANTE. En <c>true</c> (default histórico) el bloque sale completo
    /// —representante legal, razón social y NIT—; en <c>false</c> sale la redacción de persona natural.
    /// </param>
    /// <param name="organismo">
    /// Organismo real a nombrar. Nulo ⇒ el organismo de ejemplo asociado a la redacción. Lo usa el
    /// simulador (HU #11706), que simula sobre un OT concreto y no sobre una redacción abstracta.
    /// </param>
    /// <param name="mandatario">Mandatario real (con su firma resuelta). Nulo ⇒ marcadores.</param>
    /// <param name="tipologiaCodigo">
    /// Tipología del trámite simulado. Cambia el OBJETO del contrato: traspaso de propiedad o
    /// matrícula inicial. Nulo ⇒ traspaso (el comportamiento histórico de esta muestra).
    /// </param>
    /// <param name="datosDeMuestra">
    /// En <c>true</c> el mandante y la placa salen con datos ficticios legibles en vez de marcadores
    /// entre corchetes, para juzgar el documento como se verá impreso. Son datos de MUESTRA y así se
    /// anuncian en el propio texto: no corresponden a ninguna persona.
    /// </param>
    public static MandatoData Build(
        string templateCode,
        bool esJuridica = true,
        OrganismoTransito? organismo = null,
        MandatarioFirmante? mandatario = null,
        string? tipologiaCodigo = null,
        bool datosDeMuestra = false)
    {
        var tipologia = string.IsNullOrWhiteSpace(tipologiaCodigo)
            ? TramiteTipologiaCatalog.CodigoTraspasoStandard
            : tipologiaCodigo.Trim();
        var esTraspaso = string.Equals(
            tipologia, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase);

        var normalized = (templateCode ?? string.Empty).Trim().ToLowerInvariant();
        var (officeCode, officeName, city) = normalized switch
        {
            MandatoTemplateResolver.Sabaneta => ("5631000", "STRIA MOVILIDAD SABANETA", "Sabaneta"),
            MandatoTemplateResolver.Bello => ("5088000", "STRIA MOVILIDAD BELLO", "Bello"),
            MandatoTemplateResolver.Municipio => ("5266000", "STRIA TTEyTTO ENVIGADO", "Envigado"),
            _ => ("05001000", "SECRETARIA DE MOVILIDAD DE MEDELLIN", "Medellín"),
        };

        var razonSocial = datosDeMuestra ? MuestraRazonSocial : PhRazonSocial;
        var nit = datosDeMuestra ? MuestraNit : PhNit;
        var rlNombre = datosDeMuestra ? MuestraRlNombre : PhRlNombre;
        var rlDocumento = datosDeMuestra ? MuestraRlDocumento : PhRlDocumento;
        var pnNombre = datosDeMuestra ? MuestraPnNombre : PhPnNombre;
        var pnDocumento = datosDeMuestra ? MuestraPnDocumento : PhPnDocumento;
        var placa = datosDeMuestra ? MuestraPlaca : PhPlaca;

        var mandante = esJuridica
            ? new DocumentParte(
                "vendedor",
                razonSocial,
                nit,
                null,
                "NIT",
                null,
                EsJuridica: true,
                RepresentanteLegalNombre: rlNombre,
                RepresentanteLegalTipoDoc: "CC",
                RepresentanteLegalDocumento: rlDocumento)
            : new DocumentParte(
                "vendedor",
                pnNombre,
                pnDocumento,
                null,
                "CC",
                null,
                EsJuridica: false);

        var tramite = new FurDocumentData(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "PREV-MANDATO",
            Modalidad: esTraspaso ? "traspaso" : "matricula_inicial",
            TipologiaCodigo: tipologia,
            Vehiculo: new VehiculoDatos(
                null, null, null, null, null, null, null, null, placa),
            Organismo: organismo ?? new OrganismoTransito(officeCode, officeName, city),
            Partes: [mandante],
            ValorVenta: null,
            Causal: null,
            SellosFirma: [],
            FechaTramite: DateTime.UtcNow);

        var firmante = mandatario ?? (datosDeMuestra
            ? new MandatarioFirmante(MuestraMandatarioNombre, MuestraMandatarioDocumento)
            : new MandatarioFirmante(PhMandatarioNombre, PhMandatarioDocumento));

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

            MandatoTemplateResolver.Municipio => new MandatoData(
                tramite,
                MandatoTemplateResolver.Municipio,
                null,
                null,
                firmante,
                MandatoFamilia.Individuo,
                PhCamara,
                null,
                ModoFirmaMandatario: MandatarioFirmaModo.Estampada),

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
