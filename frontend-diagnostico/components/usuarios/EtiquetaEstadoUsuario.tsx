import { Icono } from "@flit/ui/Icono";
import { ETIQUETA_ESTADO, ESTILO_ESTADO, type EstadoUsuario } from "@/lib/usuarios/tipos";
import { Etiqueta } from "@/components/tablero/vistasDetalle/comun";

/**
 * Estado de un usuario de Diagnóstico (spec-ui §10.1, T2): etiqueta del kit con icono y texto, nunca solo color. Activo
 * verde (`success`), Invitado azul (`info`), Suspendido naranja (`warning`). Sin `role="status"`: en una tabla serían
 * decenas de regiones vivas; el cambio se anuncia en la región de la pantalla.
 */
export function EtiquetaEstadoUsuario({ estado }: { estado: EstadoUsuario }) {
  const { tono, icono } = ESTILO_ESTADO[estado];
  return (
    <Etiqueta tono={tono}>
      <Icono nombre={icono} size={14} className="mr-1 shrink-0" />
      {ETIQUETA_ESTADO[estado]}
    </Etiqueta>
  );
}
