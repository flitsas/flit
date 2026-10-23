"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { InlineAlert, Modal } from "@flit/ui";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { revisarLote, type FilaLote } from "@/lib/dp";
import type { FilaBandeja, Tipologia } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

export function aFilaLote(f: FilaBandeja): FilaLote {
  return {
    id: f.id,
    numero: f.numero,
    organismoTransitoId: f.organismoTransitoId,
    organismo: f.organismo,
    activo: f.activo,
    pagado: f.pagado,
    tieneDp: !!f.dpRadicado || !!f.dpEstado,
  };
}

/**
 * Envío masivo de derechos de petición desde la selección de la bandeja (HU #13491 sobre la API de la HU #13489, §6.17):
 * una tipología que admita masivo y un solo organismo. El lote se procesa en segundo plano; el avance se ve en /dp/lotes.
 */
export function EnvioMasivoDp({ filas, onCerrar }: { filas: FilaBandeja[]; onCerrar: () => void }) {
  const router = useRouter();
  const tipologias = useCarga("tipologias-masivas", (signal) => apiFetch<Tipologia[]>("/tipologias", { signal, query: { soloHabilitadas: true } }));
  const masivas = (tipologias.datos ?? []).filter((t) => t.permiteMasivo);
  const [tipologia, setTipologia] = useState("");
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const revision = revisarLote(filas.map(aFilaLote));

  const crear = async () => {
    setOcupado(true);
    setError(null);
    try {
      const r = await apiFetch<{ id: string }>("/dp/lotes", { method: "POST", body: { tipologiaId: tipologia, comparendoIds: filas.map((f) => f.id) } });
      router.push(`/dp/lotes?lote=${r.id}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo crear el envío masivo.");
      setOcupado(false);
    }
  };

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={ocupado}
      size="lg"
      title={`Enviar derecho de petición a ${filas.length} comparendo${filas.length === 1 ? "" : "s"}`}
      description={revision.organismo ? `Organismo de tránsito: ${revision.organismo}` : undefined}
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar} disabled={ocupado}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || !tipologia || revision.errores.length > 0} onClick={crear}>
            {ocupado ? "Creando…" : "Iniciar envío"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        {revision.errores.length > 0 && (
          <InlineAlert tone="error" title="Revisa la selección">
            {revision.errores.join(" ")}
          </InlineAlert>
        )}
        {tipologias.estado === "error" && <InlineAlert tone="error">{tipologias.error}</InlineAlert>}
        {tipologias.estado === "ready" && masivas.length === 0 && (
          <InlineAlert tone="warning">Tu empresa no tiene habilitada ninguna tipología que admita envío masivo.</InlineAlert>
        )}
        {masivas.length > 0 && (
          <label className={ETIQUETA_CLS}>
            Tipología
            <select className={INPUT_CLS} value={tipologia} onChange={(e) => setTipologia(e.target.value)}>
              <option value="">Elige una tipología</option>
              {masivas.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.codigo} — {t.nombre}
                </option>
              ))}
            </select>
          </label>
        )}
        <p className="text-xs opacity-70">
          Cada comparendo se revisa al enviarlo: los que no cumplan (estado legal, representante legal, ya tienen DP…) quedan omitidos con su motivo y no
          detienen el lote.
        </p>
        {revision.omitiran.length > 0 && (
          <InlineAlert tone="warning" compact title={`${revision.omitiran.length} se omitirán`}>
            {revision.omitiran
              .slice(0, 10)
              .map((o) => `${o.numero}: ${o.motivo.toLowerCase()}`)
              .join(" · ")}
            {revision.omitiran.length > 10 && ` · y ${revision.omitiran.length - 10} más`}
          </InlineAlert>
        )}
        {error && <InlineAlert tone="error">{error}</InlineAlert>}
      </div>
    </Modal>
  );
}
