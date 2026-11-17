import { describe, expect, it } from "vitest";
import { FILTROS_TABLERO, filtrarVehiculos, tarjetasVisibles, buscarVehiculos, veredictoDeFila } from "@/lib/tablero/filtrosTablero";
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
