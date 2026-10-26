import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import Inicio from "@/app/(app)/page";
import { sesion } from "@/__tests__/configuracion/datos";
import { simularApi } from "@/components/express/__tests__/datos";

// Uso de ejemplo: con la capacidad crearDiagnosticos el inicio ofrece «Nuevo diagnóstico (lote)», que lleva a
// /diagnosticos/nuevo (cabecera del lote) y de ahí a /diagnosticos/{id}/carga con la plantilla Excel (HU #13754).

vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }),
}));

afterEach(() => vi.unstubAllGlobals());

function montarInicio(crearDiagnosticos: boolean) {
  simularApi({ "GET /sesion": () => Response.json(sesion({ crearDiagnosticos, verDiagnosticos: true })) });
  render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
      <Inicio />
    </DiagnosticoShell>,
  );
}

describe("Inicio — entrada a la carga masiva (HU #13754)", () => {
  it("con crearDiagnosticos muestra «Nuevo diagnóstico (lote)» hacia /diagnosticos/nuevo", async () => {
    montarInicio(true);
    const enlace = await screen.findByRole("link", { name: "Nuevo diagnóstico (lote)" });
    expect(enlace).toHaveAttribute("href", "/diagnosticos/nuevo");
    const bloque = screen.getByRole("heading", { name: "Diagnóstico por lote" }).closest("section");
    expect(bloque).toHaveTextContent(/varias placas o VIN/i);
    expect(bloque).toHaveTextContent(/plantilla Excel/i);
  });

  it("el enlace tiene foco visible (outline del dock) para teclado", async () => {
    montarInicio(true);
    const enlace = await screen.findByRole("link", { name: "Nuevo diagnóstico (lote)" });
    expect(enlace.className).toContain("focus-visible:outline");
  });

  it("sin crearDiagnosticos no muestra la entrada ni ningún enlace a /diagnosticos/nuevo", async () => {
    montarInicio(false);
    await screen.findByRole("heading", { level: 1, name: "Diagnóstico vehicular" });
    expect(screen.queryByRole("link", { name: "Nuevo diagnóstico (lote)" })).not.toBeInTheDocument();
    expect(document.querySelector('a[href="/diagnosticos/nuevo"]')).toBeNull();
  });
});
