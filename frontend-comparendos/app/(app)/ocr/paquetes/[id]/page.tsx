"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { AlertTriangle, ArrowLeft, CheckCheck, Download, Play, RefreshCw, RotateCcw, Trash2 } from "lucide-react";
import { DataTable, InlineAlert, Modal, usePaginacion } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { DetalleComparendo } from "@/components/detalle/DetalleComparendo";
import { Contador, EstadoDocumento, Progreso, useSondeo } from "@/components/ocr/comunes";
import { OfertaPlacas } from "@/components/ocr/OfertaPlacas";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { Chip } from "@/components/ui/Chip";
import { BOTON_PELIGRO_CLS, BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { formatoFechaHora } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { type DocumentoPaquete, ESTADO_DOCUMENTO, ESTADO_PAQUETE, ESTADOS_DOCUMENTO, hayEnCurso, hojaPaquete, motivoDocumento, TIPO_FALLO } from "@/lib/ocr";
import { tienePermiso } from "@/lib/sesion";
import { useCarga } from "@/lib/useCarga";
import { buildXlsx, XLSX_MIME } from "@/lib/xlsx";

interface Paquete {
  id: string;
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
  creadoEn: string;
  procesamientoIniciadoEn: string | null;
  cerradoEn: string | null;
}

interface DocumentoFila extends DocumentoPaquete {
  id: string;
  tipoArchivo: string;
  intentos: number;
  procesadoEn: string | null;
  reprocesable: boolean;
}

interface ResultadoRegistro {
  registrados: { documentoId: string; numero: string; placa: string | null; placaYaMonitoreada: boolean }[];
  omitidos: { documentoId: string; nombreArchivo: string; codigo: string; motivo: string }[];
  placasPorMonitorear: string[];
}

function Detalle({ id }: { id: string }) {
  const router = useRouter();
  const toast = useToast();
  const { sesion } = useSesion();
  const opera = !sesion?.usuario.esSuperAdmin && tienePermiso(sesion, P.ocrGestionar);
  const [estado, setEstado] = useState("");
  const [ocupado, setOcupado] = useState(false);
  const [resultado, setResultado] = useState<ResultadoRegistro | null>(null);
  const [descartando, setDescartando] = useState(false);
  const [verComparendo, setVerComparendo] = useState<string | null>(null);
  const [motivo, setMotivo] = useState("Ilegible o no es un comparendo");
  const [reprocesando, setReprocesando] = useState(false);
  const pg = usePaginacion(25);
  const carga = useCarga(`ocr-paquete:${id}:${estado}`, (signal) =>
    apiFetch<{ paquete: Paquete; documentos: DocumentoFila[] }>(`/ocr/paquetes/${id}`, { signal, query: { estado } }),
  );
  const p = carga.datos?.paquete;
  const docs = carga.datos?.documentos ?? [];
  useSondeo(!!p && hayEnCurso([p.estado]), carga.recargar);

  const accion = async (hacer: () => Promise<void>) => {
    setOcupado(true);
    try {
      await hacer();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo completar la acción.", "error");
    } finally {
      setOcupado(false);
      carga.recargar();
    }
  };

  const iniciar = () =>
    accion(async () => {
      await apiFetch(`/ocr/paquetes/${id}/iniciar`, { method: "POST" });
      toast.show("Procesamiento iniciado.", "success");
    });

  const registrarCompletos = () =>
    accion(async () => {
      const r = await apiFetch<ResultadoRegistro>(`/ocr/paquetes/${id}/registrar-completos`, { method: "POST" });
      setResultado(r);
      toast.show(`${r.registrados.length} comparendos registrados${r.omitidos.length ? `, ${r.omitidos.length} omitidos` : ""}.`, r.omitidos.length ? "error" : "success");
    });

  const descartarFallidos = () =>
    accion(async () => {
      const r = await apiFetch<{ descartados: number }>(`/ocr/paquetes/${id}/descartar-fallidos`, { method: "POST", body: { motivo } });
      setDescartando(false);
      toast.show(`${r.descartados} documentos descartados.`, "success");
    });

  const reprocesarIncompletos = () =>
    accion(async () => {
      const r = await apiFetch<{ encolados: number; omitidos: number }>(`/ocr/paquetes/${id}/reprocesar-incompletos`, { method: "POST" });
      setReprocesando(false);
      toast.show(
        r.encolados === 1
          ? `1 documento volvió a la cola${r.omitidos ? `; ${r.omitidos} ya llegaron al máximo de reprocesos` : ""}.`
          : `${r.encolados} documentos volvieron a la cola${r.omitidos ? `; ${r.omitidos} ya llegaron al máximo de reprocesos` : ""}.`,
        r.encolados ? "success" : "error",
      );
    });

  // HU #13831: siempre el paquete completo, aunque la tabla esté filtrada por estado.
  const exportar = () =>
    accion(async () => {
      const todo = await apiFetch<{ paquete: Paquete; documentos: DocumentoFila[] }>(`/ocr/paquetes/${id}`);
      const url = URL.createObjectURL(new Blob([buildXlsx(hojaPaquete(todo.paquete, todo.documentos))], { type: XLSX_MIME }));
      const enlace = document.createElement("a");
      enlace.href = url;
      enlace.download = `paquete-ocr-${todo.paquete.creadoEn.slice(0, 10)}.xlsx`;
      enlace.click();
      URL.revokeObjectURL(url);
    });

  if (carga.estado === "error" && !p) return <InlineAlert tone="error">{carga.error}</InlineAlert>;
  if (!p) return <p className="text-sm opacity-60">Cargando paquete…</p>;
  const cerrado = p.estado === "Cerrado";
  // Con el filtro puesto la lista puede no traer los incompletos: el botón se guía por el contador del paquete.
  const reprocesables = estado && estado !== "Incompleto" ? p.cantidadIncompletos : docs.filter((d) => d.estado === "Incompleto" && d.reprocesable).length;

  return (
    <section className="flex flex-col gap-6">
      <Link href="/ocr" className="inline-flex items-center gap-1 text-xs font-semibold text-[#3B4FD6] hover:underline">
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden />
        Paquetes OCR
      </Link>
      <Encabezado
        titulo={p.nombre}
        descripcion={
          <span className="flex flex-wrap items-center gap-3">
            <Chip e={ESTADO_PAQUETE[p.estado] ?? { texto: p.estado, tono: "neutral" }} />
            <span>Creado {formatoFechaHora(p.creadoEn)}</span>
            {p.cerradoEn && <span>· Cerrado {formatoFechaHora(p.cerradoEn)} (solo lectura)</span>}
            <Progreso p={p} />
          </span>
        }
        acciones={
          <>
            <button type="button" className={controlCls(false)} onClick={carga.recargar}>
              <RefreshCw className="h-3.5 w-3.5" aria-hidden />
              Actualizar
            </button>
            <button type="button" className={controlCls(false)} disabled={ocupado} onClick={exportar}>
              <Download className="h-3.5 w-3.5" aria-hidden />
              Exportar
            </button>
            {opera && !cerrado && reprocesables > 0 && (
              <button type="button" className={controlCls(false)} disabled={ocupado} onClick={() => setReprocesando(true)}>
                <RotateCcw className="h-3.5 w-3.5" aria-hidden />
                Reprocesar incompletos
              </button>
            )}
            {opera && p.estado === "Pendiente" && (
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={iniciar}>
                <Play className="h-3.5 w-3.5" aria-hidden />
                Iniciar procesamiento
              </button>
            )}
            {opera && !cerrado && p.cantidadFallidos > 0 && (
              <button type="button" className={controlCls(false)} disabled={ocupado} onClick={() => setDescartando(true)}>
                <Trash2 className="h-3.5 w-3.5" aria-hidden />
                Descartar fallidos
              </button>
            )}
            {opera && !cerrado && p.cantidadCompletos > 0 && (
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={registrarCompletos}>
                <CheckCheck className="h-3.5 w-3.5" aria-hidden />
                {ocupado ? "Registrando…" : `Registrar los ${p.cantidadCompletos} completos`}
              </button>
            )}
          </>
        }
      />

      <div className="flex flex-wrap gap-2">
        <Contador etiqueta="Documentos" valor={p.cantidadDocumentos} />
        <Contador etiqueta="En cola" valor={p.cantidadPendientes} clase="text-[#3B4FD6]" />
        <Contador etiqueta="Completos" valor={p.cantidadCompletos} clase="text-[#1E7B3C]" />
        <Contador etiqueta="Incompletos" valor={p.cantidadIncompletos} clase="text-[#B45309]" />
        <Contador etiqueta="Fallidos" valor={p.cantidadFallidos} clase="text-[#B42318]" />
        <Contador etiqueta="Duplicados" valor={p.cantidadDuplicados} clase="text-[#6D28D9]" />
        <Contador etiqueta="Registrados" valor={p.cantidadRegistrados} />
        <Contador etiqueta="Descartados" valor={p.cantidadDescartados} />
      </div>

      {resultado && (
        <div className="flex flex-col gap-2">
          {resultado.omitidos.length > 0 && (
            <InlineAlert tone="warning" title={`${resultado.omitidos.length} documentos no se registraron`}>
              <ul className="list-disc pl-4">
                {resultado.omitidos.map((o) => (
                  <li key={o.documentoId}>
                    {o.nombreArchivo}: {o.motivo}
                  </li>
                ))}
              </ul>
            </InlineAlert>
          )}
          <OfertaPlacas placas={resultado.placasPorMonitorear} />
        </div>
      )}

      <div className="flex flex-col gap-2">
        <label className={`${ETIQUETA_CLS} w-56`}>
          Estado del documento
          <select
            className={INPUT_CLS}
            value={estado}
            onChange={(e) => {
              setEstado(e.target.value);
              pg.setPage(1);
            }}
          >
            <option value="">Todos</option>
            {ESTADOS_DOCUMENTO.map((e) => (
              <option key={e} value={e}>
                {ESTADO_DOCUMENTO[e].texto}
              </option>
            ))}
          </select>
        </label>
        <DataTable
          ariaLabel="Documentos del paquete"
          rows={pg.paginar(docs)}
          getRowKey={(d) => d.id}
          onRowClick={(d) => router.push(`/ocr/documentos/${d.id}`)}
          status={carga.estado === "loading" && !carga.datos ? "loading" : undefined}
          emptyMessage={estado ? "No hay documentos en ese estado." : "El paquete no tiene documentos."}
          minWidth={1000}
          columns={[
            { key: "archivo", header: "Archivo", render: (d) => <span className="font-semibold">{d.nombreArchivo}</span> },
            { key: "estado", header: "Estado", render: (d) => <EstadoDocumento estado={d.estado} /> },
            {
              key: "numero",
              header: "Número extraído",
              render: (d) => (
                <span className="flex flex-col">
                  {d.numeroComparendoExtraido ?? "—"}
                  {d.avisoNumero && (
                    <span className="inline-flex items-center gap-1 text-[11px] font-semibold text-[#B45309]" title={d.avisoNumero}>
                      <AlertTriangle className="h-3 w-3" aria-hidden />
                      Verificar número
                    </span>
                  )}
                </span>
              ),
            },
            {
              key: "detalle",
              header: "Faltantes / motivo",
              render: (d) => {
                const texto = motivoDocumento(d);
                if (!texto) return "—";
                const tono = d.estado === "Fallido" ? "text-[#B42318]" : d.estado === "Duplicado" ? "text-[#6D28D9]" : d.estado === "Descartado" ? "opacity-60" : "text-[#B45309]";
                return (
                  <span className={tono} title={d.estado === "Fallido" && d.tipoFallo ? TIPO_FALLO[d.tipoFallo] : undefined}>
                    {texto}
                  </span>
                );
              },
            },
            { key: "intentos", header: "Intentos", align: "right", render: (d) => d.intentos },
            {
              key: "acciones",
              header: "",
              align: "right",
              render: (d) =>
                d.comparendoId ? (
                  <button
                    type="button"
                    className="text-xs font-semibold text-[#3B4FD6] hover:underline"
                    onClick={(e) => {
                      e.stopPropagation();
                      setVerComparendo(d.comparendoId);
                    }}
                  >
                    Ver comparendo
                  </button>
                ) : (
                  <span className="text-xs font-semibold text-[#3B4FD6]">{opera && ["Completo", "Incompleto", "Duplicado", "Fallido"].includes(d.estado) ? "Revisar" : "Ver"}</span>
                ),
            },
          ]}
          pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: docs.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
        />
      </div>

      <DetalleComparendo id={verComparendo} onCerrar={() => setVerComparendo(null)} />

      {reprocesando && (
        <Modal
          open
          onClose={() => setReprocesando(false)}
          busy={ocupado}
          title="Reprocesar los documentos incompletos"
          description="Se vuelven a leer los documentos incompletos. Lo que ya corregiste a mano se conserva; cada documento se reprocesa máximo 3 veces."
          footer={
            <div className="flex justify-end gap-2">
              <button type="button" className={controlCls(false)} onClick={() => setReprocesando(false)}>
                Cancelar
              </button>
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={reprocesarIncompletos}>
                Reprocesar
              </button>
            </div>
          }
        >
          <p className="text-sm">Los que ya se reprocesaron 3 veces se omiten.</p>
        </Modal>
      )}

      {descartando && (
        <Modal
          open
          onClose={() => setDescartando(false)}
          busy={ocupado}
          title="Descartar los documentos fallidos"
          description={`Se descartan los ${p.cantidadFallidos} documentos fallidos del paquete. Quedan en el historial; no se borran.`}
          footer={
            <div className="flex justify-end gap-2">
              <button type="button" className={controlCls(false)} onClick={() => setDescartando(false)}>
                Cancelar
              </button>
              <button type="button" className={BOTON_PELIGRO_CLS} disabled={ocupado || !motivo.trim()} onClick={descartarFallidos}>
                Descartar
              </button>
            </div>
          }
        >
          <label className={ETIQUETA_CLS}>
            Motivo
            <input className={INPUT_CLS} value={motivo} maxLength={500} onChange={(e) => setMotivo(e.target.value)} />
          </label>
        </Modal>
      )}
    </section>
  );
}

/** Detalle de un paquete OCR (HU #13471; §7.6). */
export default function PaqueteOcr() {
  const { id } = useParams<{ id: string }>();
  return (
    <Requiere permiso={P.ocrVer}>
      <Detalle id={id} />
    </Requiere>
  );
}
