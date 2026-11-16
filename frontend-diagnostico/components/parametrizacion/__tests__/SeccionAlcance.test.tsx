import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { SeccionAlcance, type SeccionAlcanceProps } from "@/components/parametrizacion/SeccionAlcance";
import { DISPONIBLES_CON_IMPUESTO, DISPONIBLES_SIN_IMPUESTO } from "./datos";

// Uso de ejemplo: <SeccionAlcance habilitadas={["RUNT", "SIMIT_VEHICULO"]} seleccionadas={["RUNT"]} obligatorias={[]}
// disponibles={configuracion.fuentes_disponibles} onAlternar={(f) => ...} /> pinta los tres grupos con solo las fuentes contratadas.

function montar(props: Partial<SeccionAlcanceProps> = {}) {
  const onAlternar = vi.fn();
  render(
    <SeccionAlcance
      habilitadas={["RUNT", "RUNT_PROPIETARIO", "RUNT_LOCATARIO", "SIMIT_VEHICULO", "SIMIT_PROPIETARIO", "SIMIT_LOCATARIO", "AVALUO"]}
      disponibles={DISPONIBLES_SIN_IMPUESTO}
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

  it("rotula las fuentes de persona como dependientes del propietario y del locatario que reporta el RUNT (HU #14583, A2.6)", () => {
    montar();
    const propietario = screen.getByRole("group", { name: "Por Propietario" });
    const locatario = screen.getByRole("group", { name: "Por Locatario" });
    expect(propietario).toHaveAccessibleDescription(/depende del propietario que reporta el RUNT/i);
    expect(locatario).toHaveAccessibleDescription("Depende del locatario que reporta el RUNT vehículo.");
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
    // HU #14583 (A2.4): el grupo sin ninguna fuente contratada no se pinta, y el texto «Tu contrato no incluye…» se retiró.
    expect(screen.queryByRole("group", { name: "Por Propietario" })).toBeNull();
    expect(screen.queryByText(/tu contrato no incluye/i)).toBeNull();
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

afterEach(() => vi.unstubAllGlobals());

// HU #13865: el impuesto departamental se ofrece solo cuando el backend lo declara en fuentes_disponibles.
describe("SeccionAlcance — impuesto según fuentes_disponibles (HU #13865)", () => {
  it("AC1: con el impuesto habilitado y disponible aparece en el grupo Por Vehículo y se puede marcar", () => {
    const onAlternar = montar({ habilitadas: ["RUNT", "IMPUESTO_DEPARTAMENTAL"], disponibles: DISPONIBLES_CON_IMPUESTO });
    const impuesto = within(screen.getByRole("group", { name: "Por Vehículo" })).getByRole("checkbox", { name: "Impuesto departamental" });
    expect(impuesto).not.toBeChecked();
    expect(impuesto).not.toHaveAttribute("aria-disabled");
    fireEvent.click(impuesto);
    expect(onAlternar).toHaveBeenCalledWith("IMPUESTO_DEPARTAMENTAL");
  });

  it("AC16 (HU #14562): contratada pero fuera de fuentes_disponibles aparece deshabilitada con su ayuda y no se alterna", () => {
    const onAlternar = montar({ habilitadas: ["RUNT", "SIMIT_VEHICULO", "IMPUESTO_DEPARTAMENTAL"], disponibles: DISPONIBLES_SIN_IMPUESTO });
    const impuesto = screen.getByRole("checkbox", { name: "Impuesto departamental" });
    expect(impuesto).toBeDisabled();
    expect(impuesto).toHaveAccessibleDescription("Disponible cuando la consulta automatizada esté en producción");
    fireEvent.click(impuesto);
    expect(onAlternar).not.toHaveBeenCalled();
    expect(screen.getAllByRole("checkbox").map((c) => c.getAttribute("value"))).toEqual(["RUNT", "SIMIT_VEHICULO", "IMPUESTO_DEPARTAMENTAL"]);
  });

  it("AC16 (HU #14562): una fuente no contratada no aparece aunque esté disponible", () => {
    montar({ habilitadas: ["RUNT"], disponibles: DISPONIBLES_SIN_IMPUESTO });
    expect(screen.queryByRole("checkbox", { name: "SIMIT vehículo" })).toBeNull();
  });
});

// HU #14583 (spec-ui §2, A2.1–A2.8): contador «N de M» con las fuentes ofrecidas a la compañía, en la línea del título y
// anunciado; grupos en filas con las fuentes en horizontal, en una rejilla fija por container query (mismo ancho en todos
// los grupos); grupo sin fuentes contratadas oculto; contratadas sin consulta automática siguen visibles y deshabilitadas.
describe("SeccionAlcance — contador y disposición (HU #14583)", () => {
  const contador = () => screen.getByText((_, el) => el?.getAttribute("aria-live") === "polite");

  it("A2.1: M = fuentes ofrecidas (contratadas y disponibles), no el catálogo; N = marcadas", () => {
    montar({ habilitadas: ["RUNT", "SIMIT_VEHICULO", "AVALUO"], seleccionadas: ["RUNT", "SIMIT_VEHICULO"] });
    expect(contador()).toHaveTextContent(/^2 de 3 fuentes seleccionadas$/);
  });

  it("A2.1: las contratadas sin consulta automática no suman a M", () => {
    montar({ habilitadas: ["RUNT", "SIMIT_VEHICULO", "IMPUESTO_DEPARTAMENTAL"], disponibles: DISPONIBLES_SIN_IMPUESTO });
    expect(contador()).toHaveTextContent(/^1 de 2 fuentes seleccionadas$/);
  });

  it("A2.1/A2.2: el contador va en la misma fila que el título «Alcance», con aria-live y el texto sr-only «seleccionadas»", () => {
    montar();
    const titulo = screen.getByRole("heading", { name: "Alcance" });
    const nodo = contador();
    expect(nodo.parentElement).toBe(titulo.parentElement);
    expect(nodo.parentElement?.className).toMatch(/(^| )flex( |$)/);
    expect(nodo.parentElement?.className).toContain("items-center");
    expect(within(nodo).getByText("seleccionadas", { exact: false }).className).toContain("sr-only");
    expect(nodo.className).toContain("bg-[var(--badge-info-bg)]");
    expect(nodo.className).toContain("rounded-full");
  });

  it("vacío (solo RUNT ofrecida): «1 de 1», solo el grupo Vehículo con la tarjeta bloqueada", () => {
    montar({ habilitadas: ["RUNT"] });
    expect(contador()).toHaveTextContent(/^1 de 1 fuentes seleccionadas$/);
    expect(screen.getByRole("group", { name: "Por Vehículo" })).toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Por Propietario" })).toBeNull();
    expect(screen.queryByRole("group", { name: "Por Locatario" })).toBeNull();
  });

  it("A2.4: un grupo con fuentes contratadas pero sin consulta automática sigue visible, con sus casillas deshabilitadas", () => {
    montar({ habilitadas: ["RUNT", "RUNT_LOCATARIO"], disponibles: ["RUNT"] });
    const locatario = screen.getByRole("group", { name: "Por Locatario" });
    expect(within(locatario).getByRole("checkbox", { name: "RUNT locatario" })).toBeDisabled();
  });

  it("A2.3: todas las listas de fuentes usan la misma rejilla fija por container query (sin auto-fit ni flex-1)", () => {
    const { container } = render(
      <SeccionAlcance
        habilitadas={["RUNT", "RUNT_PROPIETARIO", "SIMIT_PROPIETARIO", "SIMIT_VEHICULO", "AVALUO"]}
        disponibles={DISPONIBLES_SIN_IMPUESTO}
        seleccionadas={["RUNT"]}
        obligatorias={[]}
        onAlternar={vi.fn()}
      />,
    );
    const listas = Array.from(container.querySelectorAll("ul"));
    expect(listas.length).toBe(2);
    for (const ul of listas) {
      for (const c of ["grid", "grid-cols-1", "@[22rem]:grid-cols-2", "@[40rem]:grid-cols-4"]) expect(ul.classList).toContain(c);
      expect(ul.className).not.toMatch(/auto-fit|auto-fill|flex-col/);
    }
    expect(Array.from(container.querySelectorAll("section")).some((el) => el.classList.contains("@container"))).toBe(true);
    for (const li of Array.from(container.querySelectorAll("li"))) expect(li.querySelector("label")?.className ?? "").not.toMatch(/(^| )flex-1( |$)/);
  });

  it("los grupos van apilados en filas, con divisor entre ellos (el primero sin borde)", () => {
    montar();
    const grupos = ["Por Vehículo", "Por Propietario", "Por Locatario"].map((n) => screen.getByRole("group", { name: n }));
    const contenedor = grupos[0].parentElement!;
    expect(contenedor.className).toContain("flex-col");
    expect(grupos[0].className).not.toContain("border-t");
    expect(grupos[1].className).toContain("border-t");
    expect(grupos[2].className).toContain("border-t");
  });

  it("A2.5: la insignia «Dependiente» sube a 12 px y el candado de RUNT se conserva", () => {
    montar();
    const insignia = within(screen.getByRole("group", { name: "Por Propietario" })).getAllByText("Dependiente")[0];
    expect(insignia.className).toContain("text-xs");
    expect(insignia.className).not.toContain("text-[10px]");
    const runt = screen.getByRole("checkbox", { name: "RUNT vehículo" });
    expect(runt.closest("label")?.querySelector("svg")).not.toBeNull();
  });

  it("el tooltip del candado es el Tooltip del kit: se abre al enfocar la casilla y Esc lo cierra sin perder el foco", () => {
    montar({ seleccionadas: [] });
    const runt = screen.getByRole("checkbox", { name: "RUNT vehículo" });
    const tooltip = screen.getAllByRole("tooltip").find((t) => /siempre se consulta/i.test(t.textContent ?? ""))!;
    expect(tooltip.className).toContain("rounded-[9px]");
    expect(tooltip).toHaveAttribute("data-abierto", "false");
    act(() => runt.focus());
    expect(tooltip).toHaveAttribute("data-abierto", "true");
    fireEvent.keyDown(runt, { key: "Escape" });
    expect(tooltip).toHaveAttribute("data-abierto", "false");
    expect(runt).toHaveFocus();
  });
});
