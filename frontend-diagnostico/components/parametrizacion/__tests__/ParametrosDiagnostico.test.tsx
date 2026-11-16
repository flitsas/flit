import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ParametrosDiagnostico, type ParametrosDiagnosticoProps } from "@/components/parametrizacion/ParametrosDiagnostico";
import { useParametrizacion } from "@/components/parametrizacion/useParametrizacion";
import { configuracion, simularApi, tiposOk } from "./datos";

// Uso de ejemplo: const p = useParametrizacion(configuracion);
//   <ParametrosDiagnostico p={p} nombre={nombre} onNombre={setNombre} disposicion="horizontal">{acción}</ParametrosDiagnostico>
// HU #14583 (spec-ui §2.4, A2.7): en la disposición horizontal (Express y Masivo comparten el componente), Alcance pasa a
// fila completa bajo «Información general · Modo» desde md, para que el grupo Vehículo quepa en una sola fila de 4.

const CONFIGURACION = configuracion({ contrato: { fuentes_habilitadas: ["RUNT", "SIMIT_VEHICULO", "AVALUO"] } });

function Pantalla({ disposicion }: { disposicion?: ParametrosDiagnosticoProps["disposicion"] }) {
  const p = useParametrizacion(CONFIGURACION);
  return <ParametrosDiagnostico p={p} nombre="" onNombre={() => {}} disposicion={disposicion} />;
}

afterEach(() => vi.unstubAllGlobals());

describe("ParametrosDiagnostico — Alcance a fila completa (HU #14583)", () => {
  it("horizontal: la zona de Alcance ocupa toda la fila (col-span-full) y la rejilla tiene 2 columnas también en xl", async () => {
    simularApi({ "GET /tipos-tramite": tiposOk });
    const { container } = render(<Pantalla disposicion="horizontal" />);
    await screen.findByRole("group", { name: "Alcance" });
    const rejilla = container.querySelector('[data-disposicion="horizontal"]')!;
    const alcance = container.querySelector('[data-zona="alcance"]')!;
    expect(alcance.className).toContain("col-span-full");
    expect(alcance.className).not.toMatch(/xl:col-span-1|xl:border-l/);
    expect(rejilla.className).not.toContain("minmax(0,2fr)");
    expect(rejilla.className).toContain("md:grid-cols-2");
    // Alcance va después de Información general y Modo en el DOM (orden de lectura = visual).
    const zonas = Array.from(rejilla.children).map((el) => el.getAttribute("data-zona"));
    expect(zonas).toEqual(["informacion", "modo", "alcance"]);
  });

  it("el contador usa las fuentes ofrecidas a la compañía (3), no el catálogo", async () => {
    simularApi({ "GET /tipos-tramite": tiposOk });
    render(<Pantalla disposicion="horizontal" />);
    const grupo = await screen.findByRole("group", { name: "Alcance" });
    expect(grupo).toHaveTextContent(/1 de 3 fuentes/);
  });

  it("vertical: Alcance sigue apilado entre Información general y Modo", async () => {
    simularApi({ "GET /tipos-tramite": tiposOk });
    render(<Pantalla />);
    expect(await screen.findByRole("group", { name: "Alcance" })).toBeInTheDocument();
  });
});
