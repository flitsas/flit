import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { GrillaSemaforos } from "@/components/tablero/GrillaSemaforos";
import { FUENTES, nombreFuente, nombreSubfuente } from "@/lib/configuracion";
import { V1, V2, V3, celda, grilla, llamadas, loteTerminadoConErrores, simularApi, vehiculo } from "@/lib/tablero/__tests__/datos";
import type { Grilla } from "@/lib/tablero/api";
import { simularDesborde } from "@/components/comun/__tests__/desborde";
import { CONTENEDOR_TABLA_CLS } from "@/components/tablero/vistasDetalle/comun";
import { createElement, type ComponentType } from "react";
import { join, resolve } from "node:path";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";

// Uso de ejemplo: <GrillaSemaforos grilla={grilla} puedeReprocesar={sesion.capacidades.reprocesarConsultas}
// onReprocesada={(celda) => …} onErrorReproceso={(mensaje) => …} /> pinta una fila por vehículo (placa o VIN con el ícono de
// cambio de propietario; sin columnas de documento desde HU #14585) y SIEMPRE las 8 fuentes agrupadas por pilar (RUNT, SIMIT, Avalúo comercial,
// Impuesto departamental): la fuente no pedida dice «Sin solicitar»; la pedida sin celda, «Sin ejecutar». «Reprocesar» solo
// en las rojas y solo si la sesión trae la capacidad (HU #14087 sobre HU #13748).

afterEach(() => vi.unstubAllGlobals());

const pintar = (g: Grilla = grilla(), puedeReprocesar = false, props: Partial<Parameters<typeof GrillaSemaforos>[0]> = {}) =>
  render(<GrillaSemaforos grilla={g} puedeReprocesar={puedeReprocesar} {...props} />);

const celdaDe = (container: HTMLElement, id: string) => {
  const td = container.querySelector<HTMLElement>(`[data-celda="${id}"]`);
  if (!td) throw new Error(`No se pintó la celda ${id}`);
  return td;
};

const filaDe = (container: HTMLElement, vehiculoId: string) => container.querySelector<HTMLElement>(`[data-vehiculo="${vehiculoId}"]`)!;
const fuenteDe = (fila: HTMLElement, fuente: string) => fila.querySelector<HTMLElement>(`[data-fuente="${fuente}"]`)!;

const chipDe = (td: HTMLElement) => td.querySelector<HTMLElement>("[data-tono]")!;

/** Botones de acción: desde HU #14087 el semáforo es un enlace a la pestaña del detalle, así que todo botón es una acción. */
const accionesDe = (raiz: Pick<typeof screen, "queryAllByRole"> = screen) => raiz.queryAllByRole("button");

const NUEVA = " (se abre en una pestaña nueva)";
const DIR_ICONOS = resolve(__dirname, "../dx/iconos");

describe("GrillaSemaforos — HU #14087 AC3: columnas agrupadas por pilar, las 8 fuentes siempre", () => {
  it("cabecera de un nivel: Vehículo, Propietario, Locatario, Avalúo comercial e Impuesto departamental, sin grupos RUNT/SIMIT", () => {
    const { container } = pintar();
    const tabla = screen.getByRole("table", { name: /semáforo de consultas/i });
    expect(container.querySelectorAll("thead tr")).toHaveLength(1);
    const nombres = within(tabla).getAllByRole("columnheader").map((th) => th.textContent?.trim());
    expect(nombres).toEqual(["Placa o VIN", "Vehículo", "Propietario", "Locatario", "Avalúo comercial", "Impuesto departamental"]);
    for (const th of within(tabla).getAllByRole("columnheader")) {
      expect(th).toHaveAttribute("scope", "col");
      expect(th).not.toHaveAttribute("colspan");
    }
    for (const grupo of ["RUNT", "SIMIT"]) expect(within(tabla).queryByRole("columnheader", { name: grupo })).not.toBeInTheDocument();
    expect(within(tabla).queryByRole("columnheader", { name: "Por vehículo" })).not.toBeInTheDocument();
  });

  it("las 8 fuentes aparecen en cada fila en el orden de FUENTES aunque el lote pidiera solo una", () => {
    const { container } = pintar(grilla({ columnas: { por_vehiculo: ["RUNT"], por_propietario: [], por_locatario: [] }, vehiculos: [vehiculo(1, [celda(1, "RUNT", "COMPLETADO")])] }));
    const fila = filaDe(container, "vehiculo-1");
    // Por sujeto: RUNT y luego SIMIT; después Avalúo e Impuesto.
    expect(Array.from(fila.querySelectorAll("[data-fuente]")).map((td) => td.getAttribute("data-fuente"))).toEqual([
      "RUNT",
      "SIMIT_VEHICULO",
      "RUNT_PROPIETARIO",
      "SIMIT_PROPIETARIO",
      "RUNT_LOCATARIO",
      "SIMIT_LOCATARIO",
      "AVALUO",
      "IMPUESTO_DEPARTAMENTAL",
    ]);
  });

  it("la fuente no pedida en el lote dice «Sin solicitar» en cada fila y no es un botón", () => {
    const { container } = pintar();
    for (const id of ["vehiculo-1", "vehiculo-2", "vehiculo-3"]) {
      for (const fuente of ["RUNT_PROPIETARIO", "SIMIT_VEHICULO", "SIMIT_LOCATARIO", "AVALUO"]) {
        const td = fuenteDe(filaDe(container, id), fuente);
        // HU #14562 AC12: «Sin solicitar» es el estado visual NA con su motivo propio.
        expect(td).toHaveAttribute("data-estado", "NA");
        expect(td).toHaveAttribute("data-motivo", "SIN_SOLICITAR");
        expect(td).toHaveTextContent("Sin solicitar");
        expect(within(td).queryByRole("button")).not.toBeInTheDocument();
      }
    }
    expect(screen.queryAllByRole("link", { name: /^Ver detalle de (RUNT propietario|SIMIT vehículo|SIMIT locatario|Avalúo comercial) /i })).toHaveLength(0);
  });

  it("la fuente pedida cuya celda aún no llegó dice «Sin ejecutar», sin botón y sin romper la fila", () => {
    const { container } = pintar(grilla({ vehiculos: [vehiculo(5, [celda(5, "RUNT", "PENDIENTE")])] }));
    const fila = filaDe(container, "vehiculo-5");
    for (const fuente of ["IMPUESTO_DEPARTAMENTAL", "SIMIT_PROPIETARIO", "RUNT_LOCATARIO"]) {
      const td = fuenteDe(fila, fuente);
      expect(td).toHaveTextContent("Sin ejecutar");
      expect(within(td).queryByRole("button")).not.toBeInTheDocument();
    }
    // 3 columnas de sujeto (dos badges cada una) + Avalúo + Impuesto.
    expect(within(fila).getAllByRole("cell")).toHaveLength(5);
  });
});

describe("GrillaSemaforos — HU #14585: placa con su ícono de cambio de propietario, sin columnas de documento", () => {
  it("A6.1: la primera columna es «Placa o VIN» y no existen «Documento declarado» ni «Documento del propietario»", () => {
    const { container } = pintar();
    const primera = container.querySelector("thead tr:first-child th")!;
    expect(primera.textContent).toBe("Placa o VIN");
    expect(primera).toHaveAttribute("scope", "col");
    expect(screen.queryByRole("columnheader", { name: /documento/i })).toBeNull();
    expect(container.querySelector("[data-columna='documento-declarado'], [data-columna='documento-propietario']")).toBeNull();
  });

  it("la tabla recalcula su ancho mínimo sin las dos columnas (min-w ~1040 px)", () => {
    pintar();
    expect(screen.getByRole("table", { name: /semáforo de consultas/i }).className).toContain("min-w-[1040px]");
  });

  it("una fila por vehículo con su placa como encabezado de fila, su avance y el badge Terminado", () => {
    const { container } = pintar(grilla({ vehiculos: [V1, V3] }));
    expect(screen.getAllByRole("rowheader").map((f) => within(f).getByText(/^TST\d{3}$/).textContent)).toEqual(["TST001", "TST003"]);
    expect(within(filaDe(container, "vehiculo-1")).getByText("Terminado")).toBeInTheDocument();
    expect(within(filaDe(container, "vehiculo-1")).getByText("4 de 4 consultas")).toBeInTheDocument();
    expect(within(filaDe(container, "vehiculo-3")).queryByText("Terminado")).not.toBeInTheDocument();
    expect(within(filaDe(container, "vehiculo-3")).getByText("0 de 4 consultas")).toBeInTheDocument();
  });

  it("el avance cuenta como resueltas las completadas, las novedades y las de error, sobre las fuentes pedidas", () => {
    const { container } = pintar(grilla({ vehiculos: [V2] }));
    expect(within(filaDe(container, "vehiculo-2")).getByText("3 de 4 consultas")).toBeInTheDocument();
  });

  it("m del avance es el número de fuentes pedidas, no las 8 columnas", () => {
    const { container } = pintar(grilla({ columnas: { por_vehiculo: ["RUNT", "AVALUO"], por_propietario: [], por_locatario: [] }, vehiculos: [vehiculo(1, [celda(1, "RUNT", "COMPLETADO")])] }));
    expect(within(filaDe(container, "vehiculo-1")).getByText("1 de 2 consultas")).toBeInTheDocument();
  });

  it("sin placa muestra el VIN", () => {
    const vin = "9ZZPRUEBA00000007";
    const { container } = pintar(grilla({ vehiculos: [vehiculo(7, [celda(7, "RUNT", "PENDIENTE")], { placa: null, vin, identificador: vin, tipo_identificador: "VIN" })] }));
    expect(within(filaDe(container, "vehiculo-7")).getByRole("rowheader")).toHaveTextContent(vin);
  });

  it("A6.7: el ícono de cambio de propietario va en la celda de la placa solo con true", () => {
    const con = vehiculo(1, [celda(1, "RUNT", "COMPLETADO")], { cambio_propietario: true });
    const sin = vehiculo(2, [celda(2, "RUNT", "COMPLETADO")], { cambio_propietario: false });
    const nulo = vehiculo(3, [celda(3, "RUNT", "COMPLETADO")], { cambio_propietario: null });
    const { container } = pintar(grilla({ vehiculos: [con, sin, nulo] }));
    expect(within(within(filaDe(container, "vehiculo-1")).getByRole("rowheader")).getByRole("button", { name: "Cambio de propietario" })).toBeInTheDocument();
    for (const id of ["vehiculo-2", "vehiculo-3"]) expect(within(filaDe(container, id)).queryByRole("button", { name: "Cambio de propietario" })).toBeNull();
  });

  it("SP-19: aunque el backend viejo mandara documentos, el HTML de la grilla no trae el número ni su máscara", () => {
    const viejo = {
      ...vehiculo(1, [celda(1, "RUNT", "COMPLETADO")], { cambio_propietario: true }),
      documento_declarado: { rol: "LOCATARIO", tipo_documento: "CC", documento_enmascarado: "****5678" },
      propietarios: [{ tipo_documento: "NIT", documento_enmascarado: "9001234567" }],
    };
    const { container } = pintar(grilla({ vehiculos: [viejo as never] }));
    // Con el tooltip del ícono abierto tampoco: su contenido es el literal fijo.
    const icono = within(filaDe(container, "vehiculo-1")).getByRole("button", { name: "Cambio de propietario" });
    act(() => icono.focus());
    const html = document.body.innerHTML;
    for (const fuga of ["5678", "9001234567", "4567", "****"]) expect(html).not.toContain(fuga);
  });

  it("la placa no lleva botones de Dictamen ni Auditoría (A6.6): solo lo que pase accionEncabezado como marca", () => {
    const { container } = pintar(grilla({ vehiculos: [V1] }), false, { accionEncabezado: () => <span>Ajustado por FLIT</span> });
    const placa = within(filaDe(container, "vehiculo-1")).getByRole("rowheader");
    expect(within(placa).queryByRole("button", { name: /dictamen|auditor/i })).toBeNull();
    expect(placa).toHaveTextContent("Ajustado por FLIT");
  });

  it("filas con el hover del kit (--table-row-hover) y transición con el token de movimiento", () => {
    const { container } = pintar(grilla({ vehiculos: [V1] }));
    const fila = filaDe(container, "vehiculo-1");
    expect(fila.className).toContain("hover:bg-[var(--table-row-hover)]");
    expect(fila.className).toContain("motion-reduce:transition-none");
  });

  it("CR-6: contenedor CONTENEDOR_TABLA_CLS y las celdas fijas (placa y Acciones) toman el hover de la fila (group-hover)", () => {
    const { container } = pintar(grilla({ vehiculos: [V1] }), false, { accionVehiculo: () => <span>acción</span> });
    const contenedor = screen.getByRole("table", { name: /semáforo de consultas/i }).parentElement!;
    for (const c of CONTENEDOR_TABLA_CLS.split(" ")) expect(contenedor.className.split(/\s+/)).toContain(c);
    const fila = filaDe(container, "vehiculo-1");
    expect(fila.className.split(/\s+/)).toContain("group");
    expect(within(fila).getByRole("rowheader").className).toContain("group-hover:bg-[var(--table-row-hover)]");
    expect(fila.querySelector<HTMLElement>("td:last-child")!.className).toContain("group-hover:bg-[var(--table-row-hover)]");
  });
});

describe("GrillaSemaforos — HU #14584 §6.2: la celda pinta solo el chip; el detalle va al tooltip", () => {
  const conTooltip = (td: HTMLElement) => {
    const disparador = td.querySelector<HTMLElement>("a, [tabindex='0']")!;
    act(() => disparador.focus());
    return { disparador, tip: screen.getByRole("tooltip") };
  };

  it("A6.3/A6.4: Ejecutado reutilizado → tooltip «Ejecutado · Dato del …» enlazado con aria-describedby", () => {
    const { container } = pintar(grilla({ vehiculos: [V1] }));
    const { disparador, tip } = conTooltip(celdaDe(container, "celda-1-runt"));
    expect(disparador.tagName).toBe("A");
    expect(tip).toHaveTextContent("Ejecutado");
    expect(tip).toHaveTextContent("Dato del 21/10/2026 09:05");
    expect(disparador.getAttribute("aria-describedby") ?? "").toContain(tip.id);
  });

  it("Ejecutado sin reutilizar también dice la fecha del dato; sin fecha, «Consultado en este lote»", () => {
    const sinFecha = vehiculo(1, [celda(1, "RUNT", "COMPLETADO", { dato_obtenido_en: null })]);
    const { container } = pintar(grilla({ vehiculos: [sinFecha] }));
    expect(conTooltip(celdaDe(container, "celda-1-runt")).tip).toHaveTextContent("Consultado en este lote");
  });

  it("Error → «Error · No se pudo consultar»; Novedad → su motivo; Sin ejecutar → «La consulta aún no termina»", () => {
    const { container } = pintar();
    expect(conTooltip(celdaDe(container, "celda-2-simit_propietario")).tip).toHaveTextContent("No se pudo consultar");
    expect(conTooltip(celdaDe(container, "celda-1-impuesto_departamental")).tip).toHaveTextContent("Servicio público");
    expect(conTooltip(celdaDe(container, "celda-3-runt")).tip).toHaveTextContent("La consulta aún no termina");
  });

  it("Sin solicitar (sin enlace): el chip va en un span enfocable con nombre de fuente, placa y estado; tooltip «No se pidió en este lote»", () => {
    const { container } = pintar(grilla({ vehiculos: [V1] }));
    const td = fuenteDe(filaDe(container, "vehiculo-1"), "RUNT_PROPIETARIO");
    const span = td.querySelector<HTMLElement>("[tabindex='0']")!;
    // CR-8: sin aria-label en un span sin rol; el nombre va como texto sr-only dentro del disparador.
    expect(span).not.toHaveAttribute("aria-label");
    expect(span.querySelector(".sr-only")).toHaveTextContent("RUNT propietario de TST001: Sin solicitar");
    act(() => span.focus());
    expect(screen.getByRole("tooltip")).toHaveTextContent("No se pidió en este lote");
  });

  it("la línea secundaria en la celda solo se ve por debajo de 900 px y es aria-hidden (ya va en el tooltip)", () => {
    const { container } = pintar(grilla({ vehiculos: [V1] }));
    const td = fuenteDe(filaDe(container, "vehiculo-1"), "RUNT_PROPIETARIO");
    const linea = within(td).getByText("No se pidió en este lote");
    expect(linea).toHaveAttribute("aria-hidden", "true");
    expect(linea.className).toContain("lg:hidden");
  });
});

describe("GrillaSemaforos — HU #14087 AC5: estados con icono y texto", () => {
  it.each([
    // HU #14562 AC11: rótulos y tonos --dx-estado-* del kit 2026.
    // Columnas de sujeto: el badge lleva el prefijo de su consulta («RUNT · …», «SIMIT · …»).
    ["celda-3-runt", "PENDIENTE", "pendiente", "RUNT · Sin ejecutar", "var(--dx-estado-pendiente-borde)"],
    ["celda-2-runt_locatario", "EN_CURSO", "en-curso", "RUNT · Sin ejecutar (en curso)", "var(--dx-estado-en-curso-borde)"],
    ["celda-2-runt", "COMPLETADO", "ejecutado", "RUNT · Ejecutado", "var(--dx-estado-ejecutado-borde)"],
    ["celda-2-simit_propietario", "ERROR", "error", "SIMIT · Error", "var(--dx-estado-error-borde)"],
    ["celda-1-impuesto_departamental", "NA", "novedad", "Novedad", "var(--dx-estado-novedad-borde)"],
  ])("%s en %s se pinta con el tono %s, su texto «%s», un icono y el token de color de la etiqueta del kit", (id, estado, tono, texto, fondo) => {
    const { container } = pintar();
    const td = celdaDe(container, id);
    expect(td).toHaveAttribute("data-estado", estado);
    const chip = chipDe(td);
    expect(chip).toHaveAttribute("data-tono", tono);
    expect(chip).toHaveTextContent(new RegExp(`^${texto.replace(/[()]/g, "\\$&")}$`));
    expect(chip.querySelector("svg")).not.toBeNull();
    expect(chip).toHaveClass("kit-etiqueta");
    expect(chip.style.getPropertyValue("--c")).toBe(fondo);
  });

  it("HU #14562 AC5: ningún ícono de la grilla gira (ni EN_CURSO ni PENDIENTE)", () => {
    const { container } = pintar();
    expect(chipDe(celdaDe(container, "celda-2-runt_locatario")).querySelector("svg")!.getAttribute("class") ?? "").not.toMatch(/animate-/);
    expect(chipDe(celdaDe(container, "celda-3-runt")).querySelector("svg")!.getAttribute("class") ?? "").not.toMatch(/animate-/);
  });

  it("«Sin solicitar» usa su tono propio (--dx-estado-sin-solicitar), con icono", () => {
    const { container } = pintar();
    const chip = chipDe(fuenteDe(filaDe(container, "vehiculo-1"), "AVALUO"));
    expect(chip).toHaveAttribute("data-tono", "sin-solicitar");
    expect(chip.style.getPropertyValue("--c")).toBe("var(--dx-estado-sin-solicitar-borde)");
    expect(chip.querySelector("svg")).not.toBeNull();
    expect(chip).toHaveTextContent("Sin solicitar");
  });

  it("el texto del error usa la tinta AA del rojo del semáforo (--dx-estado-error)", () => {
    const { container } = pintar();
    expect(chipDe(celdaDe(container, "celda-2-simit_propietario")).style.getPropertyValue("--ct")).toBe("var(--dx-estado-error)");
  });

  it.each([
    ["celda-1-impuesto_departamental", "Servicio público"],
    ["celda-2-impuesto_departamental", "Departamento no soportado"],
    ["celda-1-runt_locatario", "El RUNT no reporta locatario"],
  ])("la novedad %s muestra su motivo «%s» debajo", (id, motivo) => {
    const { container } = pintar();
    // El enlace del semáforo lleva además el aviso sr-only de pestaña nueva (HU #14087): el motivo va justo después.
    expect(celdaDe(container, id)).toHaveTextContent(`Novedad(se abre en una pestaña nueva)${motivo}`);
  });

  it("el nombre accesible del enlace de la celda lleva fuente, identificador, estado y que abre una pestaña nueva", () => {
    pintar();
    expect(screen.getByRole("link", { name: `Ver detalle de RUNT vehículo de TST002: RUNT · Ejecutado${NUEVA}` })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: `Ver detalle de Impuesto departamental de TST001: Novedad${NUEVA}` })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: `Ver detalle de RUNT vehículo de TST003: RUNT · Sin ejecutar${NUEVA}` })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: `Ver detalle de SIMIT propietario de TST002: SIMIT · Error${NUEVA}` })).toBeInTheDocument();
  });

  it("HU #14087: el semáforo es un enlace a la página del detalle en pestaña nueva (target _blank, noopener noreferrer, texto sr-only)", () => {
    const { container } = pintar();
    const enlace = screen.getByRole("link", { name: `Ver detalle de RUNT vehículo de TST002: RUNT · Ejecutado${NUEVA}` });
    const td = enlace.closest("td")!;
    const vehiculoId = td.closest("tr")!.getAttribute("data-vehiculo")!;
    expect(enlace).toHaveAttribute("href", `/diagnosticos/${grilla().id}/vehiculos/${vehiculoId}/consultas/RUNT`);
    expect(enlace).toHaveAttribute("target", "_blank");
    expect(enlace).toHaveAttribute("rel", "noopener noreferrer");
    expect(enlace).not.toHaveAttribute("aria-haspopup");
    expect(within(enlace).getByText("(se abre en una pestaña nueva)")).toHaveClass("sr-only");
    expect(container.querySelector("[role='dialog']")).toBeNull();
    fireEvent.click(enlace);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("con hrefDetalle propio (backoffice) el enlace va a esa ruta", () => {
    pintar(grilla(), false, { hrefDetalle: (c, v) => `/backoffice/x/${v.id}/${c.fuente}?empresa=e` });
    const enlace = screen.getByRole("link", { name: `Ver detalle de RUNT vehículo de TST002: RUNT · Ejecutado${NUEVA}` });
    expect(enlace.getAttribute("href")).toMatch(/^\/backoffice\/x\/.+\/RUNT\?empresa=e$/);
  });
});

describe("GrillaSemaforos — AC6: el usuario sin rol admin no ve acciones", () => {
  it("con celdas rojas no aparece ningún botón", () => {
    pintar(loteTerminadoConErrores(), false);
    expect(screen.getAllByText("Error").length).toBeGreaterThan(0);
    expect(accionesDe()).toHaveLength(0);
  });
});

describe("GrillaSemaforos — AC6: el admin ve «Reprocesar» solo en las rojas", () => {
  it("un botón por celda roja reprocesable, con nombre accesible de fuente y vehículo", () => {
    pintar(grilla(), true);
    const botones = accionesDe();
    expect(botones).toHaveLength(1);
    expect(botones[0]).toHaveAccessibleName("Reprocesar SIMIT propietario de TST002");
  });

  it("la celda bloqueada por un RUNT en error no se reprocesa sola: dice por qué y no lleva botón", () => {
    const { container } = pintar(loteTerminadoConErrores(), true);
    expect(accionesDe()).toHaveLength(1);
    expect(accionesDe()[0]).toHaveAccessibleName("Reprocesar RUNT vehículo de TST004");
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

describe("GrillaSemaforos — fallos del proveedor: AUTENTICACION y LIMITE_PROVEEDOR", () => {
  it.each([
    ["AUTENTICACION", "Credenciales del proveedor rechazadas"],
    ["LIMITE_PROVEEDOR", "Límite del proveedor"],
    ["TIMEOUT", "No se pudo consultar"],
  ])("la celda en ERROR %s dice «%s» bajo el chip y conserva «Reprocesar»", (tipo, nota) => {
    const g = grilla({ vehiculos: [vehiculo(2, [celda(2, "RUNT", "COMPLETADO"), celda(2, "SIMIT_PROPIETARIO", "ERROR", { tipo_error: tipo })])] });
    const { container } = pintar(g, true);
    const td = celdaDe(container, "celda-2-simit_propietario");
    expect(td).toHaveTextContent(nota);
    expect(td.textContent).not.toContain(tipo);
    expect(accionesDe().map((b) => b.getAttribute("aria-label") ?? b.textContent)).toContainEqual(expect.stringMatching(/Reprocesar SIMIT propietario de TST002/));
  });
});

describe("GrillaSemaforos — AC6: dato reutilizado y sin indicador manual", () => {
  it("la celda reutilizada muestra la fecha y la hora del dato (hora de Colombia)", () => {
    const { container } = pintar();
    expect(celdaDe(container, "celda-1-runt")).toHaveTextContent("Dato del 21/10/2026 09:05");
  });

  it("HU #14584 §6.2: la celda no reutilizada también lleva la fecha del dato (tooltip y línea móvil)", () => {
    const { container } = pintar();
    expect(celdaDe(container, "celda-2-runt")).toHaveTextContent("Dato del 21/10/2026 10:00");
  });

  it("no existe ningún indicador de dato manual ni de origen", () => {
    const { container } = pintar(grilla({ vehiculos: [V1, V2, V3] }), true);
    expect(container).not.toHaveTextContent(/manual|origen|llenad[oa] a mano/i);
    expect(container.querySelector("[data-origen], [data-manual]")).toBeNull();
  });

  it("marcaCelda pinta su marca bajo el semáforo (backoffice)", () => {
    const { container } = pintar(grilla(), false, { marcaCelda: (c) => (c.id === "celda-2-runt" ? <span>marca</span> : null) });
    expect(celdaDe(container, "celda-2-runt")).toHaveTextContent("marca");
  });
});

describe("GrillaSemaforos — HU #13851: columna Reporte opcional", () => {
  it("sin accionVehiculo no hay columna Reporte (backoffice y compañía sin la capacidad)", () => {
    pintar();
    expect(screen.queryByRole("columnheader", { name: "Reporte" })).not.toBeInTheDocument();
  });

  it("con accionVehiculo pinta la columna Reporte con la acción de cada fila", () => {
    pintar(grilla(), false, { accionVehiculo: (v) => <span>{`acción ${v.identificador}`}</span> });
    expect(screen.getByRole("columnheader", { name: "Reporte" })).toBeInTheDocument();
    const fila = screen.getByRole("rowheader", { name: /TST002/ }).closest("tr")!;
    expect(within(fila).getByText("acción TST002")).toBeInTheDocument();
  });
});

describe("GrillaSemaforos — HU #14087 AC9: accesibilidad y desborde", () => {
  it("toda cabecera lleva scope y el desborde horizontal queda en el contenedor de la tabla", () => {
    const { container } = pintar(grilla(), false, { accionVehiculo: () => <span>pdf</span> });
    for (const th of within(screen.getByRole("table")).getAllByRole("columnheader")) expect(th.getAttribute("scope")).toMatch(/^(col|colgroup)$/);
    for (const th of screen.getAllByRole("rowheader")) expect(th).toHaveAttribute("scope", "row");
    expect(screen.getByRole("table").parentElement).toHaveClass("overflow-x-auto");
    for (const b of within(container).getAllByRole("link", { name: /^Ver detalle de / })) expect(b.className).toMatch(/focus-visible/);
  });
});

// HU #13866 (F09-H7): la columna del impuesto usa los chips que ya existen (CeldaSemaforo); con HU #14087 la N/A es «Novedad».
describe("GrillaSemaforos — HU #13866: columna del impuesto departamental", () => {
  const conImpuesto = (impuesto: Parameters<typeof celda>[2], cambios: Parameters<typeof celda>[3] = {}) =>
    grilla({
      columnas: { por_vehiculo: ["RUNT", "IMPUESTO_DEPARTAMENTAL"], por_propietario: [], por_locatario: [] },
      vehiculos: [vehiculo(7, [celda(7, "RUNT", "COMPLETADO"), celda(7, "IMPUESTO_DEPARTAMENTAL", impuesto, cambios)])],
    });

  it.each([
    ["SERVICIO_PUBLICO", "Servicio público"],
    ["DEPARTAMENTO_NO_SOPORTADO", "Departamento no soportado"],
  ] as const)("N/A %s pinta el chip «Novedad» con el motivo «%s» y sin Reprocesar aunque sea admin", (motivo, textoMotivo) => {
    const { container } = pintar(conImpuesto("NA", { motivo_na: motivo }), true);
    const td = celdaDe(container, "celda-7-impuesto_departamental");
    expect(td).toHaveAttribute("data-estado", "NA");
    expect(chipDe(td)).toHaveTextContent("Novedad");
    expect(td).toHaveTextContent(textoMotivo);
    expect(accionesDe()).toHaveLength(0);
    expect(within(td).getByRole("link", { name: `Ver detalle de Impuesto departamental de TST007: Novedad${NUEVA}` })).toBeInTheDocument();
  });

  it("el impuesto completado se pinta en verde («Ejecutado») y su semáforo abre el detalle", () => {
    const { container } = pintar(conImpuesto("COMPLETADO"));
    const td = celdaDe(container, "celda-7-impuesto_departamental");
    expect(chipDe(td)).toHaveAttribute("data-tono", "ejecutado");
    expect(within(td).getByRole("link", { name: `Ver detalle de Impuesto departamental de TST007: Ejecutado${NUEVA}` })).toHaveAttribute("target", "_blank");
  });

  it("el impuesto completado con deuda no es verde, con o sin monto", () => {
    const { container } = pintar(conImpuesto("COMPLETADO", { tiene_deuda: true }));
    const td = celdaDe(container, "celda-7-impuesto_departamental");
    expect(td).toHaveAttribute("data-estado", "COMPLETADO");
    expect(chipDe(td)).toHaveAttribute("data-tono", "deuda");
    expect(chipDe(td)).toHaveTextContent("Con deuda");
    expect(within(td).getByRole("link", { name: `Ver detalle de Impuesto departamental de TST007: Con deuda${NUEVA}` })).toBeInTheDocument();
  });

  it("otra fuente completada sigue en verde aunque llegue tiene_deuda", () => {
    const { container } = pintar(grilla({
      columnas: { por_vehiculo: ["RUNT"], por_propietario: [], por_locatario: [] },
      vehiculos: [vehiculo(7, [celda(7, "RUNT", "COMPLETADO", { tiene_deuda: true })])],
    }));
    expect(chipDe(celdaDe(container, "celda-7-runt"))).toHaveAttribute("data-tono", "ejecutado");
  });
});

// HU #14562, auditoría de UI (B2, M6/AC56, M7/AC28, m-4, m-5): la grilla se usa a 360 px, pasa a tarjetas bajo 900 px,
// lleva el ícono de cada fuente en la cabecera y la cabecera es la del kit (13 px en negrita).
describe("GrillaSemaforos — HU #14562 auditoría: responsive, íconos de fuente y cabecera del kit", () => {
  const completa = () =>
    pintar(grilla(), false, {
      casillaVehiculo: () => <span>casilla</span>,
      dictamenVehiculo: () => <span>Viable</span>,
      accionVehiculo: () => <span>pdf</span>,
      tituloAccionVehiculo: "Acciones",
    });
  const clases = (el: Element) => (el.getAttribute("class") ?? "").split(/\s+/);

  it("B2: ninguna columna queda fija por debajo de 1024 px (sticky y desplazamiento solo con lg)", () => {
    const { container } = completa();
    const celdas = Array.from(container.querySelectorAll("tbody th, tbody td"));
    const fijas = celdas.filter((el) => clases(el).includes("lg:sticky"));
    // Casilla, placa y acciones: las tres fijas en escritorio.
    expect(fijas).toHaveLength(3 * container.querySelectorAll("tbody tr").length);
    for (const el of celdas) expect(clases(el), el.textContent ?? "").not.toContain("sticky");
    for (const el of container.querySelectorAll("th, td")) {
      expect(clases(el).filter((c) => /^(left|right)-/.test(c)), el.textContent ?? "").toEqual([]);
    }
    const acciones = screen.getByRole("columnheader", { name: "Acciones" });
    expect(clases(acciones)).toContain("lg:right-0");
  });

  it("AC56: bajo 900 px la tabla pasa a tarjetas con las recetas TABLA_MOVIL_* del repo", () => {
    const { container } = completa();
    const tabla = screen.getByRole("table", { name: /semáforo de consultas/i });
    for (const c of TABLA_MOVIL_TABLE_CLS.split(" ")) expect(clases(tabla)).toContain(c);
    for (const c of TABLA_MOVIL_THEAD_CLS.split(" ")) expect(clases(tabla.querySelector("thead")!)).toContain(c);
    for (const c of TABLA_MOVIL_TBODY_CLS.split(" ")) expect(clases(tabla.querySelector("tbody")!)).toContain(c);
    for (const fila of container.querySelectorAll("tbody tr")) {
      for (const c of TABLA_MOVIL_ROW_CLS.split(" ")) expect(clases(fila)).toContain(c);
    }
  });

  it("AC56: cada celda de la tarjeta dice su columna con data-label (la fuente, con su nombre)", () => {
    const { container } = completa();
    const fila = filaDe(container, "vehiculo-1");
    const etiquetas = Array.from(fila.children).map((td) => td.getAttribute("data-label"));
    expect(etiquetas).toEqual([
      "Seleccionar",
      "Placa o VIN",
      "Dictamen",
      "Vehículo", "Propietario", "Locatario", "Avalúo comercial", "Impuesto departamental",
      "Acciones",
    ]);
    for (const td of fila.children) for (const c of TABLA_MOVIL_CELL_CLS.split(" ")) expect(clases(td)).toContain(c);
    for (const c of TABLA_MOVIL_CELL_TITULO_CLS.split(" ")) expect(clases(within(fila).getByRole("rowheader"))).toContain(c);
  });

  it("M7/AC28: la cabecera lleva el ícono IconoDx de cada sujeto y fuente única, decorativo y a la izquierda del rótulo", async () => {
    pintar();
    const tabla = screen.getByRole("table", { name: /semáforo de consultas/i });
    const svgDe = (nombre: string) => within(tabla).getByRole("columnheader", { name: nombre }).querySelector("svg");
    // Import dinámico y no estático: así un ícono que falta hace fallar este caso y no el archivo entero.
    const icono = async (nombre: string) => {
      const modulo = (await import(/* @vite-ignore */ join(DIR_ICONOS, `${nombre}.tsx`)).catch(() => undefined)) as
        | Record<string, ComponentType<{ tamano?: 16 }>>
        | undefined;
      expect(modulo, nombre).toBeDefined();
      return createElement(modulo![nombre], { tamano: 16 });
    };
    const esperado: Record<string, string> = {
      Vehículo: "IconoDxRunt",
      Propietario: "IconoDxRuntPersona",
      Locatario: "IconoDxLocatario",
      "Avalúo comercial": "IconoDxAvaluo",
      "Impuesto departamental": "IconoDxImpuesto",
    };
    for (const [nombre, componente] of Object.entries(esperado)) {
      const svg = svgDe(nombre);
      expect(svg, nombre).not.toBeNull();
      // Ícono a la izquierda: es el primer hijo del rótulo, antes del texto.
      expect(svg!.parentElement!.firstChild, nombre).toBe(svg);
      expect(svg).toHaveAttribute("aria-hidden", "true");
      expect(svg!.innerHTML, nombre).toBe(render(await icono(componente)).container.querySelector("svg")!.innerHTML);
    }
  });

  it("m-4: las cabeceras usan la celda de cabecera del kit (13 px en negrita)", () => {
    pintar();
    for (const th of screen.getAllByRole("columnheader")) {
      expect(clases(th)).toContain("text-[0.8125rem]");
      expect(clases(th)).toContain("font-bold");
    }
  });

  it("m-5/m-1: el enlace del semáforo anima con el token --dx-motion-rapido y queda quieto con menos movimiento", () => {
    pintar();
    const enlace = screen.getAllByRole("link", { name: /^Ver detalle de / })[0];
    expect(clases(enlace)).toContain("motion-reduce:transition-none");
    expect(clases(enlace)).toContain("duration-[var(--dx-motion-rapido)]");
  });
});

// Ola Diagnóstico UX 2026-11-15 · FE-2 (spec-ui §2.6): «RUNT» y «SIMIT» centrados sobre sus subcolumnas; las subcolumnas
// dicen «vehículo», «propietario» y «locatario», con el nombre completo para lectores; nombreFuente() no cambia.
describe("GrillaSemaforos — columnas por sujeto: dos badges RUNT/SIMIT por celda", () => {
  const todas: Grilla["columnas"] = {
    por_vehiculo: ["RUNT", "SIMIT_VEHICULO", "AVALUO", "IMPUESTO_DEPARTAMENTAL"],
    por_propietario: ["RUNT_PROPIETARIO", "SIMIT_PROPIETARIO"],
    por_locatario: ["RUNT_LOCATARIO", "SIMIT_LOCATARIO"],
  };
  const PARES = [
    ["Vehículo", "RUNT", "SIMIT_VEHICULO"],
    ["Propietario", "RUNT_PROPIETARIO", "SIMIT_PROPIETARIO"],
    ["Locatario", "RUNT_LOCATARIO", "SIMIT_LOCATARIO"],
  ] as const;

  it("sin encabezados «RUNT» ni «SIMIT»: ningún th los dice ni agrupa (colgroup)", () => {
    const { container } = pintar();
    const tabla = screen.getByRole("table", { name: /semáforo de consultas/i });
    expect(within(tabla).queryByRole("columnheader", { name: /^(RUNT|SIMIT)/ })).not.toBeInTheDocument();
    expect(container.querySelector('th[scope="colgroup"]')).toBeNull();
  });

  it.each(PARES)("la columna %s apila dos badges con prefijo, cada uno con su enlace y su aria-label con fuente y estado", (sujeto, runt, simit) => {
    const v = vehiculo(9, [celda(9, runt, "COMPLETADO"), celda(9, simit, "ERROR")]);
    const { container } = pintar(grilla({ columnas: todas, vehiculos: [v] }));
    const td = filaDe(container, "vehiculo-9").querySelector<HTMLElement>(`td[data-label="${sujeto}"]`)!;
    expect(td).not.toBeNull();
    const badges = Array.from(td.querySelectorAll<HTMLElement>("[data-fuente]"));
    expect(badges.map((b) => b.getAttribute("data-fuente"))).toEqual([runt, simit]);
    expect(chipDe(badges[0])).toHaveTextContent(/^RUNT · Ejecutado$/);
    expect(chipDe(badges[1])).toHaveTextContent(/^SIMIT · Error$/);
    for (const b of badges) expect(chipDe(b).querySelector("svg")).not.toBeNull();
    const enlaces = within(td).getAllByRole("link");
    expect(enlaces).toHaveLength(2);
    expect(enlaces[0]).toHaveAttribute("href", `/diagnosticos/${grilla().id}/vehiculos/vehiculo-9/consultas/${runt}`);
    expect(enlaces[1]).toHaveAttribute("href", `/diagnosticos/${grilla().id}/vehiculos/vehiculo-9/consultas/${simit}`);
    expect(enlaces[0]).toHaveAccessibleName(`Ver detalle de ${nombreFuente(runt)} de ${v.identificador}: RUNT · Ejecutado${NUEVA}`);
    expect(enlaces[1]).toHaveAccessibleName(`Ver detalle de ${nombreFuente(simit)} de ${v.identificador}: SIMIT · Error${NUEVA}`);
  });

  it("hrefDetalle propio (backoffice) se aplica a cada badge con su propia fuente", () => {
    const v = vehiculo(9, [celda(9, "RUNT_PROPIETARIO", "COMPLETADO"), celda(9, "SIMIT_PROPIETARIO", "COMPLETADO")]);
    const { container } = pintar(grilla({ columnas: todas, vehiculos: [v] }), false, { hrefDetalle: (c) => `/bo/${c.fuente}` });
    const td = filaDe(container, "vehiculo-9").querySelector<HTMLElement>('td[data-label="Propietario"]')!;
    expect(within(td).getAllByRole("link").map((a) => a.getAttribute("href"))).toEqual(["/bo/RUNT_PROPIETARIO", "/bo/SIMIT_PROPIETARIO"]);
  });

  it("el tooltip de cada badge nombra su fuente; el no pedido dice «Sin solicitar» con su prefijo", () => {
    const v = vehiculo(9, [celda(9, "RUNT", "COMPLETADO")]);
    const { container } = pintar(grilla({ columnas: { por_vehiculo: ["RUNT"], por_propietario: [], por_locatario: [] }, vehiculos: [v] }));
    const fila = filaDe(container, "vehiculo-9");
    act(() => within(fuenteDe(fila, "RUNT")).getByRole("link").focus());
    expect(screen.getByRole("tooltip")).toHaveTextContent("RUNT vehículo · Ejecutado");
    const simit = fuenteDe(fila, "SIMIT_VEHICULO");
    expect(chipDe(simit)).toHaveTextContent(/^SIMIT · Sin solicitar$/);
    expect(simit.querySelector(".sr-only")).toHaveTextContent(`SIMIT vehículo de ${v.identificador}: Sin solicitar`);
  });

  it("Avalúo e Impuesto no cambian: una td por fuente y badge sin prefijo", () => {
    const { container } = pintar();
    const avaluo = fuenteDe(filaDe(container, "vehiculo-1"), "AVALUO");
    expect(avaluo.tagName).toBe("TD");
    expect(chipDe(avaluo)).toHaveTextContent(/^Sin solicitar$/);
    expect(container.querySelector('[data-fuente="RUNT_PROPIETARIO"]')).toHaveAttribute("data-label", "RUNT propietario");
  });

  it("nombreSubfuente da el rótulo corto y nombreFuente sigue con el nombre completo", () => {
    expect(nombreSubfuente("RUNT")).toBe("vehículo");
    expect(nombreSubfuente("RUNT_PROPIETARIO")).toBe("propietario");
    expect(nombreSubfuente("SIMIT_LOCATARIO")).toBe("locatario");
    expect(nombreSubfuente("AVALUO")).toBe("Avalúo comercial");
    expect(FUENTES.map((f) => nombreFuente(f.codigo))).toEqual([
      "RUNT vehículo",
      "RUNT propietario",
      "RUNT locatario",
      "SIMIT vehículo",
      "SIMIT propietario",
      "SIMIT locatario",
      "Avalúo comercial",
      "Impuesto departamental",
    ]);
  });

  it("la columna de acciones mide 10rem en th y td y sigue fija a la derecha", () => {
    const { container } = pintar(grilla(), false, { accionVehiculo: () => "x", tituloAccionVehiculo: "Acciones" });
    const th = within(screen.getByRole("table", { name: /semáforo de consultas/i })).getByRole("columnheader", { name: "Acciones" });
    expect(th.className).toContain("w-[10rem]");
    expect(th.className).toContain("min-w-[10rem]");
    const td = container.querySelector('td[data-label="Acciones"]') as HTMLElement;
    expect(td.className).toContain("w-[10rem]");
    expect(td.className).toContain("lg:right-0");
  });
});

// Ronda 2 de E2E: un único patrón para los contenedores con scroll horizontal de la ola (RegionDesplazable). La grilla
// mide 1280 px como mínimo: con desborde es región con nombre y parada de Tab; sin él, div plano.
describe("GrillaSemaforos — región desplazable (ronda 2 de E2E)", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("con desborde, el contenedor de la grilla es región «Semáforo de consultas del lote, desplazable» enfocable", () => {
    simularDesborde({ ancho: 1280 });
    pintar();
    const region = screen.getByRole("region", { name: "Semáforo de consultas del lote, desplazable" });
    expect(region).toHaveAttribute("tabindex", "0");
    expect(region).toHaveClass("overflow-x-auto", "rounded-xl", "border");
    expect(within(region).getByRole("table", { name: "Semáforo de consultas del lote" })).toBeInTheDocument();
  });

  it("sin desborde no hay región ni parada de Tab", () => {
    simularDesborde({ ancho: 300 });
    pintar();
    expect(screen.queryByRole("region", { name: /desplazable/ })).not.toBeInTheDocument();
    expect(screen.getByRole("table").parentElement).not.toHaveAttribute("tabindex");
  });
});

describe("GrillaSemaforos — HU #14584 M-2 (A6.2): la cabecera de las columnas fijas queda por encima de las demás", () => {
  const clases = (el: Element) => el.className.split(/\s+/);

  it("las th fijas (casilla, «Placa o VIN» y «Acciones») van en z-20; las th sticky normales siguen en z-10", () => {
    const { container } = pintar(grilla({ vehiculos: [V1] }), false, {
      casillaVehiculo: () => <input type="checkbox" aria-label="Seleccionar" />,
      accionVehiculo: () => <span>acción</span>,
      tituloAccionVehiculo: "Acciones",
    });
    const tabla = screen.getByRole("table", { name: /semáforo de consultas/i });
    const fijas = [
      container.querySelector<HTMLElement>("thead tr:first-child th:first-child")!,
      within(tabla).getByRole("columnheader", { name: "Placa o VIN" }),
      within(tabla).getByRole("columnheader", { name: "Acciones" }),
    ];
    for (const th of fijas) {
      expect(clases(th)).toContain("z-20");
      expect(clases(th)).not.toContain("z-10");
      expect(clases(th)).toContain("lg:sticky");
    }
    const normales = Array.from(container.querySelectorAll<HTMLElement>("thead th")).filter((th) => !clases(th).includes("min-[900px]:sticky"));
    expect(normales.length).toBeGreaterThan(0);
    for (const th of normales) expect(clases(th)).toContain("z-10");
  });

  it("las celdas fijas del cuerpo quedan por debajo de la cabecera (z-10) y alineadas con su th (mismo left/right y ancho)", () => {
    const { container } = pintar(grilla({ vehiculos: [V1] }), false, {
      casillaVehiculo: () => <input type="checkbox" aria-label="Seleccionar" />,
      accionVehiculo: () => <span>acción</span>,
    });
    const fila = filaDe(container, "vehiculo-1");
    const casilla = fila.querySelector<HTMLElement>('[data-columna="seleccion"]')!;
    const placa = within(fila).getByRole("rowheader");
    const acciones = fila.querySelector<HTMLElement>("td:last-child")!;
    for (const celdaFija of [casilla, placa, acciones]) {
      expect(clases(celdaFija)).toContain("z-10");
      expect(clases(celdaFija)).not.toContain("z-20");
    }
    const ths = container.querySelectorAll<HTMLElement>("thead tr:first-child th");
    expect(clases(ths[0])).toContain("lg:left-0");
    expect(clases(casilla)).toContain("lg:left-0");
    expect(clases(ths[1])).toContain("lg:left-10");
    expect(clases(placa)).toContain("lg:left-10");
    const thAcciones = ths[ths.length - 1];
    for (const c of ["lg:right-0", "w-[10rem]", "min-w-[10rem]"]) {
      expect(clases(thAcciones)).toContain(c);
      expect(clases(acciones)).toContain(c);
    }
  });
});
