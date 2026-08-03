'use client';

import type {
  InstanceStatus,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';

// Feature #11211 — resumen hero compacto (summary-first). El detalle técnico vive en ExpedienteVisor.

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
  /**
   * Partes cuya firma se plasma desde el baúl. El resumen decía «Identidad: Verificada» para ellas
   * porque el booleano de arriba no distingue con QUÉ se firmó, y una firma del baúl también deja la
   * identidad por satisfecha. Decir «verificada» donde nadie hizo biometría desinforma al gestor.
   */
  firmaBaulPartes?: string[];
  orgTransito: { nombre?: string; ciudad?: string };
  /** SOAT del vehículo (ruta de placa, HU #10611): estado registrado + vencimiento si lo hay. */
  soat?: { estado?: string | null; vencimiento?: string | null };
}

export default function MatriculaResumen({
  modalidad,
  status,
  placa,
  vehiculo,
  comprador,
  vendedor,
  soat,
}: Props) {
  const tone = estadoChipStyle(status).color;
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
  const resumenTitulo = 'Resumen del trámite';
  const partesTxt = [vendedor?.nombre, comprador?.nombre].filter(Boolean).join(' · ');

  return (
    <section aria-label={resumenTitulo} className="rounded-xl border p-4">
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
        <div className="mb-2 flex flex-wrap items-center gap-3">
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

      {vehiculo ? (
        <p className="text-sm font-medium" style={{ color: '#162744' }}>
          {vehiculo}
        </p>
      ) : null}

      {partesTxt ? (
        <p className="mt-1 text-xs opacity-70">
          {modalidad === 'traspaso' ? 'Partes: ' : 'Comprador: '}
          {partesTxt}
        </p>
      ) : null}

      <p className="mt-3 text-[11px] opacity-60">
        Revisa el expediente digital abajo para el detalle completo.
      </p>
    </section>
  );
}
