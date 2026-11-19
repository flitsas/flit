import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render } from "@testing-library/react";
import PaginaDetalle from "@/app/(app)/comparendos/[id]/page";

// rex-review S1 🔴1: Escape que cierra un modal no debe sacar del detalle. En el navegador la capa se desmonta entre el
// listener de la trampa (document) y el de la página (window); aquí se simula quitándola en el listener de document.
const push = vi.fn();
vi.mock("next/navigation", () => ({
  useParams: () => ({ id: "c1" }),
  useRouter: () => ({ push }),
  useSearchParams: () => new URLSearchParams("volver=%2Fbandeja%3Fpagado%3Dfalse"),
}));
vi.mock("next/link", () => ({ default: ({ href, children }: { href: string; children: React.ReactNode }) => <a href={href}>{children}</a> }));
vi.mock("@/components/detalle/ContenidoDetalle", () => ({ ContenidoDetalle: () => <div>detalle</div> }));

afterEach(() => {
  cleanup();
  push.mockReset();
  document.body.innerHTML = "";
});

describe("Escape en el detalle del comparendo", () => {
  it("sin capa abierta vuelve a la pantalla de origen con su query", async () => {
    await act(async () => render(<PaginaDetalle />));
    fireEvent.keyDown(document.body, { key: "Escape" });
    expect(push).toHaveBeenCalledWith("/bandeja?pagado=false");
  });

  it("con un modal abierto solo lo cierra, aunque se desmonte antes de que la página escuche", async () => {
    await act(async () => render(<PaginaDetalle />));
    const modal = document.createElement("div");
    modal.setAttribute("role", "dialog");
    document.body.appendChild(modal);
    const cerrar = (e: KeyboardEvent) => {
      if (e.key === "Escape") modal.remove();
    };
    document.addEventListener("keydown", cerrar);
    fireEvent.keyDown(document.body, { key: "Escape" });
    document.removeEventListener("keydown", cerrar);
    expect(modal.isConnected).toBe(false);
    expect(push).not.toHaveBeenCalled();
    // El siguiente Escape, ya sin capa, sí vuelve.
    fireEvent.keyDown(document.body, { key: "Escape" });
    expect(push).toHaveBeenCalledTimes(1);
  });

  it("con el menú de cuenta abierto no sale de la página", async () => {
    await act(async () => render(<PaginaDetalle />));
    const cuenta = document.createElement("div");
    cuenta.id = "flit-suite-account";
    document.body.appendChild(cuenta);
    fireEvent.keyDown(document.body, { key: "Escape" });
    expect(push).not.toHaveBeenCalled();
  });
});
