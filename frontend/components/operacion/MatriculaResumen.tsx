'use client';

import type {
  InstanceStatus,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';

// Resumen consolidado de la matrícula (paso FUR): muestra el estado final de un
// vistazo (placa, vehículo, comprador, identidad, documentos, organismo) sin
// tener que entrar a cada pestaña del expediente. El detalle por pestañas sigue
// debajo (ExpedienteVisor). Adaptado del MatriculaResumen de Johan a la capa de
// datos de FLIT (InstanceStatus + field_values + actors).

interface Props {
  /** Modalidad del trámite: ajusta el título del resumen (matrícula vs traspaso). */
  modalidad: WizardModalidad;
  status: InstanceStatus;
  placa: string;
  vehiculo: string;
  vin: string;
  /** Parte saliente. Solo en traspaso; en matrícula es `null` y no se pinta. */
  vendedor?: { nombre?: string; documento?: string; tipoDoc?: string } | null;
  comprador: { nombre?: string; documento?: string; tipoDoc?: string } | null;
  archivosCount: number;
  identidadAprobada: boolean;
  orgTransito: { nombre?: string; ciudad?: string };
  /** SOAT del vehículo (ruta de placa, HU #10611): estado registrado + vencimiento si lo hay. */
  soat?: { estado?: string | null; vencimiento?: string | null };
}

// N 03 — labels/tonos desde la fuente única lib/tramites/estados.ts (6 estados de negocio).

function Field({ label, value }: { label: string; value?: string | null }) {
  return (
    <div>
      <p className="text-[10px] font-semibold uppercase tracking-wide opacity-60">{label}</p>
      <p className="text-sm">{value || '—'}</p>
    </div>
  );
}

export default function MatriculaResumen({
  modalidad,
  status,
  placa,
  vehiculo,
  vin,
  vendedor,
  comprador,
  archivosCount,
  identidadAprobada,
  orgTransito,
  soat,
}: Props) {
  const tone = estadoChipStyle(status).color;
  const orgTxt = [orgTransito?.nombre, orgTransito?.ciudad].filter(Boolean).join(' · ');
  // SOAT (ruta de placa): el field soat_estado se registra en 'asignado' (RUNT o PDF, HU #10611).
  const soatEstado = (soat?.estado ?? '').toLowerCase();
  const soatLabel =
    soatEstado === 'vigente'
      ? 'Vigente'
      : soatEstado === 'vencido'
        ? 'Vencido'
        : soatEstado === 'unknown'
          ? 'No reportado'
          : '—';
  const soatColor =
    soatEstado === 'vigente' ? '#15803d' : soatEstado === 'vencido' ? '#c2410c' : undefined;
  // Traspaso y matrícula son procesos distintos: el resumen se rotula acorde.
  const resumenTitulo =
    modalidad === 'traspaso' ? 'Resumen del traspaso' : 'Resumen de la matrícula';

  return (
    <section
      aria-label={resumenTitulo}
      className="rounded-xl border p-4"
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <span className="h-5 w-1.5 rounded-full" style={{ background: tone }} aria-hidden="true" />
          <h4 className="text-xs font-bold uppercase tracking-[0.18em]">{resumenTitulo}</h4>
        </div>
        <span
          className="rounded-full px-3 py-1 text-[11px] font-semibold"
          style={{ background: `color-mix(in srgb, ${tone} 14%, transparent)`, color: tone }}
        >
          {estadoLabel(status)}
        </span>
      </div>

      {placa && (
        <div className="mb-3 flex items-center gap-3">
          <span className="font-mono text-2xl font-bold tracking-widest" style={{ color: tone }}>
            {placa}
          </span>
          <span
            className="text-xs opacity-70"
            style={soatColor ? { color: soatColor, opacity: 1 } : undefined}
          >
            SOAT: {soatLabel}
            {soatEstado === 'vigente' && soat?.vencimiento ? ` · vence ${soat.vencimiento}` : ''}
          </span>
        </div>
      )}

      <div className="grid grid-cols-2 gap-x-4 gap-y-3 sm:grid-cols-3">
        <Field label="Vehículo" value={vehiculo} />
        <Field label="VIN" value={vin} />
        {vendedor && (
          <>
            <Field label="Vendedor" value={vendedor.nombre} />
            <Field
              label="Documento vendedor"
              value={vendedor.documento ? `${vendedor.tipoDoc || 'CC'} ${vendedor.documento}` : null}
            />
          </>
        )}
        <Field label="Comprador" value={comprador?.nombre} />
        <Field
          label={vendedor ? 'Documento comprador' : 'Documento'}
          value={comprador?.documento ? `${comprador?.tipoDoc || 'CC'} ${comprador.documento}` : null}
        />
        <Field label="Identidad" value={identidadAprobada ? 'Verificada' : 'Pendiente'} />
        <Field label="Documentos" value={`${archivosCount} cargado${archivosCount === 1 ? '' : 's'}`} />
        <Field label="Organismo de tránsito" value={orgTxt} />
      </div>

      <p className="mt-3 text-[11px] opacity-60">
        El detalle completo (documentos, identidad, FUR) está en el expediente, abajo.
      </p>
    </section>
  );
}
