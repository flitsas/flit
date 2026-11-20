import { act, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ToastProvider, useToast } from "@/components/Toast";

// Responsive iOS (S3): la ✕ del toast mide 14 px; en táctil necesita al menos 40×40 sin cambiar cómo se ve ni su foco.
function Disparar() {
  const { show } = useToast();
  return (
    <button type="button" onClick={() => show("Guardado")}>
      disparar
    </button>
  );
}

describe("Toast: área táctil del cierre", () => {
  it("la ✕ conserva su nombre y extiende el toque a 40×40 con un ::after centrado", () => {
    render(
      <ToastProvider>
        <Disparar />
      </ToastProvider>,
    );
    act(() => screen.getByText("disparar").click());
    const cerrar = screen.getByRole("button", { name: "Cerrar notificación" });
    const clases = cerrar.className.split(/\s+/);
    for (const c of ["relative", "after:absolute", "after:size-10", "after:left-1/2", "after:top-1/2", "after:-translate-x-1/2", "after:-translate-y-1/2", "after:content-['']"]) {
      expect(clases, c).toContain(c);
    }
    // Sin padding ni tamaño propio: el botón (y su anillo de foco) mide lo mismo que el ícono.
    expect(clases.some((c) => /^(p|px|py|size|w|h|min-w|min-h)-/.test(c))).toBe(false);
  });
});
