import { describe, expect, it } from "vitest";
import { armarAsignacion, erroresAsignacion, type FormConductor } from "@/lib/conductor";

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
