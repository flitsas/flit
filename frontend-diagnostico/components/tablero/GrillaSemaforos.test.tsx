import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { GrillaSemaforos } from "@/components/tablero/GrillaSemaforos";
import { V1, V2, V3, celda, grilla, llamadas, loteTerminadoConErrores, simularApi, vehiculo } from "@/lib/tablero/__tests__/datos";
import type { Grilla } from "@/lib/tablero/api";

// Uso de ejemplo: <GrillaSemaforos grilla={grilla} puedeReprocesar={sesion.capacidades.reprocesarConsultas}
// onReprocesada={(celda) => …} onErrorReproceso={(mensaje) => …} /> pinta una fila por vehículo y una columna por fuente,
// agrupadas Por vehículo / Por propietario / Por locatario, con el semáforo de cada celda; «Reprocesar» solo en las rojas y
// solo si la sesión trae la capacidad.

afterEach(() => vi.unstubAllGlobals());

const pintar = (g: Grilla = grilla(), puedeReprocesar = false, props: Partial<Parameters<typeof GrillaSemaforos>[0]> = {}) =>
  render(<GrillaSemaforos grilla={g} puedeReprocesar={puedeReprocesar} {...props} />);

const celdaDe = (container: HTMLElement, id: string) => {
  const td = container.querySelector<HTMLElement>(`[data-celda="${id}"]`);
  if (!td) throw new Error(`No se pintó la celda ${id}`);
  return td;
};

const chipDe = (td: HTMLElement) => td.querySelector<HTMLElement>("[data-tono]")!;

describe("GrillaSemaforos — AC1: filas, columnas agrupadas, color y motivo", () => {
  it("una fila por vehículo con su identificador como encabezado de fila", () => {
    pintar();
    const filas = screen.getAllByRole("rowheader");
    expect(filas.map((f) => within(f).getByText(/^TST\d{3}$/).textContent)).toEqual(["TST001", "TST002", "TST003"]);
  });

  it("columnas agrupadas Por vehículo, Por propietario y Por locatario con el nombre de cada fuente", () => {
    pintar();
    const tabla = screen.getByRole("table", { name: /semáforo de consultas/i });
    const grupoVehiculo = within(tabla).getByRole("columnheader", { name: "Por vehículo" });
    expect(grupoVehiculo).toHaveAttribute("colspan", "2");
    expect(within(tabla).getByRole("columnheader", { name: "Por propietario" })).toHaveAttribute("colspan", "1");
    expect(within(tabla).getByRole("columnheader", { name: "Por locatario" })).toHaveAttribute("colspan", "1");
    for (const nombre of ["RUNT vehículo", "Impuesto departamental", "SIMIT propietario", "RUNT locatario"])
      expect(within(tabla).getByRole("columnheader", { name: nombre })).toBeInTheDocument();
  });

  it("omite el grupo sin fuentes", () => {
    pintar(grilla({ columnas: { por_vehiculo: ["RUNT"], por_propietario: [], por_locatario: [] }, vehiculos: [vehiculo(1, [celda(1, "RUNT", "PENDIENTE")])] }));
    expect(screen.getByRole("columnheader", { name: "Por vehículo" })).toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Por propietario" })).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Por locatario" })).not.toBeInTheDocument();
  });

  it.each([
    ["celda-3-runt", "PENDIENTE", "neutral", "Pendiente", "var(--badge-neutral-bg)"],
    ["celda-2-runt_locatario", "EN_CURSO", "neutral", "En curso", "var(--badge-neutral-bg)"],
    ["celda-2-runt", "COMPLETADO", "success", "Completada", "var(--badge-success-bg)"],
    ["celda-2-simit_propietario", "ERROR", "danger", "Error", "var(--badge-danger-bg)"],
    ["celda-1-impuesto_departamental", "NA", "na", "No aplica", "var(--color-flit-bg)"],
  ])("%s en %s se pinta con el tono %s, su texto «%s» y el token de fondo", (id, estado, tono, texto, fondo) => {
    const { container } = pintar();
    const td = celdaDe(container, id);
    expect(td).toHaveAttribute("data-estado", estado);
    const chip = chipDe(td);
    expect(chip).toHaveAttribute("data-tono", tono);
    expect(chip).toHaveTextContent(texto);
    expect(chip.style.background).toBe(fondo);
  });

  it("el texto del error usa la tinta AA del rojo (no el --badge-danger-fg de 4,46:1)", () => {
    const { container } = pintar();
    expect(chipDe(celdaDe(container, "celda-2-simit_propietario")).style.color).toBe("var(--badge-pending-fg)");
  });

  it.each([
    ["celda-1-impuesto_departamental", "Servicio público"],
    ["celda-2-impuesto_departamental", "Departamento no soportado"],
    ["celda-1-runt_locatario", "Sin documento del locatario"],
  ])("la N/A %s muestra su motivo «%s»", (id, motivo) => {
    const { container } = pintar();
    expect(celdaDe(container, id)).toHaveTextContent(motivo);
  });

  it("una celda que aún no llegó se pinta como sin dato, sin romper la fila", () => {
    const { container } = pintar(grilla({ vehiculos: [vehiculo(5, [celda(5, "RUNT", "PENDIENTE")])] }));
    const fila = container.querySelector<HTMLElement>('[data-vehiculo="vehiculo-5"]')!;
    expect(within(fila).getAllByRole("cell")).toHaveLength(4);
    expect(within(fila).getAllByText("Sin dato")).toHaveLength(3);
  });
});

describe("GrillaSemaforos — AC3: avance por vehículo", () => {
  it("un vehículo con todo en verde o N/A aparece terminado aunque otro siga pendiente", () => {
    const { container } = pintar(grilla({ vehiculos: [V1, V3] }));
    const terminado = container.querySelector<HTMLElement>('[data-vehiculo="vehiculo-1"]')!;
    const pendiente = container.querySelector<HTMLElement>('[data-vehiculo="vehiculo-3"]')!;
    expect(within(terminado).getByText("Terminado")).toBeInTheDocument();
    expect(within(terminado).getByText("4 de 4 consultas")).toBeInTheDocument();
    expect(within(pendiente).queryByText("Terminado")).not.toBeInTheDocument();
    expect(within(pendiente).getByText("0 de 4 consultas")).toBeInTheDocument();
  });

  it("el avance cuenta como resueltas las completadas, las N/A y las de error", () => {
    const { container } = pintar(grilla({ vehiculos: [V2] }));
    expect(within(container.querySelector<HTMLElement>('[data-vehiculo="vehiculo-2"]')!).getByText("3 de 4 consultas")).toBeInTheDocument();
  });
});

describe("GrillaSemaforos — AC4 (negativo): el usuario sin rol admin no ve acciones", () => {
  it("con celdas rojas no aparece ningún botón", () => {
    pintar(loteTerminadoConErrores(), false);
    expect(screen.getAllByText("Error").length).toBeGreaterThan(0);
    expect(screen.queryAllByRole("button")).toHaveLength(0);
  });
});

describe("GrillaSemaforos — AC5: el admin ve «Reprocesar» solo en las rojas", () => {
  it("un botón por celda roja reprocesable, con nombre accesible de fuente y vehículo", () => {
    pintar(grilla(), true);
    const botones = screen.getAllByRole("button");
    expect(botones).toHaveLength(1);
    expect(botones[0]).toHaveAccessibleName("Reprocesar SIMIT propietario de TST002");
  });

  it("la celda bloqueada por un RUNT en error no se reprocesa sola: dice por qué y no lleva botón", () => {
    const { container } = pintar(loteTerminadoConErrores(), true);
    expect(screen.getAllByRole("button")).toHaveLength(1);
    expect(screen.getByRole("button")).toHaveAccessibleName("Reprocesar RUNT vehículo de TST004");
    expect(celdaDe(container, "celda-4-impuesto_departamental")).toHaveTextContent("Espera al RUNT del vehículo");
  });

  it("pulsar Reprocesar hace POST /consultas/{cid}/reprocesar y avisa la celda reprocesada", async () => {
    const api = simularApi({ "POST /consultas/celda-2-simit_propietario/reprocesar": () => new Response(null, { status: 202 }) });
    const onReprocesada = vi.fn();
    pintar(grilla(), true, { onReprocesada });
    fireEvent.click(screen.getByRole("button", { name: /reprocesar/i }));
    await waitFor(() => expect(onReprocesada).toHaveBeenCalledWith(expect.objectContaining({ id: "celda-2-simit_propietario" })));
    expect(llamadas(api, "POST", "/consultas/celda-2-simit_propietario/reprocesar")).toHaveLength(1);
  });

  it.each([
    [403, "No tienes permiso para reprocesar consultas."],
    [409, "Esta consulta ya no se puede reprocesar"],
  ])("un %s del reproceso se avisa con un mensaje claro y no marca la celda", async (status, mensaje) => {
    simularApi({ "POST /consultas/celda-2-simit_propietario/reprocesar": () => Response.json({ code: "X" }, { status }) });
    const onReprocesada = vi.fn();
    const onErrorReproceso = vi.fn();
    pintar(grilla(), true, { onReprocesada, onErrorReproceso });
    fireEvent.click(screen.getByRole("button", { name: /reprocesar/i }));
    await waitFor(() => expect(onErrorReproceso).toHaveBeenCalledWith(expect.stringContaining(mensaje), status === 409));
    expect(onReprocesada).not.toHaveBeenCalled();
  });

  it("mientras se envía, el botón queda deshabilitado y lo anuncia", async () => {
    simularApi({ "POST /consultas/celda-2-simit_propietario/reprocesar": () => new Promise<Response>(() => {}) });
    pintar(grilla(), true);
    fireEvent.click(screen.getByRole("button", { name: /reprocesar/i }));
    await waitFor(() => expect(screen.getByRole("button", { name: /reprocesar/i })).toBeDisabled());
    expect(screen.getByRole("button", { name: /reprocesar/i })).toHaveAttribute("aria-busy", "true");
  });
});

describe("GrillaSemaforos — AC6: dato reutilizado y sin indicador manual", () => {
  it("la celda reutilizada muestra la fecha y la hora del dato (hora de Colombia)", () => {
    const { container } = pintar();
    expect(celdaDe(container, "celda-1-runt")).toHaveTextContent("Dato del 21/10/2026 09:05");
  });

  it("la celda no reutilizada no repite la fecha del dato", () => {
    const { container } = pintar();
    expect(celdaDe(container, "celda-2-runt")).not.toHaveTextContent(/Dato del/);
  });

  it("no existe ningún indicador de dato manual ni de origen", () => {
    const { container } = pintar(grilla({ vehiculos: [V1, V2, V3] }), true);
    expect(container).not.toHaveTextContent(/manual|origen|llenad[oa] a mano/i);
    expect(container.querySelector("[data-origen], [data-manual]")).toBeNull();
  });
});
