import { describe, expect, it } from "vitest";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { armarPasos, grupoDePendiente, PASOS_BASE, pasoInicial } from "@/lib/tramite/pasos";

const SOPORTADOS = new Set(["vehicle_query", "plate_request", "actor_form", "biometric", "commercial", "prenda_decision", "document_checklist", "generic_form"]);

describe("pasos del wizard desde la definición del tipo", () => {
  it("siempre son los cinco pasos del portal, con los nombres del diseño, sea cual sea el tipo", () => {
    const r = armarPasos(definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]]), SOPORTADOS);

    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.pasos.map((p) => p.titulo)).toEqual(["Diagnóstico", "Actores", "Requisitos", "Resumen", "Envío"]);
    expect(r.pasos.map((p) => p.miga).at(-1)).toBe("Confirmación");
    expect(PASOS_BASE).toHaveLength(5);
  });

  it("cada sección cae en el paso que le corresponde y conserva el paso de la definición que la declara", () => {
    const r = armarPasos(definicionDe([["vehicle_query", "actor_form"], ["commercial", "prenda_decision", "document_checklist"], ["generic_form"]]), SOPORTADOS);

    expect(r.ok).toBe(true);
    if (!r.ok) return;
    const tipos = (i: number) => r.pasos[i].secciones.map((s) => s.sectionType);
    expect(tipos(0)).toEqual(["vehicle_query"]);
    expect(tipos(1)).toEqual(["actor_form"]);
    expect(tipos(2)).toEqual(["commercial", "prenda_decision", "document_checklist", "generic_form"]);
    expect(r.pasos[0].secciones[0].paso?.clave).toBe("p1");
    expect(r.pasos[3].secciones).toEqual([{ clave: "portal:resumen", sectionType: "resumen", paso: null }]);
  });

  it("un tipo sin vehículo que consultar deja el paso Diagnóstico sin secciones, pero el paso sigue estando", () => {
    const r = armarPasos(definicionDe([["actor_form"]]), SOPORTADOS);

    expect(r.ok && r.pasos[0].secciones).toEqual([]);
  });

  it("la firma del FUR no oculta el trámite: la cubre el Resumen y se puede llegar a radicar", () => {
    const r = armarPasos(definicionDe([["vehicle_query"], ["signature_fur"]]), SOPORTADOS);

    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.pasos[0].secciones.map((s) => s.sectionType)).toEqual(["vehicle_query"]);
    expect(r.pasos[3].secciones.map((s) => s.sectionType)).toEqual(["resumen"]);
    expect(r.pasos[4].secciones.map((s) => s.sectionType)).toEqual(["envio"]);
  });

  it("un section_type sin renderer vuelve el tipo no disponible entero, nunca a medias", () => {
    expect(armarPasos(definicionDe([["algo_nuevo"]]), SOPORTADOS)).toEqual({ ok: false, motivo: "seccion_desconocida" });
  });

  it("agrega un paso extra, antes de Resumen, solo si la definición trae una sección que el portal pinta y que no cabe en los cinco", () => {
    const soportados = new Set([...SOPORTADOS, "visita"]);
    const r = armarPasos(definicionDe([["vehicle_query"], ["visita"], ["actor_form"]]), soportados);

    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.pasos.map((p) => p.titulo)).toEqual(["Diagnóstico", "Actores", "Requisitos", "Sección 2", "Resumen", "Envío"]);
    expect(r.pasos[3].id).toBe("extra:p2");
    expect(r.pasos[3].secciones.map((s) => s.sectionType)).toEqual(["visita"]);
    const sinExtra = armarPasos(definicionDe([["vehicle_query"], ["actor_form"]]), SOPORTADOS);
    expect(sinExtra.ok && sinExtra.pasos).toHaveLength(5);
  });

  it("una definición no disponible o sin pasos no arma nada", () => {
    expect(armarPasos(definicionDe([["vehicle_query"]], { disponible: false, motivoNoDisponible: "sin_tarifa" }), SOPORTADOS)).toEqual({ ok: false, motivo: "sin_tarifa" });
    expect(armarPasos(definicionDe([]), SOPORTADOS)).toEqual({ ok: false, motivo: "sin_pasos" });
  });

  it("el código del tipo no decide nada: dos tipos con la misma estructura dan los mismos pasos", () => {
    const a = armarPasos(definicionDe([["vehicle_query"]], { code: "X", name: "Uno" }), SOPORTADOS);
    const b = armarPasos(definicionDe([["vehicle_query"]], { code: "Y", name: "Dos" }), SOPORTADOS);

    expect(a).toEqual(b);
  });
});

describe("dónde se reanuda un borrador", () => {
  const definicion = definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]]);
  const pasos = (() => {
    const r = armarPasos(definicion, SOPORTADOS);
    if (!r.ok) throw new Error("definición de prueba inválida");
    return r.pasos;
  })();

  it("sin diagnóstico guardado empieza en el paso 1", () => {
    expect(pasoInicial(pasos, definicion, solicitudDe())).toBe(0);
  });

  it("con el diagnóstico sin bloqueos pasa al primer paso con pendientes", () => {
    const solicitud = solicitudDe({
      datos: { diagnostico: { fecha: "2026-10-28T15:00:00Z", puedeContinuar: true, requisitos: [] } },
      pendientes: [{ codigo: "actor_requerido", referencia: "vendedor", paso: "p2" }],
    });

    expect(pasoInicial(pasos, definicion, solicitud)).toBe(1);
  });

  it("un diagnóstico con bloqueos no deja pasar", () => {
    const solicitud = solicitudDe({ datos: { diagnostico: { fecha: "2026-10-28T15:00:00Z", puedeContinuar: false, requisitos: [] } } });

    expect(pasoInicial(pasos, definicion, solicitud)).toBe(0);
  });

  it("un trámite ya enviado queda en el último paso", () => {
    expect(pasoInicial(pasos, definicion, solicitudDe({ estado: "en_cola" }))).toBe(4);
  });

  it("los pendientes del back se reparten por paso: actores, requisitos, envío y campos por el paso que los declara", () => {
    expect(grupoDePendiente({ codigo: "actor_requerido", referencia: "vendedor", paso: null }, definicion)).toBe("actores");
    expect(grupoDePendiente({ codigo: "documento_requerido", referencia: "cedula", paso: "p3" }, definicion)).toBe("requisitos");
    expect(grupoDePendiente({ codigo: "comprobante_pago_requerido", referencia: null, paso: null }, definicion)).toBe("envio");
    expect(grupoDePendiente({ codigo: "campo_requerido", referencia: "color", paso: "p1" }, definicion)).toBe("diagnostico");
  });
});

describe("Resumen y Envío no se dan por completos desde un borrador", () => {
  const definicion = definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]]);
  const r = armarPasos(definicion, SOPORTADOS);
  const pasos = r.ok ? r.pasos : [];

  it("con los tres primeros pasos completos reabre en el Resumen, para revisar y confirmar de nuevo", () => {
    const solicitud = solicitudDe({ datos: { diagnostico: { fecha: "2026-10-28T15:00:00Z", puedeContinuar: true, requisitos: [] } } });

    expect(pasoInicial(pasos, definicion, solicitud)).toBe(3);
  });

  it("el requisito de cobro que aún pide el back no atrapa al ciudadano en un paso anterior", () => {
    const solicitud = solicitudDe({
      datos: { diagnostico: { fecha: "2026-10-28T15:00:00Z", puedeContinuar: true, requisitos: [] } },
      pendientes: [{ codigo: "comprobante_pago_requerido", referencia: null, paso: null }],
    });

    expect(pasoInicial(pasos, definicion, solicitud)).toBe(3);
  });
});
