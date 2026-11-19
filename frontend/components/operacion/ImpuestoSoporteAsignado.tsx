'use client';

import { tramitesClient } from '@/lib/api/tramites-client';
import {
  cargarSoporteImpuestoAsignado,
  type ResultadoSoporteImpuesto,
} from '@/lib/tramites/soporte-impuesto-asignado';
import { SoporteAsignadoUpload } from './SoporteAsignadoUpload';

/**
 * Carga del PDF del impuesto departamental con el trámite en `asignado`, dentro del modal «Enviar al
 * OT», igual que la del SOAT ({@link SoporteAsignadoUpload}). El flujo vive en
 * `cargarSoporteImpuestoAsignado`.
 */
export function ImpuestoSoporteAsignado({
  instanceId,
  tenantId,
  abierto,
  onToggle,
  disabled = false,
  onResultado,
}: {
  instanceId: string;
  tenantId?: string;
  abierto: boolean;
  onToggle: (next: boolean) => void;
  disabled?: boolean;
  onResultado?: (r: ResultadoSoporteImpuesto) => void;
}) {
  return (
    <SoporteAsignadoUpload
      etiqueta="Cargar PDF del impuesto departamental"
      ayuda="Carga el comprobante de liquidación y pago del impuesto departamental para que quede en el trámite."
      textoSeleccionar="Selecciona el PDF del impuesto departamental"
      textoCargando="Cargando el PDF del impuesto departamental…"
      textoProcesando="Procesando el PDF del impuesto departamental…"
      abierto={abierto}
      onToggle={onToggle}
      disabled={disabled}
      onCargar={async (file) => {
        const r = await cargarSoporteImpuestoAsignado(tramitesClient, instanceId, file, tenantId);
        onResultado?.(r);
        const ok = r.estado === 'registrado';
        return {
          ok,
          titulo: ok ? 'Impuesto registrado' : 'Impuesto pendiente',
          mensaje: r.mensaje,
        };
      }}
    />
  );
}
