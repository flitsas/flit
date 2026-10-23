import { describe, expect, it } from "vitest";
import { armarAsignacion, erroresAsignacion, type FormConductor, type PruebaContratos, resumenPrueba } from "@/lib/conductor";

const F: FormConductor = {
  tipoDocumento: "CC",
  numeroDocumento: " 1020304050 ",
  nombres: "Juan Pérez",
  correo: "",
  telefono: "",
  direccion: "",
  ciudad: "",
  sucursal: "",
  tipoContrato: "",
  motivo: "",
  declaracion: true,
};

describe("asignación del conductor", () => {
  it("exige la declaración de tratamiento de datos", () => {
    expect(erroresAsignacion({ ...F, declaracion: false }, false)).toContain("Marca la declaración de tratamiento de datos.");
  });

  it("exige motivo solo al reemplazar", () => {
    expect(erroresAsignacion(F, false)).toEqual([]);
    expect(erroresAsignacion(F, true)).toEqual(["Escribe el motivo del cambio."]);
  });

  it("arma el cuerpo con vacíos como null", () => {
    expect(armarAsignacion(F)).toMatchObject({ numeroDocumento: "1020304050", correo: null, motivo: null, declaracionTratamientoDatos: true });
  });
});

describe("probar la conexión con la API de contratos (HU #13843)", () => {
  const base: PruebaContratos = { conectado: true, motivo: null, httpStatus: null, duracionMs: 1234, contactoDesviado: true, correoDesvio: "buzon@flit.test", simulada: false };

  it("conectado: dice cuánto tardó, a dónde se desvía el contacto y que no consultó placas", () => {
    const r = resumenPrueba(base);
    expect(r.tono).toBe("success");
    expect(r.titulo).toBe("Conectado: Renting respondió el inicio de sesión en 1.234 ms");
    expect(r.detalle[0]).toContain("buzon@flit.test");
    expect(r.detalle).toContain("La prueba no consulta ninguna placa.");
    expect(resumenPrueba({ ...base, contactoDesviado: false, correoDesvio: null }).detalle[0]).toMatch(/Contacto real encendido/);
  });

  it("no conectado: el motivo y el código; simulada como advertencia", () => {
    const r = resumenPrueba({ ...base, conectado: false, motivo: "Renting rechazó el login (401).", httpStatus: 401 });
    expect(r).toEqual({ titulo: "No conectado", tono: "error", detalle: ["Renting rechazó el login (401).", "Código de respuesta: 401."] });
    expect(resumenPrueba({ ...base, conectado: false, motivo: "Falta configurar…", simulada: true }).tono).toBe("warning");
  });
});
