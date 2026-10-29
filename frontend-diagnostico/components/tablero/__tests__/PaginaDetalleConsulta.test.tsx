import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { PaginaDetalleConsulta } from "@/components/tablero/PaginaDetalleConsulta";
import PaginaConsulta from "@/app/(app)/diagnosticos/[id]/vehiculos/[vehiculoId]/consultas/[fuente]/page";
import type { DetalleCelda as Detalle, ResultadoSimitDetalle } from "@/lib/tablero/detalle";
import { ID, llamadas, sesion, simularApi } from "@/lib/tablero/__tests__/datos";
import {
  avaluoConValor,
  avaluoSinValor,
  comparendo,
  conductorProveedor,
  detalle,
  empresaProveedor,
  impuestoConVigencias,
  impuestoNa,
  impuestoSinVigencias,
  persona,
  respuestaFuentePersona,
  respuestaFuenteRunt,
  runtLocatarioDeclarado,
  runtPropietarioConError,
  runtTresPropietarios,
  runtVehiculo,
  runtVehiculoConRespuesta,
  simitVehiculo,
} from "@/lib/tablero/__tests__/datosDetalle";
import { simitProveedorPersona, simitProveedorVehiculo } from "@/lib/tablero/__tests__/fixturesSimitProveedor";

// Uso de ejemplo: el semáforo de la grilla abre en una pestaña nueva /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente};
// la página monta <PaginaDetalleConsulta loteId vehiculoId fuente />, que pide GET /diagnosticos/{id}/vehiculos/{vid}/
// consultas/{fuente} y pinta a ancho completo: migas «Diagnósticos › {nombre_diagnostico} › {fuente} · {identificador}», el
// botón «Volver al diagnóstico», el h1 «{fuente} · {identificador}», la «Auditoría de consulta» de FLIT (fecha y hora en
// Colombia, estado con el chip de la tabla y, por persona, rol, documento enmascarado, consultado_en y estado) y la
// «Respuesta de la fuente» (RespuestaFuente: solo datos de la consulta, en español). Cuatro estados: cargando, error (404 no
// disponible, 403 a la página 403, otro con Reintentar), vacío y lleno; sin la capacidad verDiagnosticos no se pide nada.

const h = vi.hoisted(() => ({ capacidades: { verDiagnosticos: true } as Record<string, boolean>, replace: vi.fn() }));
vi.mock("@/components/DiagnosticoShell", () => ({ useSesion: () => ({ sesion: sesion(h.capacidades), cargando: false, error: null, recargar: () => {} }) }));
vi.mock("next/navigation", () => ({ usePathname: () => "/diagnosticos/x", useRouter: () => ({ replace: h.replace, push: vi.fn(), refresh: vi.fn() }) }));

beforeEach(() => {
  h.capacidades = { verDiagnosticos: true };
  h.replace.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

const NOMBRES: Record<string, string> = {
  RUNT: "RUNT vehículo",
  SIMIT_VEHICULO: "SIMIT vehículo",
  AVALUO: "Avalúo comercial",
  RUNT_PROPIETARIO: "RUNT propietario",
  RUNT_LOCATARIO: "RUNT locatario",
  IMPUESTO_DEPARTAMENTAL: "Impuesto departamental",
};

const rutaDetalle = (fuente: string) => `GET /diagnosticos/${ID}/vehiculos/vehiculo-1/consultas/${fuente}`;

/** Monta la página de la ruta (Server Component) con la API simulada. */
async function abrir(fuente: string, respuesta: Detalle | Response) {
  const api = simularApi({ [rutaDetalle(fuente)]: () => (respuesta instanceof Response ? respuesta : Response.json(respuesta)) });
  const utils = render(await PaginaConsulta({ params: Promise.resolve({ id: ID, vehiculoId: "vehiculo-1", fuente }) }));
  return { api, pagina: utils.container };
}

/** Pinta la página con un cargador fijo y espera a que termine de cargar. */
async function pintarDetalle(d: Detalle) {
  const utils = render(<PaginaDetalleConsulta loteId={ID} vehiculoId="vehiculo-1" fuente={d.fuente} cargar={async () => d} />);
  await waitFor(() => expect(screen.queryByRole("status", { name: /cargando/i })).not.toBeInTheDocument());
  return { ...utils, pagina: utils.container };
}

const region = (pagina: HTMLElement, nombre: string | RegExp) => within(pagina).getByRole("region", { name: nombre });
const auditoria = (pagina: HTMLElement) => region(pagina, "Auditoría de consulta");
const respuesta = (pagina: HTMLElement) => region(pagina, "Respuesta de la fuente");
/** Valor de la fila «campo» de una tabla campo/valor de RespuestaFuente. */
const valorDe = (tabla: HTMLElement, campo: string) => {
  const fila = within(tabla)
    .getAllByRole("row")
    .find((r) => r.querySelector("th[scope='row']")?.textContent === campo);
  if (!fila) throw new Error(`Sin campo ${campo}`);
  return within(fila).getByRole("cell").textContent;
};

describe("PaginaDetalleConsulta — la ruta, el título, las migas y el volver", () => {
  it("la ruta pide el detalle por vehículo y fuente al BFF", async () => {
    const { api } = await abrir("SIMIT_VEHICULO", simitVehiculo());
    expect(await screen.findByRole("heading", { level: 1, name: "SIMIT vehículo · TST001" })).toBeInTheDocument();
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/vehiculos/vehiculo-1/consultas/SIMIT_VEHICULO`)).toHaveLength(1);
  });

  it("título h1 «{fuente} · {identificador}» (el VIN si no hay placa)", async () => {
    await pintarDetalle({ ...runtVehiculo(), identificador: "9ZZPRUEBA00000482" });
    expect(screen.getByRole("heading", { level: 1, name: "RUNT vehículo · 9ZZPRUEBA00000482" })).toBeInTheDocument();
  });

  it("migas «Diagnósticos › {nombre_diagnostico} › {fuente} · {identificador}», el último con aria-current", async () => {
    await pintarDetalle(simitVehiculo());
    const migas = screen.getByRole("navigation", { name: "Migas de pan" });
    const items = within(migas).getAllByRole("listitem");
    expect(items.map((i) => i.textContent?.replace("›", "").trim())).toEqual(["Diagnósticos", "Lote de prueba", "SIMIT vehículo · TST001"]);
    expect(within(migas).getByRole("link", { name: "Diagnósticos" })).toHaveAttribute("href", "/");
    expect(within(migas).getByRole("link", { name: "Lote de prueba" })).toHaveAttribute("href", `/diagnosticos/${ID}`);
    expect(within(items[2]).getByText("SIMIT vehículo · TST001")).toHaveAttribute("aria-current", "page");
  });

  it("«Volver al diagnóstico» lleva al tablero del lote", async () => {
    await pintarDetalle(simitVehiculo());
    expect(screen.getByRole("link", { name: "Volver al diagnóstico" })).toHaveAttribute("href", `/diagnosticos/${ID}`);
  });

  it("fuente desconocida en la ruta: no disponible y sin pedir nada", async () => {
    const api = simularApi({});
    render(<PaginaDetalleConsulta loteId={ID} vehiculoId="vehiculo-1" fuente="NO_EXISTE" />);
    expect(await screen.findByText(/este detalle no está disponible/i)).toBeInTheDocument();
    expect(api).not.toHaveBeenCalled();
  });

  it("sin la capacidad verDiagnosticos: sin permiso y sin pedir el detalle", async () => {
    h.capacidades = { verDiagnosticos: false };
    const cargar = vi.fn(async () => simitVehiculo());
    render(<PaginaDetalleConsulta loteId={ID} vehiculoId="vehiculo-1" fuente="SIMIT_VEHICULO" cargar={cargar} />);
    expect(await screen.findByText(/no tienes permiso para ver este detalle/i)).toBeInTheDocument();
    expect(cargar).not.toHaveBeenCalled();
  });
});

describe("PaginaDetalleConsulta — los otros estados de carga", () => {
  it("cargando: el loader de @flit/ui mientras llega el detalle", async () => {
    render(<PaginaDetalleConsulta loteId={ID} vehiculoId="vehiculo-1" fuente="RUNT" cargar={() => new Promise(() => {})} />);
    expect(await screen.findByRole("status", { name: "Cargando el detalle…" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "RUNT vehículo" })).toBeInTheDocument();
  });

  it("404: dice que no está disponible y no muestra datos", async () => {
    const { pagina } = await abrir("SIMIT_VEHICULO", Response.json({ code: "NO_ENCONTRADO", error: "No existe" }, { status: 404 }));
    expect(await within(pagina).findByText(/este detalle no está disponible/i)).toBeInTheDocument();
    expect(within(pagina).queryByRole("table")).not.toBeInTheDocument();
    expect(within(pagina).queryByRole("region", { name: "Respuesta de la fuente" })).not.toBeInTheDocument();
    expect(within(pagina).queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
  });

  it("403 de sesión (SIN_EMPRESA): a la página 403, como el tablero", async () => {
    await abrir("SIMIT_VEHICULO", Response.json({ code: "SIN_EMPRESA", error: "x" }, { status: 403 }));
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_EMPRESA"));
  });

  it("otro 403: sin permiso, con InlineAlert", async () => {
    const { pagina } = await abrir("SIMIT_VEHICULO", Response.json({ code: "OTRO", error: "x" }, { status: 403 }));
    expect(await within(pagina).findByRole("alert")).toHaveTextContent(/no tienes permiso/i);
  });

  it("otro error: InlineAlert con Reintentar que vuelve a pedir el detalle", async () => {
    let n = 0;
    const cargar = vi.fn(async () => {
      n++;
      if (n === 1) throw new Error("Diagnóstico no respondió. Intenta de nuevo en un momento.");
      return runtVehiculo();
    });
    render(<PaginaDetalleConsulta loteId={ID} vehiculoId="vehiculo-1" fuente="RUNT" cargar={cargar} />);
    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent(/no respondió/i);
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("STRIA PRUEBA")).toBeInTheDocument();
    expect(cargar).toHaveBeenCalledTimes(2);
  });

  it("las secciones son h2 bajo el h1 y las tablas llevan scope", async () => {
    const { pagina } = await pintarDetalle(runtTresPropietarios());
    expect(within(pagina).getByRole("heading", { level: 2, name: "Auditoría de consulta" })).toBeInTheDocument();
    expect(within(pagina).getByRole("heading", { level: 2, name: "Respuesta de la fuente" })).toBeInTheDocument();
    for (const th of within(pagina).getAllByRole("columnheader")) expect(th).toHaveAttribute("scope", "col");
  });
});

describe("PaginaDetalleConsulta — HU #14087 AC7: auditoría de consulta (se queda)", () => {
  it("fecha y hora de la consulta en hora de Colombia y el estado con el mismo chip de la tabla", async () => {
    const { pagina } = await abrir("SIMIT_VEHICULO", simitVehiculo());
    const a = await within(pagina).findByRole("region", { name: "Auditoría de consulta" });
    expect(a).toHaveTextContent("Fecha y hora de la consulta");
    expect(a).toHaveTextContent("22/10/2026 10:00");
    expect(within(a).getByText("Ejecutado (OK)").closest("[data-tono]")).toHaveAttribute("data-tono", "success");
    expect(a).not.toHaveTextContent(/dato original/i);
  });

  it("reutilizada: indica que la fecha es la del dato original", async () => {
    const { pagina } = await pintarDetalle({ ...runtVehiculo(), reutilizada: true });
    expect(auditoria(pagina)).toHaveTextContent(/fecha del dato original/i);
  });

  it("sin fecha (pendiente) muestra «—» y el chip «Sin ejecutar»", async () => {
    const { pagina } = await pintarDetalle(detalle("SIMIT_VEHICULO", { estado: "PENDIENTE", dato_obtenido_en: null }));
    expect(auditoria(pagina)).toHaveTextContent("Fecha y hora de la consulta—");
    expect(within(auditoria(pagina)).getByText("Sin ejecutar")).toBeInTheDocument();
  });

  it("novedad: chip «Novedad» con su motivo", async () => {
    const { pagina } = await pintarDetalle(detalle("RUNT_LOCATARIO", { estado: "NA", motivo_na: "SIN_DOCUMENTO_LOCATARIO", dato_obtenido_en: null, personas: [] }));
    expect(within(auditoria(pagina)).getByText("Novedad")).toBeInTheDocument();
    expect(auditoria(pagina)).toHaveTextContent(/sin el documento del locatario/i);
  });

  it("por persona: rol, documento enmascarado tal cual, consultado_en y estado mapeado", async () => {
    const d = detalle("RUNT_PROPIETARIO", {
      personas: [
        persona(1, { documento_enmascarado: "****1222" }),
        persona(2, { tipo_documento: "NIT", documento_enmascarado: "****4567", estado: "SIN_RESULTADO", resultado: null }),
        persona(3, { documento_enmascarado: "****", estado: "ERROR", tipo_error: "TIMEOUT", consultado_en: null, resultado: null }),
      ],
    });
    const { pagina } = await pintarDetalle(d);
    const tabla = within(auditoria(pagina)).getByRole("table", { name: "Personas consultadas" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Persona", "Rol", "Documento", "Consultado en", "Estado"]);
    const [p1, p2, p3] = within(tabla).getAllByRole("row").slice(1);
    expect(p1).toHaveTextContent("Propietario");
    expect(p1).toHaveTextContent("C ****1222");
    expect(p1).toHaveTextContent("22/10/2026 10:00");
    expect(p1).toHaveTextContent("Ejecutado (OK)");
    expect(p2).toHaveTextContent("NIT ****4567");
    expect(p2).toHaveTextContent("Sin resultado");
    expect(p3.textContent).not.toMatch(/\*{4}\d/);
    expect(p3).toHaveTextContent(/no respondió a tiempo/i);
  });

  it("el documento declarado por el cliente lleva el rótulo; el no declarado no", async () => {
    const a = await pintarDetalle(runtLocatarioDeclarado());
    expect(within(within(a.pagina).getByRole("table", { name: "Personas consultadas" })).getByText(/declarado por el cliente/i)).toBeInTheDocument();
    a.unmount();
    const b = await pintarDetalle(runtTresPropietarios());
    expect(within(b.pagina).queryByText(/declarado por el cliente/i)).not.toBeInTheDocument();
  });
});

describe("PaginaDetalleConsulta — respuesta de la fuente: solo la consulta y en español", () => {
  it("SIMIT vehículo: sin semáforo del proveedor; cantidad, recorte y comparendos traducidos", async () => {
    const { pagina } = await abrir("SIMIT_VEHICULO", simitVehiculo([comparendo(1), comparendo(2, { valor: "1250000", estado: "PAGADO" })]));
    const r = await within(pagina).findByRole("region", { name: "Respuesta de la fuente" });
    const tabla = within(r).getByRole("table", { name: "Respuesta de la fuente" });
    expect(within(tabla).getAllByRole("rowheader").map((c) => c.textContent)).toEqual(["Cantidad de comparendos", "Comparendos recortados"]);
    expect(valorDe(tabla, "Cantidad de comparendos")).toBe("2");
    expect(valorDe(tabla, "Comparendos recortados")).toBe("No");
    const comparendos = within(r).getByRole("table", { name: "Comparendos" });
    expect(within(comparendos).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Número", "Fecha", "Valor", "Estado", "Organismo"]);
    const filas = within(comparendos).getAllByRole("row").slice(1);
    expect(within(filas[1]).getAllByRole("cell").map((c) => c.textContent)).toEqual(["C-0002", "2026-01-02", "1250000", "PAGADO", "ORGANISMO PRUEBA"]);
    expect(r).not.toHaveTextContent(/ROJO|semaforo|Semáforo/);
  });

  it("una lista larga de comparendos se pinta completa (100)", async () => {
    const lista = Array.from({ length: 100 }, (_, i) => comparendo(i + 1));
    const { pagina } = await pintarDetalle(simitVehiculo(lista, true, 250));
    expect(within(within(pagina).getByRole("table", { name: "Comparendos" })).getAllByRole("row").slice(1)).toHaveLength(100);
    const tabla = within(pagina).getByRole("table", { name: "Respuesta de la fuente" });
    expect(valorDe(tabla, "Cantidad de comparendos")).toBe("250");
    expect(valorDe(tabla, "Comparendos recortados")).toBe("Sí");
  });

  it("sin comparendos: «Sin registros»", async () => {
    const { pagina } = await pintarDetalle(simitVehiculo([]));
    expect(valorDe(within(pagina).getByRole("table", { name: "Respuesta de la fuente" }), "Comparendos")).toBe("Sin registros");
  });

  it("RUNT del vehículo: sus campos traducidos y la tabla de propietarios", async () => {
    const { pagina } = await pintarDetalle(runtVehiculo());
    const tabla = within(respuesta(pagina)).getByRole("table", { name: "Respuesta de la fuente" });
    expect(within(tabla).getAllByRole("rowheader").map((c) => c.textContent)).toEqual(["Placa", "VIN", "Servicio", "Clase", "Organismo", "Locatarios"]);
    expect(valorDe(tabla, "VIN")).toBe("9ZZPRUEBA00000001");
    const propietarios = within(respuesta(pagina)).getByRole("table", { name: "Propietarios" });
    expect(within(propietarios).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Tipo de documento", "Tipo de propiedad"]);
    expect(within(propietarios).getAllByRole("cell").map((c) => c.textContent)).toEqual(["C", "PROPIA"]);
  });

  it("avalúo: números sin formato, null como «—» y las fuentes de avalúo", async () => {
    const { pagina } = await pintarDetalle(avaluoConValor());
    const tabla = within(respuesta(pagina)).getByRole("table", { name: "Respuesta de la fuente" });
    expect(valorDe(tabla, "Sin valor comercial")).toBe("No");
    expect(valorDe(tabla, "Valor sugerido")).toBe("45000000");
    expect(valorDe(tabla, "Fuente principal")).toBe("fasecolda");
    const filas = within(within(respuesta(pagina)).getByRole("table", { name: "Fuentes" })).getAllByRole("row").slice(1);
    expect(within(filas[0]).getAllByRole("cell").map((c) => c.textContent)).toEqual(["fasecolda", "OK", "45000000", "—", "2026-10-22"]);
  });

  it("avalúo sin valor comercial: «Sí» y «—»", async () => {
    const { pagina } = await pintarDetalle(avaluoSinValor());
    const tabla = within(pagina).getByRole("table", { name: "Respuesta de la fuente" });
    expect(valorDe(tabla, "Sin valor comercial")).toBe("Sí");
    expect(valorDe(tabla, "Valor sugerido")).toBe("—");
  });

  it("impuesto: monto total y vigencias con año, valor y pagada", async () => {
    const { pagina } = await pintarDetalle(impuestoConVigencias());
    expect(valorDe(within(pagina).getByRole("table", { name: "Respuesta de la fuente" }), "Monto total")).toBe("350000");
    const vigencias = within(pagina).getByRole("table", { name: "Vigencias" });
    expect(within(vigencias).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Año", "Valor", "Pagada"]);
    expect(within(vigencias).getAllByRole("row").slice(1).map((f) => within(f).getAllByRole("cell").map((c) => c.textContent))).toEqual([
      ["2025", "180000", "Sí"],
      ["2026", "350000", "No"],
    ]);
  });

  it("impuesto sin vigencias: «Sin registros»", async () => {
    const { pagina } = await pintarDetalle(impuestoSinVigencias());
    expect(valorDe(within(pagina).getByRole("table", { name: "Respuesta de la fuente" }), "Vigencias")).toBe("Sin registros");
  });

  it("fuentes de persona: una subsección por persona con su resultado traducido", async () => {
    const { pagina } = await pintarDetalle(runtTresPropietarios());
    const secciones = within(respuesta(pagina)).getAllByRole("region", { name: /^propietario \d/i });
    expect(secciones).toHaveLength(3);
    const t1 = within(secciones[0]).getByRole("table", { name: "Respuesta de la fuente · Propietario 1" });
    expect(valorDe(t1, "Inscrito")).toBe("Sí");
    expect(valorDe(t1, "Estado del conductor")).toBe("ACTIVO");
    expect(within(within(secciones[0]).getByRole("table", { name: "Licencias" })).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Categoría", "Estado", "Vigente hasta"]);
    const t2 = within(secciones[1]).getByRole("table", { name: "Respuesta de la fuente · Propietario 2" });
    expect(valorDe(t2, "Estado del conductor")).toBe("—");
    expect(valorDe(t2, "Licencias")).toBe("Sin registros");
    const t3 = within(secciones[2]).getByRole("table", { name: "Respuesta de la fuente · Propietario 3" });
    expect(valorDe(t3, "Encontrada")).toBe("Sí");
    expect(valorDe(t3, "Estado de la matrícula")).toBe("ACTIVA");
    expect(valorDe(t3, "Última renovación")).toBe("2026");
  });

  it("SIMIT por persona: los comparendos de la persona como tabla", async () => {
    const simit = simitVehiculo([comparendo(7)]).resultado as ResultadoSimitDetalle;
    const { pagina } = await pintarDetalle(detalle("SIMIT_PROPIETARIO", { personas: [persona(1, { consulta: "SIMIT", resultado: simit })] }));
    const seccion = within(respuesta(pagina)).getByRole("region", { name: /propietario 1/i });
    expect(within(seccion).getByRole("table", { name: "Comparendos" })).toHaveTextContent("C-0007");
  });

  it("vacío: sin personas del rol lo dice claro", async () => {
    const { pagina } = await pintarDetalle(detalle("RUNT_LOCATARIO", { personas: [] }));
    expect(respuesta(pagina)).toHaveTextContent("El vehículo no tiene locatarios para esta consulta.");
  });

  it("vacío: completada sin resultado lo dice sin inventar campos", async () => {
    const { pagina } = await pintarDetalle(detalle("SIMIT_VEHICULO", { resultado: null }));
    expect(respuesta(pagina)).toHaveTextContent(/no trae campos/i);
    expect(within(respuesta(pagina)).queryByRole("table")).not.toBeInTheDocument();
  });
});

describe("PaginaDetalleConsulta — sin resultado no hay campos", () => {
  it("Sin ejecutar: lo dice y no pinta campos", async () => {
    const { pagina } = await pintarDetalle(detalle("SIMIT_VEHICULO", { estado: "EN_CURSO", dato_obtenido_en: null }));
    expect(respuesta(pagina)).toHaveTextContent(/todavía no se ha ejecutado/i);
    expect(within(respuesta(pagina)).queryByRole("table")).not.toBeInTheDocument();
  });

  it("Novedad: lo dice con su motivo y no pinta campos", async () => {
    const { pagina } = await pintarDetalle(impuestoNa("SERVICIO_PUBLICO"));
    expect(respuesta(pagina)).toHaveTextContent("El vehículo es de servicio público: esta consulta no aplica.");
    expect(within(respuesta(pagina)).queryByRole("table")).not.toBeInTheDocument();
  });

  it.each([
    ["TIMEOUT", /no respondió a tiempo/i],
    ["AUTENTICACION", /rechazó las credenciales de FLIT/],
    ["LIMITE_PROVEEDOR", /limitó las consultas/],
    ["BLOQUEADA_POR_RUNT", /RUNT del vehículo/],
  ])("Error %s: el texto claro, nunca el código crudo, y sin campos", async (tipo, texto) => {
    const { pagina } = await pintarDetalle(detalle("SIMIT_VEHICULO", { estado: "ERROR", tipo_error: tipo, dato_obtenido_en: null }));
    expect(within(pagina).getByRole("alert")).toHaveTextContent(texto);
    expect(pagina.textContent).not.toContain(tipo);
    expect(within(respuesta(pagina)).queryByRole("table")).not.toBeInTheDocument();
  });

  it("ERROR PERSONAS_CON_ERROR: muestra lo que sí respondió y, en la persona que falló, que no hay respuesta", async () => {
    const { pagina } = await pintarDetalle(runtPropietarioConError());
    expect(within(pagina).getByRole("alert")).toHaveTextContent(/personas no se pudieron consultar/i);
    const [ok, fallida] = within(respuesta(pagina)).getAllByRole("region", { name: /^propietario \d/i });
    expect(valorDe(within(ok).getByRole("table", { name: /Propietario 1$/ }), "Inscrito")).toBe("Sí");
    expect(within(fallida).queryByRole("table")).not.toBeInTheDocument();
    expect(fallida).toHaveTextContent(/sin respuesta de la fuente/i);
  });
});

describe("PaginaDetalleConsulta — R-20: nada distingue una celda llenada a mano ni sale la auditoría del proveedor", () => {
  const SOSPECHOSOS = /manual|a mano|backoffice|origen|usuario|proveedor|transacci/i;
  const limpiar = (html: string) => html.replace(/\s(id|aria-labelledby|aria-describedby|aria-controls)="[^"]*"/g, "");

  it("el DOM es idéntico al de una consultada por API aunque llegaran claves extra", async () => {
    const a = await pintarDetalle(runtTresPropietarios());
    const htmlApi = limpiar(a.pagina.innerHTML);
    a.unmount();
    const b = await pintarDetalle({ ...runtTresPropietarios(), origen: "MANUAL", usuario_id: "u-1", proveedor: "x" } as unknown as Detalle);
    expect(limpiar(b.pagina.innerHTML)).toBe(htmlApi);
    // HU #14154 AC5: el aviso literal de «sin respuesta del proveedor» sale igual en API y manual; no delata el origen.
    expect(b.pagina.textContent?.replaceAll("Esta consulta no tiene la respuesta completa del proveedor. Para verla, vuelve a consultar.", "")).not.toMatch(SOSPECHOSOS);
  });

  it("dentro del resultado, el proveedor tampoco sale; el resto se traduce", async () => {
    const conExtra = {
      ...impuestoConVigencias(),
      transaccion_proveedor_id: "TX-FICTICIA-0099",
      resultado: { monto_total: 350000, proveedor: "PROVEEDOR-FICTICIO-ZETA", vigencias: [{ anio: 2025, valor: 180000, pagada: true, departamento: "DEPTO PRUEBA" }] },
    } as unknown as Detalle;
    const { pagina } = await pintarDetalle(conExtra);
    expect(pagina.textContent).not.toMatch(/TX-FICTICIA-0099|PROVEEDOR-FICTICIO-ZETA/);
    expect(within(within(pagina).getByRole("table", { name: "Vigencias" })).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Año", "Valor", "Pagada", "Departamento"]);
  });
});

const AVISO_RESUMIDO =
  "Esta consulta no tiene guardada la respuesta completa de la fuente (es anterior al cambio, se reutilizó o se registró sin consulta). Se muestra el resultado resumido.";

describe("PaginaDetalleConsulta — respuesta completa de la fuente (respuesta_fuente)", () => {
  it("con respuesta_fuente pinta sus datos (no el resultado resumido), sin la auditoría del proveedor y sin aviso", async () => {
    const { pagina } = await pintarDetalle(runtVehiculoConRespuesta() as Detalle);
    const r = respuesta(pagina);
    expect(within(r).getByRole("region", { name: "Vehículo" })).toBeInTheDocument();
    expect(within(r).getByRole("region", { name: "SOAT (2)" })).toBeInTheDocument();
    expect(within(r).getByRole("table", { name: "Direcciones" })).toHaveTextContent("DEPTO PRUEBA");
    expect(within(r).queryByRole("table", { name: "Propietarios" })).not.toBeInTheDocument();
    expect(r).not.toHaveTextContent(/PROVEEDOR-FICTICIO|Estado HTTP|200/);
    expect(r).not.toHaveTextContent(AVISO_RESUMIDO);
  });

  it("sin respuesta_fuente (null o ausente) pero con resultado: aviso neutro y el resultado resumido", async () => {
    for (const d of [runtVehiculo(), { ...runtVehiculo(), respuesta_fuente: null } as Detalle]) {
      const { pagina, unmount } = await pintarDetalle(d);
      expect(within(respuesta(pagina)).getByRole("status")).toHaveTextContent(AVISO_RESUMIDO);
      expect(within(pagina).queryByRole("alert")).not.toBeInTheDocument();
      expect(valorDe(within(respuesta(pagina)).getByRole("table", { name: "Respuesta de la fuente" }), "VIN")).toBe("9ZZPRUEBA00000001");
      unmount();
    }
  });

  it("por persona: cada persona pinta su respuesta_fuente; sin ella, su resultado con el aviso (una sola vez)", async () => {
    const d = detalle("RUNT_PROPIETARIO", {
      personas: [
        { ...persona(1), respuesta_fuente: respuestaFuentePersona("PERSONA FICTICIA UNO") },
        { ...persona(2), respuesta_fuente: respuestaFuentePersona("PERSONA FICTICIA DOS") },
      ],
    } as Partial<Detalle>);
    const a = await pintarDetalle(d);
    const [p1, p2] = within(respuesta(a.pagina)).getAllByRole("region", { name: /^propietario \d/i });
    expect(within(p1).getByRole("region", { name: "Persona" })).toHaveTextContent("PERSONA FICTICIA UNO");
    expect(within(p2).getByRole("region", { name: "Persona" })).toHaveTextContent("PERSONA FICTICIA DOS");
    expect(within(p1).getByRole("table", { name: "Licencias" })).toHaveTextContent("DEPTO PRUEBA");
    expect(respuesta(a.pagina)).not.toHaveTextContent(/PROVEEDOR-FICTICIO/);
    expect(respuesta(a.pagina)).not.toHaveTextContent(AVISO_RESUMIDO);
    a.unmount();
    const b = await pintarDetalle(runtTresPropietarios());
    expect(within(respuesta(b.pagina)).getAllByText(AVISO_RESUMIDO)).toHaveLength(1);
  });

  it("Error o Novedad con respuesta_fuente: mensaje sin campos", async () => {
    for (const d of [
      { ...detalle("RUNT", { estado: "ERROR", tipo_error: "TIMEOUT", dato_obtenido_en: null }), respuesta_fuente: respuestaFuenteRunt() },
      { ...detalle("RUNT", { estado: "NA", motivo_na: "SERVICIO_PUBLICO", dato_obtenido_en: null }), respuesta_fuente: respuestaFuenteRunt() },
    ] as Detalle[]) {
      const { pagina, unmount } = await pintarDetalle(d);
      expect(within(respuesta(pagina)).queryByRole("table")).not.toBeInTheDocument();
      expect(within(respuesta(pagina)).queryByRole("region", { name: "Vehículo" })).not.toBeInTheDocument();
      unmount();
    }
  });
});

describe("PaginaDetalleConsulta — HU #13866: impuesto departamental", () => {
  it("pide el detalle del impuesto y pinta monto total y vigencias", async () => {
    const { api, pagina } = await abrir("IMPUESTO_DEPARTAMENTAL", impuestoConVigencias());
    expect(await screen.findByRole("heading", { level: 1, name: `${NOMBRES.IMPUESTO_DEPARTAMENTAL} · TST001` })).toBeInTheDocument();
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/vehiculos/vehiculo-1/consultas/IMPUESTO_DEPARTAMENTAL`)).toHaveLength(1);
    expect(valorDe(within(pagina).getByRole("table", { name: "Respuesta de la fuente" }), "Monto total")).toBe("350000");
  });

  it("la «Novedad» por departamento no soportado muestra el motivo, sin error ni reintento", async () => {
    const { pagina } = await abrir("IMPUESTO_DEPARTAMENTAL", impuestoNa("DEPARTAMENTO_NO_SOPORTADO"));
    expect((await within(pagina).findAllByText(/El departamento del vehículo todavía no está soportado\./)).length).toBeGreaterThan(0);
    expect(within(pagina).queryByRole("alert")).not.toBeInTheDocument();
    expect(within(pagina).queryByRole("button", { name: /reprocesar|reintentar/i })).not.toBeInTheDocument();
  });
});

const AVISO_SIN_PROVEEDOR = "Esta consulta no tiene la respuesta completa del proveedor. Para verla, vuelve a consultar.";
const proveedorDe = (pagina: HTMLElement) => within(respuesta(pagina)).queryAllByRole("region", { name: /^Respuesta del proveedor/ });

/** RUNT_PROPIETARIO con un conductor y una empresa (NIT, RUES) que traen la respuesta íntegra del proveedor. */
const runtConProveedor = (fuente: "RUNT_PROPIETARIO" | "RUNT_LOCATARIO" = "RUNT_PROPIETARIO") => {
  const rol = fuente === "RUNT_PROPIETARIO" ? "PROPIETARIO" : "LOCATARIO";
  return detalle(fuente, {
    personas: [
      { ...persona(1, { rol, documento_enmascarado: "****1222" }), respuesta_proveedor: conductorProveedor() },
      {
        ...persona(2, { rol, tipo_documento: "NIT", documento_enmascarado: "****4567", consulta: "RUES", resultado: { encontrada: true, estado_matricula: "ACTIVA", ultima_renovacion: "2026" } }),
        respuesta_proveedor: empresaProveedor(),
      },
    ],
  } as Partial<Detalle>);
};

describe("PaginaDetalleConsulta — HU #14154: respuesta íntegra del proveedor en RUNT persona y RUES", () => {
  it("AC1: la persona con respuesta_proveedor tiene «Respuesta del proveedor» con todas sus claves (ids, firma y técnicos)", async () => {
    for (const fuente of ["RUNT_PROPIETARIO", "RUNT_LOCATARIO"] as const) {
      const { pagina, unmount } = await pintarDetalle(runtConProveedor(fuente));
      const [p1] = proveedorDe(pagina);
      expect(p1).toBeDefined();
      const tabla = within(p1).getByRole("table", { name: /^Respuesta del proveedor/ });
      expect(valorDe(tabla, "id_consulta")).toBe("CONS-FICT-0001");
      expect(valorDe(tabla, "Estado HTTP")).toBe("200");
      expect(within(p1).getByRole("region", { name: "Firma" })).toHaveTextContent("Certified by PROVEEDOR-FICTICIO");
      expect(within(p1).getByRole("table", { name: "Licencias" })).toHaveTextContent("L-1");
      expect(respuesta(pagina)).not.toHaveTextContent(AVISO_SIN_PROVEEDOR);
      unmount();
    }
  });

  it("AC1: sustituye la vista filtrada de esa persona (no se pinta además su respuesta de la fuente)", async () => {
    const d = runtConProveedor();
    d.personas![0].respuesta_fuente = { persona: { nombre_completo: "PERSONA FILTRADA" } };
    const { pagina } = await pintarDetalle(d);
    expect(within(respuesta(pagina)).queryByRole("table", { name: "Respuesta de la fuente · Propietario 1" })).not.toBeInTheDocument();
    expect(respuesta(pagina)).not.toHaveTextContent("PERSONA FILTRADA");
  });

  it("AC4: una sección por persona, identificada con su rol y su documento enmascarado", async () => {
    const { pagina, unmount } = await pintarDetalle(runtConProveedor());
    const secciones = proveedorDe(pagina);
    expect(secciones).toHaveLength(2);
    expect(secciones[0]).toHaveAccessibleName("Respuesta del proveedor · Propietario · C ****1222");
    expect(secciones[1]).toHaveAccessibleName("Respuesta del proveedor · Propietario · NIT ****4567");
    expect(within(secciones[1]).getByRole("table", { name: "Representantes legales" })).toHaveTextContent("REPRESENTANTE FICTICIO DOS");
    expect(within(secciones[0]).queryByRole("table", { name: "Representantes legales" })).not.toBeInTheDocument();
    unmount();
    const locatario = await pintarDetalle(runtConProveedor("RUNT_LOCATARIO"));
    expect(proveedorDe(locatario.pagina)[0]).toHaveAccessibleName("Respuesta del proveedor · Locatario · C ****1222");
  });

  it("AC4: un documento que llegara completo se enmascara en el título («****» y los 4 últimos)", async () => {
    const d = runtConProveedor();
    d.personas![0].documento_enmascarado = "9999901222";
    const { pagina } = await pintarDetalle(d);
    expect(proveedorDe(pagina)[0]).toHaveAccessibleName("Respuesta del proveedor · Propietario · C ****1222");
  });

  it("AC5: sin respuesta_proveedor (null o ausente) se pinta lo de hoy y el aviso exacto, por persona", async () => {
    const d = detalle("RUNT_PROPIETARIO", {
      personas: [
        { ...persona(1, { documento_enmascarado: "****1222" }), respuesta_proveedor: conductorProveedor() },
        { ...persona(2, { documento_enmascarado: "****3344" }), respuesta_proveedor: null },
        persona(3, { tipo_documento: "NIT", documento_enmascarado: "****4567", consulta: "RUES", resultado: { encontrada: true, estado_matricula: "ACTIVA", ultima_renovacion: "2026" } }),
      ],
    } as Partial<Detalle>);
    const { pagina } = await pintarDetalle(d);
    const [, p2, p3] = within(respuesta(pagina)).getAllByRole("region", { name: /^propietario \d/i });
    expect(within(p2).getByText(AVISO_SIN_PROVEEDOR)).toBeInTheDocument();
    expect(valorDe(within(p2).getByRole("table", { name: "Respuesta de la fuente · Propietario 2" }), "Inscrito")).toBe("Sí");
    expect(within(p3).getByText(AVISO_SIN_PROVEEDOR)).toBeInTheDocument();
    expect(valorDe(within(p3).getByRole("table", { name: "Respuesta de la fuente · Propietario 3" }), "Estado de la matrícula")).toBe("ACTIVA");
    expect(proveedorDe(pagina)).toHaveLength(1);
    expect(within(respuesta(pagina)).getAllByText(AVISO_SIN_PROVEEDOR)).toHaveLength(2);
  });

  it("AC6: RUNT del vehículo no entra en el modo de respuesta del proveedor", async () => {
    const b = await pintarDetalle(runtVehiculoConRespuesta() as Detalle);
    expect(proveedorDe(b.pagina)).toHaveLength(0);
    expect(respuesta(b.pagina)).not.toHaveTextContent(AVISO_SIN_PROVEEDOR);
    expect(respuesta(b.pagina)).not.toHaveTextContent(/PROVEEDOR-FICTICIO|Estado HTTP/);
  });

  it("AC7 vacío: una respuesta del proveedor sin datos lo dice explícitamente", async () => {
    const d = detalle("RUNT_PROPIETARIO", { personas: [{ ...persona(1), respuesta_proveedor: {} }] } as Partial<Detalle>);
    const { pagina } = await pintarDetalle(d);
    expect(proveedorDe(pagina)[0]).toHaveTextContent("El proveedor no devolvió datos para esta persona.");
  });

  it("AC7 cargando y error: el loader mientras llega y el error con Reintentar", async () => {
    let n = 0;
    let soltar: (d: Detalle) => void = () => {};
    const cargar = vi.fn(() => {
      n++;
      if (n === 1) return Promise.reject(new Error("Diagnóstico no respondió. Intenta de nuevo en un momento."));
      return new Promise<Detalle>((r) => (soltar = r));
    });
    render(<PaginaDetalleConsulta loteId={ID} vehiculoId="vehiculo-1" fuente="RUNT_PROPIETARIO" cargar={cargar} />);
    const alerta = await screen.findByRole("alert");
    expect(screen.queryAllByRole("region", { name: /^Respuesta del proveedor/ })).toHaveLength(0);
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("status", { name: /cargando/i })).toBeInTheDocument();
    soltar(runtConProveedor());
    await waitFor(() => expect(screen.getAllByRole("region", { name: /^Respuesta del proveedor/ })).toHaveLength(2));
  });

  it("AC7: «Expandir todo» y «Contraer todo» abren y cierran las subsecciones de «Respuesta del proveedor»", async () => {
    const { pagina } = await pintarDetalle(runtConProveedor());
    const [p1] = proveedorDe(pagina);
    const plegables = () => Array.from(p1.querySelectorAll("details"));
    fireEvent.click(within(p1).getByRole("button", { name: "Expandir todo" }));
    expect(plegables().length).toBeGreaterThan(0);
    expect(plegables().every((x) => x.open)).toBe(true);
    fireEvent.click(within(p1).getByRole("button", { name: "Contraer todo" }));
    expect(plegables().every((x) => !x.open)).toBe(true);
  });

  it("AC7 accesibilidad: h4 de la sección bajo el h3 de la persona y tablas con encabezados", async () => {
    const { pagina } = await pintarDetalle(runtConProveedor());
    const [p1] = proveedorDe(pagina);
    expect(p1.querySelector("h4")).toHaveTextContent("Respuesta del proveedor · Propietario · C ****1222");
    for (const th of p1.querySelectorAll("thead th")) expect(th).toHaveAttribute("scope", "col");
    for (const t of within(p1).getAllByRole("table")) expect(t).toHaveAccessibleName();
  });
});

describe("PaginaDetalleConsulta — HU #14184: respuesta del proveedor en SIMIT", () => {
  const simit = simitVehiculo([comparendo(7)]).resultado as ResultadoSimitDetalle;

  const vehiculoConCrudo = () =>
    detalle("SIMIT_VEHICULO", {
      resultado: simit,
      respuesta_proveedor: { ...simitProveedorVehiculo, claveNuevaProveedor: "se-ve" },
    } as Partial<Detalle>);

  const personaConCrudo = (fuente: "SIMIT_PROPIETARIO" | "SIMIT_LOCATARIO") => {
    const rol = fuente === "SIMIT_PROPIETARIO" ? "PROPIETARIO" : "LOCATARIO";
    return detalle(fuente, {
      personas: [
        {
          ...persona(1, { rol, consulta: "SIMIT", resultado: simit, documento_enmascarado: "****0016" }),
          respuesta_proveedor: simitProveedorPersona,
        },
      ],
    } as Partial<Detalle>);
  };

  const personaSinCrudo = (fuente: "SIMIT_PROPIETARIO" | "SIMIT_LOCATARIO") => {
    const rol = fuente === "SIMIT_PROPIETARIO" ? "PROPIETARIO" : "LOCATARIO";
    return detalle(fuente, {
      personas: [persona(1, { rol, consulta: "SIMIT", resultado: simit, documento_enmascarado: "****0016", respuesta_proveedor: null })],
    } as Partial<Detalle>);
  };

  it("SIMIT vehículo pinta la respuesta del proveedor en tablas, con identificadores y una clave nueva", async () => {
    const { pagina } = await pintarDetalle(vehiculoConCrudo());
    const bloque = proveedorDe(pagina);
    expect(bloque).toHaveLength(1);
    expect(bloque[0]).toHaveAccessibleName("Respuesta del proveedor");
    expect(respuesta(pagina)).not.toHaveTextContent(AVISO_SIN_PROVEEDOR);
    const raiz = within(bloque[0]).getByRole("table", { name: "Respuesta del proveedor" });
    expect(valorDe(raiz, "Id")).toBe("EJEMPLO-VEHICULO");
    expect(valorDe(raiz, "claveNuevaProveedor")).toBe("se-ve");
    const datos = within(bloque[0]).getByRole("table", { name: "Datos" });
    expect(valorDe(datos, "Placa")).toBe("TST202");
    expect(valorDe(datos, "Consulta por placa")).toBe("Sí");
    expect(valorDe(datos, "Multas")).toBe("Sin registros");
    expect(valorDe(datos, "Comparendos")).toBe("Sin registros");
    expect(within(bloque[0]).getByRole("region", { name: "Firma" })).toHaveTextContent("Certified by Verifik.co");
    expect(within(respuesta(pagina)).queryByRole("table", { name: "Comparendos" })).not.toBeInTheDocument();
    for (const th of bloque[0].querySelectorAll("thead th")) expect(th).toHaveAttribute("scope", "col");
    for (const t of within(bloque[0]).getAllByRole("table")) expect(t).toHaveAccessibleName();
  });

  it.each(["SIMIT_PROPIETARIO", "SIMIT_LOCATARIO"] as const)("%s pinta la respuesta del proveedor de la persona", async (fuente) => {
    const { pagina } = await pintarDetalle(personaConCrudo(fuente));
    const [bloque] = proveedorDe(pagina);
    const rol = fuente === "SIMIT_PROPIETARIO" ? "Propietario" : "Locatario";
    expect(bloque).toHaveAccessibleName(`Respuesta del proveedor · ${rol} · C ****0016`);
    expect(respuesta(pagina)).not.toHaveTextContent(AVISO_SIN_PROVEEDOR);
    const datos = within(bloque).getByRole("table", { name: "Datos" });
    expect(valorDe(datos, "Número de documento")).toBe("9999900016");
    expect(valorDe(datos, "Comparendos")).toBe("Sin registros");
    expect(valorDe(datos, "Total de multas por pagar")).toBe("1");
    const multas = within(bloque).getByRole("table", { name: "Multas" });
    expect(within(multas).getByRole("columnheader", { name: "Estado de cartera" })).toBeInTheDocument();
    expect(within(multas).getByRole("columnheader", { name: "Id del departamento" })).toBeInTheDocument();
    expect(multas).toHaveTextContent("Cobro coactivo");
    expect(multas).toHaveTextContent("119");
    expect(multas).toHaveTextContent("C24");
    expect(multas).toHaveTextContent("TST201");
    expect(multas).toHaveTextContent("9999900016");
    const rolNumero = fuente === "SIMIT_PROPIETARIO" ? "Propietario 1" : "Locatario 1";
    expect(valorDe(within(bloque).getByRole("table", { name: `Respuesta del proveedor · ${rolNumero}` }), "Id")).toBe("EJEMPLO-PERSONA");
    for (const th of bloque.querySelectorAll("thead th")) expect(th).toHaveAttribute("scope", "col");
  });

  it("SIMIT vehículo sin crudo muestra el aviso y el resultado resumido", async () => {
    const { pagina } = await pintarDetalle(simitVehiculo([comparendo(7)]));
    expect(proveedorDe(pagina)).toHaveLength(0);
    expect(respuesta(pagina)).toHaveTextContent(AVISO_SIN_PROVEEDOR);
    expect(within(respuesta(pagina)).getByRole("table", { name: "Comparendos" })).toHaveTextContent("C-0007");
  });

  it.each(["SIMIT_PROPIETARIO", "SIMIT_LOCATARIO"] as const)("%s sin crudo muestra el aviso y el resultado resumido", async (fuente) => {
    const { pagina } = await pintarDetalle(personaSinCrudo(fuente));
    expect(proveedorDe(pagina)).toHaveLength(0);
    expect(respuesta(pagina)).toHaveTextContent(AVISO_SIN_PROVEEDOR);
    expect(within(respuesta(pagina)).getByRole("table", { name: "Comparendos" })).toHaveTextContent("C-0007");
  });

  it("RUNT y avalúo no entran en el modo de respuesta del proveedor de la celda", async () => {
    const runt = await pintarDetalle({ ...runtVehiculo(), respuesta_proveedor: simitProveedorVehiculo } as Detalle);
    expect(proveedorDe(runt.pagina)).toHaveLength(0);
    expect(respuesta(runt.pagina)).not.toHaveTextContent(AVISO_SIN_PROVEEDOR);
    expect(respuesta(runt.pagina)).not.toHaveTextContent("EJEMPLO-VEHICULO");
    expect(valorDe(within(respuesta(runt.pagina)).getByRole("table", { name: "Respuesta de la fuente" }), "VIN")).toBe("9ZZPRUEBA00000001");
    runt.unmount();
    const avaluo = await pintarDetalle({ ...avaluoConValor(), respuesta_proveedor: simitProveedorPersona } as Detalle);
    expect(proveedorDe(avaluo.pagina)).toHaveLength(0);
    expect(respuesta(avaluo.pagina)).not.toHaveTextContent(AVISO_SIN_PROVEEDOR);
    expect(respuesta(avaluo.pagina)).not.toHaveTextContent("EJEMPLO-PERSONA");
  });
});
