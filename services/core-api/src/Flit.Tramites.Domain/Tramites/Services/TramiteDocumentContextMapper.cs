using System;
using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
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
/// </list>
/// </summary>
public static class TramiteDocumentContextMapper
{
    private const string NitDocumentType = "NIT";
    private const string OwnerDocumentTypeFieldKey = "owner_document_type";
    private const string VehicleServiceFieldKey = "vehicle_service";
    private const string ServicioEspecialMarker = "ESPECIAL";
    private const string LeasingFieldKey = "es_leasing";
    private const string CambioCarroceriaFieldKey = "cambio_carroceria";

    /// <summary>
    /// Construye el contexto documental a partir de la instancia (con sus colecciones cargadas:
    /// <c>Actors</c>, <c>FieldValues</c>, <c>Participants</c>). Es tolerante a colecciones nulas
    /// o vacías: en ausencia de datos devuelve un contexto sin condiciones (todo <c>false</c>).
    /// </summary>
    public static TramiteDocumentContext From(
        ProcedureInstance instance,
        MandateOtConfig? mandateConfig = null)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var actors = instance.Actors ?? [];
        var fieldValues = instance.FieldValues ?? [];
        var participants = instance.Participants ?? [];

        var ownerDocType = fieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, OwnerDocumentTypeFieldKey, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;

        // NIT / persona natural (RF35 + HU #10542): la fuente canónica es el tipo de persona del actor
        // (ActorPersonTypes), que fija explícitamente natural vs jurídica; se conserva NIT por
        // DocumentType/owner_document_type como señal adicional. Persona natural SOLO cuando el actor lo
        // declara explícitamente (PersonType=natural); sin declaración ⇒ ni PN ni NIT (conservador).
        var esNit =
            actors.Any(a => ActorPersonTypes.IsJuridical(a.PersonType))
            || actors.Any(a => string.Equals(a.DocumentType, NitDocumentType, StringComparison.OrdinalIgnoreCase))
            || string.Equals(ownerDocType, NitDocumentType, StringComparison.OrdinalIgnoreCase);

        var esPersonaNatural = !esNit && actors.Any(a => ActorPersonTypes.IsNatural(a.PersonType));

        var servicioEspecial = fieldValues.Any(f =>
            string.Equals(f.FieldKey, VehicleServiceFieldKey, StringComparison.OrdinalIgnoreCase)
            && f.ValueText is not null
            && f.ValueText.Contains(ServicioEspecialMarker, StringComparison.OrdinalIgnoreCase));

        var tieneTramitador = participants.Any(p =>
            string.Equals(p.Rol, ParticipantRoles.Mandatario, StringComparison.OrdinalIgnoreCase));

        // Banderas manuales del paso de vehículo (persistidas como field_values por el wizard).
        var tieneLeasing = LeerBool(fieldValues, LeasingFieldKey);
        var cambioCarroceria = LeerBool(fieldValues, CambioCarroceriaFieldKey);

        // Producto: el contrato de mandato aplica siempre (persona natural y jurídica).
        const bool exigeMandato = true;

        return new TramiteDocumentContext(
            // Aduana es obligatorio de base en matrícula (catálogo + matriz del gestor); no se
            // condiciona a una bandera del operador, así que EsImportado queda siempre false.
            EsImportado: false,
            EsPersonaNatural: esPersonaNatural,
            EsNit: esNit,
            TieneLeasing: tieneLeasing,
            // La prenda se gestiona en su propio agregado (ProcedureInstancePrenda / PrendaGate,
            // Feature #10585), no desde el checklist condicional; aquí siempre false.
            TienePrenda: false,
            TieneTramitador: tieneTramitador,
            CambioCarroceria: cambioCarroceria,
            ServicioEspecial: servicioEspecial,
            ExigeMandato: exigeMandato);
    }

    private static string? LeerTexto(IEnumerable<ProcedureInstanceFieldValue> fieldValues, string fieldKey) =>
        fieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;

    private static bool LeerBool(IEnumerable<ProcedureInstanceFieldValue> fieldValues, string fieldKey) =>
        string.Equals(LeerTexto(fieldValues, fieldKey), "true", StringComparison.OrdinalIgnoreCase);
}
