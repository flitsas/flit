// Lo digitado en cada modo de la pantalla única (HU #14085). Vive en la pantalla para que cambiar de modo lo conserve;
// el documento del locatario y el archivo solo están en memoria (no se guardan en el navegador ni en la URL).

export type ModoCreacion = "express" | "masivo";

export interface BorradorExpress {
  /** Placa o VIN normalizado mientras se escribe (mayúsculas, solo letras y números, sin recortar). */
  placa: string;
  tipoDocumento: string;
  documento: string;
  /** Nombre escrito por el usuario; null mientras siga el autogenerado con la placa. */
  nombreManual: string | null;
}

export interface BorradorMasivo {
  nombre: string;
  archivo: File | null;
}
