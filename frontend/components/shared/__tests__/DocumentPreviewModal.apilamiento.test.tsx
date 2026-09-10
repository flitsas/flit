// La previsualización de un documento la abren superficies que ya están muy arriba en la pila:
// el shell del detalle de trámite (z-1100, `detalle-visual.ts`) y el modal del asistente (z-1150,
// `WizardModal`). Con un z inferior el visor se monta DETRÁS de ellas: el usuario pulsa el ojo, la
// descarga arranca y no ve absolutamente nada. Este test fija esa invariante, que es de las que no
// se notan en una prueba de comportamiento —el modal SÍ está en el DOM, solo que tapado—.
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { DocumentPreviewModal } from "../DocumentPreviewModal";

/** Superficies desde las que hoy se abre el visor; debe quedar por encima de todas. */
const Z_SUPERFICIES_QUE_ABREN_EL_VISOR = [
  { nombre: "shell del detalle de trámite", z: 1100 },
  { nombre: "modal del asistente (wizard)", z: 1150 },
];

function zDelOverlay(): number {
  const overlay = screen.getByRole("dialog").closest("[class*='z-[']");
  const clase = overlay?.className ?? "";
  const match = /z-\[(\d+)\]/.exec(clase);
  expect(match, `no se encontró clase z-[] en: ${clase}`).not.toBeNull();
  return Number(match![1]);
}

describe("DocumentPreviewModal — apilamiento", () => {
  it("se monta por encima de toda superficie desde la que se abre", () => {
    render(
      <DocumentPreviewModal
        open
        onClose={vi.fn()}
        title="consolidado.pdf"
        mimetype="application/pdf"
        url="blob:fake"
        loading={false}
        error={null}
      />,
    );

    const z = zDelOverlay();
    for (const superficie of Z_SUPERFICIES_QUE_ABREN_EL_VISOR) {
      expect(z, `debe quedar sobre el ${superficie.nombre}`).toBeGreaterThan(superficie.z);
    }
  });
});
