import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { SeccionAlcance, type SeccionAlcanceProps } from "@/components/parametrizacion/SeccionAlcance";

// Uso de ejemplo: <SeccionAlcance habilitadas={["RUNT", "SIMIT_VEHICULO"]} seleccionadas={["RUNT"]} obligatorias={[]}
// onAlternar={(f) => ...} /> pinta los tres grupos con solo las fuentes contratadas.

function montar(props: Partial<SeccionAlcanceProps> = {}) {
  const onAlternar = vi.fn();
  render(
    <SeccionAlcance
      habilitadas={["RUNT", "RUNT_PROPIETARIO", "RUNT_LOCATARIO", "SIMIT_VEHICULO", "SIMIT_PROPIETARIO", "SIMIT_LOCATARIO", "AVALUO"]}
      seleccionadas={["RUNT"]}
      obligatorias={[]}
      onAlternar={onAlternar}
      {...props}
    />,
  );
  return onAlternar;
}

describe("SeccionAlcance — grupos y RUNT (AC 2)", () => {
  it("muestra los grupos Por Vehículo, Por Propietario y Por Locatario", () => {
    montar();
    expect(screen.getByRole("group", { name: "Por Vehículo" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Por Propietario" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Por Locatario" })).toBeInTheDocument();
  });

  it("RUNT vehículo va marcado y bloqueado, con tooltip, y no se puede desmarcar", () => {
    const onAlternar = montar({ seleccionadas: [] });
    const runt = screen.getByRole("checkbox", { name: "RUNT vehículo" });
    expect(runt).toBeChecked();
    expect(runt).toHaveAttribute("aria-disabled", "true");
    expect(runt).toHaveAccessibleDescription(/siempre se consulta/i);
    expect(screen.getAllByRole("tooltip").some((t) => /siempre se consulta/i.test(t.textContent ?? ""))).toBe(true);

    fireEvent.click(runt);
    expect(onAlternar).not.toHaveBeenCalled();
    expect(runt).toBeChecked();
  });

  it("rotula las fuentes de persona como dependientes del RUNT y del documento del locatario", () => {
    montar();
    const propietario = screen.getByRole("group", { name: "Por Propietario" });
    const locatario = screen.getByRole("group", { name: "Por Locatario" });
    expect(propietario).toHaveAccessibleDescription(/depende del propietario que reporta el RUNT/i);
    expect(locatario).toHaveAccessibleDescription(/depende del documento del locatario/i);
    for (const nombre of ["RUNT propietario", "SIMIT propietario", "RUNT locatario", "SIMIT locatario"])
      expect(screen.getByRole("checkbox", { name: nombre })).toHaveAccessibleDescription(/dependiente/i);
    expect(within(propietario).getAllByText("Dependiente")).toHaveLength(2);
  });
});

describe("SeccionAlcance — solo fuentes contratadas (AC 3)", () => {
  it("con habilitadas RUNT y SIMIT vehículo ofrece solo esas dos y respeta las seleccionadas", () => {
    montar({ habilitadas: ["RUNT", "SIMIT_VEHICULO"], seleccionadas: ["RUNT"] });
    const casillas = screen.getAllByRole("checkbox");
    expect(casillas.map((c) => c.getAttribute("value"))).toEqual(["RUNT", "SIMIT_VEHICULO"]);
    expect(screen.getByRole("checkbox", { name: "RUNT vehículo" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "SIMIT vehículo" })).not.toBeChecked();
    expect(within(screen.getByRole("group", { name: "Por Propietario" })).getByText(/tu contrato no incluye fuentes de este grupo/i)).toBeInTheDocument();
  });

  it("no ofrece IMPUESTO_DEPARTAMENTAL aunque el contrato lo habilite", () => {
    montar({ habilitadas: ["RUNT", "IMPUESTO_DEPARTAMENTAL"] });
    expect(screen.queryByRole("checkbox", { name: "Impuesto departamental" })).toBeNull();
  });

  it("una fuente libre se alterna con un clic", () => {
    const onAlternar = montar();
    fireEvent.click(screen.getByRole("checkbox", { name: "SIMIT vehículo" }));
    expect(onAlternar).toHaveBeenCalledWith("SIMIT_VEHICULO");
  });
});

describe("SeccionAlcance — obligatorias del tipo (AC 5)", () => {
  it("las fuentes obligatorias quedan marcadas y bloqueadas con tooltip", () => {
    const onAlternar = montar({ obligatorias: ["SIMIT_VEHICULO", "RUNT_PROPIETARIO"] });
    for (const nombre of ["SIMIT vehículo", "RUNT propietario"]) {
      const casilla = screen.getByRole("checkbox", { name: nombre });
      expect(casilla).toBeChecked();
      expect(casilla).toHaveAttribute("aria-disabled", "true");
      expect(casilla).toHaveAccessibleDescription(/obligatoria para el tipo de trámite/i);
      fireEvent.click(casilla);
    }
    expect(onAlternar).not.toHaveBeenCalled();
  });

  it("el error de la API se asocia al grupo de alcance", () => {
    montar({ error: "La fuente no está habilitada." });
    expect(screen.getByRole("group", { name: "Alcance" })).toHaveAccessibleDescription(/la fuente no está habilitada/i);
  });
});
