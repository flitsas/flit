"use client";

import { useCallback } from "react";
import { BotonDescargaMasivaZip } from "@/components/operacion/DescargaMasivaConfirmModal";
import { useMostrarLoteDescarga } from "@/components/shared/LoteDescargaTracker";
import { crearLoteConsolidadosOt, type OtApiScope } from "@/lib/api/admin-ot";
import type { ModeloSeleccionLote } from "@/hooks/useSeleccionLote";
import type { LoteConsolidados } from "@/lib/api/types-consolidado-lotes";
import type { OtClientProceduresParams } from "@/lib/api/types-ot";

/**
 * HU #13394 (épica #13216, Feature #13308) — «Descargar ZIP» de la barra de selección de la bandeja
 * del OT. Reutiliza el botón y la confirmación de #13381 con el texto del maestro (CF-11) y la ruta
 * de la bandeja (`crearLoteConsolidadosOt`, con el mismo `scope` que el resto de la sección: el
 * SuperAdmin manda `?transitOfficeId`). El lote creado y el del 409 pasan al aviso global (#13382).
 * El permiso (AC6) lo resuelve la sección: sin él no hay barra ni este botón.
 */
export function BotonDescargaMaestrosOt({
  seleccion,
  contador,
  scope,
  onCreado,
}: {
  seleccion: ModeloSeleccionLote<OtClientProceduresParams>;
  contador: number;
  scope?: OtApiScope;
  /** El lote se creó: la sección limpia la selección (AC2). */
  onCreado: () => void;
}) {
  const { mostrarLote } = useMostrarLoteDescarga();
  const crear = useCallback(
    (sel: ModeloSeleccionLote<OtClientProceduresParams>) =>
      crearLoteConsolidadosOt(
        { tipoDocumento: "consolidado_maestro", confirmaEfectos: true, seleccion: sel },
        undefined,
        scope,
      ),
    [scope],
  );
  const alCrear = useCallback(
    (lote: LoteConsolidados) => {
      onCreado();
      mostrarLote(lote);
    },
    [onCreado, mostrarLote],
  );

  return (
    <BotonDescargaMasivaZip
      variante="maestro"
      seleccion={seleccion}
      contador={contador}
      crear={crear}
      onCreado={alCrear}
      onLoteActivo={mostrarLote}
    />
  );
}
