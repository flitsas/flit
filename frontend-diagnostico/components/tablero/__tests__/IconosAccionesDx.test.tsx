import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { ReactElement } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { Icono } from "@flit/ui/Icono";
import { MarcaManual } from "@/components/backoffice/GrillaBackoffice";
import { IconoDxCrearTramite } from "@/components/dx/iconos/IconoDxCrearTramite";
import { IconoDxDescargarZip } from "@/components/dx/iconos/IconoDxDescargarZip";
import { IconoDxDictamen } from "@/components/dx/iconos/IconoDxDictamen";
import { IconoDxManual } from "@/components/dx/iconos/IconoDxManual";
import { BarraSeleccionDerivacion, CasillaDerivacion } from "@/components/tablero/BarraSeleccionDerivacion";
import { AccionTramiteFila } from "@/components/tablero/BotonCrearTramite";
import { DescargasLote } from "@/components/tablero/DescargasLote";
import { derivacionDeFila, rotuloMotivo } from "@/lib/tablero/derivacion";
import { ID, V1, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: «Crear trámite» de la fila lleva <IconoDxCrearTramite tamano={16} /> (el mismo del pie del panel), «PDF de
// todos (ZIP)» lleva <IconoDxDescargarZip />, la marca «Manual» del backoffice <IconoDxManual /> y la barra de selección ya
// no usa lucide. HU #14562 · N5 y M7 (reauditoría y auditoría de UI) y m-9; m-8: el motivo de la casilla deshabilitada
// llega como descripción accesible y no solo en `title`; m-10: el visto del dictamen se pinta con la superficie.

afterEach(() => vi.unstubAllGlobals());

const DIR = join(__dirname, "..");
const fuente = (archivo: string) => readFileSync(join(DIR, archivo), "utf8");
/** Trazos del glifo de referencia, para comparar con el svg que pinta el botón. */
const trazos = (icono: ReactElement) => {
  const { container, unmount } = render(icono);
  const html = container.querySelector("svg")!.innerHTML;
  unmount();
  return html;
};

describe("Íconos de acción de Diagnóstico (HU #14562 · N5, M7 y m-9)", () => {
  it("N5: «Crear trámite» de la fila usa IconoDxCrearTramite, el mismo glifo del pie del panel", () => {
    const esperado = trazos(<IconoDxCrearTramite tamano={16} />);
    render(
      <AccionTramiteFila
        diagnosticoId={ID}
        vehiculo={{ ...V1, elegible_tramite: true, motivo_no_elegible: null, tramite: null }}
        tramitesUrl="https://tramites.prueba"
        onCreado={vi.fn()}
        onRefrescar={vi.fn()}
      />,
    );
    const svg = screen.getByRole("button", { name: `Crear trámite para la placa ${V1.placa}` }).querySelector("svg");
    expect(svg).not.toBeNull();
    expect(svg).toHaveAttribute("aria-hidden", "true");
    expect(svg!.innerHTML).toBe(esperado);
  });

  it("N5: BotonCrearTramite ya no pinta FilePlus2 de lucide para crear", () => {
    expect(fuente("BotonCrearTramite.tsx")).not.toMatch(/FilePlus2/);
  });

  it("m-9: «Crear trámites (N)» usa IconoDxCrearTramite y «Seleccionar todos» el check del kit; sin lucide", () => {
    const crear = trazos(<IconoDxCrearTramite tamano={16} />);
    const check = trazos(<Icono nombre="check" className="h-4 w-4" />);
    render(
      <BarraSeleccionDerivacion elegibles={3} seleccionadas={2} enviando={false} onSeleccionarTodas={vi.fn()} onQuitar={vi.fn()} onCrear={vi.fn()} />,
    );
    expect(screen.getByRole("button", { name: "Crear trámites (2)" }).querySelector("svg")!.innerHTML).toBe(crear);
    expect(screen.getByRole("button", { name: "Seleccionar todos los elegibles" }).querySelector("svg")!.innerHTML).toBe(check);
    expect(fuente("BarraSeleccionDerivacion.tsx")).not.toMatch(/lucide-react/);
  });

  it("M7: «PDF de todos (ZIP)» usa IconoDxDescargarZip", () => {
    simularApi({});
    const esperado = trazos(<IconoDxDescargarZip tamano={16} />);
    render(<DescargasLote diagnosticoId={ID} estadoLote="COMPLETADO" puedeDescargar />);
    const svg = screen.getByRole("button", { name: "PDF de todos (ZIP)" }).querySelector("svg");
    expect(svg!.innerHTML).toBe(esperado);
  });

  it("M7: la marca «Manual» del backoffice usa IconoDxManual, decorativo junto al texto", () => {
    const esperado = trazos(<IconoDxManual tamano={16} />);
    const { container } = render(<MarcaManual />);
    expect(container).toHaveTextContent("Manual");
    const svg = container.querySelector("svg");
    expect(svg).toHaveAttribute("aria-hidden", "true");
    expect(svg!.innerHTML).toBe(esperado);
  });
});

describe("CasillaDerivacion: motivo accesible (HU #14562 · m-8)", () => {
  it("la casilla deshabilitada lleva el motivo como descripción accesible, no solo en title", () => {
    const vehiculo = { ...V1, elegible_tramite: false, motivo_no_elegible: "NO_VIABLE", tramite: null };
    render(<CasillaDerivacion vehiculo={vehiculo} derivacion={derivacionDeFila(vehiculo)} marcada={false} onCambiar={vi.fn()} />);
    const casilla = screen.getByRole("checkbox", { name: `Seleccionar la placa ${V1.placa}` });
    expect(casilla).toBeDisabled();
    expect(casilla).toHaveAccessibleDescription(rotuloMotivo("NO_VIABLE"));
    expect(casilla).toHaveAttribute("aria-describedby");
  });

  it("la casilla elegible no tiene descripción de motivo", () => {
    const vehiculo = { ...V1, elegible_tramite: true, motivo_no_elegible: null, tramite: null };
    render(<CasillaDerivacion vehiculo={vehiculo} derivacion={derivacionDeFila(vehiculo)} marcada={false} onCambiar={vi.fn()} />);
    expect(screen.getByRole("checkbox")).not.toHaveAttribute("aria-describedby");
  });
});

describe("IconoDxDictamen (HU #14562 · m-10)", () => {
  it("el visto se traza con la superficie (blanco en claro), no con el tinte", () => {
    const { container } = render(<IconoDxDictamen />);
    const trazosVisto = Array.from(container.querySelectorAll("path")).map((p) => p.getAttribute("style") ?? "");
    expect(trazosVisto.some((s) => s.includes("var(--di-surface-elevated)"))).toBe(true);
    expect(container.innerHTML).not.toContain("--dx-icono-tinte");
  });
});
