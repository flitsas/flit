"use client";

import { useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Plus, RefreshCw, Trash2, Upload } from "lucide-react";
import { DataTable, InlineAlert, Modal, usePaginacion } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { Contador, Progreso, useSondeo } from "@/components/ocr/comunes";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { useToast } from "@/components/Toast";
import { Chip } from "@/components/ui/Chip";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { formatoFechaHora } from "@/lib/format/date";
import { P } from "@/lib/nav";
import {
  agregarArchivos,
  armarPaquete,
  type ArchivoElegido,
  erroresPaquete,
  ESTADO_PAQUETE,
  ESTADOS_PAQUETE,
  hayEnCurso,
  MAXIMO_DOCUMENTOS,
} from "@/lib/ocr";
import { capacidades, tienePermiso } from "@/lib/sesion";
import { useCarga } from "@/lib/useCarga";

interface PaqueteResumen {
  id: string;
  tenantId: string;
  nombre: string;
  estado: string;
  cantidadDocumentos: number;
  cantidadPendientes: number;
  cantidadCompletos: number;
  cantidadIncompletos: number;
  cantidadFallidos: number;
  cantidadDuplicados: number;
  cantidadRegistrados: number;
  cantidadDescartados: number;
  procesados: number;
  creadoPor: string;
  creadoPorNombre: string | null;
  creadoEn: string;
  procesamientoIniciadoEn: string | null;
  cerradoEn: string | null;
}

interface ListaPaquetes {
  contadores: {
    paquetes: number;
    documentos: number;
    pendientes: number;
    completos: number;
    incompletos: number;
    fallidos: number;
    duplicados: number;
    registrados: number;
    descartados: number;
  } | null;
  items: PaqueteResumen[];
}

interface Periodo {
  periodo: string;
  procesados: number;
  completos: number;
  incompletos: number;
  fallidos: number;
  duplicados: number;
  descartados: number;
}

const estadoPaquete = (v: string) => ESTADO_PAQUETE[v] ?? { texto: v, tono: "neutral" as const };
const corto = (id: string) => id.slice(0, 8);

function NuevoPaquete({ onCerrar, onCreado }: { onCerrar: () => void; onCreado: (id: string) => void }) {
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const [nombre, setNombre] = useState("");
  const [elegidos, setElegidos] = useState<ArchivoElegido[]>([]);
  const [arrastrando, setArrastrando] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const errores = erroresPaquete(nombre, elegidos);
  const validos = elegidos.filter((a) => !a.error).length;
  const invalidos = elegidos.length - validos;

  const agregar = (lista: FileList | null) => lista && setElegidos((actuales) => agregarArchivos(actuales, Array.from(lista)));

  const crear = async (iniciar: boolean) => {
    setEnviando(true);
    setError(null);
    try {
      const r = await apiFetch<PaqueteResumen>("/ocr/paquetes", { method: "POST", body: armarPaquete(nombre, elegidos, iniciar) });
      toast.show(iniciar ? `Paquete creado con ${validos} archivos: ya se está procesando.` : "Paquete creado. Inícialo cuando quieras.", "success");
      onCreado(r.id);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo crear el paquete.");
    } finally {
      setEnviando(false);
    }
  };

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={enviando}
      size="xl"
      title="Nuevo paquete OCR"
      description={`Hasta ${MAXIMO_DOCUMENTOS} archivos JPG, PNG, TIFF o PDF de hasta 20 MB. Cada archivo es un comparendo (un PDF de varias páginas también).`}
      footer={
        <div className="flex flex-wrap justify-end gap-2">
          <button type="button" className={controlCls(false)} disabled={enviando || errores.length > 0} onClick={() => crear(false)}>
            Crear sin iniciar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={enviando || errores.length > 0} onClick={() => crear(true)}>
            {enviando ? "Subiendo…" : "Iniciar procesamiento"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-4">
        <label className={ETIQUETA_CLS}>
          Nombre del paquete
          <input className={INPUT_CLS} value={nombre} maxLength={150} placeholder="p. ej. Comparendos físicos octubre" onChange={(e) => setNombre(e.target.value)} />
        </label>
        <div
          role="button"
          tabIndex={0}
          aria-label="Agregar archivos"
          onClick={() => input.current?.click()}
          onKeyDown={(e) => (e.key === "Enter" || e.key === " ") && input.current?.click()}
          onDragOver={(e) => {
            e.preventDefault();
            setArrastrando(true);
          }}
          onDragLeave={() => setArrastrando(false)}
          onDrop={(e) => {
            e.preventDefault();
            setArrastrando(false);
            agregar(e.dataTransfer.files);
          }}
          className={`flex cursor-pointer flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed px-4 py-8 text-center text-xs transition ${
            arrastrando ? "border-[#557EFF] bg-[#EFF6FF] dark:bg-[#557EFF]/10" : "border-[#DFE5ED] dark:border-white/15"
          }`}
        >
          <Upload className="h-5 w-5 text-[#557EFF]" aria-hidden />
          <span className="font-semibold">Arrastra los archivos aquí o haz clic para elegirlos</span>
          <span className="opacity-60">JPG, PNG, TIFF o PDF · máximo 20 MB cada uno</span>
          <input
            ref={input}
            type="file"
            multiple
            className="hidden"
            accept=".jpg,.jpeg,.png,.tif,.tiff,.pdf,image/jpeg,image/png,image/tiff,application/pdf"
            onChange={(e) => {
              agregar(e.target.files);
              e.target.value = "";
            }}
          />
        </div>
        {elegidos.length > 0 && (
          <div className="flex flex-col gap-2">
            <div className="flex items-center justify-between text-xs">
              <span>
                <b>{validos}</b> archivos listos{invalidos > 0 && <> · <b className="text-[#B42318]">{invalidos}</b> rechazados</>}
              </span>
              <button type="button" className="text-xs font-semibold text-[#3B4FD6] hover:underline" onClick={() => setElegidos([])}>
                Quitar todos
              </button>
            </div>
            <ul className="max-h-60 overflow-y-auto rounded-xl border border-[#DFE5ED] text-xs dark:border-white/15">
              {elegidos.map((a, i) => (
                <li key={`${a.archivo.name}-${a.archivo.size}`} className="flex items-center justify-between gap-3 border-b border-[#EEF1F6] px-3 py-1.5 last:border-0 dark:border-white/10">
                  <span className={`truncate ${a.error ? "text-[#B42318]" : ""}`}>
                    {a.archivo.name}
                    <span className="ml-2 opacity-60">{(a.archivo.size / 1024 / 1024).toFixed(1)} MB</span>
                    {a.error && <span className="ml-2 font-semibold">· {a.error}</span>}
                  </span>
                  <button type="button" aria-label={`Quitar ${a.archivo.name}`} onClick={() => setElegidos((l) => l.filter((_, j) => j !== i))}>
                    <Trash2 className="h-3.5 w-3.5 opacity-60 hover:opacity-100" aria-hidden />
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}
        {error && <InlineAlert tone="error">{error}</InlineAlert>}
      </div>
    </Modal>
  );
}

function Estadisticas({ empresa }: { empresa: string | null }) {
  const [desde, setDesde] = useState("");
  const [hasta, setHasta] = useState("");
  const datos = useCarga(`ocr-estadisticas:${empresa}:${desde}:${hasta}`, (signal) =>
    apiFetch<Periodo[]>("/ocr/estadisticas", { signal, query: { empresa, desde, hasta } }),
  );
  const filas = datos.datos ?? [];
  const tasa = (p: Periodo) => (p.procesados === 0 ? "—" : `${Math.round((p.completos / p.procesados) * 100)} %`);

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h2 className="text-sm font-semibold">Estadísticas de extracción por mes</h2>
        <div className="flex flex-wrap items-end gap-2">
          <label className={ETIQUETA_CLS}>
            Desde
            <input type="date" className={INPUT_CLS} value={desde} onChange={(e) => setDesde(e.target.value)} />
          </label>
          <label className={ETIQUETA_CLS}>
            Hasta
            <input type="date" className={INPUT_CLS} value={hasta} onChange={(e) => setHasta(e.target.value)} />
          </label>
        </div>
      </div>
      <DataTable
        ariaLabel="Estadísticas OCR por mes"
        rows={filas}
        getRowKey={(p) => p.periodo}
        status={datos.estado}
        errorMessage={datos.error ?? undefined}
        onRetry={datos.recargar}
        emptyMessage="Todavía no hay documentos procesados en ese periodo."
        columns={[
          { key: "periodo", header: "Mes", render: (p) => p.periodo },
          { key: "procesados", header: "Procesados", align: "right", render: (p) => p.procesados },
          { key: "completos", header: "Completos", align: "right", render: (p) => p.completos },
          { key: "incompletos", header: "Incompletos", align: "right", render: (p) => p.incompletos },
          { key: "fallidos", header: "Fallidos", align: "right", render: (p) => p.fallidos },
          { key: "duplicados", header: "Duplicados", align: "right", render: (p) => p.duplicados },
          { key: "descartados", header: "Descartados", align: "right", render: (p) => p.descartados },
          { key: "tasa", header: "Completos al primer intento", align: "right", render: tasa },
        ]}
      />
    </div>
  );
}

function Paquetes() {
  const router = useRouter();
  const { sesion } = useSesion();
  const esSuper = !!sesion?.usuario.esSuperAdmin;
  const puedeCrear = !esSuper && tienePermiso(sesion, P.ocrGestionar);
  const [empresa, setEmpresa] = useState<string | null>(null);
  const [estado, setEstado] = useState("");
  const [desde, setDesde] = useState("");
  const [hasta, setHasta] = useState("");
  const [creando, setCreando] = useState(false);
  const pg = usePaginacion(25);
  const lista = useCarga(`ocr-paquetes:${empresa}:${estado}:${desde}:${hasta}`, (signal) =>
    apiFetch<ListaPaquetes>("/ocr/paquetes", { signal, query: { empresa, estado, desde, hasta } }),
  );
  const items = lista.datos?.items ?? [];
  const c = lista.datos?.contadores;
  useSondeo(hayEnCurso(items.map((p) => p.estado)), lista.recargar);

  if (!esSuper && !capacidades(sesion).ocr)
    return (
      <InlineAlert tone="warning" title="Carga por OCR no habilitada">
        Tu empresa no tiene habilitada la carga por OCR. Pídela a FLIT.
      </InlineAlert>
    );

  const filtrar = (f: () => void) => {
    f();
    pg.setPage(1);
  };

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Paquetes OCR"
        descripcion="Carga comparendos físicos en lotes: un modelo de lenguaje lee cada archivo, tú revisas lo extraído y lo registras."
        acciones={
          <>
            <button type="button" className={controlCls(false)} onClick={lista.recargar}>
              <RefreshCw className="h-3.5 w-3.5" aria-hidden />
              Actualizar
            </button>
            {puedeCrear && (
              <button type="button" className={BOTON_PRIMARIO_CLS} onClick={() => setCreando(true)}>
                <Plus className="h-3.5 w-3.5" aria-hidden />
                Nuevo paquete
              </button>
            )}
          </>
        }
      />

      {esSuper && (
        <div className="flex flex-col gap-1">
          <SelectorEmpresa valor={empresa} onCambio={(id) => filtrar(() => setEmpresa(id))} />
          <span className="text-[11px] opacity-60">Sin empresa elegida ves los paquetes de todas. Como administrador de FLIT solo lees.</span>
        </div>
      )}

      <div className="flex flex-wrap gap-2">
        <Contador etiqueta="Paquetes" valor={c?.paquetes} />
        <Contador etiqueta="Documentos" valor={c?.documentos} />
        <Contador etiqueta="En cola" valor={c?.pendientes} clase="text-[#3B4FD6]" />
        <Contador etiqueta="Completos" valor={c?.completos} clase="text-[#1E7B3C]" />
        <Contador etiqueta="Incompletos" valor={c?.incompletos} clase="text-[#B45309]" />
        <Contador etiqueta="Fallidos" valor={c?.fallidos} clase="text-[#B42318]" />
        <Contador etiqueta="Duplicados" valor={c?.duplicados} clase="text-[#6D28D9]" />
        <Contador etiqueta="Registrados" valor={c?.registrados} />
      </div>

      <div className="flex flex-col gap-2">
        <div className="flex flex-wrap items-end gap-2">
          <label className={ETIQUETA_CLS}>
            Estado
            <select className={`${INPUT_CLS} w-48`} value={estado} onChange={(e) => filtrar(() => setEstado(e.target.value))}>
              <option value="">Todos</option>
              {ESTADOS_PAQUETE.map((e) => (
                <option key={e} value={e}>
                  {estadoPaquete(e).texto}
                </option>
              ))}
            </select>
          </label>
          <label className={ETIQUETA_CLS}>
            Creado desde
            <input type="date" className={INPUT_CLS} value={desde} onChange={(e) => filtrar(() => setDesde(e.target.value))} />
          </label>
          <label className={ETIQUETA_CLS}>
            Hasta
            <input type="date" className={INPUT_CLS} value={hasta} onChange={(e) => filtrar(() => setHasta(e.target.value))} />
          </label>
        </div>
        <DataTable
          ariaLabel="Paquetes OCR"
          rows={pg.paginar(items)}
          getRowKey={(p) => p.id}
          onRowClick={(p) => router.push(`/ocr/paquetes/${p.id}`)}
          status={lista.estado === "loading" && !lista.datos ? "loading" : lista.estado === "error" ? "error" : undefined}
          errorMessage={lista.error ?? undefined}
          onRetry={lista.recargar}
          emptyMessage={puedeCrear ? "Todavía no hay paquetes. Crea el primero con «Nuevo paquete»." : "No hay paquetes con esos filtros."}
          minWidth={1100}
          columns={[
            { key: "nombre", header: "Paquete", render: (p) => <span className="font-semibold">{p.nombre}</span> },
            { key: "fecha", header: "Creado", render: (p) => formatoFechaHora(p.creadoEn) },
            { key: "creador", header: "Creador", render: (p) => (p.creadoPorNombre ? <span title={p.creadoPor}>{p.creadoPorNombre}</span> : <span title={p.creadoPor} className="font-mono text-[11px] opacity-70">{corto(p.creadoPor)}</span>) },
            { key: "estado", header: "Estado", render: (p) => <Chip e={estadoPaquete(p.estado)} /> },
            { key: "progreso", header: "Progreso", render: (p) => <Progreso p={p} /> },
            { key: "completos", header: "Completos", align: "right", render: (p) => p.cantidadCompletos },
            { key: "incompletos", header: "Incompletos", align: "right", render: (p) => p.cantidadIncompletos },
            { key: "fallidos", header: "Fallidos", align: "right", render: (p) => p.cantidadFallidos },
            { key: "duplicados", header: "Duplicados", align: "right", render: (p) => p.cantidadDuplicados },
            { key: "registrados", header: "Registrados", align: "right", render: (p) => p.cantidadRegistrados },
          ]}
          pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: items.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
        />
      </div>

      <Estadisticas empresa={empresa} />

      {creando && <NuevoPaquete onCerrar={() => setCreando(false)} onCreado={(id) => router.push(`/ocr/paquetes/${id}`)} />}
    </section>
  );
}

/** Listado de paquetes OCR (HU #13471 sobre la API de la HU #13469; §7.6). */
export default function Ocr() {
  return (
    <Requiere permiso={P.ocrVer}>
      <Paquetes />
    </Requiere>
  );
}
