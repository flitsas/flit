// Tipos del contrato de la captura manual pública (Épica #13202, contrato de API §2).
// Si el contrato cambia, se reporta al architecture-agent; aquí solo se refleja.

export interface ManualCaptureView {
  fullName: string;
  documentType: string;
  documentNumber: string;
  /** El backend manda null en la prevalidación standalone (sin trámite). */
  productName: string | null;
  expiresAt: string;
  consentTextVersion: string;
}

export interface ConsentBody {
  accepted: true;
  textVersion: string;
}

export interface ManualSubmitFiles {
  rostro: Blob;
  anverso: Blob;
  reverso: Blob;
  /** PNG */
  firma: Blob;
}

/** Cliente de la captura manual: lo implementan el adaptador simulado y el real. */
export interface ManualCaptureClient {
  getManualCapture(token: string): Promise<ManualCaptureView>;
  postConsent(token: string, body: ConsentBody): Promise<void>;
  submit(token: string, files: ManualSubmitFiles): Promise<{ status: string }>;
}

/** Motivo por el que el enlace ya no sirve (estados terminales). */
export type LinkTerminalKind = "expirada" | "reemplazado" | "estado_invalido" | "not_found";

export class ManualCaptureError extends Error {
  readonly status: number;
  readonly code: string;
  constructor(status: number, code: string, message: string) {
    super(message);
    this.name = "ManualCaptureError";
    this.status = status;
    this.code = code;
  }
}

/** Traduce un error del cliente al estado terminal del enlace; null si es un fallo transitorio. */
export function terminalKindOf(error: unknown): LinkTerminalKind | null {
  if (!(error instanceof ManualCaptureError)) return null;
  if (error.status === 404) return "not_found";
  if (error.status === 410) return error.code === "reemplazado" ? "reemplazado" : "expirada";
  // 409 se discrimina por el code del cuerpo: «estado_invalido» es terminal; «consentimiento_requerido» es
  // recuperable (el submit vuelve al paso Datos) y cualquier otro 409 desconocido no cierra el enlace.
  if (error.status === 409) return error.code === "estado_invalido" ? "estado_invalido" : null;
  return null;
}
