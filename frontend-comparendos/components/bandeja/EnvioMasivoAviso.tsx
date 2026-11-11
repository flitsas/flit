"use client";

import { useState } from "react";
import { InlineAlert, Modal } from "@flit/ui";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { MAXIMO_AVISOS_MASIVO, opcionesMasivo, resumirMasivo, type ResultadoMasivo, type TipoAvisoManual } from "@/lib/avisos";
import type { FilaBandeja } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

/**
 * Envío manual de un aviso a la selección de la bandeja (HU #13485, POST /avisos/masivo). Cada comparendo se revisa en
 * la API: los que no proceden (sin conductor o sin correo, pagado, aviso ya enviado…) quedan omitidos con su motivo.
 */
export function EnvioMasivoAviso({ filas, onCerrar, onHecho }: { filas: FilaBandeja[]; onCerrar: () => void; onHecho: () => void }) {
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [resultado, setResultado] = useState<ReturnType<typeof resumirMasivo> | null>(null);
  const demasiados = filas.length > MAXIMO_AVISOS_MASIVO;
  const ids = filas.map((f) => f.id);
  // Bug #14270 (AV-03): solo se ofrecen los avisos que proceden en algún comparendo de la selección, con cuántos.
  const cuenta = useCarga(demasiados ? null : `avisos-masivo-permitidos:${ids.join(",")}`, (signal) =>
    apiFetch<{ tipo: string; aplica: number }[]>("/avisos/masivo/permitidos", { method: "POST", body: { comparendoIds: ids }, signal }),
  );
  const opciones = opcionesMasivo(cuenta.datos ?? [], filas.length);
  const [elegido, setElegido] = useState<TipoAvisoManual | null>(null);
  // Por defecto, el que procede en más comparendos.
  const tipo = opciones.find((o) => o.valor === elegido)?.valor ?? [...opciones].sort((a, b) => b.aplica - a.aplica)[0]?.valor ?? null;

  const enviar = async () => {
    if (!tipo) return;
    setOcupado(true);
    setError(null);
    try {
      const r = await apiFetch<ResultadoMasivo[]>("/avisos/masivo", { method: "POST", body: { tipo, comparendoIds: filas.map((f) => f.id) } });
      setResultado(resumirMasivo(r));
      onHecho();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo enviar el aviso.");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={ocupado}
      size="lg"
      title={`Enviar aviso a ${filas.length} comparendo${filas.length === 1 ? "" : "s"}`}
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar} disabled={ocupado}>
            {resultado ? "Cerrar" : "Cancelar"}
          </button>
          {!resultado && (
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || demasiados || !tipo} onClick={enviar}>
              {ocupado ? "Enviando…" : "Enviar aviso"}
            </button>
          )}
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        {!resultado && (
          <>
            {cuenta.estado === "loading" && !demasiados && <p className="text-xs opacity-70">Revisando qué aviso corresponde a cada comparendo…</p>}
            {cuenta.error && <InlineAlert tone="error">{cuenta.error}</InlineAlert>}
            {cuenta.estado === "ready" && !demasiados && opciones.length === 0 && (
              <InlineAlert tone="warning" compact>
                Hoy no corresponde enviar ningún aviso a los comparendos seleccionados.
              </InlineAlert>
            )}
            {opciones.length > 0 && tipo && (
              <label className={ETIQUETA_CLS}>
                Aviso
                <select className={INPUT_CLS} value={tipo} onChange={(e) => setElegido(e.target.value as TipoAvisoManual)}>
                  {opciones.map((t) => (
                    <option key={t.valor} value={t.valor}>
                      {t.etiqueta}
                    </option>
                  ))}
                </select>
              </label>
            )}
            <p className="text-xs opacity-70">Los que no procedan quedan omitidos con su motivo y no detienen el envío.</p>
            {demasiados && <InlineAlert tone="warning" compact>Selecciona máximo {MAXIMO_AVISOS_MASIVO} comparendos por envío.</InlineAlert>}
          </>
        )}
        {resultado && (
          <>
            <InlineAlert tone={resultado.enviados > 0 ? "success" : "warning"} compact>
              {resultado.enviados} aviso{resultado.enviados === 1 ? "" : "s"} registrado{resultado.enviados === 1 ? "" : "s"}: salen en los próximos minutos (Centro de
              Mensajes).
            </InlineAlert>
            {resultado.omitidos.length > 0 && (
              <div className="flex flex-col gap-1 text-xs">
                <span className="font-semibold">{resultado.omitidos.length} omitido{resultado.omitidos.length === 1 ? "" : "s"}</span>
                <ul className="max-h-48 overflow-y-auto">
                  {resultado.omitidos.map((o) => (
                    <li key={o.numero}>
                      <span className="font-mono">{o.numero}</span>: {o.motivo}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </>
        )}
        {error && <InlineAlert tone="error">{error}</InlineAlert>}
      </div>
    </Modal>
  );
}
