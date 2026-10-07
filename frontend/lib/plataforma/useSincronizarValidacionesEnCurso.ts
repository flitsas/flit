"use client";

import { useEffect, useRef } from "react";
import type { MandateSigner, MandateSignerIdentityReconcile } from "@/lib/api/admin-mandate-signers";
import { puedeConsultarEstado, sincronizarValidacionesEnCurso } from "./mandatario-validacion";

/** Cada cuánto se vuelve a consultar mientras la lista está abierta y hay validaciones en curso. */
export const INTERVALO_CONSULTA_MS = 30_000;

/**
 * Consulta automática del estado de las validaciones propias en curso mientras la lista de mandatarios está abierta,
 * como la pantalla de espera del trámite: el mandatario puede aprobar (p. ej. en el segundo intento) sin que el aviso
 * del proveedor llegue. Solo corre con la pestaña visible y si hay alguna en curso; si alguna cambió, avisa para
 * recargar la lista. La primera consulta la hace la carga de la lista; este hook se encarga de las siguientes.
 */
export function useSincronizarValidacionesEnCurso(
  signers: readonly MandateSigner[],
  consultar: (signer: MandateSigner) => Promise<MandateSignerIdentityReconcile>,
  onCambio: () => void,
  enabled = true,
): void {
  const signersRef = useRef(signers);
  const consultarRef = useRef(consultar);
  const onCambioRef = useRef(onCambio);

  useEffect(() => {
    signersRef.current = signers;
    consultarRef.current = consultar;
    onCambioRef.current = onCambio;
  });

  const hayEnCurso = enabled && signers.some(puedeConsultarEstado);

  useEffect(() => {
    if (!hayEnCurso) return;
    let enVuelo = false;
    const id = window.setInterval(() => {
      if (enVuelo || document.visibilityState !== "visible") return;
      enVuelo = true;
      void sincronizarValidacionesEnCurso(signersRef.current, consultarRef.current)
        .then((cambio) => {
          if (cambio) onCambioRef.current();
        })
        .finally(() => {
          enVuelo = false;
        });
    }, INTERVALO_CONSULTA_MS);
    return () => window.clearInterval(id);
  }, [hayEnCurso]);
}
