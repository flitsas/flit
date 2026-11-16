import { describe, expect, it } from "vitest";
import {
  FILTROS_GRILLA_VACIOS,
  FILTROS_SANEAMIENTO,
  FILTROS_TABLERO,
  aplicarFiltrosGrilla,
  contarCambioPropietario,
  estadoFiltroDe,
  fuentesFiltrables,
  hayDatoCambioPropietario,
  hayFiltrosGrilla,
  hayVeredictos,
  opcionesEstado,
  buscarVehiculos,
  filtrarPorSaneamiento,
  filtrarVehiculos,
  hayDatosSaneamiento,
  leerFiltroSaneamiento,
  tarjetasVisibles,
  veredictoDeFila,
} from "@/lib/tablero/filtrosTablero";
import { V1, V2, V3, celda, grilla, vehiculo } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: filtrarVehiculos(grilla.vehiculos, "bloqueados") → solo las filas con veredicto BLOQUEADO; tarjetasVisibles(grilla)
// → las tarjetas-filtro que se pueden pintar con los conteos del servidor (sin conteos, solo Vehículos y Terminados del avance).

const CONTEOS = { vehiculos: 3, terminados: 1, con_error: 1, con_deuda: 1, bloqueados: 1, listos_tramite: 1 };

describe("filtrosTablero — HU #14562 AC27, AC28 y AC31", () => {
  it("AC27: seis tarjetas en orden, con la cifra del servidor (no la suma de filas)", () => {
    const g = grilla({ conteos: { ...CONTEOS, vehiculos: 40 } });
    const tarjetas = tarjetasVisibles(g);
    expect(tarjetas.map((t) => t.etiqueta)).toEqual(["Vehículos", "Terminados", "Con error", "Con deuda", "Bloqueados", "Listos para trámite"]);
    expect(tarjetas[0].cifra).toBe(40);
    expect(FILTROS_TABLERO).toHaveLength(6);
  });

  it("AC31: sin conteos solo quedan Vehículos y Terminados, tomados del avance", () => {
    const tarjetas = tarjetasVisibles(grilla());
    expect(tarjetas.map((t) => [t.etiqueta, t.cifra])).toEqual([["Vehículos", 3], ["Terminados", 1]]);
  });

  it("AC27: cada filtro deja pasar solo sus filas", () => {
    const deuda = vehiculo(5, [celda(5, "IMPUESTO_DEPARTAMENTAL", "COMPLETADO", { tiene_deuda: true })]);
    const bloqueado = { ...V1, id: "b", veredicto: "BLOQUEADO" as const };
    const listo = { ...V1, id: "l", veredicto: "VIABLE" as const, derivacion: { elegible: true, motivo_no_elegible: null, estado: null, tramite: null } };
    const filas = [V1, V2, V3, deuda, bloqueado, listo];
    expect(filtrarVehiculos(filas, null)).toHaveLength(6);
    expect(filtrarVehiculos(filas, "vehiculos")).toHaveLength(6);
    expect(filtrarVehiculos(filas, "terminados").map((v) => v.id)).toEqual([V1.id, deuda.id, "b", "l"]);
    expect(filtrarVehiculos(filas, "con_error").map((v) => v.id)).toEqual([V2.id]);
    expect(filtrarVehiculos(filas, "con_deuda").map((v) => v.id)).toEqual([deuda.id]);
    expect(filtrarVehiculos(filas, "bloqueados").map((v) => v.id)).toEqual(["b"]);
    expect(filtrarVehiculos(filas, "listos_tramite").map((v) => v.id)).toEqual(["l"]);
  });

  it("AC28: la búsqueda filtra por placa o VIN sin distinguir mayúsculas ni espacios", () => {
    const conVin = vehiculo(7, [], { placa: null, vin: "9BWZZZ377VT004251", identificador: "9BWZZZ377VT004251" });
    expect(buscarVehiculos([V1, V2, conVin], " tst002 ").map((v) => v.id)).toEqual([V2.id]);
    expect(buscarVehiculos([V1, V2, conVin], "vt0042").map((v) => v.id)).toEqual([conVin.id]);
    expect(buscarVehiculos([V1, V2], "")).toHaveLength(2);
  });

  it("AC28/AC31: Viable, Bloqueado o Pendiente (sin la clave o null)", () => {
    expect(veredictoDeFila({ ...V1, veredicto: "VIABLE" }).texto).toBe("Viable");
    expect(veredictoDeFila({ ...V1, veredicto: "BLOQUEADO" }).texto).toBe("Bloqueado");
    expect(veredictoDeFila({ ...V1, veredicto: null }).texto).toBe("Pendiente");
    expect(veredictoDeFila(V1).texto).toBe("Pendiente");
  });
});

// Uso de ejemplo (HU #14577 AC9): filtrarPorSaneamiento(filas, "con") → solo las filas con saneamiento (cualquier estado no
// anulado); "sin" → las que no lo tienen; null → todas. leerFiltroSaneamiento("con") → "con" (el valor de ?saneamiento=).
describe("filtrosTablero — saneamiento (HU #14577 AC9)", () => {
  const sin = { estado: null, elegible: true, motivo_no_elegible: null } as const;
  const conEstado = (n: number, estado: "SOLICITADO" | "EN_GESTION" | "SANEADO" | "NO_PROCEDE") =>
    vehiculo(n, [], { saneamiento: { estado, elegible: false, motivo_no_elegible: "YA_SOLICITADO" } });
  const filas = [
    conEstado(10, "SOLICITADO"),
    conEstado(11, "EN_GESTION"),
    conEstado(12, "SANEADO"),
    conEstado(13, "NO_PROCEDE"),
    vehiculo(14, [], { saneamiento: sin }),
    vehiculo(15, []),
  ];

  it("«con» deja pasar los cuatro estados no anulados; «sin», las filas sin saneamiento o sin la clave", () => {
    expect(filtrarPorSaneamiento(filas, "con").map((v) => v.id)).toEqual(["vehiculo-10", "vehiculo-11", "vehiculo-12", "vehiculo-13"]);
    expect(filtrarPorSaneamiento(filas, "sin").map((v) => v.id)).toEqual(["vehiculo-14", "vehiculo-15"]);
  });

  it("sin filtro (null) devuelve todas las filas, en el mismo orden", () => {
    expect(filtrarPorSaneamiento(filas, null)).toEqual(filas);
  });

  it("un ANULADO que llegue por error cuenta como sin saneamiento (la compañía no ve anulados)", () => {
    const anulado = vehiculo(16, [], { saneamiento: { estado: "ANULADO" as never, elegible: true, motivo_no_elegible: null } });
    expect(filtrarPorSaneamiento([anulado], "con")).toEqual([]);
    expect(filtrarPorSaneamiento([anulado], "sin")).toEqual([anulado]);
  });

  it("lee ?saneamiento=con|sin de la URL; cualquier otro valor, vacío o repetido toma el primero o nada", () => {
    expect(leerFiltroSaneamiento("con")).toBe("con");
    expect(leerFiltroSaneamiento("sin")).toBe("sin");
    expect(leerFiltroSaneamiento(["sin", "con"])).toBe("sin");
    expect(leerFiltroSaneamiento("todos")).toBeNull();
    expect(leerFiltroSaneamiento("")).toBeNull();
    expect(leerFiltroSaneamiento(undefined)).toBeNull();
    expect(leerFiltroSaneamiento(null)).toBeNull();
  });

  it("los dos chips en orden, con su texto", () => {
    expect(FILTROS_SANEAMIENTO).toEqual([
      { clave: "con", etiqueta: "Con saneamiento" },
      { clave: "sin", etiqueta: "Sin saneamiento" },
    ]);
  });

  it("hayDatosSaneamiento: solo si alguna fila trae la clave (backend previo: sin chips)", () => {
    expect(hayDatosSaneamiento([vehiculo(15, [])])).toBe(false);
    expect(hayDatosSaneamiento(filas)).toBe(true);
    expect(hayDatosSaneamiento([])).toBe(false);
  });
});

// ---------- HU #14584 (spec-ui §6.6, A6.8): fuente + estado, dictamen, cambio de propietario y «Limpiar filtros» ----------
// Uso de ejemplo: aplicarFiltrosGrilla(grilla.vehiculos, { ...FILTROS_GRILLA_VACIOS, fuente: "SIMIT_PROPIETARIO", estado: "ERROR" }) → solo los vehículos cuya celda SIMIT propietario está en Error.
describe("filtrosTablero — HU #14584: barra de filtros de la grilla (en memoria, solo códigos)", () => {
  const impuestoConDeuda = vehiculo(4, [celda(4, "IMPUESTO_DEPARTAMENTAL", "COMPLETADO", { tiene_deuda: true }), celda(4, "SIMIT_PROPIETARIO", "COMPLETADO")], {
    veredicto: "BLOQUEADO",
    cambio_propietario: true,
  });
  const viable = vehiculo(5, [celda(5, "SIMIT_PROPIETARIO", "ERROR")], { veredicto: "VIABLE", cambio_propietario: false });
  const filas = [V1, V2, V3, impuestoConDeuda, viable];

  it("los filtros vacíos dejan pasar todo y no cuentan como activos", () => {
    expect(aplicarFiltrosGrilla(filas, FILTROS_GRILLA_VACIOS)).toEqual(filas);
    expect(hayFiltrosGrilla(FILTROS_GRILLA_VACIOS)).toBe(false);
    expect(hayFiltrosGrilla({ ...FILTROS_GRILLA_VACIOS, busqueda: "  " })).toBe(false);
  });

  it("estado visible de la celda: Ejecutado, Con deuda (impuesto), Error, Novedad y Sin ejecutar (pendiente, en curso o sin celda)", () => {
    expect(estadoFiltroDe(V1, "RUNT")).toBe("EJECUTADO");
    expect(estadoFiltroDe(impuestoConDeuda, "IMPUESTO_DEPARTAMENTAL")).toBe("CON_DEUDA");
    expect(estadoFiltroDe(V2, "SIMIT_PROPIETARIO")).toBe("ERROR");
    expect(estadoFiltroDe(V1, "IMPUESTO_DEPARTAMENTAL")).toBe("NOVEDAD");
    expect(estadoFiltroDe(V2, "RUNT_LOCATARIO")).toBe("SIN_EJECUTAR");
    expect(estadoFiltroDe(V3, "RUNT")).toBe("SIN_EJECUTAR");
    expect(estadoFiltroDe(impuestoConDeuda, "RUNT")).toBe("SIN_EJECUTAR");
  });

  it("A6.8: fuente + estado combinados (SIMIT propietario = Error) dejan solo esas filas", () => {
    const r = aplicarFiltrosGrilla(filas, { ...FILTROS_GRILLA_VACIOS, fuente: "SIMIT_PROPIETARIO", estado: "ERROR" });
    expect(r.map((v) => v.id)).toEqual([V2.id, viable.id]);
  });

  it("impuesto = Con deuda deja solo la fila con deuda confirmada", () => {
    const r = aplicarFiltrosGrilla(filas, { ...FILTROS_GRILLA_VACIOS, fuente: "IMPUESTO_DEPARTAMENTAL", estado: "CON_DEUDA" });
    expect(r.map((v) => v.id)).toEqual([impuestoConDeuda.id]);
  });

  it("solo la fuente, sin estado, no filtra", () => {
    expect(aplicarFiltrosGrilla(filas, { ...FILTROS_GRILLA_VACIOS, fuente: "RUNT" })).toEqual(filas);
  });

  it("opciones de estado: solo las presentes en la columna, con su conteo, en el orden del semáforo", () => {
    expect(opcionesEstado(filas, "SIMIT_PROPIETARIO")).toEqual([
      { estado: "EJECUTADO", etiqueta: "Ejecutado", cantidad: 2 },
      { estado: "ERROR", etiqueta: "Error", cantidad: 2 },
      { estado: "SIN_EJECUTAR", etiqueta: "Sin ejecutar", cantidad: 1 },
    ]);
  });

  it("opciones de fuente: solo las pedidas por el lote, en el orden de la tabla", () => {
    expect(fuentesFiltrables(grilla().columnas)).toEqual(["RUNT", "RUNT_LOCATARIO", "SIMIT_PROPIETARIO", "IMPUESTO_DEPARTAMENTAL"]);
  });

  it("dictamen: Viable, Bloqueado y Pendiente (sin veredicto)", () => {
    const f = (dictamen: "VIABLE" | "BLOQUEADO" | "PENDIENTE") => aplicarFiltrosGrilla(filas, { ...FILTROS_GRILLA_VACIOS, dictamen }).map((v) => v.id);
    expect(f("VIABLE")).toEqual([viable.id]);
    expect(f("BLOQUEADO")).toEqual([impuestoConDeuda.id]);
    expect(f("PENDIENTE")).toEqual([V1.id, V2.id, V3.id]);
  });

  it("cambio de propietario: solo las filas con true (false y null no cuentan)", () => {
    expect(aplicarFiltrosGrilla(filas, { ...FILTROS_GRILLA_VACIOS, cambioPropietario: true }).map((v) => v.id)).toEqual([impuestoConDeuda.id]);
    expect(contarCambioPropietario(filas)).toBe(1);
    expect(hayDatoCambioPropietario(filas)).toBe(true);
    expect(hayDatoCambioPropietario([V1, V2])).toBe(false);
    expect(hayVeredictos(filas)).toBe(true);
    expect(hayVeredictos([V1, V2])).toBe(false);
  });

  it("todos los filtros se combinan (Y) con la búsqueda por placa o VIN", () => {
    const r = aplicarFiltrosGrilla(filas, { ...FILTROS_GRILLA_VACIOS, busqueda: "tst 005", fuente: "SIMIT_PROPIETARIO", estado: "ERROR", dictamen: "VIABLE" });
    expect(r.map((v) => v.id)).toEqual([viable.id]);
    expect(aplicarFiltrosGrilla(filas, { ...FILTROS_GRILLA_VACIOS, busqueda: "TST002", dictamen: "VIABLE" })).toEqual([]);
  });

  it("SP-21: el estado de los filtros es serializable y solo lleva códigos (sin documentos ni nombres)", () => {
    const valor = { ...FILTROS_GRILLA_VACIOS, fuente: "SIMIT_PROPIETARIO" as const, estado: "ERROR" as const, dictamen: "VIABLE" as const, cambioPropietario: true };
    expect(Object.keys(valor).sort()).toEqual(["busqueda", "cambioPropietario", "dictamen", "estado", "fuente"]);
    expect(JSON.stringify(valor)).not.toMatch(/\d{5,}|\*{2,}/);
  });
});
