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

        // ADR-0036 — MANDATO autogenerado: aparece en el checklist cuando ExigeMandato (siempre PN/PJ).
        // No es carga del cliente: el sistema lo genera (FUR handler) y lo pega al consolidado, por eso es
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
    /// Reglas de la cancelación de matrícula: los documentos que acredita CADA causal.
    ///
    /// <para>Una cancelación por decisión judicial se prueba con el acto del juez; una pérdida total
    /// —por fuerza mayor o por accidente— con los tres certificados a la vez (DIJIN o Policía,
    /// aseguradora o perito, autoridad administrativa); y una decisión voluntaria con el certificado
    /// de la DIJIN. Todos los de la causal son obligatorios: no basta uno cualquiera.</para>
    ///
    /// <para>Los documentos de las OTRAS causales se ocultan en cuanto hay una declarada, para que el
    /// gestor no vea como opcional un certificado que su trámite no usa. Sin causal declarada no se
    /// exige ni se oculta nada: el checklist queda como estaba y el paso no deja continuar hasta que
    /// el gestor elija, que es donde corresponde pedírselo.</para>
    ///
    /// <para>El certificado de tradición NO sale de aquí: es obligatorio de base en las cuatro
    /// causales y lo pone el catálogo.</para>
    /// </summary>
    private static IEnumerable<ConditionalRule> Cancelacion()
    {
        foreach (var docTipo in CancelacionCausales.TodosLosDocumentos)
        {
            var doc = docTipo;
            var item = Item(doc, CancelacionCausales.EtiquetaDocumento(doc), true, doc);

            yield return new ConditionalRule(
                $"cancelacion_{doc}",
                c => CancelacionCausales.Exige(c.CancelacionCausal, doc),
                ConditionalEffect.Require,
                item);

            yield return new ConditionalRule(
                $"cancelacion_{doc}_no_aplica",
                c => c.CancelacionCausal != CancelacionCausal.Ninguna
                    && !CancelacionCausales.Exige(c.CancelacionCausal, doc),
                ConditionalEffect.Hide,
                item);
        }
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
        // Cancelación de matrícula: SOLO sus reglas de causal, sin `Comunes()`. Ningún tipo de la
        // familia OTROS aplicaba condicionales hasta ahora, así que encenderle de paso mandato,
        // cédulas y poderes sería un cambio de comportamiento que nadie pidió — y que además tocaría
        // por igual a los otros doce tipos el día que se sumen.
        CancelacionCausales.TipoCodigo => Cancelacion().ToList(),
        // Tipología desconocida ⇒ sin reglas (checklist base sin cambios).
        _ => [],
    };
}
