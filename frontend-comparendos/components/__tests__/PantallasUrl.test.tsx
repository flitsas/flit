import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import CentroMensajes from "@/app/(app)/mensajes/page";
import Productividad from "@/app/(app)/reportes/productividad/page";
import { ToastProvider } from "@/components/Toast";

// Rediseño S1: Mensajes y Productividad vuelven del detalle del comparendo como estaban. La API está simulada.
let qs = "";
const replace = vi.fn();
vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(qs),
  useRouter: () => ({ replace, push: vi.fn() }),
}));
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({
    sesion: {
      usuario: { id: "u0", esSuperAdmin: false },
      permisos: ["comparendos.mensajes.ver", "comparendos.reportes.productividad", "comparendos.reportes.ver"],
      empresa: { id: "e1", nombre: "Empresa", capacidades: { reportes: true } },
    },
    cargando: false,
    error: null,
  }),
}));
vi.mock("@/components/Pantalla", async (original) => ({
  ...(await original<typeof import("@/components/Pantalla")>()),
  Requiere: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));
vi.mock("@/components/reportes/RequiereReportes", () => ({ RequiereReportes: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof import("@/lib/api")>()),
  apiFetch: (...a: unknown[]) => apiFetch(...a),
}));

const llamadas = (ruta: string) => apiFetch.mock.calls.filter(([r]) => r === ruta).map(([, o]) => (o as { query?: Record<string, unknown> }).query ?? {});

beforeEach(() => {
  qs = "";
  apiFetch.mockImplementation((ruta: string) => {
    if (ruta === "/mensajes") return Promise.resolve({ items: [], total: 0 });
    if (ruta.startsWith("/mensajes/")) return new Promise(() => {});
    if (ruta === "/reportes/productividad") return Promise.resolve({ filas: [{ usuarioId: "u1", usuario: "Ana Gestora", gestionados: 1, dpEnviados: 0, contraventoresAsignados: 0, pagos: 2, avisosManuales: 0, ocrRegistrados: 0, respuestasOt: 0 }] });
    if (ruta === "/reportes/productividad/detalle") return Promise.resolve([]);
    return Promise.resolve([]);
  });
});
afterEach(() => {
  cleanup();
  apiFetch.mockReset();
  replace.mockReset();
});

describe("Centro de mensajes", () => {
  it("al volver restaura los filtros, la página y reabre el mensaje desde la URL", async () => {
    qs = "tipo=Aviso1&placa=ABC123&pagina=2&mensaje=m1";
    await act(async () => render(<ToastProvider><CentroMensajes /></ToastProvider>));
    await waitFor(() => expect(llamadas("/mensajes").at(-1)).toMatchObject({ tipo: "Aviso1", placa: "ABC123", pagina: 2 }));
    expect(screen.getByLabelText("Tipo")).toHaveValue("Aviso1");
    expect(apiFetch.mock.calls.some(([r]) => r === "/mensajes/m1")).toBe(true);
  });

  it("cada filtro queda en la URL con replace", async () => {
    await act(async () => render(<ToastProvider><CentroMensajes /></ToastProvider>));
    const tipo = screen.getByLabelText("Tipo");
    await act(async () => {
      (tipo as HTMLSelectElement).value = "Aviso2";
      tipo.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(replace).toHaveBeenLastCalledWith("/mensajes?tipo=Aviso2", { scroll: false });
  });
});

describe("Productividad", () => {
  it("al volver restaura el periodo y reabre la cifra que estaba abierta", async () => {
    qs = "desde=2026-10-01&hasta=2026-10-31&usuario=u1&metrica=pagos";
    await act(async () => render(<ToastProvider><Productividad /></ToastProvider>));
    await waitFor(() => expect(llamadas("/reportes/productividad").at(-1)).toMatchObject({ desde: "2026-10-01", hasta: "2026-10-31" }));
    await waitFor(() => expect(llamadas("/reportes/productividad/detalle").at(-1)).toMatchObject({ usuarioId: "u1", metrica: "pagos", desde: "2026-10-01" }));
    expect(await screen.findByText("Pagos registrados · Ana Gestora")).toBeInTheDocument();
  });

  it("abrir una cifra la deja en la URL", async () => {
    await act(async () => render(<ToastProvider><Productividad /></ToastProvider>));
    const cifra = await screen.findByRole("button", { name: "2" });
    await act(async () => cifra.click());
    expect(replace).toHaveBeenLastCalledWith("/reportes/productividad?usuario=u1&metrica=pagos", { scroll: false });
  });
});
