import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { ConfiguracionTabs } from "@/components/configuracion/ConfiguracionTabs";
import { PestanaContrato } from "@/components/configuracion/PestanaContrato";
import { PestanaOperacion } from "@/components/configuracion/PestanaOperacion";
import { configuracion } from "./datos";

// Uso de ejemplo (HU #14562 · F-07, maqueta Configuración del UI Kit 2026): las pestañas Operación y Contrato con su ícono,
// «Quién ve los diagnósticos» como SelectorSegmentado (radiogroup con flechas) y el Contrato en solo lectura con su rótulo
// «Solo lectura» y el aviso de que lo administra FLIT. Datos sintéticos.

afterEach(() => vi.unstubAllGlobals());

describe("HU14562 F-07 — rediseño visual de Configuración", () => {
  it("cada pestaña con ícono muestra su glifo decorativo y conserva el nombre accesible", () => {
    render(
      <ConfiguracionTabs
        etiqueta="Configuración de Diagnóstico"
        pestanas={[
          { id: "operacion", etiqueta: "Operación", icono: "ajustes", contenido: <p>Op</p> },
          { id: "contrato", etiqueta: "Contrato", icono: "documento", contenido: <p>Ct</p> },
        ]}
      />,
    );
    const tab = screen.getByRole("tab", { name: "Operación" });
    expect(tab.querySelector("svg[aria-hidden='true']")).not.toBeNull();
    expect(screen.getByRole("tab", { name: "Contrato" }).querySelector("svg[aria-hidden='true']")).not.toBeNull();
  });

  it("«Quién ve los diagnósticos» es un selector segmentado: radiogroup con flechas", () => {
    render(<PestanaOperacion configuracion={configuracion()} puedeEditar />);
    const grupo = screen.getByRole("radiogroup", { name: "Quién ve los diagnósticos" });
    const [toda, propios] = within(grupo).getAllByRole("radio");
    const marcada = toda.getAttribute("aria-checked") === "true" ? toda : propios;
    const otra = marcada === toda ? propios : toda;
    fireEvent.keyDown(marcada, { key: "ArrowRight" });
    expect(otra).toHaveAttribute("aria-checked", "true");
    expect(screen.getByText("Los cambios aplican a los diagnósticos nuevos")).toBeInTheDocument();
  });

  it("Contrato de la compañía: rótulo «Solo lectura» y aviso de que lo administra FLIT", () => {
    render(<PestanaContrato contrato={configuracion().contrato} puedeEditar={false} />);
    const tarjeta = screen.getByRole("region", { name: "Lo contratado por tu compañía" });
    expect(within(tarjeta).getByText("Solo lectura")).toBeInTheDocument();
    expect(within(tarjeta).getByText("FLIT administra tu contrato. Para cambiarlo, escribe a tu ejecutivo de cuenta")).toBeInTheDocument();
    expect(within(tarjeta).queryByRole("checkbox")).toBeNull();
  });
});
