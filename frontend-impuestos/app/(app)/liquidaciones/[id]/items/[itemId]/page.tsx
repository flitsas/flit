"use client";

import { useParams } from "next/navigation";
import { useSesion } from "@/components/ImpuestosShell";
import { DetalleItem } from "@/components/liquidaciones/DetalleItem";
import { PERMISOS, tienePermiso } from "@/lib/sesion";

/** /liquidaciones/[id]/items/[itemId] (plan §6.1, spec-visual §3.6): detalle de la placa, montos, recibos y relanzar. */
export default function PaginaItem() {
  const { id, itemId } = useParams<{ id: string; itemId: string }>();
  const { sesion } = useSesion();
  return (
    <DetalleItem
      liquidacionId={id}
      itemId={itemId}
      puedeRelanzar={tienePermiso(sesion, PERMISOS.liquidacionesCrear)}
      puedeDescargar={tienePermiso(sesion, PERMISOS.recibosDescargar)}
      esSuperAdmin={sesion?.esSuperAdmin === true}
    />
  );
}
