'use client';

import type { InstanceStatus } from '@/lib/api/types/procedure-runtime';

// Resumen consolidado de la matrícula (paso FUR): muestra el estado final de un
// vistazo (placa, vehículo, comprador, identidad, documentos, organismo) sin
// tener que entrar a cada pestaña del expediente. El detalle por pestañas sigue
// debajo (ExpedienteVisor). Adaptado del MatriculaResumen de Johan a la capa de
// datos de FLIT (InstanceStatus + field_values + actors).

interface Props {
  status: InstanceStatus;
  placa: string;
  vehiculo: string;
  vin: string;
  comprador: { nombre?: string; documento?: string; tipoDoc?: string } | null;
  archivosCount: number;
  identidadAprobada: boolean;
  orgTransito: { nombre?: string; ciudad?: string };
}

/** Etiqueta + tono por estado de la instancia (espejo de InstanceStatus). */
const ESTADO_LABEL: Record<InstanceStatus, string> = {
  draft: 'Borrador (en preparación)',
  submitted: 'Enviado a tránsito',
  in_review: 'En revisión',
  completed: 'Completada',
  rejected: 'Devuelto con observación',
};

const ESTADO_TONE: Record<InstanceStatus, string> = {
  draft: '#9AA5B1',
  submitted: '#557EFF',
  in_review: '#557EFF',
  completed: '#5B8A1F',
  rejected: '#FF4E00',
};

function Field({ label, value }: { label: string; value?: string | null }) {
  return (
    <div>
      <p className="text-[10px] font-semibold uppercase tracking-wide opacity-60">{label}</p>
      <p className="text-sm">{value || '—'}</p>
    </div>
  );
}

export default function MatriculaResumen({
  status,
  placa,
  vehiculo,
  vin,
  comprador,
  archivosCount,
  identidadAprobada,
  orgTransito,
}: Props) {
  const tone = ESTADO_TONE[status];
  const orgTxt = [orgTransito?.nombre, orgTransito?.ciudad].filter(Boolean).join(' · ');

  return (
    <section
      aria-label="Resumen de la matrícula"
      className="rounded-xl border p-4"
      style={{ borderColor: '#DFE5ED' }}
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <span className="h-5 w-1.5 rounded-full" style={{ background: tone }} aria-hidden="true" />
          <h4 className="text-xs font-bold uppercase tracking-[0.18em]">Resumen de la matrícula</h4>
        </div>
        <span
          className="rounded-full px-3 py-1 text-[11px] font-semibold"
          style={{ background: `color-mix(in srgb, ${tone} 14%, transparent)`, color: tone }}
        >
          {ESTADO_LABEL[status]}
        </span>
      </div>

      {placa && (
        <div className="mb-3 flex items-center gap-3">
          <span className="font-mono text-2xl font-bold tracking-widest" style={{ color: tone }}>
            {placa}
          </span>
          <span className="text-xs opacity-70">SOAT: —</span>
        </div>
      )}

      <div className="grid grid-cols-2 gap-x-4 gap-y-3 sm:grid-cols-3">
        <Field label="Vehículo" value={vehiculo} />
        <Field label="VIN" value={vin} />
        <Field label="Comprador" value={comprador?.nombre} />
        <Field
          label="Documento"
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
