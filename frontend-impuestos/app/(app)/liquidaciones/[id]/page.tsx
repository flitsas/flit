"use client";

import { useParams } from "next/navigation";
import { useSesion } from "@/components/ImpuestosShell";
import { DetalleLote } from "@/components/liquidaciones/DetalleLote";
import { PERMISOS, tienePermiso } from "@/lib/sesion";

/** /liquidaciones/[id] (plan §6.1, spec-visual §3.5): avance del lote, cierre manual (Admin o SA) y relanzar. */
export default function PaginaLote() {
  const { id } = useParams<{ id: string }>();
  const { sesion } = useSesion();
  return (
    <DetalleLote
      liquidacionId={id}
      puedeCerrar={tienePermiso(sesion, PERMISOS.liquidacionesCerrar)}
      puedeRelanzar={tienePermiso(sesion, PERMISOS.liquidacionesCrear)}
      esSuperAdmin={sesion?.esSuperAdmin === true}
    />
  );
}
