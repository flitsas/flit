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
///   <item><b>Importado, leasing, prenda, cambio de carrocería</b>: sin campo de captura propio
///   hoy ⇒ <c>false</c> (pendiente de habilitar su captura para que la regla condicional aplique).</item>
/// </list>
/// </summary>
public static class TramiteDocumentContextMapper
{
    private const string NitDocumentType = "NIT";
    private const string OwnerDocumentTypeFieldKey = "owner_document_type";
    private const string VehicleServiceFieldKey = "vehicle_service";
    private const string ServicioEspecialMarker = "ESPECIAL";

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

        return new TramiteDocumentContext(
            EsImportado: false,
            EsPersonaNatural: esPersonaNatural,
            EsNit: esNit,
            TieneLeasing: false,
            TienePrenda: false,
            TieneTramitador: tieneTramitador,
            CambioCarroceria: false,
            ServicioEspecial: servicioEspecial);
    }
}
