import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import Inicio from "@/app/(app)/page";
import { sesion } from "@/__tests__/configuracion/datos";

// Uso de ejemplo (HU #14085): la tarjeta del título del inicio trae «Placa o VIN» + «Diagnosticar», que valida y abre
// /diagnosticos/nuevo en Express con la placa precargada, y «Carga masiva», que la abre en Masivo. El listado sigue debajo.

const router = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
  useRouter: () => router,
}));

beforeEach(() => router.push.mockReset());
afterEach(() => vi.unstubAllGlobals());

function montarInicio(crearDiagnosticos: boolean) {
  // GET /sesion y el listado del inicio (GET /diagnosticos?…, HU #13852) vacío.
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) =>
      String(url).includes("/diagnosticos")
        ? Response.json({ pagina: 1, tamano: 10, total: 0, diagnosticos: [] })
        : Response.json(sesion({ crearDiagnosticos, verDiagnosticos: true })),
    ),
  );
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
      <Inicio />
    </DiagnosticoShell>,
  );
}

const tarjetaTitulo = async () => (await screen.findByRole("heading", { level: 1, name: "Diagnóstico vehicular" })).closest("section") as HTMLElement;

describe("Inicio — entrada a la creación desde la tarjeta del título (AC6)", () => {
  it("la tarjeta del título trae «Placa o VIN», «Diagnosticar» y «Carga masiva»", async () => {
    montarInicio(true);
    const tarjeta = await tarjetaTitulo();
    expect(tarjeta).toContainElement(await screen.findByRole("textbox", { name: "Placa o VIN" }));
    expect(tarjeta).toContainElement(screen.getByRole("button", { name: "Diagnosticar" }));
    expect(tarjeta).toContainElement(screen.getByRole("link", { name: "Carga masiva" }));
  });

  it("con una placa válida, «Diagnosticar» abre /diagnosticos/nuevo en Express con la placa precargada", async () => {
    montarInicio(true);
    fireEvent.change(await screen.findByRole("textbox", { name: "Placa o VIN" }), { target: { value: "tst 123" } });
    fireEvent.click(screen.getByRole("button", { name: "Diagnosticar" }));
    expect(router.push).toHaveBeenCalledWith("/diagnosticos/nuevo?modo=express&placa=TST123");
  });

  it("«Carga masiva» abre /diagnosticos/nuevo en Masivo, con foco visible", async () => {
    montarInicio(true);
    const enlace = await screen.findByRole("link", { name: "Carga masiva" });
    expect(enlace).toHaveAttribute("href", "/diagnosticos/nuevo?modo=masivo");
    expect(enlace.className).toContain("focus-visible:outline");
  });

  it("ya no hay tarjetas separadas «Diagnóstico Express» ni «Diagnóstico por lote»", async () => {
    montarInicio(true);
    await screen.findByRole("textbox", { name: "Placa o VIN" });
    expect(screen.queryByRole("heading", { name: "Diagnóstico Express" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Diagnóstico por lote" })).not.toBeInTheDocument();
  });

  it("sin crearDiagnosticos no aparecen la captura ni «Carga masiva»; el listado sigue debajo", async () => {
    montarInicio(false);
    await tarjetaTitulo();
    expect(screen.queryByRole("textbox", { name: "Placa o VIN" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Carga masiva" })).not.toBeInTheDocument();
    expect(document.querySelector('a[href^="/diagnosticos/nuevo"]')).toBeNull();
    expect(await screen.findByText("Todavía no hay diagnósticos")).toBeInTheDocument();
  });
});
