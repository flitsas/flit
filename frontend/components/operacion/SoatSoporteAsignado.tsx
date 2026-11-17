'use client';

import { tramitesClient } from '@/lib/api/tramites-client';
import {
  cargarSoporteSoatAsignado,
  type ResultadoSoporteSoat,
} from '@/lib/tramites/soporte-soat-asignado';
import { SoporteAsignadoUpload } from './SoporteAsignadoUpload';

/**
 * Bug #13194 (P3) — carga del PDF del SOAT con el trámite en `asignado`, dentro del modal «Enviar al
 * OT». Es la salida del gestor cuando el RUNT no reporta el SOAT y la compañía no deja continuar sin
 * él: subir el PDF, registrar su lectura y reintentar el envío. La caja es la compartida
 * ({@link SoporteAsignadoUpload}); el flujo vive en `cargarSoporteSoatAsignado`.
 */
export function SoatSoporteAsignado({
  instanceId,
  tenantId,
  abierto,
  onToggle,
  disabled = false,
  onResultado,
}: {
  instanceId: string;
  tenantId?: string;
  /** Desplegado (lo abre el modal al recibir 409 `soat_no_vigente`). */
  abierto: boolean;
  onToggle: (next: boolean) => void;
  disabled?: boolean;
  onResultado?: (r: ResultadoSoporteSoat) => void;
}) {
  return (
    <SoporteAsignadoUpload
      etiqueta="Cargar PDF del SOAT"
      ayuda="Si el RUNT no reporta el SOAT del vehículo, carga el PDF de la póliza vigente y vuelve a enviar el trámite."
      textoSeleccionar="Selecciona el PDF del SOAT"
      textoCargando="Cargando y leyendo el PDF del SOAT…"
      textoProcesando="Procesando el PDF del SOAT…"
      abierto={abierto}
      onToggle={onToggle}
      disabled={disabled}
      onCargar={async (file) => {
        const r = await cargarSoporteSoatAsignado(tramitesClient, instanceId, file, tenantId);
        onResultado?.(r);
        const ok = r.estado === 'vigente';
        return { ok, titulo: ok ? 'SOAT registrado' : 'SOAT pendiente', mensaje: r.mensaje };
      }}
    />
  );
}
