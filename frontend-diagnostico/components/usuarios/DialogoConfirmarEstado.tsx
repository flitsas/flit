"use client";

import { useState } from "react";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Modal } from "@flit/ui/Modal";
import { ApiError } from "@/lib/api";
import { reactivarUsuario, suspenderUsuario, traducirErrorUsuarios } from "@/lib/usuarios/api";
import type { UsuarioDiagnostico } from "@/lib/usuarios/tipos";

export type AccionEstado = "SUSPENDER" | "REACTIVAR";

export interface DialogoConfirmarEstadoProps {
  usuario: UsuarioDiagnostico;
  accion: AccionEstado;
  empresa: string | null;
  onCerrar: () => void;
  /** Fila devuelta por la API (o null si hay que releer la lista, p. ej. 409 ROL_YA_VIGENTE). */
  onHecho: (fila: UsuarioDiagnostico | null, aviso: string) => void;
}

/**
 * Confirmación de «Suspender» y «Reactivar» (spec-ui §10.3). Suspender solo quita el acceso a Diagnóstico (SP-12=a): la
 * cuenta y los demás productos no cambian. El foco inicial va a «Cancelar» y la acción destructiva usa el botón de alerta.
 * Un error de la API queda dentro del diálogo, que no se cierra.
 */
export function DialogoConfirmarEstado({ usuario, accion, empresa, onCerrar, onHecho }: DialogoConfirmarEstadoProps) {
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const suspender = accion === "SUSPENDER";

  async function confirmar() {
    if (enviando) return;
    setEnviando(true);
    setError(null);
    try {
      const fila = suspender ? await suspenderUsuario(empresa, usuario.id) : await reactivarUsuario(empresa, usuario.id);
      onHecho(fila, suspender ? `${usuario.nombre} quedó suspendido en Diagnóstico.` : `${usuario.nombre} volvió a tener acceso a Diagnóstico.`);
    } catch (e) {
      // 409 ROL_YA_VIGENTE (contrato §3.3): el hub ya le dio otro rol; la marca se borró. Se relee la lista.
      if (!suspender && e instanceof ApiError && e.code === "ROL_YA_VIGENTE") {
        onHecho(null, traducirErrorUsuarios(e, ""));
        return;
      }
      setError(traducirErrorUsuarios(e, suspender ? "No se pudo suspender al usuario. Intenta de nuevo." : "No se pudo reactivar al usuario. Intenta de nuevo."));
      setEnviando(false);
    }
  }

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={enviando}
      title={suspender ? "Suspender usuario" : "Reactivar usuario"}
      icono={suspender ? "alerta" : "check"}
      size="sm"
      footer={
        <div className="flex flex-wrap justify-end gap-3">
          <Button variant="secondary" onClick={onCerrar} disabled={enviando} data-autofocus>
            Cancelar
          </Button>
          <Button variant={suspender ? "alerta" : "primary"} onClick={() => void confirmar()} cargando={enviando}>
            {suspender ? "Suspender" : "Reactivar"}
          </Button>
        </div>
      }
    >
      <p className="text-sm text-flit-primary">
        {suspender
          ? `${usuario.nombre} no podrá entrar a Diagnóstico hasta que lo reactives. Sus diagnósticos se conservan.`
          : `${usuario.nombre} podrá volver a entrar a Diagnóstico con su rol de antes.`}
      </p>
      {error && (
        <div className="mt-4">
          <InlineAlert tone="error" title={suspender ? "No se pudo suspender" : "No se pudo reactivar"}>
            {error}
          </InlineAlert>
        </div>
      )}
    </Modal>
  );
}
