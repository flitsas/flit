import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { GrillaSemaforos } from "@/components/tablero/GrillaSemaforos";
import { FUENTES } from "@/lib/configuracion";
import { V1, V2, V3, celda, grilla, llamadas, loteTerminadoConErrores, simularApi, vehiculo } from "@/lib/tablero/__tests__/datos";
import type { Grilla } from "@/lib/tablero/api";

// Uso de ejemplo: <GrillaSemaforos grilla={grilla} puedeReprocesar={sesion.capacidades.reprocesarConsultas}
// onReprocesada={(celda) => …} onErrorReproceso={(mensaje) => …} /> pinta una fila por vehículo (placa o VIN, documento
// declarado y documentos del propietario) y SIEMPRE las 8 fuentes agrupadas por pilar (RUNT, SIMIT, Avalúo comercial,
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

/** Botones de acción: todos menos el semáforo, que desde HU #13801 es un botón «Ver detalle de …» que abre el detalle. */
const accionesDe = (raiz: Pick<typeof screen, "queryAllByRole"> = screen) =>
  raiz.queryAllByRole("button").filter((b) => !/^Ver detalle de /.test(b.getAttribute("aria-label") ?? ""));

describe("GrillaSemaforos — HU #14087 AC3: columnas agrupadas por pilar, las 8 fuentes siempre", () => {
  it("cabecera de dos niveles: RUNT y SIMIT agrupan sus 3 fuentes; Avalúo comercial e Impuesto departamental, la suya", () => {
    pintar();
    const tabla = screen.getByRole("table", { name: /semáforo de consultas/i });
    const runt = within(tabla).getByRole("columnheader", { name: "RUNT" });
    expect(runt).toHaveAttribute("colspan", "3");
    expect(runt).toHaveAttribute("scope", "colgroup");
    const simit = within(tabla).getByRole("columnheader", { name: "SIMIT" });
    expect(simit).toHaveAttribute("colspan", "3");
    expect(simit).toHaveAttribute("scope", "colgroup");
    for (const nombre of FUENTES.map((f) => f.nombre)) expect(within(tabla).getByRole("columnheader", { name: nombre })).toBeInTheDocument();
    expect(within(tabla).queryByRole("columnheader", { name: "Por vehículo" })).not.toBeInTheDocument();
  });

  it("las 8 fuentes aparecen en cada fila en el orden de FUENTES aunque el lote pidiera solo una", () => {
    const { container } = pintar(grilla({ columnas: { por_vehiculo: ["RUNT"], por_propietario: [], por_locatario: [] }, vehiculos: [vehiculo(1, [celda(1, "RUNT", "COMPLETADO")])] }));
    const fila = filaDe(container, "vehiculo-1");
    expect(Array.from(fila.querySelectorAll("[data-fuente]")).map((td) => td.getAttribute("data-fuente"))).toEqual(FUENTES.map((f) => f.codigo));
    const cabeceras = Array.from(container.querySelectorAll("thead tr:last-child th")).map((th) => th.textContent);
    expect(cabeceras).toEqual(["RUNT vehículo", "RUNT propietario", "RUNT locatario", "SIMIT vehículo", "SIMIT propietario", "SIMIT locatario"]);
  });

  it("la fuente no pedida en el lote dice «Sin solicitar» en cada fila y no es un botón", () => {
    const { container } = pintar();
    for (const id of ["vehiculo-1", "vehiculo-2", "vehiculo-3"]) {
      for (const fuente of ["RUNT_PROPIETARIO", "SIMIT_VEHICULO", "SIMIT_LOCATARIO", "AVALUO"]) {
        const td = fuenteDe(filaDe(container, id), fuente);
        expect(td).toHaveAttribute("data-estado", "SIN_SOLICITAR");
        expect(td).toHaveTextContent("Sin solicitar");
        expect(within(td).queryByRole("button")).not.toBeInTheDocument();
      }
    }
    expect(screen.queryAllByRole("button", { name: /^Ver detalle de (RUNT propietario|SIMIT vehículo|SIMIT locatario|Avalúo comercial) /i })).toHaveLength(0);
  });

  it("la fuente pedida cuya celda aún no llegó dice «Sin ejecutar», sin botón y sin romper la fila", () => {
    const { container } = pintar(grilla({ vehiculos: [vehiculo(5, [celda(5, "RUNT", "PENDIENTE")])] }));
    const fila = filaDe(container, "vehiculo-5");
    for (const fuente of ["IMPUESTO_DEPARTAMENTAL", "SIMIT_PROPIETARIO", "RUNT_LOCATARIO"]) {
      const td = fuenteDe(fila, fuente);
      expect(td).toHaveTextContent("Sin ejecutar");
      expect(within(td).queryByRole("button")).not.toBeInTheDocument();
    }
    expect(within(fila).getAllByRole("cell")).toHaveLength(2 + 8);
  });
});

describe("GrillaSemaforos — HU #14087 AC4: placa, documento declarado y documentos del propietario", () => {
  it("columnas Placa o VIN, Documento declarado y Documento del propietario antes de las fuentes", () => {
    const { container } = pintar();
    const primeras = Array.from(container.querySelectorAll("thead tr:first-child th")).slice(0, 3);
    expect(primeras.map((th) => th.textContent)).toEqual(["Placa o VIN", "Documento declarado", "Documento del propietario"]);
    for (const th of primeras) expect(th).toHaveAttribute("scope", "col");
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

  it("el documento declarado se ve con su tipo, enmascarado y su rol; uno completo se vuelve a enmascarar", () => {
    const declarado = vehiculo(1, [celda(1, "RUNT", "COMPLETADO")], {
      documento_declarado: { rol: "LOCATARIO", tipo_documento: "C", documento_enmascarado: "****5678" },
    });
    const completo = vehiculo(2, [celda(2, "RUNT", "COMPLETADO")], {
      documento_declarado: { rol: "PROPIETARIO", tipo_documento: "NIT", documento_enmascarado: "9001234567" },
    });
    const { container } = pintar(grilla({ vehiculos: [declarado, completo] }));
    const d1 = filaDe(container, "vehiculo-1").querySelector<HTMLElement>("[data-columna='documento-declarado']")!;
    expect(d1).toHaveTextContent("C ****5678");
    expect(d1).toHaveTextContent("Locatario");
    const d2 = filaDe(container, "vehiculo-2").querySelector<HTMLElement>("[data-columna='documento-declarado']")!;
    expect(d2).toHaveTextContent("NIT ****4567");
    expect(d2).toHaveTextContent("Propietario");
    expect(container).not.toHaveTextContent("9001234567");
  });

  it("los documentos del propietario van uno por línea, con su tipo y enmascarados", () => {
    const v = vehiculo(1, [celda(1, "RUNT", "COMPLETADO")], {
      propietarios: [
        { tipo_documento: "C", documento_enmascarado: "****1222" },
        { tipo_documento: "NIT", documento_enmascarado: "8001112223" },
      ],
    });
    const { container } = pintar(grilla({ vehiculos: [v] }));
    const td = filaDe(container, "vehiculo-1").querySelector<HTMLElement>("[data-columna='documento-propietario']")!;
    expect(within(td).getAllByRole("listitem").map((li) => li.textContent)).toEqual(["C ****1222", "NIT ****2223"]);
    expect(container).not.toHaveTextContent("8001112223");
  });

  it("sin documento declarado ni propietarios (o sin las claves) muestra «—»", () => {
    const sinClaves = { ...vehiculo(2, [celda(2, "RUNT", "PENDIENTE")]) } as Record<string, unknown>;
    delete sinClaves.documento_declarado;
    delete sinClaves.propietarios;
    const { container } = pintar(grilla({ vehiculos: [vehiculo(1, [celda(1, "RUNT", "PENDIENTE")]), sinClaves as never] }));
    for (const id of ["vehiculo-1", "vehiculo-2"]) {
      expect(filaDe(container, id).querySelector("[data-columna='documento-declarado']")).toHaveTextContent(/^—$/);
      expect(filaDe(container, id).querySelector("[data-columna='documento-propietario']")).toHaveTextContent(/^—$/);
    }
  });
});

describe("GrillaSemaforos — HU #14087 AC5: estados con icono y texto", () => {
  it.each([
    ["celda-3-runt", "PENDIENTE", "neutral", "Sin ejecutar", "var(--badge-neutral-bg)"],
    ["celda-2-runt_locatario", "EN_CURSO", "neutral", "Sin ejecutar", "var(--badge-neutral-bg)"],
    ["celda-2-runt", "COMPLETADO", "success", "Ejecutado (OK)", "var(--badge-success-bg)"],
    ["celda-2-simit_propietario", "ERROR", "danger", "Error", "var(--badge-danger-bg)"],
    ["celda-1-impuesto_departamental", "NA", "warning", "Novedad", "var(--badge-warning-bg)"],
  ])("%s en %s se pinta con el tono %s, su texto «%s», un icono y el token de fondo", (id, estado, tono, texto, fondo) => {
    const { container } = pintar();
    const td = celdaDe(container, id);
    expect(td).toHaveAttribute("data-estado", estado);
    const chip = chipDe(td);
    expect(chip).toHaveAttribute("data-tono", tono);
    expect(chip).toHaveTextContent(new RegExp(`^${texto.replace(/[()]/g, "\\$&")}$`));
    expect(chip.querySelector("svg")).not.toBeNull();
    expect(chip.style.background).toBe(fondo);
  });

  it("EN_CURSO conserva el icono animado; PENDIENTE no", () => {
    const { container } = pintar();
    expect(chipDe(celdaDe(container, "celda-2-runt_locatario")).querySelector("svg")!.getAttribute("class")).toMatch(/animate-spin/);
    expect(chipDe(celdaDe(container, "celda-3-runt")).querySelector("svg")!.getAttribute("class")).not.toMatch(/animate-spin/);
  });

  it("«Sin solicitar» usa el tono na punteado, con icono", () => {
    const { container } = pintar();
    const chip = chipDe(fuenteDe(filaDe(container, "vehiculo-1"), "AVALUO"));
    expect(chip).toHaveAttribute("data-tono", "na");
    expect(chip.style.borderStyle).toBe("dashed");
    expect(chip.querySelector("svg")).not.toBeNull();
    expect(chip).toHaveTextContent("Sin solicitar");
  });

  it("el texto del error usa la tinta AA del rojo (no el --badge-danger-fg de 4,46:1)", () => {
    const { container } = pintar();
    expect(chipDe(celdaDe(container, "celda-2-simit_propietario")).style.color).toBe("var(--badge-pending-fg)");
  });

  it.each([
    ["celda-1-impuesto_departamental", "Servicio público"],
    ["celda-2-impuesto_departamental", "Departamento no soportado"],
    ["celda-1-runt_locatario", "Sin documento del locatario"],
  ])("la novedad %s muestra su motivo «%s» debajo", (id, motivo) => {
    const { container } = pintar();
    expect(celdaDe(container, id)).toHaveTextContent(`Novedad${motivo}`);
  });

  it("el nombre accesible del botón de la celda lleva fuente, identificador y estado nuevo", () => {
    pintar();
    expect(screen.getByRole("button", { name: "Ver detalle de RUNT vehículo de TST002: Ejecutado (OK)" })).toHaveAttribute("aria-haspopup", "dialog");
    expect(screen.getByRole("button", { name: "Ver detalle de Impuesto departamental de TST001: Novedad" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ver detalle de RUNT vehículo de TST003: Sin ejecutar" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ver detalle de SIMIT propietario de TST002: Error" })).toBeInTheDocument();
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
    for (const b of within(container).getAllByRole("button", { name: /^Ver detalle de / })) expect(b.className).toMatch(/focus-visible/);
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
    expect(within(td).getByRole("button", { name: "Ver detalle de Impuesto departamental de TST007: Novedad" })).toBeInTheDocument();
  });

  it("el impuesto completado se pinta en verde («Ejecutado (OK)») y su semáforo abre el detalle", () => {
    const { container } = pintar(conImpuesto("COMPLETADO"));
    const td = celdaDe(container, "celda-7-impuesto_departamental");
    expect(chipDe(td)).toHaveAttribute("data-tono", "success");
    expect(within(td).getByRole("button", { name: "Ver detalle de Impuesto departamental de TST007: Ejecutado (OK)" })).toHaveAttribute("aria-haspopup", "dialog");
  });
});
