using System;
using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Catalog;

/// <summary>
/// Catálogo de reglas condicionales de obligatoriedad documental por tipología (HU #10521,
/// RF33/34/35/37/38/39). Fuente de verdad en código. Cada regla exige un documento solo bajo
/// su condición (PN/NIT, leasing, tramitador, carrocería, servicio especial), de modo que ningún
/// documento se pide cuando no aplica ni se omite cuando sí.
/// </summary>
public static class ConditionalDocumentRules
{
    private static ChecklistItem Item(string id, string label, bool obligatorio, string docTipo, string? ayuda = null)
        => new(id, label, obligatorio, docTipo, ayuda);

    // Reglas compartidas por matrícula y traspaso (aplican por atributo, no por tipología).
    private static IEnumerable<ConditionalRule> Comunes()
    {
        // La prenda (RF37) NO se resuelve aquí: vive en su propio agregado (ProcedureInstancePrenda /
        // PrendaGate, Feature #10585), que exige su documento de soporte en la radicación del traspaso.

        // RF39 — poderes de tramitador solo si hay processor activo.
        yield return new ConditionalRule("tramitador_poder", c => c.TieneTramitador, ConditionalEffect.Require,
            Item("poder_tramitador", "Poder del tramitador", true, "poder_tramitador"));

        // RF33 (carrocería) — factura de carrocería si hay cambio de carrocería.
        yield return new ConditionalRule("carroceria_factura", c => c.CambioCarroceria, ConditionalEffect.Require,
            Item("factura_carroceria", "Factura de carrocería", true, "factura_carroceria"));

        // RF33 (servicio especial) — anexos de servicio especial (opcional salvo parametrización).
        yield return new ConditionalRule("servicio_especial_anexos", c => c.ServicioEspecial, ConditionalEffect.Add,
            Item("anexos_servicio_especial", "Anexos de servicio especial", false, "anexos_generales"));

        // RF35 — identidad del actor (matrícula y traspaso). El actor NIT ya NO carga Certificado RUES
        // ni documento de identificación: el sistema autogenera el certificado RUES (tipo
        // `certificado_rues`, vía FUR) y lo pega al consolidado, y ese certificado cubre la
        // identificación del NIT. Persona natural: identidad digital (biométrica), sin cédula manual.
        // En ambos casos se oculta la cédula del checklist de carga (para NIT esto retira además el
        // documento de identidad que el traspaso trae en el checklist base).
        yield return new ConditionalRule("nit_sin_cedula", c => c.EsNit, ConditionalEffect.Hide,
            Item("cedulas", "Documento de identidad", false, "cedulas"));
        yield return new ConditionalRule("pn_sin_cedula", c => c.EsPersonaNatural, ConditionalEffect.Hide,
            Item("cedulas", "Documento de identidad", false, "cedulas"));

        // ADR-0036 (HU #10913) — MANDATO autogenerado: aparece en el checklist cuando el trámite lo
        // exige (persona jurídica siempre; persona natural en OT que lo exija, p. ej. Sabaneta). No es
        // carga del cliente: el sistema lo genera (FUR handler) y lo pega al consolidado, por eso es
        // OPCIONAL en el checklist (obligatorio=false, no bloquea la radicación).
        yield return new ConditionalRule("mandato_autogenerado", c => c.ExigeMandato, ConditionalEffect.Add,
            Item("mandato", "Mandato (autogenerado por el sistema)", false, "mandato"));
    }

    /// <summary>Reglas para traspaso.</summary>
    private static IEnumerable<ConditionalRule> Traspaso()
    {
        // RF38 — leasing: contrato (precargado) + declaración de la compañía arrendadora.
        yield return new ConditionalRule("leasing_contrato", c => c.TieneLeasing, ConditionalEffect.Require,
            Item("contrato_leasing", "Contrato de leasing", true, "contrato_leasing"));
        yield return new ConditionalRule("leasing_declaracion", c => c.TieneLeasing, ConditionalEffect.Require,
            Item("declaracion_arrendadora", "Declaración de la compañía arrendadora", true, "declaracion_arrendadora"));
    }

    /// <summary>
    /// Reglas condicionales para una tipología, o lista vacía si la tipología no tiene reglas
    /// (⇒ el checklist queda idéntico al base, sin cambios de comportamiento).
    /// </summary>
    public static IReadOnlyList<ConditionalRule> For(string? codigo) => codigo switch
    {
        // Matrícula inicial no tiene reglas propias: aduana es obligatorio de base (catálogo + matriz).
        TramiteTipologiaCatalog.CodigoMatriculaInicial => Comunes().ToList(),
        TramiteTipologiaCatalog.CodigoTraspasoStandard => Traspaso().Concat(Comunes()).ToList(),
        // Tipología desconocida ⇒ sin reglas (checklist base sin cambios).
        _ => [],
    };
}
