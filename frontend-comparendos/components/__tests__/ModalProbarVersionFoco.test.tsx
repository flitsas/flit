import { describe, expect, it } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { Modal } from "@flit/ui";

// Uso de ejemplo: el modal «Probar la versión N» de app/(app)/admin/ocr/prompts/page.tsx abre con un <input type="file"
// className="hidden"> como primer campo y el botón «Elegir archivo de muestra» que lo dispara.
// HU #14562 · N1 (reauditoría de UI): la trampa de foco del kit descartaba solo [hidden] e [inert], así que el foco inicial
// iba al input oculto por CSS, focus() no hacía nada y quedaba en body. Aquí se reproduce la estructura del modal con la
// clase `hidden` de Tailwind servida por una hoja de estilos, para que jsdom calcule display:none igual que el navegador.
// Sin matchers de jest-dom en los tipos de esta app: el foco se compara con document.activeElement.

function ModalProbarVersion() {
  return (
    <Modal
      icono="destello"
      open
      onClose={() => {}}
      size="2xl"
      title="Probar la versión 3"
      description="Se envía un archivo de muestra al modelo con este prompt. No se guarda nada."
    >
      <div className="flex flex-col gap-3">
        <div className="flex items-center gap-3">
          <input type="file" className="hidden" data-testid="archivo" accept=".jpg,.jpeg,.png,.tif,.tiff,.pdf" />
          <button type="button">Elegir archivo de muestra</button>
          <span className="text-xs opacity-70">JPG, PNG, TIFF o PDF de hasta 20 MB.</span>
        </div>
      </div>
    </Modal>
  );
}

function conHojaTailwind() {
  const hoja = document.createElement("style");
  hoja.textContent = ".hidden { display: none; }";
  document.head.appendChild(hoja);
  return () => hoja.remove();
}

describe("Modal «Probar la versión» de OCR con el input de archivo oculto (HU #14562 · N1)", () => {
  it("el foco inicial no se pierde en body: queda en el título del modal", () => {
    const quitar = conHojaTailwind();
    try {
      render(<ModalProbarVersion />);
      expect(document.activeElement).not.toBe(screen.getByTestId("archivo"));
      expect(document.activeElement).not.toBe(document.body);
      expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Probar la versión 3" }));
    } finally {
      quitar();
    }
  });

  it("Tab y Mayús+Tab circulan entre los controles visibles sin pasar por el input oculto", () => {
    const quitar = conHojaTailwind();
    try {
      render(<ModalProbarVersion />);
      const elegir = screen.getByRole("button", { name: "Elegir archivo de muestra" });
      const cerrar = screen.getByRole("button", { name: "Cerrar" });
      elegir.focus();
      fireEvent.keyDown(elegir, { key: "Tab" });
      fireEvent.keyDown(document.activeElement!, { key: "Tab", shiftKey: true });
      expect(document.activeElement === elegir || document.activeElement === cerrar).toBe(true);
      expect(document.activeElement).not.toBe(screen.getByTestId("archivo"));
      // Mayús+Tab desde el título (antes del primer control) va al último control visible.
      screen.getByRole("heading", { name: "Probar la versión 3" }).focus();
      fireEvent.keyDown(document.activeElement!, { key: "Tab", shiftKey: true });
      expect(document.activeElement).not.toBe(screen.getByTestId("archivo"));
      expect(document.activeElement).not.toBe(document.body);
    } finally {
      quitar();
    }
  });
});
