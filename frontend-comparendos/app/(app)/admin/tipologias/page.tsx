"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { DataTable, InlineAlert, Modal, StatusBadge, activeTone } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { Interruptor } from "@/components/ui/Interruptor";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { estadoLegal } from "@/lib/etiquetas";
import { ADJUNTOS_TIPOLOGIA, ESTADOS_TIPOLOGIA, erroresTipologia, type TipologiaForm } from "@/lib/parametrizador";
import type { Tipologia } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const VACIA: TipologiaForm = {
  codigo: "",
  nombre: "",
  descripcionJuridica: "",
  prefijoRadicado: "",
  estadosLegalesPermitidos: ["EnTiempo"],
  permiteMasivo: false,
  plazoRespuestaOtDiasHabiles: 15,
  adjuntosRequeridos: [],
  activo: true,
};

function Formulario({ tipologia, onCerrar, onGuardada }: { tipologia: Tipologia | null; onCerrar: () => void; onGuardada: () => void }) {
  const [t, setT] = useState<TipologiaForm>(
    tipologia
      ? {
          codigo: tipologia.codigo,
          nombre: tipologia.nombre,
          descripcionJuridica: tipologia.descripcionJuridica,
          prefijoRadicado: tipologia.prefijoRadicado,
          estadosLegalesPermitidos: tipologia.estadosLegalesPermitidos,
          permiteMasivo: tipologia.permiteMasivo,
          plazoRespuestaOtDiasHabiles: tipologia.plazoRespuestaOtDiasHabiles,
          adjuntosRequeridos: tipologia.adjuntosRequeridos,
          activo: tipologia.activo,
        }
      : VACIA,
  );
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const errores = erroresTipologia(t);
  const alternar = (lista: string[], v: string) => (lista.includes(v) ? lista.filter((x) => x !== v) : [...lista, v]);

  const guardar = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(tipologia ? `/admin/tipologias/${tipologia.id}` : "/admin/tipologias", { method: tipologia ? "PUT" : "POST", body: t });
      onGuardada();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo guardar.");
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
      title={tipologia ? `Tipología ${tipologia.codigo}` : "Nueva tipología"}
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || errores.length > 0} onClick={guardar}>
            {ocupado ? "Guardando…" : "Guardar"}
          </button>
        </div>
      }
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <label className={ETIQUETA_CLS}>
          Código
          <input className={INPUT_CLS} value={t.codigo} onChange={(e) => setT({ ...t, codigo: e.target.value.toUpperCase() })} />
        </label>
        <label className={ETIQUETA_CLS}>
          Prefijo del radicado
          <input className={INPUT_CLS} value={t.prefijoRadicado} maxLength={6} onChange={(e) => setT({ ...t, prefijoRadicado: e.target.value.toUpperCase() })} />
        </label>
        <label className={ETIQUETA_CLS}>
          Plazo de respuesta del OT (días hábiles)
          <input className={INPUT_CLS} type="number" min={1} max={90} value={t.plazoRespuestaOtDiasHabiles} onChange={(e) => setT({ ...t, plazoRespuestaOtDiasHabiles: Number(e.target.value) })} />
        </label>
        <label className={`${ETIQUETA_CLS} md:col-span-3`}>
          Nombre
          <input className={INPUT_CLS} value={t.nombre} onChange={(e) => setT({ ...t, nombre: e.target.value })} />
        </label>
        <label className={`${ETIQUETA_CLS} md:col-span-3`}>
          Descripción jurídica (se usa como fundamento del DP)
          <textarea className={TEXTAREA_CLS} rows={4} value={t.descripcionJuridica} onChange={(e) => setT({ ...t, descripcionJuridica: e.target.value })} />
        </label>
        <fieldset className={`${ETIQUETA_CLS} md:col-span-3`}>
          <legend>Estados legales en que procede</legend>
          <div className="flex flex-wrap gap-2">
            {ESTADOS_TIPOLOGIA.map((e) => (
              <button key={e.valor} type="button" aria-pressed={t.estadosLegalesPermitidos.includes(e.valor)} className={controlCls(t.estadosLegalesPermitidos.includes(e.valor))}
                onClick={() => setT({ ...t, estadosLegalesPermitidos: alternar(t.estadosLegalesPermitidos, e.valor) })}>
                {e.texto}
              </button>
            ))}
          </div>
        </fieldset>
        <fieldset className={`${ETIQUETA_CLS} md:col-span-3`}>
          <legend>Documentos del representante que se adjuntan</legend>
          <div className="flex flex-wrap gap-2">
            {ADJUNTOS_TIPOLOGIA.map((a) => (
              <button key={a.valor} type="button" aria-pressed={t.adjuntosRequeridos.includes(a.valor)} className={controlCls(t.adjuntosRequeridos.includes(a.valor))}
                onClick={() => setT({ ...t, adjuntosRequeridos: alternar(t.adjuntosRequeridos, a.valor) })}>
                {a.texto}
              </button>
            ))}
          </div>
        </fieldset>
        <Interruptor activo={t.permiteMasivo} onCambio={(v) => setT({ ...t, permiteMasivo: v })} etiqueta="Admite envío en lote" />
        <Interruptor activo={t.activo} onCambio={(v) => setT({ ...t, activo: v })} etiqueta="Activa en el catálogo" ayuda="Desactivarla no afecta los DP ya enviados." />
      </div>
      {errores.length > 0 && (
        <InlineAlert tone="warning" compact className="mt-3">
          {errores.join(" ")}
        </InlineAlert>
      )}
      {error && <InlineAlert tone="error" compact className="mt-3">{error}</InlineAlert>}
    </Modal>
  );
}

/** Catálogo global de tipologías de DP (HU #13476, §7.15): solo el SuperAdmin, sin desplegar código (RN-130). */
export default function Tipologias() {
  const toast = useToast();
  const lista = useCarga("tipologias-admin", (signal) => apiFetch<Tipologia[]>("/admin/tipologias", { signal }));
  const [editar, setEditar] = useState<Tipologia | "nueva" | null>(null);

  return (
    <Requiere superAdmin>
      <section className="flex flex-col gap-4">
        <Encabezado
          titulo="Tipologías de derecho de petición"
          descripcion="Las tesis jurídicas que se pueden radicar, en qué estados legales proceden y con qué prefijo se radican."
          acciones={
            <button type="button" className={BOTON_PRIMARIO_CLS} onClick={() => setEditar("nueva")}>
              <Plus className="h-3.5 w-3.5" aria-hidden />
              Nueva tipología
            </button>
          }
        />
        <DataTable
          ariaLabel="Tipologías"
          rows={lista.datos ?? []}
          getRowKey={(t) => t.id}
          status={lista.estado}
          errorMessage={lista.error ?? undefined}
          onRetry={lista.recargar}
          onRowClick={setEditar}
          emptyMessage="El catálogo está vacío."
          columns={[
            { key: "codigo", header: "Código", render: (t) => <span className="font-mono font-semibold">{t.codigo}</span> },
            { key: "nombre", header: "Nombre", render: (t) => t.nombre },
            { key: "prefijo", header: "Prefijo", render: (t) => t.prefijoRadicado },
            {
              key: "estados",
              header: "Procede en",
              render: (t) => (
                <div className="flex flex-wrap gap-1">
                  {t.estadosLegalesPermitidos.map((e) => <StatusBadge key={e} label={estadoLegal(e).texto} tone={estadoLegal(e).tono} />)}
                </div>
              ),
            },
            { key: "masivo", header: "Lote", render: (t) => (t.permiteMasivo ? "Sí" : "No") },
            { key: "plazo", header: "Plazo OT", align: "right", render: (t) => `${t.plazoRespuestaOtDiasHabiles} días` },
            { key: "adjuntos", header: "Adjuntos", render: (t) => t.adjuntosRequeridos.length || "—" },
            { key: "empresas", header: "Empresas", align: "right", render: (t) => t.empresasHabilitadas ?? 0 },
            { key: "activo", header: "Estado", render: (t) => <StatusBadge label={t.activo ? "Activa" : "Inactiva"} tone={activeTone(t.activo)} /> },
          ]}
        />
        {editar && (
          <Formulario
            key={editar === "nueva" ? "nueva" : editar.id}
            tipologia={editar === "nueva" ? null : editar}
            onCerrar={() => setEditar(null)}
            onGuardada={() => {
              setEditar(null);
              lista.recargar();
              toast.show("Tipología guardada.", "success");
            }}
          />
        )}
      </section>
    </Requiere>
  );
}
