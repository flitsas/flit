import type { Operacion } from "@/lib/reportes";

/** `GET /reportes/gestion` (Reportes 2.0, S3). Sin periodo, `operacion` es exactamente la de `/comparendos/operacion`. */
export interface RespuestaGestion {
  alcance: { empresa: string | null; nit: string | null; desde: string | null; hasta: string | null };
  gestion: {
    conPeriodo: boolean;
    operacion: Operacion;
    topOrganismos: { organismoId: string | null; nombre: string; cantidad: number; valor: number }[];
  };
}
