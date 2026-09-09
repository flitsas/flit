// HU #12209 (Feature #12201, CF-25) — reglas del prellenado: qué se hidrata, qué se bloquea y qué
// se señala como discrepancia sin sobrescribir.
// Uso de ejemplo:
//   aplicarPrellenado({ valoresActuales: { marca: "" }, campos: [{ key: "marca", value: "MAZDA" }],
//                       fuenteBloque: "RUNT" })
import { describe, expect, it } from "vitest";
import {
  CAMPOS_VEHICULO_SIEMPRE_EDITABLES,
  CAMPOS_VEHICULO_SIEMPRE_MANUALES,
  aplicarPrellenado,
} from "../prefill-hidratacion";
import type { PrefillField } from "@/lib/api/types-generacion-documental";

/** Las 12 variables de vehículo que el RUNT sí devuelve (la 13.ª es la licencia de tránsito). */
const CAMPOS_RUNT: PrefillField[] = [
  { key: "placa", value: "ABC123" },
  { key: "marca", value: "MAZDA" },
  { key: "linea", value: "CX-30" },
  { key: "modeloAnio", value: "2022" },
  { key: "claseVehiculo", value: "CAMIONETA" },
  { key: "tipoCarroceria", value: "WAGON" },
  { key: "color", value: "GRIS" },
  { key: "noMotor", value: "MTR-0001" },
  { key: "noChasis", value: "CHS-0001" },
  { key: "noSerie", value: "SRE-0001" },
  { key: "servicio", value: "PARTICULAR" },
  { key: "organismoTransito", value: "SECRETARÍA DE MOVILIDAD DE MEDELLÍN" },
];

const vacios = () =>
  Object.fromEntries(CAMPOS_RUNT.map((c) => [c.key, ""])) as Record<string, string>;

function hidratarVehiculo(valoresActuales: Record<string, string | undefined>) {
  return aplicarPrellenado({
    valoresActuales,
    campos: CAMPOS_RUNT,
    fuenteBloque: "RUNT",
    siempreEditables: CAMPOS_VEHICULO_SIEMPRE_EDITABLES,
    siempreManuales: CAMPOS_VEHICULO_SIEMPRE_MANUALES,
  });
}

describe("aplicarPrellenado — vehículo por placa", () => {
  /** Happy path (AC «la placa hidrata el bloque»): 12 de las 13 variables del anexo §5.1. */
  it("hidrata las 12 variables que devuelve el RUNT y declara la fuente de cada una", () => {
    const { parche, hidratados } = hidratarVehiculo({ ...vacios(), placa: "ABC123" });

    expect(Object.keys(hidratados)).toHaveLength(12);
    for (const campo of CAMPOS_RUNT) {
      expect(hidratados[campo.key].fuente).toBe("RUNT");
    }
    // La placa ya estaba capturada: se marca como dato de la fuente, no se reescribe.
    expect(parche.placa).toBeUndefined();
    expect(parche.marca).toBe("MAZDA");
  });

  /**
   * Decisión del PO (adenda §15.1), literal: `color` y `tipoCarroceria` editables sin ninguna
   * acción previa; los otros 10 hidratados, bloqueados.
   */
  it("deja color y tipoCarroceria editables y bloquea los otros 10", () => {
    const { hidratados } = hidratarVehiculo({ ...vacios(), placa: "ABC123" });

    expect(hidratados.color.bloqueado).toBe(false);
    expect(hidratados.tipoCarroceria.bloqueado).toBe(false);

    const bloqueados = Object.entries(hidratados).filter(([, v]) => v.bloqueado);
    expect(bloqueados).toHaveLength(10);
    expect(bloqueados.map(([k]) => k)).not.toContain("color");
    expect(bloqueados.map(([k]) => k)).not.toContain("tipoCarroceria");
  });

  /** El número de licencia de tránsito no lo devuelve ninguna consulta: está en el cartón físico. */
  it("nunca hidrata el número de licencia de tránsito, aunque la respuesta lo traiga", () => {
    const { parche, hidratados } = aplicarPrellenado({
      valoresActuales: { noLicenciaTransito: "" },
      campos: [{ key: "noLicenciaTransito", value: "LT-999" }],
      fuenteBloque: "RUNT",
      siempreManuales: CAMPOS_VEHICULO_SIEMPRE_MANUALES,
    });

    expect(parche.noLicenciaTransito).toBeUndefined();
    expect(hidratados.noLicenciaTransito).toBeUndefined();
  });

  /** Anti-pisado (AC «el prellenado no pisa lo ya escrito»): gana lo que escribió el usuario. */
  it("conserva el valor escrito por el usuario y señala la discrepancia", () => {
    const { parche, hidratados, discrepancias } = hidratarVehiculo({
      ...vacios(),
      placa: "ABC123",
      noMotor: "MTR-ESCRITO-A-MANO",
    });

    expect(parche.noMotor).toBeUndefined();
    expect(hidratados.noMotor).toBeUndefined();
    expect(discrepancias).toEqual([
      { campo: "noMotor", valorFuente: "MTR-0001", fuente: "RUNT" },
    ]);
  });

  /** Si lo escrito coincide (salvo mayúsculas/espacios) no hay discrepancia: es el mismo dato. */
  it("no reporta discrepancia cuando el valor capturado equivale al de la fuente", () => {
    const { discrepancias, hidratados } = hidratarVehiculo({
      ...vacios(),
      placa: "abc123",
      marca: "  mazda ",
    });

    expect(discrepancias).toHaveLength(0);
    expect(hidratados.marca.fuente).toBe("RUNT");
  });

  /** Carrera: el usuario vacía un campo mientras la consulta viaja. Su decisión manda. */
  it("respeta un campo que el usuario tocó y dejó vacío mientras la consulta estaba en vuelo", () => {
    const { parche } = aplicarPrellenado({
      valoresActuales: { marca: "" },
      tocados: new Set(["marca"]),
      campos: [{ key: "marca", value: "MAZDA" }],
      fuenteBloque: "RUNT",
    });

    expect(parche.marca).toBeUndefined();
  });
});

describe("aplicarPrellenado — precedencia por parte", () => {
  /**
   * El RUES NO devuelve al representante legal: certifica la facultad de representación, no la
   * persona. Si llegara en la respuesta, se ignora y el campo queda manual.
   */
  it("con fuente RUES no hidrata al representante legal ni su documento", () => {
    const { parche, hidratados } = aplicarPrellenado({
      valoresActuales: { nombreRazonSocial: "", domicilio: "", representanteLegal: "", ccRepresentanteLegal: "" },
      campos: [
        { key: "nombreRazonSocial", value: "LEASING DE PRUEBA S.A." },
        { key: "domicilio", value: "BOGOTÁ D.C." },
        { key: "representanteLegal", value: "NOMBRE QUE EL RUES NO CERTIFICA" },
        { key: "ccRepresentanteLegal", value: "0000" },
      ],
      fuenteBloque: "RUES",
    });

    expect(parche.nombreRazonSocial).toBe("LEASING DE PRUEBA S.A.");
    expect(parche.domicilio).toBe("BOGOTÁ D.C.");
    expect(parche.representanteLegal).toBeUndefined();
    expect(hidratados.representanteLegal).toBeUndefined();
    expect(hidratados.ccRepresentanteLegal).toBeUndefined();
  });

  /** El directorio de representantes legales SÍ los aporta: es la primera fuente de la cadena. */
  it("con fuente del directorio sí hidrata al representante legal", () => {
    const { parche, hidratados } = aplicarPrellenado({
      valoresActuales: { representanteLegal: "", ccRepresentanteLegal: "" },
      campos: [
        { key: "representanteLegal", value: "REPRESENTANTE DEL DIRECTORIO" },
        { key: "ccRepresentanteLegal", value: "1010101010" },
      ],
      fuenteBloque: "DIRECTORIO_RL",
    });

    expect(parche.representanteLegal).toBe("REPRESENTANTE DEL DIRECTORIO");
    expect(hidratados.ccRepresentanteLegal.fuente).toBe("DIRECTORIO_RL");
  });

  /** `contact-lookup` nunca devuelve nombre: si es la fuente efectiva, el nombre queda manual. */
  it("con fuente contact-lookup hidrata el domicilio pero no el nombre", () => {
    const { parche, hidratados } = aplicarPrellenado({
      valoresActuales: { nombreRazonSocial: "", domicilio: "" },
      campos: [
        { key: "nombreRazonSocial", value: "NOMBRE QUE EL CONTACTO NO DEVUELVE" },
        { key: "domicilio", value: "CALI" },
      ],
      fuenteBloque: "CONTACT_LOOKUP",
    });

    expect(parche.nombreRazonSocial).toBeUndefined();
    expect(hidratados.nombreRazonSocial).toBeUndefined();
    expect(parche.domicilio).toBe("CALI");
  });

  /** La fuente puede declararse POR CAMPO: en una misma respuesta conviven dos orígenes. */
  it("respeta la fuente declarada campo a campo por encima de la del bloque", () => {
    const { hidratados } = aplicarPrellenado({
      valoresActuales: { nombreRazonSocial: "", domicilio: "", representanteLegal: "" },
      campos: [
        { key: "nombreRazonSocial", value: "COMPAÑÍA DE PRUEBA S.A.S.", source: "DIRECTORIO_RL" },
        { key: "domicilio", value: "MEDELLÍN", source: "RUES" },
        { key: "representanteLegal", value: "IGNORADO", source: "RUES" },
      ],
      fuenteBloque: "DIRECTORIO_RL",
    });

    expect(hidratados.nombreRazonSocial.fuente).toBe("DIRECTORIO_RL");
    expect(hidratados.domicilio.fuente).toBe("RUES");
    // El RL viene marcado como RUES: se descarta aunque la fuente del bloque sí pudiera aportarlo.
    expect(hidratados.representanteLegal).toBeUndefined();
  });
});

describe("aplicarPrellenado — contrato", () => {
  /** Sin fuente declarada no se hidrata: la interfaz no podría decir de dónde salió el dato. */
  it("ignora campos sin fuente y campos con valor vacío o nulo", () => {
    const { parche, hidratados, discrepancias } = aplicarPrellenado({
      valoresActuales: { a: "", b: "", c: "" },
      campos: [
        { key: "a", value: "SIN FUENTE" },
        { key: "b", value: "", source: "RUNT" },
        { key: "c", value: null, source: "RUNT" },
      ],
      fuenteBloque: null,
    });

    expect(parche).toEqual({});
    expect(hidratados).toEqual({});
    expect(discrepancias).toEqual([]);
  });

  /** Forma del resultado: tres propiedades, siempre presentes. */
  it("devuelve siempre parche, hidratados y discrepancias", () => {
    const resultado = aplicarPrellenado({ valoresActuales: {}, campos: [] });

    expect(resultado).toHaveProperty("parche");
    expect(resultado).toHaveProperty("hidratados");
    expect(resultado).toHaveProperty("discrepancias");
    expect(Array.isArray(resultado.discrepancias)).toBe(true);
  });
});
