import { describe, expect, it } from "vitest";
import { agregarHora, cuerpoCanal, erroresCanal, formDeCanal, horaColombia, normalizarHora, proveedoresDelCanal, type CanalConsulta, type FormCanal, type ProveedorConsulta } from "@/lib/consultas";

const PROVEEDORES: ProveedorConsulta[] = [
  { clave: "verifik_simit", nombre: "Verifik SIMIT", canales: ["SimitNit", "SimitPlaca"], conectado: true, motivo: null },
  { clave: "smartcloud", nombre: "SmartCloud (SIMIT)", canales: ["SimitNit", "SimitPlaca"], conectado: false, motivo: "Falta el contrato." },
  { clave: "municipal", nombre: "API municipal", canales: ["MunicipalNit", "MunicipalPlaca"], conectado: false, motivo: "Falta el contrato." },
];

const BASE: FormCanal = { encendido: true, proveedor: "verifik_simit", modo: "HorasFijas", horas: ["07:00", "13:00"], intervaloMinutos: "60", desde: "06:00", hasta: "20:00", soloDiasHabiles: true };

describe("agenda de consultas", () => {
  it("normaliza y valida horas", () => {
    expect(normalizarHora("7:00")).toBe("07:00");
    expect(normalizarHora(" 13:05 ")).toBe("13:05");
    expect(normalizarHora("24:00")).toBeNull();
    expect(normalizarHora("7")).toBeNull();
  });

  it("agrega horas ordenadas y sin repetir", () => {
    expect(agregarHora(["13:00"], "7:00")).toEqual(["07:00", "13:00"]);
    expect(agregarHora(["07:00"], "07:00")).toEqual(["07:00"]);
    expect(agregarHora([], "25:00")).toBeNull();
  });

  it("valida con los mismos límites que la API", () => {
    expect(erroresCanal(BASE, PROVEEDORES, "SimitNit")).toEqual([]);
    expect(erroresCanal({ ...BASE, horas: [] }, PROVEEDORES, "SimitNit")).toContain("Agrega al menos una hora.");
    expect(erroresCanal({ ...BASE, proveedor: "smartcloud" }, PROVEEDORES, "SimitNit")[0]).toMatch(/no está conectado/);
    expect(erroresCanal({ ...BASE, proveedor: "smartcloud", encendido: false }, PROVEEDORES, "SimitNit")).toEqual([]);
    expect(erroresCanal(BASE, PROVEEDORES, "MunicipalNit")[0]).toMatch(/no atiende/);
    const intervalo: FormCanal = { ...BASE, modo: "Intervalo", intervaloMinutos: "10" };
    expect(erroresCanal(intervalo, PROVEEDORES, "SimitNit")[0]).toMatch(/30 minutos/);
    expect(erroresCanal({ ...intervalo, intervaloMinutos: "60", desde: "20:00", hasta: "06:00" }, PROVEEDORES, "SimitNit")[0]).toMatch(/posterior/);
  });

  it("arma el cuerpo del PUT según el modo", () => {
    expect(cuerpoCanal(BASE)).toEqual({ encendido: true, proveedor: "verifik_simit", modo: "HorasFijas", horas: ["07:00", "13:00"], intervaloMinutos: null, desde: null, hasta: null, soloDiasHabiles: true });
    expect(cuerpoCanal({ ...BASE, modo: "Intervalo", intervaloMinutos: "90", desde: "6:00" })).toEqual({
      encendido: true, proveedor: "verifik_simit", modo: "Intervalo", horas: [], intervaloMinutos: 90, desde: "06:00", hasta: "20:00", soloDiasHabiles: true,
    });
  });

  it("parte del canal guardado y filtra los proveedores por canal", () => {
    const canal: CanalConsulta = {
      canal: "SimitNit", etiqueta: "SIMIT por NIT", configurado: true, encendido: false, modo: "Intervalo", horas: [], intervaloMinutos: 120, desde: "08:00",
      hasta: "18:00", soloDiasHabiles: false, descripcion: null, ultimaConsulta: null, ultimoResultado: null, proximaConsulta: null, proveedor: "verifik_simit",
    };
    expect(formDeCanal(canal)).toMatchObject({ modo: "Intervalo", intervaloMinutos: "120", desde: "08:00", proveedor: "verifik_simit" });
    expect(proveedoresDelCanal(PROVEEDORES, "MunicipalPlaca").map((p) => p.clave)).toEqual(["municipal"]);
  });

  it("muestra la hora de Colombia sin convertir zonas", () => {
    expect(horaColombia("2026-10-13T13:00:00")).toBe("13 oct 2026, 13:00");
    expect(horaColombia(null)).toBe("—");
  });
});
