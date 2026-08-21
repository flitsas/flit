import type { FurPrendaKind } from "@/lib/api/admin-plataforma-fur";

/** Rótulos oficiales del numeral 3 (Anexo 46). */
export const FUR_CASILLA_LABEL: Record<number, string> = {
  1: "Matrícula / Registro",
  2: "Traspaso",
  3: "Traslado matrícula / registro",
  4: "Radicado matrícula / registro",
  5: "Cambio de color",
  7: "Regrabar motor",
  8: "Regrabar chasis",
  10: "Duplicado licencia tránsito",
  11: "Inscrip. prenda",
  12: "Levanta prenda",
  13: "Cancelación matrícula / registro",
  15: "Duplicado de placas",
  16: "Rematrícula",
  17: "Cambio de carrocería",
  18: "Otros",
};

export interface FurGuideInput {
  code: string;
  family: string;
  prenda: FurPrendaKind;
  color: boolean;
  carroceria: boolean;
  combustible: boolean;
  blindaje: boolean;
}

export interface FurGuideResult {
  casillas: { n: number; label: string }[];
  observaciones: string[];
  notas: string[];
}

function norm(s: string): string {
  return s.trim().toUpperCase();
}

function baseBoxes(code: string, family: string): number[] {
  if (code === "CANCELACION_MATRICULA") return [13];
  if (code === "REMATRICULA") return [16];
  if (code === "MATRICULA_NUEVA" || code === "MATRICULA_LEASING" || code === "MATRICULA_INICIAL") return [1];
  if (code.includes("TRASPASO")) return [2];
  if (code === "TRASLADO_CUENTA") return [3];
  if (code === "RADICADO_CUENTA") return [4];
  if (code === "CAMBIO_COLOR") return [5];
  if (code === "REGRABAR_MOTOR_CHASIS") return [7, 8];
  if (code === "DUPLICADO_TARJETA") return [10];
  if (code === "PRENDA_INSCRIPCION") return [11];
  if (code === "LEVANTAMIENTO_PRENDA") return [12];
  if (code === "LEVANTAR_INSCRIBIR_PRENDA") return [11, 12];
  if (code === "DUPLICADO_PLACA") return [15];
  if (code === "CAMBIO_CARROCERIA") return [17];
  if (code === "CONVERSION_COMBUSTIBLE" || code === "CAMBIO_LOCATARIO" || code === "CAMBIO_ACREEDOR") return [18];
  if (code === "BLINDAJE") return [];
  if (family.includes("TRASPASO") || family === "TRASPASO") return [2];
  if (code.includes("MATRICULA") || family.includes("MATRICULA") || family === "MATRICULAS") return [1];
  return [];
}

function isPrendaBase(code: string): boolean {
  return (
    code === "PRENDA_INSCRIPCION" ||
    code === "LEVANTAMIENTO_PRENDA" ||
    code === "LEVANTAR_INSCRIBIR_PRENDA"
  );
}

/**
 * Guía del simulador: casillas del numeral 3 y bloques del párrafo 23 (observaciones).
 * Espejo de FurNumeral3Marks + compositores de observaciones.
 */
export function buildFurGuide(input: FurGuideInput): FurGuideResult {
  const code = norm(input.code);
  const family = norm(input.family);
  const marks = new Set(baseBoxes(code, family));

  if (!isPrendaBase(code)) {
    if (input.prenda === "inscripcion" || input.prenda === "ambas") marks.add(11);
    if (input.prenda === "levantamiento" || input.prenda === "ambas") marks.add(12);
  }
  if (code !== "CAMBIO_COLOR" && input.color) marks.add(5);
  if (code !== "CAMBIO_CARROCERIA" && input.carroceria) marks.add(17);
  if (code !== "CONVERSION_COMBUSTIBLE" && input.combustible) marks.add(18);

  const casillas = [...marks]
    .sort((a, b) => a - b)
    .map((n) => ({ n, label: FUR_CASILLA_LABEL[n] ?? `Casilla ${n}` }));

  const observaciones: string[] = [];
  if (code === "MATRICULA_LEASING") {
    observaciones.push(
      "Matrícula con locatario por Leasing de {PROPIETARIO} a LOCATARIO TIPO DE DOCUMENTO {TIPO}, NÚMERO DE DOCUMENTO {NUMERO}",
    );
  }
  if (code === "TRASPASO_UNILATERAL") {
    observaciones.push(
      "Traspaso unilateral por leasing a {NOMBRE LOCATARIO}., tipo de documento {TIPO}, número de documento {NUMERO}.",
    );
  }

  const prendaInscribe =
    input.prenda === "inscripcion" ||
    input.prenda === "ambas" ||
    code === "PRENDA_INSCRIPCION" ||
    code === "LEVANTAR_INSCRIBIR_PRENDA";
  const prendaLevanta =
    input.prenda === "levantamiento" ||
    input.prenda === "ambas" ||
    code === "LEVANTAMIENTO_PRENDA" ||
    code === "LEVANTAR_INSCRIBIR_PRENDA";
  if (prendaLevanta) observaciones.push("Levantamiento de prenda a favor de {NOMBRE ACREEDOR}");
  if (prendaInscribe) observaciones.push("Inscripción de prenda a favor de {NOMBRE ACREEDOR}");

  const color = input.color || code === "CAMBIO_COLOR";
  const carroceria = input.carroceria || code === "CAMBIO_CARROCERIA";
  const combustible = input.combustible || code === "CONVERSION_COMBUSTIBLE";
  if (color) observaciones.push("Color nuevo(NUEVO COLOR: {COLOR_NUEVO})");
  if (carroceria) observaciones.push("Carroceria nueva(NUEVA CARROCERIA: {CARROCERIA_NUEVA})");
  if (combustible) observaciones.push("COMBUSTIBLE_NUEVO: {COMBUSTIBLE_NUEVO}");

  if (code === "CAMBIO_LOCATARIO") observaciones.push("CAMBIO DE LOCATARIO: {NOMBRE} - {DOC}.");
  if (code === "CAMBIO_ACREEDOR") observaciones.push("CAMBIO DE ACREEDOR PRENDARIO: {NOMBRE} - NIT {DOC}.");
  if (code === "REGRABAR_MOTOR_CHASIS") {
    observaciones.push("Regrabación de motor: {MOTOR}. Regrabación de chasis: {CHASIS}.");
  }

  const notas: string[] = [];
  if (input.blindaje || code.includes("BLINDAJE")) {
    notas.push("Vehículo blindado: SI (características, no casilla del numeral 3).");
  }
  if (code === "TRASPASO_UNILATERAL") {
    notas.push("El locatario / comprador no firma (art. 5.3.2.2).");
  }
  if (observaciones.length > 1) {
    notas.push("Los textos del párrafo 23 se concatenan; no se reemplazan entre sí.");
  }

  return { casillas, observaciones, notas };
}
