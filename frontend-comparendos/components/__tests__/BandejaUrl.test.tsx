import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, render, screen } from "@testing-library/react";
import Bandeja from "@/app/(app)/bandeja/page";
import { ToastProvider } from "@/components/Toast";

// rex-review S1 🟡3: la bandeja se rearma cuando su URL cambia por otra vía (dock, Inicio) y no con su propio eco.
// La API está simulada: nada sale a la red.
let qs = "";
const replace = vi.fn();
vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(qs),
  useRouter: () => ({ replace, push: vi.fn() }),
}));
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({ sesion: { usuario: { id: "u1", esSuperAdmin: false }, permisos: ["comparendos.bandeja.ver"], empresa: { id: "e1", nombre: "Empresa", capacidades: {} } }, cargando: false, error: null }),
}));
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof import("@/lib/api")>()),
  apiFetch: (...a: unknown[]) => apiFetch(...a),
}));

const listas = () => apiFetch.mock.calls.filter(([r]) => r === "/comparendos").length;
const montar = () => render(<ToastProvider><Bandeja /></ToastProvider>);
const navegar = async (nueva: string, vista: ReturnType<typeof montar>) => {
  qs = nueva;
  await act(async () => vista.rerender(<ToastProvider><Bandeja /></ToastProvider>));
};

beforeEach(() => {
  qs = "";
  apiFetch.mockImplementation((ruta: string) => {
    if (ruta === "/comparendos") return Promise.resolve({ items: [], total: 0 });
    if (ruta === "/comparendos/contadores") return Promise.resolve({ estadosLegales: {}, proximosAVencer: 0, sinContraventor: 0, rechazadosOt: 0 });
    return Promise.resolve([]);
  });
});
afterEach(() => {
  cleanup();
  apiFetch.mockReset();
  replace.mockReset();
});

describe("bandeja con su estado en la URL", () => {
  it("tras filtrar, el dock (/bandeja) la rearma limpia, y un enlace con la query vieja la vuelve a filtrar", async () => {
    qs = "pagado=false";
    let vista!: ReturnType<typeof montar>;
    await act(async () => {
      vista = montar();
    });
    expect(await screen.findByText("Sin pago")).toBeInTheDocument();
    await navegar("", vista);
    expect(screen.queryByText("Sin pago")).toBeNull();
    await navegar("pagado=false", vista);
    expect(await screen.findByText("Sin pago")).toBeInTheDocument();
  });

  it("su propio eco no la rearma ni vuelve a pedir la lista", async () => {
    let vista!: ReturnType<typeof montar>;
    await act(async () => {
      vista = montar();
    });
    const chip = await screen.findByRole("button", { name: "Sin gestión" });
    await act(async () => chip.click());
    expect(replace).toHaveBeenLastCalledWith("/bandeja?sinGestion=true", { scroll: false });
    const antes = listas();
    // Lo que la bandeja escribió vuelve como params: es su eco y no debe rearmarla.
    await navegar("sinGestion=true", vista);
    expect(listas()).toBe(antes);
    expect(screen.getByRole("button", { name: "Sin gestión" })).toHaveAttribute("aria-pressed", "true");
    // Y después el dock lleva a /bandeja sin query: la bandeja se rearma limpia (hallazgo del rex-review).
    await navegar("", vista);
    expect(screen.getByRole("button", { name: "Sin gestión" })).toHaveAttribute("aria-pressed", "false");
  });
});
