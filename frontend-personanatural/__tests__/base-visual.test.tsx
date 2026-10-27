import { readFileSync } from "node:fs";
import path from "node:path";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import Inicio from "@/app/page";
import { MobilityLoader } from "@/components/MobilityLoader";

const css = readFileSync(path.resolve(__dirname, "../app/globals.css"), "utf8");
const referencia = path.resolve(__dirname, "../../../transfer-step-trail/src/styles.css");

describe("base visual portada del diseño de referencia", () => {
  it("la página de humo pinta cabecera, contenido y pie con las clases del diseño", () => {
    const { container } = render(<Inicio />);

    expect(container.querySelector("header.dashboard-header")).not.toBeNull();
    expect(container.querySelector("main.dashboard-layout")).not.toBeNull();
    expect(container.querySelector("footer.dashboard-footer")).not.toBeNull();
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Trámites de tránsito 100% en línea");
  });

  it("el cargador es accesible (role=status) y usa la órbita del diseño", () => {
    const { container } = render(<MobilityLoader />);

    expect(screen.getByRole("status")).toHaveTextContent("Cargando...");
    expect(container.querySelector(".mobility-track .mobility-orbit")).not.toBeNull();
  });

  it("conserva los tokens y las clases clave del diseño", () => {
    for (const fragmento of ["--primary:", "--background:", ".dashboard-header", ".dashboard-footer", ".mobility-track", "wizard-header", ".field"]) {
      expect(css).toContain(fragmento);
    }
    expect(css).toContain('@custom-variant dark');
  });

  it("los tokens de color son idénticos a los del repo de referencia (cuando está en la máquina)", () => {
    let original: string;
    try {
      original = readFileSync(referencia, "utf8");
    } catch {
      return; // el repo de referencia no está en CI: la comparación solo corre en la máquina del desarrollador
    }
    const tokens = (s: string) => s.split(/\r?\n/).filter((l) => /^\s+--[a-z0-9-]+:\s*oklch\(/.test(l)).map((l) => l.trim());

    expect(tokens(css)).toEqual(tokens(original));
  });
});
