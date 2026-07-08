using System;
using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Deriva el <see cref="TramiteDocumentContext"/> (RF30) desde los datos persistidos de la
/// instancia de trámite: actores, valores de campo (RUNT), adjuntos y participantes. Puro y
/// testeable. Cada atributo se resuelve solo desde una fuente concreta; los que aún no tienen
/// captura en el modelo de datos quedan en <c>false</c> (no fuerzan documentos):
/// <list type="bullet">
///   <item><b>NIT / persona natural</b> (RF35): <c>procedure_instance_actors.document_type</c>
///   (o el campo <c>owner_document_type</c>). Cualquier actor con documento <c>NIT</c> ⇒ NIT;
///   si hay actores y ninguno es NIT ⇒ persona natural.</item>
///   <item><b>Servicio especial</b> (RF33): campo <c>vehicle_service</c> del RUNT que contiene
///   <c>ESPECIAL</c>.</item>
///   <item><b>Tramitador</b> (RF39): un participante con rol <c>mandatario</c>.</item>
///   <item><b>Leasing</b> (RF38) y <b>cambio de carrocería</b> (RF33): banderas manuales del
///   operador persistidas como field_values <c>es_leasing</c> / <c>cambio_carroceria</c>
///   (mismo canal que <c>vehicle_service</c>); en producción v1.0 son un checkbox/selector del
///   paso de vehículo.</item>
///   <item><b>Prenda</b> (RF37): field_value <c>accion_prenda</c> (<c>registrar|levantar|omitir</c>);
///   solo <c>registrar</c> exige los documentos de prenda. La presencia automática desde la
///   consulta RUNT (<c>guaranteeMobiliary</c>) queda pendiente de que el DTO de consulta la pueble.</item>
///   <item><b>Importado</b> (RF33): bandera manual del operador persistida como field_value
///   <c>es_importado</c> (checkbox del paso de vehículo en matrícula inicial); dispara pedir el
///   documento de Aduana.</item>
/// </list>
/// </summary>
public static class TramiteDocumentContextMapper
{
    private const string NitDocumentType = "NIT";
    private const string OwnerDocumentTypeFieldKey = "owner_document_type";
    private const string VehicleServiceFieldKey = "vehicle_service";
    private const string ServicioEspecialMarker = "ESPECIAL";
    private const string ImportadoFieldKey = "es_importado";
    private const string LeasingFieldKey = "es_leasing";
    private const string CambioCarroceriaFieldKey = "cambio_carroceria";
    private const string AccionPrendaFieldKey = "accion_prenda";
    private const string AccionPrendaRegistrar = "registrar";

    /// <summary>
    /// Construye el contexto documental a partir de la instancia (con sus colecciones cargadas:
    /// <c>Actors</c>, <c>FieldValues</c>, <c>Participants</c>). Es tolerante a colecciones nulas
    /// o vacías: en ausencia de datos devuelve un contexto sin condiciones (todo <c>false</c>).
    /// </summary>
    public static TramiteDocumentContext From(ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var actors = instance.Actors ?? [];
        var fieldValues = instance.FieldValues ?? [];
        var participants = instance.Participants ?? [];

        var ownerDocType = fieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, OwnerDocumentTypeFieldKey, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;

        var esNit =
            actors.Any(a => string.Equals(a.DocumentType, NitDocumentType, StringComparison.OrdinalIgnoreCase))
            || string.Equals(ownerDocType, NitDocumentType, StringComparison.OrdinalIgnoreCase);

        // Persona natural: hay identificación de actor y ninguna es NIT.
        var tieneIdentificacion = actors.Count > 0 || !string.IsNullOrWhiteSpace(ownerDocType);
        var esPersonaNatural = !esNit && tieneIdentificacion;

        var servicioEspecial = fieldValues.Any(f =>
            string.Equals(f.FieldKey, VehicleServiceFieldKey, StringComparison.OrdinalIgnoreCase)
            && f.ValueText is not null
            && f.ValueText.Contains(ServicioEspecialMarker, StringComparison.OrdinalIgnoreCase));

        var tieneTramitador = participants.Any(p =>
            string.Equals(p.Rol, ParticipantRoles.Mandatario, StringComparison.OrdinalIgnoreCase));

        // Banderas manuales del paso de vehículo (persistidas como field_values por el wizard).
        var esImportado = LeerBool(fieldValues, ImportadoFieldKey);
        var tieneLeasing = LeerBool(fieldValues, LeasingFieldKey);
        var cambioCarroceria = LeerBool(fieldValues, CambioCarroceriaFieldKey);
        // Prenda: solo "registrar" exige inscripción/paz y salvo; "levantar"/"omitir" no.
        var accionPrenda = LeerTexto(fieldValues, AccionPrendaFieldKey);
        var tienePrenda = string.Equals(accionPrenda, AccionPrendaRegistrar, StringComparison.OrdinalIgnoreCase);

        return new TramiteDocumentContext(
            EsImportado: esImportado,
            EsPersonaNatural: esPersonaNatural,
            EsNit: esNit,
            TieneLeasing: tieneLeasing,
            TienePrenda: tienePrenda,
            TieneTramitador: tieneTramitador,
            CambioCarroceria: cambioCarroceria,
            ServicioEspecial: servicioEspecial);
    }

    private static string? LeerTexto(IEnumerable<ProcedureInstanceFieldValue> fieldValues, string fieldKey) =>
        fieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;

    private static bool LeerBool(IEnumerable<ProcedureInstanceFieldValue> fieldValues, string fieldKey) =>
        string.Equals(LeerTexto(fieldValues, fieldKey), "true", StringComparison.OrdinalIgnoreCase);
}
