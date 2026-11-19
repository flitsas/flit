import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import CentroMensajes from "@/app/(app)/mensajes/page";
import Productividad from "@/app/(app)/reportes/productividad/page";
import { ToastProvider } from "@/components/Toast";

// Rediseño S1: Mensajes y Productividad vuelven del detalle del comparendo como estaban. La API está simulada.
let qs = "";
const replace = vi.fn();
const push = vi.fn();
// Como en Next, el router es el mismo objeto en cada render.
// Y como en el navegador, `replace` cambia la URL de la página (sin eso, la pantalla creería que ya está escrita).
const router = {
  replace: (...a: unknown[]) => {
    replace(...a);
    window.history.replaceState(null, "", String(a[0]));
  },
  push: (...a: unknown[]) => push(...a),
};
vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(qs),
  useRouter: () => router,
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
    if (ruta.startsWith("/mensajes/")) return Promise.resolve({ id: "m1", asunto: "Aviso", tipo: "Aviso1", estado: "Enviado", creadoEn: "2026-11-01T10:00:00Z", destinatarios: null, comparendoId: "c7", adjuntos: [], intentos: [], aperturas: 0, html: null });
    if (ruta === "/reportes/productividad") return Promise.resolve({ filas: [{ usuarioId: "u1", usuario: "Ana Gestora", gestionados: 1, dpEnviados: 0, contraventoresAsignados: 0, pagos: 2, avisosManuales: 0, ocrRegistrados: 0, respuestasOt: 0 }] });
    if (ruta === "/reportes/productividad/detalle") return Promise.resolve([{ id: "c9", numero: "0508800009", placa: "ABC123", fechaImposicion: null, valorTotal: 1000, acciones: [] }]);
    return Promise.resolve([]);
  });
});
afterEach(() => {
  cleanup();
  apiFetch.mockReset();
  replace.mockReset();
  push.mockReset();
  window.history.replaceState(null, "", "/");
});

const volverDe = () => decodeURIComponent(new URL(String(push.mock.calls.at(-1)?.[0]), "http://x").searchParams.get("volver") ?? "");

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

describe("Centro de mensajes: volver y escritura", () => {
  it("«Ver el comparendo» vuelve al Centro con sus filtros y el mensaje abierto", async () => {
    qs = "tipo=Aviso1&mensaje=m1";
    await act(async () => render(<ToastProvider><CentroMensajes /></ToastProvider>));
    const ver = await screen.findByRole("button", { name: "Ver el comparendo" });
    await act(async () => ver.click());
    expect(String(push.mock.calls.at(-1)?.[0])).toMatch(/^\/comparendos\/c7\?/);
    expect(volverDe()).toBe("/mensajes?tipo=Aviso1&mensaje=m1");
  });

  it("la placa se escribe en la URL al dejar de teclear, no letra por letra", async () => {
    await act(async () => render(<ToastProvider><CentroMensajes /></ToastProvider>));
    const placa = screen.getByLabelText("Placa");
    for (const v of ["A", "AB", "ABC"]) {
      await act(async () => {
        fireEvent.change(placa, { target: { value: v } });
      });
    }
    expect(replace.mock.calls.some(([u]) => String(u).includes("placa=A&") || String(u).endsWith("placa=A"))).toBe(false);
    await waitFor(() => expect(replace).toHaveBeenLastCalledWith("/mensajes?placa=ABC", { scroll: false }));
    expect(replace.mock.calls.filter(([u]) => String(u).includes("placa=")).length).toBe(1);
  });

  it("pasar de Placa a Comparendo antes de que termine la espera no descarta la placa", async () => {
    await act(async () => render(<ToastProvider><CentroMensajes /></ToastProvider>));
    await act(async () => {
      fireEvent.change(screen.getByLabelText("Placa"), { target: { value: "ABC123" } });
      fireEvent.change(screen.getByLabelText("Comparendo"), { target: { value: "0508" } });
    });
    await waitFor(() => expect(replace).toHaveBeenLastCalledWith("/mensajes?comparendo=0508&placa=ABC123", { scroll: false }));
  });

  it("una ?empresa= puesta a mano no viaja para quien no es SuperAdmin", async () => {
    qs = "empresa=otra";
    await act(async () => render(<ToastProvider><CentroMensajes /></ToastProvider>));
    await waitFor(() => expect(llamadas("/mensajes").length).toBeGreaterThan(0));
    expect(llamadas("/mensajes").every((q) => q.empresa === undefined)).toBe(true);
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

  it("ver un comparendo de la cifra vuelve con el periodo y la cifra abierta", async () => {
    qs = "desde=2026-10-01&hasta=2026-10-31&usuario=u1&metrica=pagos";
    await act(async () => render(<ToastProvider><Productividad /></ToastProvider>));
    const fila = await screen.findByText("0508800009");
    await act(async () => fila.click());
    expect(String(push.mock.calls.at(-1)?.[0])).toMatch(/^\/comparendos\/c9\?/);
    expect(volverDe()).toBe("/reportes/productividad?desde=2026-10-01&hasta=2026-10-31&usuario=u1&metrica=pagos");
    // No se cierra la cifra antes de navegar: ningún replace la quita de la URL.
    expect(replace.mock.calls.some(([u]) => !String(u).includes("metrica=pagos"))).toBe(false);
  });

  it("una ?empresa= puesta a mano no viaja para quien no es SuperAdmin", async () => {
    qs = "empresa=otra";
    await act(async () => render(<ToastProvider><Productividad /></ToastProvider>));
    await waitFor(() => expect(llamadas("/reportes/productividad").length).toBeGreaterThan(0));
    expect(llamadas("/reportes/productividad").every((q) => q.empresa == null)).toBe(true);
  });

  it("consultar otro periodo cierra la cifra abierta", async () => {
    qs = "desde=2026-10-01&hasta=2026-10-31&usuario=u1&metrica=pagos";
    await act(async () => render(<ToastProvider><Productividad /></ToastProvider>));
    await screen.findByText("Pagos registrados · Ana Gestora");
    // La cifra abre un modal encima: se cierra con su X para llegar a Consultar, como haría la gestora.
    await act(async () => screen.getByRole("button", { name: "Consultar" }).click());
    await waitFor(() => expect(String(replace.mock.calls.at(-1)?.[0])).not.toContain("metrica="));
  });

  it("una cifra cuyo usuario no está en el periodo no queda en la URL", async () => {
    qs = "usuario=fantasma&metrica=pagos";
    await act(async () => render(<ToastProvider><Productividad /></ToastProvider>));
    await waitFor(() => expect(llamadas("/reportes/productividad").length).toBeGreaterThan(0));
    await waitFor(() => expect(replace).toHaveBeenLastCalledWith("/reportes/productividad", { scroll: false }));
  });

  it("abrir una cifra la deja en la URL", async () => {
    await act(async () => render(<ToastProvider><Productividad /></ToastProvider>));
    const cifra = await screen.findByRole("button", { name: "2" });
    await act(async () => cifra.click());
    expect(replace).toHaveBeenLastCalledWith("/reportes/productividad?usuario=u1&metrica=pagos", { scroll: false });
  });
});
