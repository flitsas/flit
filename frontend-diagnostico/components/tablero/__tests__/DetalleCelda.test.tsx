import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DetalleCelda } from "@/components/tablero/DetalleCelda";
import { GrillaSemaforos } from "@/components/tablero/GrillaSemaforos";
import type { Fuente } from "@/lib/configuracion";
import type { DetalleCelda as Detalle, ResultadoSimitDetalle } from "@/lib/tablero/detalle";
import { ID, celda, grilla, llamadas, simularApi, vehiculo } from "@/lib/tablero/__tests__/datos";
import {
  avaluoConValor,
  avaluoSinValor,
  comparendo,
  detalle,
  impuestoConVigencias,
  impuestoNa,
  impuestoSinVigencias,
  persona,
  runtLocatarioDeclarado,
  runtPropietarioConError,
  runtTresPropietarios,
  runtVehiculo,
  simitVehiculo,
} from "@/lib/tablero/__tests__/datosDetalle";

// Uso de ejemplo: en el tablero, cada semáforo es un botón «Ver detalle de {fuente} de {placa}»; al pulsarlo (clic, Enter o
// Espacio) GrillaSemaforos abre <DetalleCelda loteId vehiculoId fuente … onClose devolverFocoA={semáforo} />, un diálogo
// accesible que pide GET /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente} y pinta la vista de esa fuente: datos del RUNT,
// comparendos del SIMIT, avalúo («sin valor comercial») o una sección por persona con su documento enmascarado.

afterEach(() => vi.unstubAllGlobals());

const FUENTES: Fuente[] = ["RUNT", "SIMIT_VEHICULO", "AVALUO", "RUNT_PROPIETARIO", "RUNT_LOCATARIO"];
const NOMBRES: Record<string, string> = {
  RUNT: "RUNT vehículo",
  SIMIT_VEHICULO: "SIMIT vehículo",
  AVALUO: "Avalúo comercial",
  RUNT_PROPIETARIO: "RUNT propietario",
  RUNT_LOCATARIO: "RUNT locatario",
  IMPUESTO_DEPARTAMENTAL: "Impuesto departamental",
};

const rutaDetalle = (fuente: Fuente) => `GET /diagnosticos/${ID}/vehiculos/vehiculo-1/consultas/${fuente}`;

/** Tablero de un vehículo TST001 con una celda por fuente en el estado pedido. */
function pintarGrilla(estados: Partial<Record<Fuente, Parameters<typeof celda>[2]>> = {}) {
  const v = vehiculo(
    1,
    FUENTES.map((f) => celda(1, f, estados[f] ?? "COMPLETADO")),
  );
  const g = grilla({
    vehiculos: [v],
    columnas: { por_vehiculo: ["RUNT", "SIMIT_VEHICULO", "AVALUO"], por_propietario: ["RUNT_PROPIETARIO"], por_locatario: ["RUNT_LOCATARIO"] },
  });
  return render(<GrillaSemaforos grilla={g} puedeReprocesar={false} />);
}

const semaforo = (fuente: Fuente) => screen.getByRole("button", { name: new RegExp(`^Ver detalle de ${NOMBRES[fuente]} de TST001`) });

async function abrir(fuente: Fuente, respuesta: Detalle | Response) {
  const api = simularApi({ [rutaDetalle(fuente)]: () => (respuesta instanceof Response ? respuesta : Response.json(respuesta)) });
  pintarGrilla();
  const boton = semaforo(fuente);
  boton.focus();
  fireEvent.click(boton);
  const dialogo = await screen.findByRole("dialog", { name: `${NOMBRES[fuente]} · TST001` });
  return { api, dialogo, boton };
}

/** Pinta DetalleCelda suelto con un cargador fijo y espera a que termine de cargar. */
async function pintarDetalle(d: Detalle) {
  const utils = render(
    <DetalleCelda loteId={ID} vehiculoId="vehiculo-1" fuente={d.fuente} nombreFuente={NOMBRES[d.fuente]} identificador="TST001" onClose={() => {}} cargar={async () => d} />,
  );
  const dialogo = await screen.findByRole("dialog");
  await waitFor(() => expect(within(dialogo).queryByRole("status", { name: /cargando/i })).not.toBeInTheDocument());
  return { ...utils, dialogo };
}

describe("DetalleCelda — el semáforo abre el detalle", () => {
  it("cada semáforo es un botón con nombre accesible de fuente, vehículo y estado, y anuncia que abre un diálogo", () => {
    simularApi({});
    pintarGrilla({ AVALUO: "ERROR" });
    const boton = semaforo("AVALUO");
    expect(boton).toHaveAttribute("type", "button");
    expect(boton).toHaveAttribute("aria-haspopup", "dialog");
    expect(boton).toHaveAccessibleName("Ver detalle de Avalúo comercial de TST001: Error");
  });

  it("pide el detalle por vehículo y fuente al BFF", async () => {
    const { api } = await abrir("SIMIT_VEHICULO", simitVehiculo());
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/vehiculos/vehiculo-1/consultas/SIMIT_VEHICULO`)).toHaveLength(1);
  });
});

describe("DetalleCelda — AC1: SIMIT con comparendos, valores, estados y fecha del dato", () => {
  it("pinta la tabla de comparendos con número, fecha, valor en pesos, estado y organismo, y la fecha del dato", async () => {
    const { dialogo } = await abrir("SIMIT_VEHICULO", simitVehiculo([comparendo(1), comparendo(2, { valor: "1250000", estado: "PAGADO" })]));
    const tabla = within(dialogo).getByRole("table", { name: /comparendos/i });
    const filas = within(tabla).getAllByRole("row").slice(1);
    expect(filas).toHaveLength(2);
    expect(filas[0]).toHaveTextContent("C-0001");
    expect(filas[0]).toHaveTextContent("02/01/2026");
    expect(filas[0].textContent?.replace(/\s/g, " ")).toContain("$ 500.000");
    expect(filas[0]).toHaveTextContent("PENDIENTE");
    expect(filas[1].textContent?.replace(/\s/g, " ")).toContain("$ 1.250.000");
    expect(filas[1]).toHaveTextContent("PAGADO");
    expect(within(dialogo).getByText(/2 comparendos/i)).toBeInTheDocument();
    expect(within(dialogo).getByText("Dato del 22/10/2026 10:00")).toBeInTheDocument();
  });

  it("sin comparendos lo dice en vez de pintar una tabla vacía", async () => {
    const { dialogo } = await pintarDetalle(simitVehiculo([]));
    expect(within(dialogo).queryByRole("table")).not.toBeInTheDocument();
    expect(within(dialogo).getByText("Sin comparendos registrados.")).toBeInTheDocument();
  });

  it("una lista larga se limita a 10 y se expande con un botón que informa su estado", async () => {
    const lista = Array.from({ length: 30 }, (_, i) => comparendo(i + 1));
    const { dialogo } = await pintarDetalle(simitVehiculo(lista));
    const tabla = within(dialogo).getByRole("table", { name: /comparendos/i });
    expect(within(tabla).getAllByRole("row").slice(1)).toHaveLength(10);
    const ver = within(dialogo).getByRole("button", { name: "Ver los 30 comparendos" });
    expect(ver).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(ver);
    expect(within(tabla).getAllByRole("row").slice(1)).toHaveLength(30);
    expect(within(dialogo).getByRole("button", { name: "Ver menos" })).toHaveAttribute("aria-expanded", "true");
  });

  it("avisa cuando la API cortó la lista (comparendos_truncados)", async () => {
    const lista = Array.from({ length: 100 }, (_, i) => comparendo(i + 1));
    const { dialogo } = await pintarDetalle(simitVehiculo(lista, true, 250));
    expect(within(dialogo).getByText(/se muestran 100 de 250 comparendos/i)).toBeInTheDocument();
  });
});

describe("DetalleCelda — vistas por fuente", () => {
  it("RUNT del vehículo: placa, VIN, servicio, clase, organismo y propietarios", async () => {
    const { dialogo } = await pintarDetalle(runtVehiculo());
    for (const texto of ["TST001", "9ZZPRUEBA00000001", "PARTICULAR", "AUTOMOVIL", "STRIA PRUEBA"]) expect(within(dialogo).getByText(texto)).toBeInTheDocument();
    expect(within(dialogo).getByText(/1 propietario/i)).toBeInTheDocument();
  });

  it("avalúo con valor: valor sugerido y una fila por fuente", async () => {
    const { dialogo } = await pintarDetalle(avaluoConValor());
    expect(within(dialogo).getAllByText((_, el) => el?.textContent?.replace(/\s/g, " ") === "$ 45.000.000").length).toBeGreaterThan(0);
    const tabla = within(dialogo).getByRole("table", { name: /fuentes del avalúo/i });
    expect(within(tabla).getAllByRole("row").slice(1)).toHaveLength(2);
    expect(within(tabla).getByText("Fasecolda")).toBeInTheDocument();
    expect(within(tabla).getByText("Mercado Libre")).toBeInTheDocument();
  });
});

describe("DetalleCelda — AC2: documento declarado por el cliente", () => {
  it("el documento aparece enmascarado tal cual llega con el rótulo «declarado por el cliente»", async () => {
    const { dialogo } = await abrir("RUNT_LOCATARIO", runtLocatarioDeclarado());
    const seccion = within(dialogo).getByRole("region", { name: /locatario 1/i });
    expect(within(seccion).getByText("****5678")).toBeInTheDocument();
    expect(within(seccion).getByText(/declarado por el cliente/i)).toBeInTheDocument();
  });

  it("un documento corto llega como «****» y así se muestra, sin intentar reconstruirlo", async () => {
    const { dialogo } = await pintarDetalle(detalle("RUNT_LOCATARIO", { personas: [persona(1, { rol: "LOCATARIO", documento_enmascarado: "****" })] }));
    const seccion = within(dialogo).getByRole("region", { name: /locatario 1/i });
    expect(within(seccion).getByText("****")).toBeInTheDocument();
    expect(seccion.textContent).not.toMatch(/\*{4}\d/);
  });

  it("sin declarar no lleva el rótulo", async () => {
    const { dialogo } = await pintarDetalle(runtTresPropietarios());
    expect(within(dialogo).queryByText(/declarado por el cliente/i)).not.toBeInTheDocument();
  });
});

describe("DetalleCelda — AC3: una sección por persona", () => {
  it("tres propietarios, uno por RUES: tres secciones con su documento enmascarado y su resultado", async () => {
    const { dialogo } = await abrir("RUNT_PROPIETARIO", runtTresPropietarios());
    const secciones = within(dialogo).getAllByRole("region", { name: /^propietario \d/i });
    expect(secciones).toHaveLength(3);
    expect(within(secciones[0]).getByText("****1222")).toBeInTheDocument();
    expect(within(secciones[0]).getByText("Inscrito en el RUNT")).toBeInTheDocument();
    expect(within(secciones[0]).getByText(/B1/)).toBeInTheDocument();
    expect(within(secciones[1]).getByText("****3344")).toBeInTheDocument();
    expect(within(secciones[1]).getByText("No inscrito en el RUNT")).toBeInTheDocument();
    expect(within(secciones[2]).getByText("****4567")).toBeInTheDocument();
    expect(within(secciones[2]).getByText(/consultado en el RUES/i)).toBeInTheDocument();
    expect(within(secciones[2]).getByText("ACTIVA")).toBeInTheDocument();
    for (const s of secciones) expect(within(s).getByText("Consultada")).toBeInTheDocument();
  });

  it("SIMIT por persona reutiliza la vista de comparendos", async () => {
    const simit = simitVehiculo([comparendo(7)]).resultado as ResultadoSimitDetalle;
    const { dialogo } = await pintarDetalle(detalle("SIMIT_PROPIETARIO", { personas: [persona(1, { consulta: "SIMIT", resultado: simit })] }));
    const seccion = within(dialogo).getByRole("region", { name: /propietario 1/i });
    expect(within(seccion).getByRole("table", { name: /comparendos/i })).toHaveTextContent("C-0007");
  });

  it("celda ERROR PERSONAS_CON_ERROR: muestra lo que sí respondió y el error de cada persona que falló", async () => {
    const { dialogo } = await pintarDetalle(runtPropietarioConError());
    expect(within(dialogo).getByText(/personas no se pudieron consultar/i)).toBeInTheDocument();
    const [ok, fallida] = within(dialogo).getAllByRole("region", { name: /^propietario \d/i });
    expect(within(ok).getByText("Consultada")).toBeInTheDocument();
    expect(within(ok).getByText("Inscrito en el RUNT")).toBeInTheDocument();
    expect(within(fallida).getByText("Error")).toBeInTheDocument();
    expect(within(fallida).getByText(/no respondió a tiempo/i)).toBeInTheDocument();
    expect(within(fallida).getByText("****4567")).toBeInTheDocument();
  });

  it("sin personas del rol: estado vacío claro", async () => {
    const { dialogo } = await pintarDetalle(detalle("RUNT_LOCATARIO", { personas: [] }));
    expect(within(dialogo).getByText("El vehículo no tiene locatarios para esta consulta.")).toBeInTheDocument();
  });
});

describe("DetalleCelda — AC4: avalúo sin valor", () => {
  it("se lee «Sin valor comercial»", async () => {
    const { dialogo } = await abrir("AVALUO", avaluoSinValor());
    expect(within(dialogo).getByText("Sin valor comercial")).toBeInTheDocument();
    expect(within(dialogo).queryByText(/\$\s?\d/)).not.toBeInTheDocument();
  });
});

describe("DetalleCelda — AC5 (negativo): nada distingue una celda llenada a mano", () => {
  const SOSPECHOSOS = /manual|a mano|backoffice|origen|usuario|proveedor|transacci/i;

  it("ni texto ni icono ni estilo: el DOM es idéntico al de una consultada por API aunque llegaran claves extra", async () => {
    const api = runtTresPropietarios();
    const manual = { ...runtTresPropietarios(), origen: "MANUAL", usuario_id: "u-1", proveedor: "x" } as unknown as Detalle;
    const limpiar = (html: string) => html.replace(/\s(id|aria-labelledby|aria-describedby|aria-controls)="[^"]*"/g, "");

    const a = await pintarDetalle(api);
    const htmlApi = limpiar(a.dialogo.innerHTML);
    a.unmount();
    const b = await pintarDetalle(manual);
    expect(limpiar(b.dialogo.innerHTML)).toBe(htmlApi);
    expect(b.dialogo.textContent).not.toMatch(SOSPECHOSOS);
    expect(b.dialogo.querySelector("[data-origen]")).toBeNull();
  });

  it("tampoco en el SIMIT ni en el avalúo", async () => {
    for (const d of [simitVehiculo(), avaluoSinValor(), runtVehiculo()]) {
      const { dialogo, unmount } = await pintarDetalle({ ...d, origen: "MANUAL" } as unknown as Detalle);
      expect(dialogo.textContent).not.toMatch(SOSPECHOSOS);
      unmount();
    }
  });
});

describe("DetalleCelda — N/A y ERROR", () => {
  it("N/A muestra el motivo", async () => {
    const { dialogo } = await pintarDetalle(detalle("RUNT_LOCATARIO", { estado: "NA", motivo_na: "SIN_DOCUMENTO_LOCATARIO", dato_obtenido_en: null, personas: [] }));
    expect(within(dialogo).getByText("No aplica")).toBeInTheDocument();
    expect(within(dialogo).getByText(/sin el documento del locatario/i)).toBeInTheDocument();
  });

  it("ERROR muestra el código traducido, no el código crudo", async () => {
    const { dialogo } = await pintarDetalle(detalle("SIMIT_VEHICULO", { estado: "ERROR", tipo_error: "TIMEOUT", dato_obtenido_en: null }));
    expect(within(dialogo).getByRole("alert")).toHaveTextContent(/no respondió a tiempo/i);
    expect(dialogo.textContent).not.toContain("TIMEOUT");
  });

  it("ERROR bloqueado por el RUNT lo explica", async () => {
    const { dialogo } = await pintarDetalle(detalle("AVALUO", { estado: "ERROR", tipo_error: "BLOQUEADA_POR_RUNT", dato_obtenido_en: null }));
    expect(within(dialogo).getByRole("alert")).toHaveTextContent(/RUNT del vehículo/i);
  });

  it("pendiente: dice que la consulta todavía no termina", async () => {
    const { dialogo } = await pintarDetalle(detalle("SIMIT_VEHICULO", { estado: "EN_CURSO", dato_obtenido_en: null }));
    expect(within(dialogo).getByText(/todavía no termina/i)).toBeInTheDocument();
  });
});

describe("DetalleCelda — AC6 (negativo) y los otros estados de carga", () => {
  it("404: el panel dice que no está disponible y no muestra datos", async () => {
    const { dialogo } = await abrir("SIMIT_VEHICULO", Response.json({ code: "NO_ENCONTRADO", error: "No existe" }, { status: 404 }));
    expect(await within(dialogo).findByText(/este detalle no está disponible/i)).toBeInTheDocument();
    expect(within(dialogo).queryByRole("table")).not.toBeInTheDocument();
    expect(within(dialogo).queryByRole("region")).not.toBeInTheDocument();
    expect(within(dialogo).queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
  });

  it("403: sin permiso, con InlineAlert", async () => {
    const { dialogo } = await abrir("SIMIT_VEHICULO", Response.json({ code: "SIN_EMPRESA", error: "x" }, { status: 403 }));
    expect(await within(dialogo).findByRole("alert")).toHaveTextContent(/no tienes permiso/i);
  });

  it("cargando: el loader de @flit/ui mientras llega el detalle", async () => {
    render(<DetalleCelda loteId={ID} vehiculoId="vehiculo-1" fuente="RUNT" nombreFuente="RUNT vehículo" identificador="TST001" onClose={() => {}} cargar={() => new Promise(() => {})} />);
    expect(await screen.findByRole("status", { name: "Cargando el detalle…" })).toBeInTheDocument();
  });

  it("otro error: InlineAlert con Reintentar que vuelve a pedir el detalle", async () => {
    let n = 0;
    const cargar = vi.fn(async () => {
      n++;
      if (n === 1) throw new Error("Diagnóstico no respondió. Intenta de nuevo en un momento.");
      return runtVehiculo();
    });
    render(<DetalleCelda loteId={ID} vehiculoId="vehiculo-1" fuente="RUNT" nombreFuente="RUNT vehículo" identificador="TST001" onClose={() => {}} cargar={cargar} />);
    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent(/no respondió/i);
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("STRIA PRUEBA")).toBeInTheDocument();
    expect(cargar).toHaveBeenCalledTimes(2);
  });
});

describe("DetalleCelda — accesibilidad del diálogo", () => {
  it("diálogo modal con aria-labelledby al título; el foco entra al diálogo", async () => {
    const { dialogo } = await abrir("RUNT", runtVehiculo());
    expect(dialogo).toHaveAttribute("aria-modal", "true");
    const titulo = document.getElementById(dialogo.getAttribute("aria-labelledby")!);
    expect(titulo).toHaveTextContent("RUNT vehículo · TST001");
    await waitFor(() => expect(dialogo.contains(document.activeElement)).toBe(true));
  });

  it("Escape cierra y devuelve el foco al semáforo", async () => {
    const { boton } = await abrir("RUNT", runtVehiculo());
    act(() => {
      fireEvent.keyDown(document, { key: "Escape" });
    });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(document.activeElement).toBe(boton);
  });

  it("la X cierra y devuelve el foco al semáforo", async () => {
    const { dialogo, boton } = await abrir("RUNT", runtVehiculo());
    fireEvent.click(within(dialogo).getByRole("button", { name: "Cerrar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(document.activeElement).toBe(boton);
  });

  it("el foco queda atrapado: Tab en el último vuelve al primero y Shift+Tab en el primero va al último", async () => {
    const lista = Array.from({ length: 30 }, (_, i) => comparendo(i + 1));
    const { dialogo } = await abrir("SIMIT_VEHICULO", simitVehiculo(lista));
    await within(dialogo).findByRole("button", { name: "Ver los 30 comparendos" });
    const enfocables = within(dialogo).getAllByRole("button");
    const primero = enfocables[0];
    const ultimo = enfocables[enfocables.length - 1];
    ultimo.focus();
    fireEvent.keyDown(document, { key: "Tab" });
    expect(document.activeElement).toBe(primero);
    fireEvent.keyDown(document, { key: "Tab", shiftKey: true });
    expect(document.activeElement).toBe(ultimo);
  });

  it("el semáforo de una fuente aún no llegada no es botón", () => {
    simularApi({});
    const v = vehiculo(1, [celda(1, "RUNT", "COMPLETADO")]);
    render(<GrillaSemaforos grilla={grilla({ vehiculos: [v], columnas: { por_vehiculo: ["RUNT", "AVALUO"], por_propietario: [], por_locatario: [] } })} puedeReprocesar={false} />);
    expect(screen.getAllByRole("button", { name: /^Ver detalle/ })).toHaveLength(1);
  });
});

// HU #13866 (F09-H7): celda y detalle del impuesto departamental. Uso de ejemplo: el semáforo «Ver detalle de Impuesto
// departamental de TST001» abre el diálogo con «Total adeudado» en pesos enteros y la tabla Vigencia / Valor / Estado, o el
// texto del motivo cuando la celda es N/A (servicio público; departamento no soportado o sin departamento derivado).
describe("DetalleCelda — HU #13866: impuesto departamental", () => {
  const texto = (el: Element) => (el.textContent ?? "").replace(/\s/g, " ");

  /** Grilla de TST001 con RUNT completado y el impuesto en el estado pedido. */
  function grillaImpuesto(impuesto: ReturnType<typeof celda>, puedeReprocesar = false) {
    const v = vehiculo(1, [celda(1, "RUNT", "COMPLETADO"), impuesto]);
    return render(
      <GrillaSemaforos grilla={grilla({ vehiculos: [v], columnas: { por_vehiculo: ["RUNT", "IMPUESTO_DEPARTAMENTAL"], por_propietario: [], por_locatario: [] } })} puedeReprocesar={puedeReprocesar} />,
    );
  }

  it("AC1: desde el semáforo verde pide el detalle del impuesto y pinta «Total adeudado» con $ 350.000 y la tabla de vigencias con su estado", async () => {
    const api = simularApi({ [rutaDetalle("IMPUESTO_DEPARTAMENTAL")]: () => Response.json(impuestoConVigencias()) });
    grillaImpuesto(celda(1, "IMPUESTO_DEPARTAMENTAL", "COMPLETADO"));
    fireEvent.click(screen.getByRole("button", { name: /^Ver detalle de Impuesto departamental de TST001: Completada$/ }));
    const dialogo = await screen.findByRole("dialog", { name: "Impuesto departamental · TST001" });
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/vehiculos/vehiculo-1/consultas/IMPUESTO_DEPARTAMENTAL`)).toHaveLength(1);

    const total = await within(dialogo).findByText("Total adeudado");
    expect(texto(total.parentElement!)).toContain("$ 350.000");

    const tabla = within(dialogo).getByRole("table", { name: "Vigencias del impuesto" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Vigencia", "Valor", "Estado"]);
    for (const c of within(tabla).getAllByRole("columnheader")) expect(c).toHaveAttribute("scope", "col");
    const filas = within(tabla).getAllByRole("row").slice(1);
    expect(filas).toHaveLength(2);
    expect(filas[0]).toHaveTextContent("2025");
    expect(texto(filas[0])).toContain("$ 180.000");
    expect(filas[0]).toHaveTextContent("Pagada");
    expect(filas[1]).toHaveTextContent("2026");
    expect(texto(filas[1])).toContain("$ 350.000");
    expect(filas[1]).toHaveTextContent("Pendiente");
  });

  it("AC1: los montos se muestran en pesos enteros con el formato del avalúo, sin decimales", async () => {
    const { dialogo } = await pintarDetalle(impuestoConVigencias([{ anio: 2026, valor: 1234567, pagada: false }], 1234567));
    expect(texto(dialogo)).toContain("$ 1.234.567");
    expect(texto(dialogo)).not.toMatch(/1\.234\.567,\d/);
  });

  it("AC1 (vacío): sin vigencias dice «No registra vigencias» en vez de pintar una tabla vacía", async () => {
    const { dialogo } = await pintarDetalle(impuestoSinVigencias());
    expect(within(dialogo).getByText("No registra vigencias")).toBeInTheDocument();
    expect(within(dialogo).queryByRole("table")).not.toBeInTheDocument();
    expect(texto(within(dialogo).getByText("Total adeudado").parentElement!)).toContain("$ 0");
  });

  it("AC2: servicio público — el detalle explica el motivo con el texto existente y el chip «No aplica»", async () => {
    const { dialogo } = await pintarDetalle(impuestoNa("SERVICIO_PUBLICO"));
    expect(within(dialogo).getByText("No aplica")).toBeInTheDocument();
    expect(within(dialogo).getByText("El vehículo es de servicio público: esta consulta no aplica.")).toBeInTheDocument();
    expect(within(dialogo).queryByText("Total adeudado")).not.toBeInTheDocument();
  });

  it("AC2: desde la grilla, el chip N/A «Servicio público» del impuesto abre su detalle con el motivo", async () => {
    simularApi({ [rutaDetalle("IMPUESTO_DEPARTAMENTAL")]: () => Response.json(impuestoNa("SERVICIO_PUBLICO")) });
    const { container } = grillaImpuesto(celda(1, "IMPUESTO_DEPARTAMENTAL", "NA", { motivo_na: "SERVICIO_PUBLICO" }));
    expect(container.querySelector('[data-celda="celda-1-impuesto_departamental"]')).toHaveTextContent("Servicio público");
    fireEvent.click(screen.getByRole("button", { name: /^Ver detalle de Impuesto departamental de TST001: No aplica$/ }));
    const dialogo = await screen.findByRole("dialog", { name: "Impuesto departamental · TST001" });
    expect(await within(dialogo).findByText("El vehículo es de servicio público: esta consulta no aplica.")).toBeInTheDocument();
  });

  it("AC3 (negativo): departamento no soportado — texto existente, sin error ni botón de reprocesar", async () => {
    simularApi({ [rutaDetalle("IMPUESTO_DEPARTAMENTAL")]: () => Response.json(impuestoNa("DEPARTAMENTO_NO_SOPORTADO")) });
    grillaImpuesto(celda(1, "IMPUESTO_DEPARTAMENTAL", "NA", { motivo_na: "DEPARTAMENTO_NO_SOPORTADO" }), true);
    expect(screen.queryByRole("button", { name: /reprocesar/i })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /^Ver detalle de Impuesto departamental de TST001: No aplica$/ }));
    const dialogo = await screen.findByRole("dialog", { name: "Impuesto departamental · TST001" });
    expect(await within(dialogo).findByText("El departamento del vehículo todavía no está soportado.")).toBeInTheDocument();
    expect(within(dialogo).queryByRole("alert")).not.toBeInTheDocument();
    expect(within(dialogo).queryByRole("button", { name: /reprocesar|reintentar/i })).not.toBeInTheDocument();
    expect(within(dialogo).queryByText(/error/i)).not.toBeInTheDocument();
  });

  it("R-20: aunque el payload traiga un proveedor ficticio, transacción, origen o departamento, el detalle no los muestra", async () => {
    const limpio = impuestoConVigencias();
    const conFuga = {
      ...impuestoConVigencias(),
      proveedor: "PROVEEDOR-FICTICIO-ZETA",
      transaccion_proveedor_id: "TX-FICTICIA-0099",
      origen: "API",
      departamento_codigo: "05",
      resultado: {
        monto_total: 350000,
        proveedor: "PROVEEDOR-FICTICIO-ZETA",
        vigencias: [
          { anio: 2025, valor: 180000, pagada: true, proveedor: "PROVEEDOR-FICTICIO-ZETA" },
          { anio: 2026, valor: 350000, pagada: false },
        ],
      },
    } as unknown as Detalle;
    const limpiar = (html: string) => html.replace(/\s(id|aria-labelledby|aria-describedby|aria-controls)="[^"]*"/g, "");

    const a = await pintarDetalle(limpio);
    await within(a.dialogo).findByText("Total adeudado");
    const htmlLimpio = limpiar(a.dialogo.innerHTML);
    a.unmount();
    const b = await pintarDetalle(conFuga);
    await within(b.dialogo).findByText("Total adeudado");
    expect(b.dialogo.textContent).not.toMatch(/PROVEEDOR-FICTICIO-ZETA|TX-FICTICIA-0099|proveedor|transacci|origen|departamento/i);
    expect(limpiar(b.dialogo.innerHTML)).toBe(htmlLimpio);
  });
});
