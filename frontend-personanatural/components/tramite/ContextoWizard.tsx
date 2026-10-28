"use client";

import { createContext, useContext, type ComponentType } from "react";
import type { ClientePortal, DefinicionTipo, Solicitud } from "@/lib/api/cliente";
import type { SeccionWizard } from "@/lib/tramite/pasos";
import type { ContextoTramite } from "@/lib/tramite/contexto";

/** Lo que una sección le informa al cascarón para decidir si se puede avanzar. */
export interface EstadoSeccion {
  /** Todo lo obligatorio de la sección está completo según la definición. */
  completo: boolean;
  /** Motivo por el que «Continuar» queda deshabilitado (un bloqueo duro, p. ej. el diagnóstico con pendientes). */
  bloqueo?: string;
}

export interface PropsSeccion {
  seccion: SeccionWizard;
  /** El paso está a la vista: las secciones de pasos ocultos no consultan ni piden captcha. */
  activa: boolean;
  /** Sube cada vez que el ciudadano intenta avanzar con el paso incompleto: la sección marca sus campos inválidos. */
  intentoAvance: number;
  onEstado: (estado: EstadoSeccion) => void;
}

/** Registro de renderers: `section_type` → componente. Las llaves son el catálogo que el portal sabe pintar. */
export type RegistroRenderers = Readonly<Record<string, ComponentType<PropsSeccion>>>;

export interface ApiWizard {
  cliente: ClientePortal;
  definicion: DefinicionTipo;
  solicitud: Solicitud;
  /** Contexto en memoria que dejó la landing (cédula, placa…): nunca en la URL ni en el almacenamiento. */
  contexto: ContextoTramite | null;
  /** El borrador cambió en el servidor (cambia su `version`): vuelve a leerlo. */
  recargar: () => Promise<unknown>;
  /**
   * Guarda con la `version` más reciente que se conoce y deja la respuesta como el borrador vigente. Un 409
   * `conflicto_version` lo atiende el cascarón (avisa y ofrece recargar); los demás errores le llegan a quien guarda.
   */
  guardarConVersion: (escribir: (version: number) => Promise<Solicitud>) => Promise<void>;
  /** Una sección registra cómo guardar lo suyo antes de pasar al siguiente paso (o `null` al desmontarse). */
  registrarGuardado: (clave: string, guardar: (() => Promise<void>) | null) => void;
  /** Cualquier error de una petición pasa por aquí para que el cascarón atienda la sesión vencida. */
  reportarError: (error: unknown) => void;
}

const Contexto = createContext<ApiWizard | null>(null);

export const ProveedorWizard = Contexto.Provider;

export function useWizard(): ApiWizard {
  const valor = useContext(Contexto);
  if (!valor) throw new Error("useWizard se usa dentro del wizard.");
  return valor;
}
