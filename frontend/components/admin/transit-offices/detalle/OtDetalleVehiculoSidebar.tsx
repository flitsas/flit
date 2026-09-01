"use client";

import type { OtClientProcedure } from "@/lib/api/types-ot";
import {
  DETALLE_CARD,
  DETALLE_META,
  DETALLE_NAVY,
} from "@/components/operacion/detalle/detalle-visual";
import { formatOtDate } from "../ot-utils";

/**
 * Columna de identificación del vehículo del modal de detalle del OT — equivalente a la del detalle
 * del gestor, pero alimentada por `OtClientProcedure` en vez de `InstanceSummary`: el OT lee por su
 * propia puerta (`/admin/ot/client-procedures`) y nunca ve el contrato del runtime de trámites.
 *
 * Placa, marca/línea y VIN viven AQUÍ y por eso no se repiten en las especificaciones técnicas.
 */
export function OtDetalleVehiculoSidebar({ procedure }: { procedure: OtClientProcedure }) {
  const modelo = [procedure.marca, procedure.linea].filter(Boolean).join(" ") || "—";

  return (
    <aside className={`${DETALLE_CARD} h-full`} aria-label="Datos del vehículo">
      <div
        className="aspect-[4/3] w-full rounded-xl bg-gradient-to-br from-[#DFE5ED]/60 to-[#DFE5ED]/20 dark:from-white/10 dark:to-white/5"
        role="img"
        aria-label={`Vehículo con placa ${procedure.placa ?? "sin placa"}`}
      />
      <div className="mt-3 text-center">
        <p className="text-sm font-semibold" style={{ color: DETALLE_NAVY }}>
          <span className="dark:text-white">
            Placa: <span className="font-bold uppercase tracking-wider">{procedure.placa || "—"}</span>
            <span className="mx-2 opacity-40">|</span>
            Modelo: <span className="font-bold">{modelo}</span>
          </span>
        </p>
        <p className="mt-1 font-mono text-[11px] opacity-70">VIN: {procedure.vin ?? "—"}</p>
      </div>
      <div
        className="mt-3 border-t pt-3 text-[11px] border-[#DFE5ED] dark:border-white/5"
        style={{ color: DETALLE_META }}
      >
        <p>Radicado: {formatOtDate(procedure.createdAt)}</p>
        <p>Entregado: {procedure.submittedAt ? formatOtDate(procedure.submittedAt) : "—"}</p>
        <p>Gestor: {procedure.gestorNombre ?? "—"}</p>
      </div>
    </aside>
  );
}
