import { normalizarPlaca, normalizarVin, placaValida, vinValido } from "@/lib/entradas";

/** Cómo identifica el vehículo cada tipo de trámite, según `flags.entryMode` de su definición (PLATE, VIN o BOTH). */
export type ModoVehiculo = "PLATE" | "VIN" | "BOTH";

/** Mientras la definición no informe el modo (cargando, con error o con un valor desconocido) el campo es la placa. */
export function modoVehiculo(entryMode: string | undefined): ModoVehiculo {
  const modo = entryMode?.toUpperCase();
  return modo === "VIN" || modo === "BOTH" ? modo : "PLATE";
}

export const ETIQUETA_VEHICULO: Record<ModoVehiculo, string> = {
  PLATE: "Placa del vehículo",
  VIN: "VIN del vehículo",
  BOTH: "Placa o VIN del vehículo",
};

export const MARCADOR_VEHICULO: Record<ModoVehiculo, string> = {
  PLATE: "Ej. ABC123",
  VIN: "Número de VIN",
  BOTH: "Placa o número de VIN",
};

export function normalizarVehiculo(modo: ModoVehiculo, valor: string): string {
  return modo === "PLATE" ? normalizarPlaca(valor) : normalizarVin(valor);
}

export function vehiculoValido(modo: ModoVehiculo, valor: string): boolean {
  if (modo === "PLATE") return placaValida(valor);
  if (modo === "VIN") return vinValido(valor);
  return placaValida(valor) || vinValido(valor);
}

/** Lo que se manda al BFF: placa o VIN. Con ambos modos, 5 o 6 caracteres son una placa y de 11 en adelante un VIN. */
export function datosVehiculo(modo: ModoVehiculo, valor: string): { placa?: string; vin?: string } {
  if (modo === "PLATE") return { placa: valor };
  if (modo === "VIN") return { vin: valor };
  return placaValida(valor) ? { placa: valor } : { vin: valor };
}
