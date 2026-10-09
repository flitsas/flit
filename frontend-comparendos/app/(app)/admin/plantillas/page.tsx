"use client";

import { useRef, useState } from "react";
import { Copy, FilePlus2, Send } from "lucide-react";
import { InlineAlert, Modal, StatusBadge } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { EditorHtml } from "@/components/plantillas/EditorHtml";
import { ListaPlantillas } from "@/components/plantillas/ListaPlantillas";
import { VistaPrevia } from "@/components/plantillas/VistaPrevia";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { variablesUsadas } from "@/lib/editorHtml";
import { esPlantillaDeCorreo, TIPO_PLANTILLA } from "@/lib/etiquetas";
import type { PlantillaDetalle, PlantillaResumen, VariablePlantilla, VersionPlantilla } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const TONO_VERSION: Record<string, "success" | "info" | "neutral"> = { Publicada: "success", Borrador: "info", Retirada: "neutral" };

interface Borrador {
  versionId: string;
  asunto: string;
  cuerpoHtml: string;
}

function Editor({ plantilla, onCerrar, onCambio }: { plantilla: PlantillaResumen; onCerrar: () => void; onCambio: () => void }) {
  const toast = useToast();
  const correo = esPlantillaDeCorreo(plantilla.tipo);
  const detalle = useCarga(`plantilla-admin:${plantilla.id}`, (signal) => apiFetch<PlantillaDetalle>(`/plantillas/${plantilla.id}`, { signal }));
  const catalogo = useCarga("variables", (signal) => apiFetch<VariablePlantilla[]>("/plantillas/variables", { signal }));
  const [versionId, setVersionId] = useState<string | null>(null);
  const elegida = versionId ?? detalle.datos?.versiones.find((v) => v.estado === "Borrador")?.id ?? detalle.datos?.versionPublicadaId ?? null;
  const version = useCarga(elegida ? `version-admin:${elegida}` : null, (signal) => apiFetch<VersionPlantilla>(`/plantillas/versiones/${elegida}`, { signal }));
  const [borrador, setBorrador] = useState<Borrador | null>(null);
  const [vista, setVista] = useState<{ asunto: string | null; cuerpoHtml: string } | null>(null);
  const [nota, setNota] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const temporizador = useRef<ReturnType<typeof setTimeout> | null>(null);

  const v = version.datos;
  const editable = v?.estado === "Borrador";
  const actual: Borrador | null = v ? (borrador && borrador.versionId === v.id ? borrador : { versionId: v.id, asunto: v.asunto ?? "", cuerpoHtml: v.cuerpoHtml }) : null;
  const desconocidas = actual && catalogo.datos ? variablesUsadas(`${actual.asunto} ${actual.cuerpoHtml}`).filter((n) => !catalogo.datos!.some((c) => c.nombre === n)) : [];

  const editar = (parcial: Partial<Borrador>) => {
    if (!actual) return;
    const nuevo = { ...actual, ...parcial };
    setBorrador(nuevo);
    if (temporizador.current) clearTimeout(temporizador.current);
    temporizador.current = setTimeout(() => setVista({ asunto: nuevo.asunto || null, cuerpoHtml: nuevo.cuerpoHtml }), 700);
  };

  const ejecutar = async (accion: () => Promise<void>, ok: string) => {
    setOcupado(true);
    setError(null);
    try {
      await accion();
      toast.show(ok, "success");
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo completar la acción.");
    } finally {
      setOcupado(false);
    }
  };

  const nuevaVersion = () =>
    ejecutar(async () => {
      const creada = await apiFetch<VersionPlantilla>(`/admin/plantillas/${plantilla.id}/versiones`, {
        method: "POST",
        body: { asunto: v?.asunto ?? null, cuerpoHtml: v?.cuerpoHtml ?? "<p></p>", notaCambio: null },
      });
      detalle.recargar();
      setVersionId(creada.id);
      setBorrador(null);
      setVista(null);
    }, "Borrador creado a partir de la versión elegida.");

  const guardar = () =>
    ejecutar(async () => {
      await apiFetch(`/admin/plantillas/versiones/${actual!.versionId}`, { method: "PUT", body: { asunto: correo ? actual!.asunto : null, cuerpoHtml: actual!.cuerpoHtml, notaCambio: nota || null } });
      version.recargar();
      setBorrador(null);
    }, "Borrador guardado.");

  const publicar = () =>
    ejecutar(async () => {
      await apiFetch(`/admin/plantillas/versiones/${actual!.versionId}`, { method: "PUT", body: { asunto: correo ? actual!.asunto : null, cuerpoHtml: actual!.cuerpoHtml, notaCambio: nota } });
      await apiFetch(`/admin/plantillas/versiones/${actual!.versionId}/publicar`, { method: "POST", body: { nota } });
      setNota("");
      setBorrador(null);
      detalle.recargar();
      version.recargar();
      onCambio();
    }, "Versión publicada. La anterior quedó retirada.");

  const copiarARenting = () =>
    ejecutar(async () => {
      await apiFetch("/admin/plantillas", { method: "POST", body: { conjunto: "Renting", tipo: plantilla.tipo, tipologiaId: plantilla.tipologiaId } });
      onCambio();
    }, "Plantilla de Renting creada con un borrador copiado de FLIT.");

  return (
    <Modal open onClose={onCerrar} size="2xl" title={TIPO_PLANTILLA[plantilla.tipo] ?? plantilla.tipo} description={`${plantilla.conjunto === "Renting" ? "Renting" : "FLIT"}${plantilla.tipologia ? ` · ${plantilla.tipologia}` : ""}`}>
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-[200px_1fr_1fr]">
        <aside className="flex flex-col gap-2">
          <h3 className="text-xs font-semibold">Versiones</h3>
          {(detalle.datos?.versiones ?? []).map((x) => (
            <button key={x.id} type="button" className={`${controlCls(x.id === elegida)} justify-between`} onClick={() => { setVersionId(x.id); setBorrador(null); setVista(null); }}>
              <span>v{x.numeroVersion}</span>
              <StatusBadge label={x.estado} tone={TONO_VERSION[x.estado] ?? "neutral"} />
            </button>
          ))}
          {v && !editable && (
            <button type="button" className={controlCls(false)} disabled={ocupado} onClick={nuevaVersion}>
              <FilePlus2 className="h-3.5 w-3.5" aria-hidden />
              Nueva versión desde v{v.numeroVersion}
            </button>
          )}
          {plantilla.conjunto === "Flit" && (
            <button type="button" className={controlCls(false)} disabled={ocupado} onClick={copiarARenting} title="Si Renting no tiene la suya, usa la de FLIT">
              <Copy className="h-3.5 w-3.5" aria-hidden />
              Crear versión para Renting
            </button>
          )}
        </aside>

        <div className="flex flex-col gap-3">
          {version.estado === "loading" && <p className="text-sm opacity-60">Cargando…</p>}
          {actual && (
            <>
              {!editable && <InlineAlert tone="info" compact>Esta versión está {v?.estado === "Publicada" ? "publicada" : "retirada"} y no se edita: crea una nueva versión.</InlineAlert>}
              {correo && (
                <label className={ETIQUETA_CLS}>
                  Asunto
                  <input className={INPUT_CLS} value={actual.asunto} disabled={!editable} onChange={(e) => editar({ asunto: e.target.value })} />
                </label>
              )}
              {editable ? (
                <EditorHtml key={actual.versionId} inicial={actual.cuerpoHtml} variables={catalogo.datos ?? []} onCambio={(cuerpoHtml) => editar({ cuerpoHtml })} />
              ) : (
                <pre className="max-h-[420px] overflow-auto whitespace-pre-wrap rounded-xl border border-[#DFE5ED] p-3 font-mono text-[11px] dark:border-white/15">{actual.cuerpoHtml}</pre>
              )}
              {desconocidas.length > 0 && (
                <InlineAlert tone="warning" compact>
                  Estas variables no existen y no dejarán publicar: {desconocidas.map((x) => `{{${x}}}`).join(", ")}
                </InlineAlert>
              )}
              {editable && (
                <>
                  <label className={ETIQUETA_CLS}>
                    Qué cambió (obligatorio para publicar)
                    <textarea className={TEXTAREA_CLS} rows={2} value={nota} onChange={(e) => setNota(e.target.value)} />
                  </label>
                  <div className="flex justify-end gap-2">
                    <button type="button" className={controlCls(false)} disabled={ocupado} onClick={guardar}>
                      Guardar borrador
                    </button>
                    <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || !nota.trim()} onClick={publicar}>
                      <Send className="h-3.5 w-3.5" aria-hidden />
                      Publicar
                    </button>
                  </div>
                </>
              )}
            </>
          )}
          {error && <InlineAlert tone="error" compact>{error}</InlineAlert>}
        </div>

        <VistaPrevia
          contenido={actual ? { tipo: plantilla.tipo, asunto: correo ? (vista?.asunto ?? actual.asunto) || null : null, cuerpoHtml: vista?.cuerpoHtml ?? actual.cuerpoHtml } : null}
        />
      </div>
    </Modal>
  );
}

/** Editor de plantillas del SuperAdmin (HU #13486, §7.16): borradores, vista previa en vivo y publicación con nota [Excel J3]. */
export default function EditorPlantillas() {
  const [elegida, setElegida] = useState<PlantillaResumen | null>(null);
  const [recarga, setRecarga] = useState(0);
  return (
    <Requiere superAdmin>
      <section className="flex flex-col gap-4">
        <Encabezado
          titulo="Editor de plantillas"
          descripcion="Cambia los textos sin desplegar código. Cada publicación crea una versión que no se modifica; los DP y correos enviados conservan la suya."
        />
        <ListaPlantillas onElegir={setElegida} recarga={recarga} />
        {elegida && <Editor key={elegida.id} plantilla={elegida} onCerrar={() => setElegida(null)} onCambio={() => setRecarga((r) => r + 1)} />}
      </section>
    </Requiere>
  );
}
