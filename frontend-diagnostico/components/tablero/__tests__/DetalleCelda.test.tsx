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
  respuestaFuentePersona,
  respuestaFuenteRunt,
  runtLocatarioDeclarado,
  runtPropietarioConError,
  runtTresPropietarios,
  runtVehiculo,
  runtVehiculoConRespuesta,
  simitVehiculo,
} from "@/lib/tablero/__tests__/datosDetalle";

// Uso de ejemplo: en el tablero, cada semáforo es un botón «Ver detalle de {fuente} de {placa}: {estado}»; al pulsarlo (clic,
// Enter o Espacio) GrillaSemaforos abre <DetalleCelda loteId vehiculoId fuente … onClose devolverFocoA={semáforo} />, un
// diálogo accesible que pide GET /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente} y pinta (HU #14087) la «Auditoría de
// consulta» (fecha y hora en Colombia, estado con el chip de la tabla y, por persona, rol, documento enmascarado, consultado_en
// y estado) y la «Respuesta de la fuente» con TODOS los campos del resultado tal como llegan (RespuestaFuente).

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

const region = (dialogo: HTMLElement, nombre: string | RegExp) => within(dialogo).getByRole("region", { name: nombre });
const auditoria = (dialogo: HTMLElement) => region(dialogo, "Auditoría de consulta");
const respuesta = (dialogo: HTMLElement) => region(dialogo, "Respuesta de la fuente");
/** Valor de la fila «campo» de una tabla campo/valor de RespuestaFuente. */
const valorDe = (tabla: HTMLElement, campo: string) => {
  const fila = within(tabla)
    .getAllByRole("row")
    .find((r) => r.querySelector("th[scope='row']")?.textContent === campo);
  if (!fila) throw new Error(`Sin campo ${campo}`);
  return within(fila).getByRole("cell").textContent;
};

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

describe("DetalleCelda — HU #14087 AC7: auditoría de consulta", () => {
  it("fecha y hora de la consulta en hora de Colombia y el estado con el mismo chip de la tabla", async () => {
    const { dialogo } = await abrir("SIMIT_VEHICULO", simitVehiculo());
    const a = await within(dialogo).findByRole("region", { name: "Auditoría de consulta" });
    expect(within(a).getByRole("heading", { name: "Auditoría de consulta" })).toBeInTheDocument();
    expect(a).toHaveTextContent("Fecha y hora de la consulta");
    expect(a).toHaveTextContent("22/10/2026 10:00");
    expect(within(a).getByText("Ejecutado (OK)").closest("[data-tono]")).toHaveAttribute("data-tono", "success");
    expect(a).not.toHaveTextContent(/dato original/i);
  });

  it("reutilizada: indica que la fecha es la del dato original", async () => {
    const { dialogo } = await pintarDetalle({ ...runtVehiculo(), reutilizada: true });
    expect(auditoria(dialogo)).toHaveTextContent("22/10/2026 10:00");
    expect(auditoria(dialogo)).toHaveTextContent(/fecha del dato original/i);
  });

  it("sin fecha (pendiente) muestra «—» y el chip «Sin ejecutar»", async () => {
    const { dialogo } = await pintarDetalle(detalle("SIMIT_VEHICULO", { estado: "PENDIENTE", dato_obtenido_en: null }));
    expect(auditoria(dialogo)).toHaveTextContent("Fecha y hora de la consulta—");
    expect(within(auditoria(dialogo)).getByText("Sin ejecutar")).toBeInTheDocument();
  });

  it("novedad: chip «Novedad» con su motivo", async () => {
    const { dialogo } = await pintarDetalle(detalle("RUNT_LOCATARIO", { estado: "NA", motivo_na: "SIN_DOCUMENTO_LOCATARIO", dato_obtenido_en: null, personas: [] }));
    expect(within(auditoria(dialogo)).getByText("Novedad")).toBeInTheDocument();
    expect(auditoria(dialogo)).toHaveTextContent(/sin el documento del locatario/i);
  });

  it("por persona: rol, documento enmascarado tal cual, consultado_en y estado mapeado", async () => {
    const d = detalle("RUNT_PROPIETARIO", {
      personas: [
        persona(1, { documento_enmascarado: "****1222" }),
        persona(2, { tipo_documento: "NIT", documento_enmascarado: "****4567", estado: "SIN_RESULTADO", resultado: null }),
        persona(3, { documento_enmascarado: "****", estado: "ERROR", tipo_error: "TIMEOUT", consultado_en: null, resultado: null }),
      ],
    });
    const { dialogo } = await pintarDetalle(d);
    const tabla = within(auditoria(dialogo)).getByRole("table", { name: "Personas consultadas" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Persona", "Rol", "Documento", "Consultado en", "Estado"]);
    for (const c of within(tabla).getAllByRole("columnheader")) expect(c).toHaveAttribute("scope", "col");
    const [p1, p2, p3] = within(tabla).getAllByRole("row").slice(1);
    expect(p1).toHaveTextContent("Propietario");
    expect(p1).toHaveTextContent("C ****1222");
    expect(p1).toHaveTextContent("22/10/2026 10:00");
    expect(p1).toHaveTextContent("Ejecutado (OK)");
    expect(p2).toHaveTextContent("NIT ****4567");
    expect(p2).toHaveTextContent("Novedad");
    expect(p2).toHaveTextContent("Sin resultado");
    expect(p3).toHaveTextContent("C ****");
    expect(p3.textContent).not.toMatch(/\*{4}\d/);
    expect(p3).toHaveTextContent("Error");
    expect(p3).toHaveTextContent(/no respondió a tiempo/i);
    expect(p3).toHaveTextContent("—");
  });

  it("el documento declarado por el cliente lleva el rótulo; el no declarado no", async () => {
    const { dialogo } = await abrir("RUNT_LOCATARIO", runtLocatarioDeclarado());
    const tabla = await within(dialogo).findByRole("table", { name: "Personas consultadas" });
    expect(tabla).toHaveTextContent("C ****5678");
    expect(tabla).toHaveTextContent("Locatario");
    expect(within(tabla).getByText(/declarado por el cliente/i)).toBeInTheDocument();
  });

  it("sin declarar no lleva el rótulo", async () => {
    const { dialogo } = await pintarDetalle(runtTresPropietarios());
    expect(within(dialogo).queryByText(/declarado por el cliente/i)).not.toBeInTheDocument();
  });
});

describe("DetalleCelda — HU #14087 AC8: respuesta de la fuente con todos los campos verbatim", () => {
  it("SIMIT vehículo: semaforo, cantidad, truncados y la tabla de comparendos con sus claves y valores tal cual", async () => {
    const { dialogo } = await abrir("SIMIT_VEHICULO", simitVehiculo([comparendo(1), comparendo(2, { valor: "1250000", estado: "PAGADO" })]));
    const r = await within(dialogo).findByRole("region", { name: "Respuesta de la fuente" });
    const tabla = within(r).getByRole("table", { name: "Respuesta de la fuente" });
    expect(valorDe(tabla, "semaforo")).toBe("ROJO");
    expect(valorDe(tabla, "cantidad_comparendos")).toBe("2");
    expect(valorDe(tabla, "comparendos_truncados")).toBe("false");
    const comparendos = within(r).getByRole("table", { name: "comparendos" });
    expect(within(comparendos).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["numero", "fecha", "valor", "estado", "organismo"]);
    const filas = within(comparendos).getAllByRole("row").slice(1);
    expect(within(filas[0]).getAllByRole("cell").map((c) => c.textContent)).toEqual(["C-0001", "2026-01-02", "500000", "PENDIENTE", "ORGANISMO PRUEBA"]);
    expect(within(filas[1]).getAllByRole("cell").map((c) => c.textContent)).toEqual(["C-0002", "2026-01-02", "1250000", "PAGADO", "ORGANISMO PRUEBA"]);
  });

  it("una lista larga de comparendos se pinta completa (100), sin cortar ni inventar", async () => {
    const lista = Array.from({ length: 100 }, (_, i) => comparendo(i + 1));
    const { dialogo } = await pintarDetalle(simitVehiculo(lista, true, 250));
    expect(within(within(dialogo).getByRole("table", { name: "comparendos" })).getAllByRole("row").slice(1)).toHaveLength(100);
    const tabla = within(dialogo).getByRole("table", { name: "Respuesta de la fuente" });
    expect(valorDe(tabla, "cantidad_comparendos")).toBe("250");
    expect(valorDe(tabla, "comparendos_truncados")).toBe("true");
  });

  it("sin comparendos: «[]»", async () => {
    const { dialogo } = await pintarDetalle(simitVehiculo([]));
    expect(valorDe(within(dialogo).getByRole("table", { name: "Respuesta de la fuente" }), "comparendos")).toBe("[]");
  });

  it("RUNT del vehículo: todos sus campos y la tabla de propietarios", async () => {
    const { dialogo } = await pintarDetalle(runtVehiculo());
    const tabla = within(respuesta(dialogo)).getByRole("table", { name: "Respuesta de la fuente" });
    expect(within(tabla).getAllByRole("rowheader").map((c) => c.textContent)).toEqual(["placa", "vin", "servicio", "clase", "organismo", "locatarios"]);
    expect(valorDe(tabla, "vin")).toBe("9ZZPRUEBA00000001");
    expect(valorDe(tabla, "locatarios")).toBe("0");
    const propietarios = within(respuesta(dialogo)).getByRole("table", { name: "propietarios" });
    expect(within(propietarios).getAllByRole("cell").map((c) => c.textContent)).toEqual(["C", "PROPIA"]);
  });

  it("avalúo: valores numéricos sin formato y null verbatim", async () => {
    const { dialogo } = await pintarDetalle(avaluoConValor());
    const tabla = within(respuesta(dialogo)).getByRole("table", { name: "Respuesta de la fuente" });
    expect(valorDe(tabla, "sin_valor_comercial")).toBe("false");
    expect(valorDe(tabla, "valor_sugerido")).toBe("45000000");
    expect(valorDe(tabla, "fuente_principal")).toBe("fasecolda");
    const fuentes = within(respuesta(dialogo)).getByRole("table", { name: "fuentes" });
    const filas = within(fuentes).getAllByRole("row").slice(1);
    expect(within(filas[0]).getAllByRole("cell").map((c) => c.textContent)).toEqual(["fasecolda", "OK", "45000000", "null", "2026-10-22"]);
    expect(dialogo.textContent).not.toMatch(/\$\s?45/);
  });

  it("avalúo sin valor comercial: sin_valor_comercial true y valor_sugerido null", async () => {
    const { dialogo } = await abrir("AVALUO", avaluoSinValor());
    const tabla = await within(dialogo).findByRole("table", { name: "Respuesta de la fuente" });
    expect(valorDe(tabla, "sin_valor_comercial")).toBe("true");
    expect(valorDe(tabla, "valor_sugerido")).toBe("null");
  });

  it("impuesto: monto_total y la tabla de vigencias con anio, valor y pagada tal cual", async () => {
    const { dialogo } = await pintarDetalle(impuestoConVigencias());
    expect(valorDe(within(dialogo).getByRole("table", { name: "Respuesta de la fuente" }), "monto_total")).toBe("350000");
    const vigencias = within(dialogo).getByRole("table", { name: "vigencias" });
    expect(within(vigencias).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["anio", "valor", "pagada"]);
    expect(within(vigencias).getAllByRole("row").slice(1).map((f) => within(f).getAllByRole("cell").map((c) => c.textContent))).toEqual([
      ["2025", "180000", "true"],
      ["2026", "350000", "false"],
    ]);
  });

  it("impuesto sin vigencias: «[]»", async () => {
    const { dialogo } = await pintarDetalle(impuestoSinVigencias());
    expect(valorDe(within(dialogo).getByRole("table", { name: "Respuesta de la fuente" }), "vigencias")).toBe("[]");
  });

  it("fuentes de persona: una subsección por persona con todos los campos de su resultado", async () => {
    const { dialogo } = await abrir("RUNT_PROPIETARIO", runtTresPropietarios());
    const r = await within(dialogo).findByRole("region", { name: "Respuesta de la fuente" });
    const secciones = within(r).getAllByRole("region", { name: /^propietario \d/i });
    expect(secciones).toHaveLength(3);
    const t1 = within(secciones[0]).getByRole("table", { name: "Respuesta de la fuente · Propietario 1" });
    expect(valorDe(t1, "inscrito")).toBe("true");
    expect(valorDe(t1, "estado_conductor")).toBe("ACTIVO");
    expect(within(within(secciones[0]).getByRole("table", { name: "licencias" })).getAllByRole("cell").map((c) => c.textContent)).toEqual(["B1", "VIGENTE", "2030-01-01"]);
    const t2 = within(secciones[1]).getByRole("table", { name: "Respuesta de la fuente · Propietario 2" });
    expect(valorDe(t2, "inscrito")).toBe("false");
    expect(valorDe(t2, "estado_conductor")).toBe("null");
    expect(valorDe(t2, "licencias")).toBe("[]");
    const t3 = within(secciones[2]).getByRole("table", { name: "Respuesta de la fuente · Propietario 3" });
    expect(valorDe(t3, "encontrada")).toBe("true");
    expect(valorDe(t3, "estado_matricula")).toBe("ACTIVA");
    expect(valorDe(t3, "ultima_renovacion")).toBe("2026");
  });

  it("SIMIT por persona: los comparendos de la persona como tabla", async () => {
    const simit = simitVehiculo([comparendo(7)]).resultado as ResultadoSimitDetalle;
    const { dialogo } = await pintarDetalle(detalle("SIMIT_PROPIETARIO", { personas: [persona(1, { consulta: "SIMIT", resultado: simit })] }));
    const seccion = within(respuesta(dialogo)).getByRole("region", { name: /propietario 1/i });
    expect(within(seccion).getByRole("table", { name: "comparendos" })).toHaveTextContent("C-0007");
  });

  it("sin personas del rol: estado vacío claro", async () => {
    const { dialogo } = await pintarDetalle(detalle("RUNT_LOCATARIO", { personas: [] }));
    expect(respuesta(dialogo)).toHaveTextContent("El vehículo no tiene locatarios para esta consulta.");
  });
});

describe("DetalleCelda — HU #14087 AC8: sin resultado no hay campos", () => {
  it("Sin ejecutar: lo dice y no pinta campos", async () => {
    const { dialogo } = await pintarDetalle(detalle("SIMIT_VEHICULO", { estado: "EN_CURSO", dato_obtenido_en: null }));
    expect(respuesta(dialogo)).toHaveTextContent(/todavía no se ha ejecutado/i);
    expect(within(respuesta(dialogo)).queryByRole("table")).not.toBeInTheDocument();
  });

  it("Novedad: lo dice con su motivo y no pinta campos", async () => {
    const { dialogo } = await pintarDetalle(impuestoNa("SERVICIO_PUBLICO"));
    expect(respuesta(dialogo)).toHaveTextContent("El vehículo es de servicio público: esta consulta no aplica.");
    expect(respuesta(dialogo)).toHaveTextContent(/sin respuesta de la fuente/i);
    expect(within(respuesta(dialogo)).queryByRole("table")).not.toBeInTheDocument();
  });

  it("Error: el código traducido (nunca crudo) y sin campos", async () => {
    const { dialogo } = await pintarDetalle(detalle("SIMIT_VEHICULO", { estado: "ERROR", tipo_error: "TIMEOUT", dato_obtenido_en: null }));
    expect(within(dialogo).getByRole("alert")).toHaveTextContent(/no respondió a tiempo/i);
    expect(dialogo.textContent).not.toContain("TIMEOUT");
    expect(within(respuesta(dialogo)).queryByRole("table")).not.toBeInTheDocument();
  });

  it.each([
    ["AUTENTICACION", "El proveedor rechazó las credenciales de FLIT. Avísale al administrador para que las renueve; luego puedes reprocesar la consulta."],
    ["LIMITE_PROVEEDOR", "El proveedor limitó las consultas por ahora. Se reintentará automáticamente."],
  ])("Error %s del proveedor: el texto claro, nunca el código crudo", async (tipo, texto) => {
    const { dialogo } = await pintarDetalle(detalle("SIMIT_VEHICULO", { estado: "ERROR", tipo_error: tipo, dato_obtenido_en: null }));
    expect(within(dialogo).getByRole("alert")).toHaveTextContent(texto);
    expect(dialogo.textContent).not.toContain(tipo);
  });

  it("persona en ERROR por AUTENTICACION muestra el texto del proveedor bajo su fila", async () => {
    const d = detalle("RUNT_PROPIETARIO", {
      personas: [persona(1, { documento_enmascarado: "****", estado: "ERROR", tipo_error: "AUTENTICACION", consultado_en: null, resultado: null })],
    });
    const { dialogo } = await pintarDetalle(d);
    expect(within(auditoria(dialogo)).getByText(/rechazó las credenciales de FLIT/)).toBeInTheDocument();
    expect(dialogo.textContent).not.toContain("AUTENTICACION");
  });

  it("Error bloqueado por el RUNT lo explica", async () => {
    const { dialogo } = await pintarDetalle(detalle("AVALUO", { estado: "ERROR", tipo_error: "BLOQUEADA_POR_RUNT", dato_obtenido_en: null }));
    expect(within(dialogo).getByRole("alert")).toHaveTextContent(/RUNT del vehículo/i);
  });

  it("ERROR PERSONAS_CON_ERROR: muestra lo que sí respondió y, en la persona que falló, que no hay respuesta", async () => {
    const { dialogo } = await pintarDetalle(runtPropietarioConError());
    expect(within(dialogo).getByRole("alert")).toHaveTextContent(/personas no se pudieron consultar/i);
    const [ok, fallida] = within(respuesta(dialogo)).getAllByRole("region", { name: /^propietario \d/i });
    expect(valorDe(within(ok).getByRole("table", { name: /Propietario 1$/ }), "inscrito")).toBe("true");
    expect(within(fallida).queryByRole("table")).not.toBeInTheDocument();
    expect(fallida).toHaveTextContent(/sin respuesta de la fuente/i);
  });

  it("completada sin resultado (backoffice): lo dice sin inventar campos", async () => {
    const { dialogo } = await pintarDetalle(detalle("SIMIT_VEHICULO", { resultado: null }));
    expect(respuesta(dialogo)).toHaveTextContent(/no trae campos/i);
    expect(within(respuesta(dialogo)).queryByRole("table")).not.toBeInTheDocument();
  });
});

describe("DetalleCelda — R-20 (negativo): nada distingue una celda llenada a mano ni sale crudo del proveedor", () => {
  const SOSPECHOSOS = /manual|a mano|backoffice|origen|usuario|proveedor|transacci/i;
  const limpiar = (html: string) => html.replace(/\s(id|aria-labelledby|aria-describedby|aria-controls)="[^"]*"/g, "");

  it("ni texto ni icono ni estilo: el DOM es idéntico al de una consultada por API aunque llegaran claves extra", async () => {
    const api = runtTresPropietarios();
    const manual = { ...runtTresPropietarios(), origen: "MANUAL", usuario_id: "u-1", proveedor: "x" } as unknown as Detalle;
    const a = await pintarDetalle(api);
    const htmlApi = limpiar(a.dialogo.innerHTML);
    a.unmount();
    const b = await pintarDetalle(manual);
    expect(limpiar(b.dialogo.innerHTML)).toBe(htmlApi);
    expect(b.dialogo.textContent).not.toMatch(SOSPECHOSOS);
    expect(b.dialogo.querySelector("[data-origen]")).toBeNull();
  });

  it("tampoco en el SIMIT, el avalúo ni el RUNT", async () => {
    for (const d of [simitVehiculo(), avaluoSinValor(), runtVehiculo()]) {
      const { dialogo, unmount } = await pintarDetalle({ ...d, origen: "MANUAL" } as unknown as Detalle);
      expect(dialogo.textContent).not.toMatch(SOSPECHOSOS);
      unmount();
    }
  });

  it("fuera de resultado y respuesta_fuente las claves extra no se pintan; dentro, todo se pinta verbatim (decisión «todo»)", async () => {
    const conExtra = {
      ...impuestoConVigencias(),
      transaccion_proveedor_id: "TX-FICTICIA-0099",
      origen: "API",
      departamento_codigo: "05",
      resultado: { monto_total: 350000, proveedor: "PROVEEDOR-FICTICIO-ZETA", vigencias: [{ anio: 2025, valor: 180000, pagada: true, departamento: "DEPTO PRUEBA" }] },
    } as unknown as Detalle;
    const { dialogo } = await pintarDetalle(conExtra);
    expect(dialogo.textContent).not.toMatch(/TX-FICTICIA-0099|departamento_codigo|origen/i);
    expect(valorDe(within(dialogo).getByRole("table", { name: "Respuesta de la fuente" }), "proveedor")).toBe("PROVEEDOR-FICTICIO-ZETA");
    expect(within(within(dialogo).getByRole("table", { name: "vigencias" })).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["anio", "valor", "pagada", "departamento"]);
  });
});

const AVISO_RESUMIDO =
  "Esta consulta no tiene guardada la respuesta completa de la fuente (es anterior al cambio, se reutilizó o se registró sin consulta). Se muestra el resultado resumido.";

describe("DetalleCelda — HU #14087 AC8 ajustado: respuesta completa de la fuente (respuesta_fuente)", () => {
  it("con respuesta_fuente pinta todos sus campos (no el resultado resumido) y sin aviso", async () => {
    const { dialogo } = await pintarDetalle(runtVehiculoConRespuesta() as Detalle);
    const r = respuesta(dialogo);
    const tabla = within(r).getByRole("table", { name: "Respuesta de la fuente" });
    expect(valorDe(tabla, "proveedor")).toBe("PROVEEDOR-FICTICIO");
    expect(valorDe(tabla, "http_status")).toBe("200");
    expect(within(r).getByRole("region", { name: "vehiculo" })).toBeInTheDocument();
    expect(within(r).getByRole("region", { name: "soat (2)" })).toBeInTheDocument();
    expect(within(r).getByRole("table", { name: "direcciones" })).toHaveTextContent("DEPTO PRUEBA");
    expect(within(r).queryByRole("table", { name: "propietarios" })).not.toBeInTheDocument();
    expect(r).not.toHaveTextContent(AVISO_RESUMIDO);
  });

  it("sin respuesta_fuente (null o ausente) pero con resultado: aviso neutro y el resultado resumido", async () => {
    for (const d of [runtVehiculo(), { ...runtVehiculo(), respuesta_fuente: null } as Detalle]) {
      const { dialogo, unmount } = await pintarDetalle(d);
      const aviso = within(respuesta(dialogo)).getByRole("status");
      expect(aviso).toHaveTextContent(AVISO_RESUMIDO);
      expect(within(dialogo).queryByRole("alert")).not.toBeInTheDocument();
      expect(valorDe(within(respuesta(dialogo)).getByRole("table", { name: "Respuesta de la fuente" }), "vin")).toBe("9ZZPRUEBA00000001");
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
    const [p1, p2] = within(respuesta(a.dialogo)).getAllByRole("region", { name: /^propietario \d/i });
    expect(valorDe(within(p1).getByRole("table", { name: "Respuesta de la fuente · Propietario 1" }), "proveedor")).toBe("PROVEEDOR-FICTICIO");
    expect(within(p1).getByRole("region", { name: "persona" })).toHaveTextContent("PERSONA FICTICIA UNO");
    expect(within(p2).getByRole("region", { name: "persona" })).toHaveTextContent("PERSONA FICTICIA DOS");
    expect(within(p1).getByRole("table", { name: "licencias" })).toHaveTextContent("DEPTO PRUEBA");
    expect(respuesta(a.dialogo)).not.toHaveTextContent(AVISO_RESUMIDO);
    a.unmount();
    const b = await pintarDetalle(runtTresPropietarios());
    expect(within(respuesta(b.dialogo)).getAllByText(AVISO_RESUMIDO)).toHaveLength(1);
    expect(valorDe(within(respuesta(b.dialogo)).getByRole("table", { name: "Respuesta de la fuente · Propietario 1" }), "inscrito")).toBe("true");
  });

  it("Error o Novedad con respuesta_fuente: igual que hoy, mensaje sin campos", async () => {
    for (const d of [
      { ...detalle("RUNT", { estado: "ERROR", tipo_error: "TIMEOUT", dato_obtenido_en: null }), respuesta_fuente: respuestaFuenteRunt() },
      { ...detalle("RUNT", { estado: "NA", motivo_na: "SERVICIO_PUBLICO", dato_obtenido_en: null }), respuesta_fuente: respuestaFuenteRunt() },
    ] as Detalle[]) {
      const { dialogo, unmount } = await pintarDetalle(d);
      expect(within(respuesta(dialogo)).queryByRole("table")).not.toBeInTheDocument();
      expect(dialogo.textContent).not.toContain("PROVEEDOR-FICTICIO");
      unmount();
    }
  });

  it("los títulos plegables entran en el foco atrapado: Tab en el último summary vuelve al primero", async () => {
    render(
      <DetalleCelda loteId={ID} vehiculoId="vehiculo-1" fuente="RUNT" nombreFuente="RUNT vehículo" identificador="TST001" onClose={() => {}} cargar={async () => runtVehiculoConRespuesta() as Detalle} />,
    );
    const dialogo = await screen.findByRole("dialog");
    await within(dialogo).findByRole("region", { name: "vehiculo" });
    const summaries = dialogo.querySelectorAll("summary");
    const ultimo = summaries[summaries.length - 1] as HTMLElement;
    const primero = within(dialogo).getAllByRole("button")[0];
    ultimo.focus();
    fireEvent.keyDown(document, { key: "Tab" });
    expect(document.activeElement).toBe(primero);
  });
});

describe("DetalleCelda — los otros estados de carga", () => {
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

  it("las secciones son h3 bajo el título del diálogo y las tablas llevan scope", async () => {
    const { dialogo } = await pintarDetalle(runtTresPropietarios());
    expect(within(dialogo).getByRole("heading", { level: 3, name: "Auditoría de consulta" })).toBeInTheDocument();
    expect(within(dialogo).getByRole("heading", { level: 3, name: "Respuesta de la fuente" })).toBeInTheDocument();
    for (const th of within(dialogo).getAllByRole("columnheader")) expect(th).toHaveAttribute("scope", "col");
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
    render(
      <DetalleCelda
        loteId={ID}
        vehiculoId="vehiculo-1"
        fuente="SIMIT_VEHICULO"
        nombreFuente="SIMIT vehículo"
        identificador="TST001"
        onClose={() => {}}
        cargar={async () => simitVehiculo()}
        acciones={<button type="button">Acción de prueba</button>}
      />,
    );
    const dialogo = await screen.findByRole("dialog");
    await within(dialogo).findByRole("region", { name: "Respuesta de la fuente" });
    const enfocables = within(dialogo).getAllByRole("button");
    expect(enfocables.length).toBeGreaterThan(1);
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

// HU #13866 (F09-H7) con HU #14087: celda y detalle del impuesto departamental. El semáforo «Ver detalle de Impuesto
// departamental de TST001» abre el diálogo con la auditoría y los campos verbatim, o el motivo cuando es Novedad.
describe("DetalleCelda — HU #13866: impuesto departamental", () => {
  /** Grilla de TST001 con RUNT completado y el impuesto en el estado pedido. */
  function grillaImpuesto(impuesto: ReturnType<typeof celda>, puedeReprocesar = false) {
    const v = vehiculo(1, [celda(1, "RUNT", "COMPLETADO"), impuesto]);
    return render(
      <GrillaSemaforos grilla={grilla({ vehiculos: [v], columnas: { por_vehiculo: ["RUNT", "IMPUESTO_DEPARTAMENTAL"], por_propietario: [], por_locatario: [] } })} puedeReprocesar={puedeReprocesar} />,
    );
  }

  it("desde el semáforo verde pide el detalle del impuesto y pinta monto_total y vigencias", async () => {
    const api = simularApi({ [rutaDetalle("IMPUESTO_DEPARTAMENTAL")]: () => Response.json(impuestoConVigencias()) });
    grillaImpuesto(celda(1, "IMPUESTO_DEPARTAMENTAL", "COMPLETADO"));
    fireEvent.click(screen.getByRole("button", { name: /^Ver detalle de Impuesto departamental de TST001: Ejecutado \(OK\)$/ }));
    const dialogo = await screen.findByRole("dialog", { name: "Impuesto departamental · TST001" });
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/vehiculos/vehiculo-1/consultas/IMPUESTO_DEPARTAMENTAL`)).toHaveLength(1);
    const tabla = await within(dialogo).findByRole("table", { name: "Respuesta de la fuente" });
    expect(valorDe(tabla, "monto_total")).toBe("350000");
    expect(within(dialogo).getByRole("table", { name: "vigencias" })).toBeInTheDocument();
  });

  it("la «Novedad» por departamento no soportado abre su detalle con el motivo, sin error ni reproceso", async () => {
    simularApi({ [rutaDetalle("IMPUESTO_DEPARTAMENTAL")]: () => Response.json(impuestoNa("DEPARTAMENTO_NO_SOPORTADO")) });
    const { container } = grillaImpuesto(celda(1, "IMPUESTO_DEPARTAMENTAL", "NA", { motivo_na: "DEPARTAMENTO_NO_SOPORTADO" }), true);
    expect(container.querySelector('[data-celda="celda-1-impuesto_departamental"]')).toHaveTextContent("Departamento no soportado");
    expect(screen.queryByRole("button", { name: /reprocesar/i })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /^Ver detalle de Impuesto departamental de TST001: Novedad$/ }));
    const dialogo = await screen.findByRole("dialog", { name: "Impuesto departamental · TST001" });
    expect((await within(dialogo).findAllByText(/El departamento del vehículo todavía no está soportado\./)).length).toBeGreaterThan(0);
    expect(within(dialogo).queryByRole("alert")).not.toBeInTheDocument();
    expect(within(dialogo).queryByRole("button", { name: /reprocesar|reintentar/i })).not.toBeInTheDocument();
    expect(within(dialogo).queryByText(/error/i)).not.toBeInTheDocument();
  });
});
