"use client";

import { useRef, useState } from "react";
import { FlaskConical, ListChecks, Pencil, Plus, Scale, Send, Undo2 } from "lucide-react";
import { DataTable, InlineAlert, Modal } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { useSondeo } from "@/components/ocr/comunes";
import { Chip } from "@/components/ui/Chip";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { formatoFechaHora } from "@/lib/format/date";
import { errorArchivoOcr, ETIQUETA_CAMPO, etiquetaEvaluacion, type EvaluacionPrompt, resumenEvaluacion, TIPO_FALLO, versionAnterior } from "@/lib/ocr";
import { useCarga } from "@/lib/useCarga";

interface Version {
  id: string;
  numeroVersion: number;
  texto: string;
  proveedor: string;
  modelo: string;
  publicada: boolean;
  creadoEn: string;
  actualizadoEn: string | null;
  retiradaEn: string | null;
  puedePublicar: boolean;
  evaluacion: EvaluacionPrompt | null;
}

interface CampoEvaluado {
  campo: string;
  esperado: string | null;
  candidata: string | null;
  vigente: string | null;
  aciertaCandidata: boolean;
  aciertaVigente: boolean;
}

interface DetalleEvaluacion extends Omit<EvaluacionPrompt, "habilita"> {
  versionNumero: number;
  detalle: { documentoId: string; archivo: string; falloCandidata: string | null; falloVigente: string | null; campos: CampoEvaluado[] }[] | null;
}

interface Prueba {
  exitoso: boolean;
  modelo: string;
  tipoFallo: string | null;
  mensaje: string | null;
  campos: Record<string, { valor: string | null; confianza: number | null }>;
  crudo: string | null;
}

type Edicion = { id: string | null; texto: string; modelo: string };

function Editor({ inicial, onCerrar, onGuardado }: { inicial: Edicion; onCerrar: () => void; onGuardado: () => void }) {
  const toast = useToast();
  const [texto, setTexto] = useState(inicial.texto);
  const [modelo, setModelo] = useState(inicial.modelo);
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const guardar = async () => {
    setEnviando(true);
    setError(null);
    try {
      const body = { texto, proveedor: "ANTHROPIC", modelo };
      if (inicial.id) await apiFetch(`/admin/ocr/prompts/${inicial.id}`, { method: "PUT", body });
      else await apiFetch("/admin/ocr/prompts", { method: "POST", body });
      toast.show(inicial.id ? "Versión actualizada." : "Versión creada. Pruébala antes de publicarla.", "success");
      onGuardado();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo guardar.");
    } finally {
      setEnviando(false);
    }
  };

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={enviando}
      size="2xl"
      title={inicial.id ? "Editar versión del prompt" : "Nueva versión del prompt"}
      description="Por ahora solo está conectado Anthropic; OpenAI y Gemini quedan para cuando se comparen con la muestra real."
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={enviando || !texto.trim() || !modelo.trim()} onClick={guardar}>
            {enviando ? "Guardando…" : "Guardar"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        <div className="grid gap-3 sm:grid-cols-2">
          <label className={ETIQUETA_CLS}>
            Proveedor
            <input className={INPUT_CLS} value="ANTHROPIC" disabled />
          </label>
          <label className={ETIQUETA_CLS}>
            Modelo
            <input className={INPUT_CLS} value={modelo} maxLength={80} onChange={(e) => setModelo(e.target.value)} />
          </label>
        </div>
        <label className={ETIQUETA_CLS}>
          Texto del prompt ({texto.length.toLocaleString("es-CO")} / 20.000)
          <textarea className={`${TEXTAREA_CLS} min-h-[50vh] font-mono`} value={texto} maxLength={20000} onChange={(e) => setTexto(e.target.value)} />
        </label>
        {error && <InlineAlert tone="error">{error}</InlineAlert>}
      </div>
    </Modal>
  );
}

function Probar({ v, onCerrar }: { v: Version; onCerrar: () => void }) {
  const input = useRef<HTMLInputElement>(null);
  const [probando, setProbando] = useState(false);
  const [resultado, setResultado] = useState<Prueba | null>(null);
  const [error, setError] = useState<string | null>(null);

  const probar = async (archivo: File) => {
    const invalido = errorArchivoOcr(archivo);
    setError(invalido);
    setResultado(null);
    if (invalido) return;
    setProbando(true);
    try {
      const form = new FormData();
      form.append("archivo", archivo);
      setResultado(await apiFetch<Prueba>(`/admin/ocr/prompts/${v.id}/probar`, { method: "POST", body: form }));
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo probar.");
    } finally {
      setProbando(false);
      if (input.current) input.current.value = "";
    }
  };

  const campos = Object.entries(resultado?.campos ?? {});
  return (
    <Modal open onClose={onCerrar} busy={probando} size="2xl" title={`Probar la versión ${v.numeroVersion}`} description="Se envía un archivo de muestra al modelo con este prompt. No se guarda nada.">
      <div className="flex flex-col gap-3">
        <div className="flex items-center gap-3">
          <input ref={input} type="file" className="hidden" accept=".jpg,.jpeg,.png,.tif,.tiff,.pdf" onChange={(e) => e.target.files?.[0] && probar(e.target.files[0])} />
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={probando} onClick={() => input.current?.click()}>
            <FlaskConical className="h-3.5 w-3.5" aria-hidden />
            {probando ? "Extrayendo…" : "Elegir archivo de muestra"}
          </button>
          <span className="text-xs opacity-70">JPG, PNG, TIFF o PDF de hasta 20 MB.</span>
        </div>
        {error && <InlineAlert tone="error">{error}</InlineAlert>}
        {resultado && !resultado.exitoso && (
          <InlineAlert tone="error" title={(resultado.tipoFallo && TIPO_FALLO[resultado.tipoFallo]) ?? "Falló la extracción"}>
            {resultado.mensaje}
          </InlineAlert>
        )}
        {resultado?.exitoso && (
          <DataTable
            ariaLabel="Campos extraídos"
            rows={campos}
            getRowKey={([k]) => k}
            emptyMessage="El modelo no devolvió campos."
            columns={[
              { key: "campo", header: "Campo", render: ([k]) => ETIQUETA_CAMPO[k] ?? k },
              { key: "valor", header: "Valor", render: ([, c]) => c.valor ?? <span className="opacity-50">null</span> },
              { key: "conf", header: "Confianza", align: "right", render: ([, c]) => (c.confianza === null ? "—" : `${Math.round(c.confianza * 100)} %`) },
            ]}
          />
        )}
        {resultado?.crudo && (
          <details className="text-xs">
            <summary className="cursor-pointer font-semibold">JSON devuelto por {resultado.modelo}</summary>
            <pre className="mt-2 max-h-80 overflow-auto rounded-xl bg-[#F8FAFC] p-3 text-[11px] dark:bg-[#0B0F14]">{formatear(resultado.crudo)}</pre>
          </details>
        )}
      </div>
    </Modal>
  );
}

function Valor({ v, ok }: { v: string | null; ok: boolean }) {
  return <span className={ok ? "" : "font-semibold text-[#C10000] dark:text-red-400"}>{v ?? <span className="opacity-50">vacío</span>}</span>;
}

/** Campo por campo, solo donde alguna de las dos versiones no acertó. */
function ResultadoEvaluacion({ id, onCerrar }: { id: string; onCerrar: () => void }) {
  const carga = useCarga(`evaluacion-${id}`, (signal) => apiFetch<DetalleEvaluacion>(`/admin/ocr/prompts/evaluaciones/${id}`, { signal }));
  const e = carga.datos;
  const filas = (e?.detalle ?? []).flatMap((d) =>
    d.campos.filter((c) => !c.aciertaCandidata || !c.aciertaVigente).map((c) => ({ ...c, archivo: d.archivo, clave: `${d.documentoId}-${c.campo}` })),
  );
  const fallos = (e?.detalle ?? []).filter((d) => d.falloCandidata || d.falloVigente);
  return (
    <Modal
      open
      onClose={onCerrar}
      size="2xl"
      title={e ? `Evaluación de la versión ${e.versionNumero}` : "Evaluación"}
      description="Se comparan los campos obligatorios contra lo que una persona revisó y registró en los últimos documentos de OCR."
    >
      <div className="flex flex-col gap-3">
        {carga.estado === "error" && <InlineAlert tone="error">{carga.error}</InlineAlert>}
        {e?.mensaje && <InlineAlert tone={e.estado === "Terminada" && e.aprobada ? "success" : "warning"}>{e.mensaje}</InlineAlert>}
        {e?.estado === "Terminada" && (
          <p className="text-xs">
            {e.documentos} documentos · {e.campos} campos. En rojo, lo que no coincide con lo registrado.
          </p>
        )}
        {fallos.map((d) => (
          <InlineAlert key={d.documentoId} tone="warning" title={d.archivo}>
            {d.falloCandidata && <>Esta versión no lo leyó: {d.falloCandidata}. </>}
            {d.falloVigente && <>La publicada no lo leyó: {d.falloVigente}.</>}
          </InlineAlert>
        ))}
        {e?.estado === "Terminada" && (
          <DataTable
            ariaLabel="Campos que no coincidieron"
            rows={filas}
            getRowKey={(f) => f.clave}
            emptyMessage="Las dos versiones acertaron todos los campos."
            columns={[
              { key: "archivo", header: "Documento", render: (f) => <span className="text-[11px]">{f.archivo}</span> },
              { key: "campo", header: "Campo", render: (f) => ETIQUETA_CAMPO[f.campo] ?? f.campo },
              { key: "esperado", header: "Registrado", render: (f) => f.esperado ?? <span className="opacity-50">vacío</span> },
              { key: "candidata", header: `Versión ${e.versionNumero}`, render: (f) => <Valor v={f.candidata} ok={f.aciertaCandidata} /> },
              { key: "vigente", header: e.vigenteNumero ? `Publicada (v${e.vigenteNumero})` : "Publicada", render: (f) => <Valor v={f.vigente} ok={f.aciertaVigente} /> },
            ]}
          />
        )}
      </div>
    </Modal>
  );
}

function formatear(crudo: string): string {
  try {
    return JSON.stringify(JSON.parse(crudo), null, 2);
  } catch {
    return crudo;
  }
}

function Prompts() {
  const toast = useToast();
  const lista = useCarga("ocr-prompts", (signal) => apiFetch<Version[]>("/admin/ocr/prompts", { signal }));
  const [edicion, setEdicion] = useState<Edicion | null>(null);
  const [prueba, setPrueba] = useState<Version | null>(null);
  const [publicar, setPublicar] = useState<Version | null>(null);
  const [resultado, setResultado] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const versiones = lista.datos ?? [];
  const publicada = versiones.find((v) => v.publicada);
  const anterior = versionAnterior(versiones);
  useSondeo(versiones.some((v) => v.evaluacion?.estado === "EnCurso"), lista.recargar);

  const evaluar = async (v: Version) => {
    try {
      await apiFetch(`/admin/ocr/prompts/${v.id}/evaluar`, { method: "POST" });
      toast.show(`Evaluando la versión ${v.numeroVersion} contra la publicada. Tarda unos minutos.`, "success");
      lista.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo evaluar.", "error");
    }
  };

  const confirmarPublicar = async () => {
    if (!publicar) return;
    setOcupado(true);
    try {
      await apiFetch(`/admin/ocr/prompts/${publicar.id}/publicar`, { method: "POST" });
      toast.show(`Versión ${publicar.numeroVersion} publicada: los paquetes que se inicien desde ahora la usan.`, "success");
      setPublicar(null);
      lista.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo publicar.", "error");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Prompt de OCR"
        descripcion="Versiones del prompt con que el modelo lee los comparendos. Una versión nueva solo se publica si su evaluación no lee peor que la publicada; una publicada no se edita, y siempre se puede volver a la anterior."
        acciones={
          <div className="flex flex-wrap gap-2">
            {anterior && (
              <button type="button" className={controlCls(false)} onClick={() => setPublicar(anterior)}>
                <Undo2 className="h-3.5 w-3.5" aria-hidden />
                Volver a la versión {anterior.numeroVersion}
              </button>
            )}
            <button
              type="button"
              className={BOTON_PRIMARIO_CLS}
              onClick={() => setEdicion({ id: null, texto: publicada?.texto ?? "", modelo: publicada?.modelo ?? "" })}
            >
              <Plus className="h-3.5 w-3.5" aria-hidden />
              Nueva versión
            </button>
          </div>
        }
      />
      {publicada && (
        <p className="text-xs">
          Publicada: <b>versión {publicada.numeroVersion}</b> · {publicada.proveedor} / {publicada.modelo}
        </p>
      )}
      <DataTable
        ariaLabel="Versiones del prompt de OCR"
        rows={versiones}
        getRowKey={(v) => v.id}
        status={lista.datos ? "ready" : lista.estado}
        errorMessage={lista.error ?? undefined}
        onRetry={lista.recargar}
        emptyMessage="No hay versiones del prompt."
        renderExpanded={(v) => (
          <details className="px-3 pb-3 text-xs">
            <summary className="cursor-pointer font-semibold">Ver el texto</summary>
            <pre className="mt-2 max-h-72 overflow-auto whitespace-pre-wrap rounded-xl bg-[#F8FAFC] p-3 text-[11px] dark:bg-[#0B0F14]">{v.texto}</pre>
          </details>
        )}
        columns={[
          { key: "version", header: "Versión", align: "right", render: (v) => <span className="font-semibold">{v.numeroVersion}</span> },
          {
            key: "estado",
            header: "Estado",
            render: (v) => <Chip e={v.publicada ? { texto: "Publicada", tono: "success" } : v.retiradaEn ? { texto: "Retirada", tono: "neutral" } : { texto: "Borrador", tono: "neutral" }} />,
          },
          {
            key: "evaluacion",
            header: "Evaluación",
            render: (v) =>
              v.publicada || v.retiradaEn ? (
                <span className="opacity-50">—</span>
              ) : (
                <div className="flex flex-col items-start gap-0.5">
                  <Chip e={etiquetaEvaluacion(v.evaluacion)} title={v.evaluacion?.mensaje ?? undefined} />
                  {v.evaluacion && resumenEvaluacion(v.evaluacion) && <span className="text-[11px] tabular-nums opacity-70">{resumenEvaluacion(v.evaluacion)}</span>}
                </div>
              ),
          },
          { key: "proveedor", header: "Proveedor", render: (v) => v.proveedor },
          { key: "modelo", header: "Modelo", render: (v) => <span className="font-mono text-[11px]">{v.modelo}</span> },
          { key: "creada", header: "Creada", render: (v) => formatoFechaHora(v.creadoEn) },
          { key: "actualizada", header: "Actualizada", render: (v) => formatoFechaHora(v.actualizadoEn) },
          {
            key: "acciones",
            header: "",
            align: "right",
            render: (v) => (
              <div className="flex justify-end gap-1">
                <button type="button" className={controlCls(false)} onClick={() => setPrueba(v)}>
                  <FlaskConical className="h-3.5 w-3.5" aria-hidden />
                  Probar
                </button>
                {v.evaluacion && v.evaluacion.estado !== "EnCurso" && !v.publicada && !v.retiradaEn && (
                  <button type="button" className={controlCls(false)} onClick={() => setResultado(v.evaluacion!.id)}>
                    <ListChecks className="h-3.5 w-3.5" aria-hidden />
                    Resultado
                  </button>
                )}
                {!v.publicada && !v.retiradaEn && (
                  <>
                    <button type="button" className={controlCls(false)} onClick={() => setEdicion({ id: v.id, texto: v.texto, modelo: v.modelo })}>
                      <Pencil className="h-3.5 w-3.5" aria-hidden />
                      Editar
                    </button>
                    <button type="button" className={controlCls(false)} disabled={v.evaluacion?.estado === "EnCurso"} onClick={() => evaluar(v)}>
                      <Scale className="h-3.5 w-3.5" aria-hidden />
                      Evaluar
                    </button>
                  </>
                )}
                {!v.publicada && (
                  <button
                    type="button"
                    className={controlCls(false)}
                    disabled={!v.puedePublicar}
                    title={v.puedePublicar ? undefined : "Necesita una evaluación aprobada contra la versión publicada."}
                    onClick={() => setPublicar(v)}
                  >
                    {v.retiradaEn ? <Undo2 className="h-3.5 w-3.5" aria-hidden /> : <Send className="h-3.5 w-3.5" aria-hidden />}
                    {v.retiradaEn ? "Volver a publicar" : "Publicar"}
                  </button>
                )}
              </div>
            ),
          },
        ]}
      />

      {edicion && (
        <Editor
          inicial={edicion}
          onCerrar={() => setEdicion(null)}
          onGuardado={() => {
            setEdicion(null);
            lista.recargar();
          }}
        />
      )}
      {prueba && <Probar v={prueba} onCerrar={() => setPrueba(null)} />}
      {resultado && <ResultadoEvaluacion id={resultado} onCerrar={() => setResultado(null)} />}
      {publicar && (
        <Modal
          open
          onClose={() => setPublicar(null)}
          busy={ocupado}
          title={publicar.retiradaEn ? `Volver a la versión ${publicar.numeroVersion}` : `Publicar la versión ${publicar.numeroVersion}`}
          description={`Reemplaza a la versión ${publicada?.numeroVersion ?? "—"} para los paquetes que se inicien desde ahora. Los paquetes ya procesados conservan su versión.`}
          footer={
            <div className="flex justify-end gap-2">
              <button type="button" className={controlCls(false)} onClick={() => setPublicar(null)}>
                Cancelar
              </button>
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={confirmarPublicar}>
                {publicar.retiradaEn ? "Volver a esta versión" : "Publicar"}
              </button>
            </div>
          }
        >
          <p className="text-xs">
            {publicar.retiradaEn ? "Ya estuvo publicada, así que no necesita evaluación." : `${publicar.evaluacion?.mensaje ?? ""} Una versión publicada ya no se puede editar.`}
          </p>
        </Modal>
      )}
    </section>
  );
}

/** Prompt de OCR versionado, solo SuperAdmin (HU #13471 sobre la API de la HU #13469; §7.24). */
export default function PromptsOcr() {
  return (
    <Requiere superAdmin>
      <Prompts />
    </Requiere>
  );
}
