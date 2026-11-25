"use client";

import { useCallback, useState } from "react";
import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { Icono } from "@flit/ui/Icono";
import { ChipsFiltro, DataTable, InlineAlert, Miga, Modal, TarjetaMetrica, usePaginacion, VehicleLoader } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { useAbrirDetalle } from "@/components/detalle/useAbrirDetalle";
import { COLOR_CIFRA_DOCUMENTO, CONTADORES_GRID_CLS, EstadoDocumento, Progreso, useSondeo } from "@/components/ocr/comunes";
import { AsociarLote } from "@/components/ocr/AsociarLote";
import { FranjaLote } from "@/components/ocr/FranjaLote";
import { OfertaPlacas } from "@/components/ocr/OfertaPlacas";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { Chip } from "@/components/ui/Chip";
import { BOTON_PELIGRO_CLS, BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { formatoFechaHora } from "@/lib/format/date";
import { P } from "@/lib/nav";
import {
  AVISO_SIN_DOCUMENTOS_LOTE,
  avisoLoteEncolado,
  cerrarResumenLote,
  colaReemplazos,
  type DocumentoPaquete,
  ESTADO_DOCUMENTO,
  ESTADO_PAQUETE,
  etiquetaAsociarLote,
  hayEnCurso,
  hojaPaquete,
  motivoDocumento,
  registroEnLote,
  resumenCola,
  resumenLoteCerrado,
  TIPO_FALLO,
  urlCola,
  type VistaPreviaLote,
} from "@/lib/ocr";
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
  cantidadAsociados?: number;
  /** §10: documentos que el lote está asociando en segundo plano. */
  cantidadPorAsociar?: number;
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
  const params = useSearchParams();
  // El filtro vive en la URL (?estado=) para que «atrás» desde un documento vuelva con el mismo filtro.
  const estado = params.get("estado") ?? "";
  const setEstado = (e: string) => router.replace(e ? `/ocr/paquetes/${id}?estado=${encodeURIComponent(e)}` : `/ocr/paquetes/${id}`, { scroll: false });
  const [ocupado, setOcupado] = useState(false);
  const [resultado, setResultado] = useState<ResultadoRegistro | null>(null);
  const [descartando, setDescartando] = useState(false);
  const setVerComparendo = useAbrirDetalle();
  const [motivo, setMotivo] = useState("Ilegible o no es un comparendo");
  const [reprocesando, setReprocesando] = useState(false);
  const pg = usePaginacion(25);
  const carga = useCarga(`ocr-paquete:${id}:${estado}`, (signal) =>
    apiFetch<{ paquete: Paquete; documentos: DocumentoFila[]; completosPorConfirmar?: number }>(`/ocr/paquetes/${id}`, { signal, query: { estado } }),
  );
  const p = carga.datos?.paquete;
  const docs = carga.datos?.documentos ?? [];
  useSondeo(!!p && hayEnCurso([p.estado]), carga.recargar);
  // GDC-EVID §9/§10: vista previa del lote (la preselección es del servidor) y el último lote del paquete (avance o
  // resumen). Se pide siempre que haya paquete: el resumen sale aunque el paquete ya esté cerrado o sin duplicados. La
  // clave cambia con los contadores, así se recalcula tras asociar, descartar o reemplazar.
  // Decisión de Samuel (2026-11-25): también en paquetes CERRADOS. Un paquete en el que todo quedó Duplicado se cierra solo
  // al terminar la lectura, y ese es justo el caso principal del lote (como ya se puede asociar uno por uno en un cerrado).
  const conLote = !!p && opera && p.cantidadDuplicados > 0;
  const vista = useCarga(p ? `ocr-lote:${id}:${p.cantidadDuplicados}:${p.cantidadAsociados ?? 0}:${p.cantidadDescartados}` : null, (signal) =>
    apiFetch<VistaPreviaLote>(`/ocr/paquetes/${id}/asociacion-lote`, { signal }),
  );
  const [asociandoLote, setAsociandoLote] = useState(false);
  const enCurso = vista.datos?.enCurso ?? null;
  const loteAvanzando = (enCurso?.porAsociar ?? 0) > 0;
  const recargarCargaYLote = useCallback(() => {
    carga.recargar();
    vista.recargar();
  }, [carga.recargar, vista.recargar]); // eslint-disable-line react-hooks/exhaustive-deps
  // Solo se consulta seguido mientras hay un lote avanzando (según el lote o el contador del paquete); sin lote no hay sondeo.
  const porAsociar = p?.cantidadPorAsociar ?? 0;
  useSondeo(loteAvanzando || porAsociar > 0, recargarCargaYLote);
  const [resumenesCerrados, setResumenesCerrados] = useState<string[]>([]);
  const verResumen = !!enCurso && (loteAvanzando || !(resumenesCerrados.includes(enCurso.loteId) || resumenLoteCerrado(enCurso.loteId)));
  const resumen = resumenCola(params);

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

  // Cola de reemplazos: siempre con el paquete completo (la tabla puede estar filtrada) y en el orden del listado.
  const revisarReemplazos = () =>
    accion(async () => {
      const todo = await apiFetch<{ documentos: DocumentoFila[] }>(`/ocr/paquetes/${id}`);
      const cola = colaReemplazos(todo.documentos);
      if (cola.length === 0) {
        toast.show("No quedan reemplazos por revisar.", "success");
        return;
      }
      router.push(urlCola(cola[0], { paquete: id, hechos: 0, total: cola.length, reemplazados: 0, descartados: 0 }));
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
  if (!p) return <VehicleLoader label="Cargando paquete…" />;
  const cerrado = p.estado === "Cerrado";
  // Con el filtro puesto la lista puede no traer los incompletos: el botón se guía por el contador del paquete.
  // GDC punto 14: los completos con el número por verificar no entran en el registro en lote.
  const lote = registroEnLote(p.cantidadCompletos, carga.datos?.completosPorConfirmar ?? 0);
  const filtrar = (e: string) => {
    setEstado(e);
    pg.setPage(1);
  };
  const enCola = estado === "Pendiente" || estado === "EnProcesamiento";
  const FILTROS: [string, string, number][] = [
    ["Completo", "Completos", p.cantidadCompletos],
    ["Incompleto", "Incompletos", p.cantidadIncompletos],
    ["Fallido", "Fallidos", p.cantidadFallidos],
    ["Duplicado", "Duplicados", p.cantidadDuplicados],
    ["Registrado", "Registrados", p.cantidadRegistrados],
    ["Asociado", "Asociados", p.cantidadAsociados ?? 0],
    // §10: mientras un lote avanza, los documentos encolados se pueden ver aparte (contador del propio paquete, así no depende
    // de la consulta del lote).
    ...(porAsociar > 0 ? [["PorAsociar", "Asociando", porAsociar] as [string, string, number]] : []),
    ["Descartado", "Descartados", p.cantidadDescartados],
  ];
  const reprocesables = estado && estado !== "Incompleto" ? p.cantidadIncompletos : docs.filter((d) => d.estado === "Incompleto" && d.reprocesable).length;

  return (
    <section className="flex flex-col gap-6">
      <Miga
        items={[{ etiqueta: "Paquetes OCR", href: "/ocr" }, { etiqueta: p.nombre }]}
        enlace={({ href, className, children }) => (
          <Link href={href} className={className}>
            {children}
          </Link>
        )}
      />
      <Encabezado
        titulo={p.nombre}
        descripcion={
          <span className="flex flex-wrap items-center gap-3">
            <Chip e={ESTADO_PAQUETE[p.estado] ?? { texto: p.estado, tono: "neutral" }} />
            <span>Creado {formatoFechaHora(p.creadoEn)}</span>
            {/* «Solo lectura» ya no es cierto si quedan duplicados por asociar: se puede asociar aunque esté cerrado. */}
            {p.cerradoEn && (
              <span>
                · Cerrado {formatoFechaHora(p.cerradoEn)}
                {conLote ? "" : " (solo lectura)"}
              </span>
            )}
            <Progreso p={p} />
          </span>
        }
        acciones={
          <>
            <button type="button" className={controlCls(false)} onClick={carga.recargar}>
              <Icono nombre="reintento" className="h-3.5 w-3.5" />
              Actualizar
            </button>
            <button type="button" className={controlCls(false)} disabled={ocupado} onClick={exportar}>
              <Icono nombre="nube-bajar" className="h-3.5 w-3.5" />
              Exportar
            </button>
            {opera && !cerrado && reprocesables > 0 && (
              <button type="button" className={controlCls(false)} disabled={ocupado} onClick={() => setReprocesando(true)}>
                <Icono nombre="reintento" className="h-3.5 w-3.5" />
                Reprocesar incompletos
              </button>
            )}
            {opera && p.estado === "Pendiente" && (
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={iniciar}>
                <Icono nombre="avance" className="h-3.5 w-3.5" />
                Iniciar procesamiento
              </button>
            )}
            {opera && !cerrado && p.cantidadFallidos > 0 && (
              <button type="button" className={controlCls(false)} disabled={ocupado} onClick={() => setDescartando(true)}>
                <Icono nombre="papelera" className="h-3.5 w-3.5" />
                Descartar fallidos
              </button>
            )}
            {conLote && (vista.datos?.porRevisar.reemplazos ?? 0) > 0 && (
              <button type="button" className={controlCls(false)} disabled={ocupado} onClick={revisarReemplazos}>
                <Icono nombre="documento" className="h-3.5 w-3.5" />
                Revisar reemplazos ({vista.datos!.porRevisar.reemplazos})
              </button>
            )}
            {/* Un solo primario por zona: con completos por registrar, «Registrar» es el primario y «Asociar» secundario. Mientras
                carga la vista previa se reserva el lugar con el botón deshabilitado, para que la fila no salte. */}
            {conLote && !vista.datos && vista.estado === "loading" && (
              <button type="button" className={lote.registrables > 0 ? controlCls(false) : BOTON_PRIMARIO_CLS} disabled>
                <Icono nombre="documento" className="h-3.5 w-3.5" />
                Calculando…
              </button>
            )}
            {conLote && (vista.datos?.asociables ?? 0) > 0 && (
              <button type="button" className={lote.registrables > 0 ? controlCls(false) : BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={() => setAsociandoLote(true)}>
                <Icono nombre="documento" className="h-3.5 w-3.5" />
                {etiquetaAsociarLote(vista.datos!.asociables)}
              </button>
            )}
            {opera && !cerrado && lote.registrables > 0 && (
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={registrarCompletos}>
                <Icono nombre="doble-check" className="h-3.5 w-3.5" />
                {ocupado ? "Registrando…" : lote.registrables === 1 ? "Registrar el completo" : `Registrar los ${lote.registrables} completos`}
              </button>
            )}
          </>
        }
      />

      <div className="flex flex-col gap-2">
        <p className="text-xs text-muted-foreground">Toca una cifra para ver solo esos documentos; tócala de nuevo para verlos todos.</p>
        <div className={CONTADORES_GRID_CLS} role="group" aria-label="Filtrar documentos por estado">
          <TarjetaMetrica etiqueta="Documentos" cifra={p.cantidadDocumentos} activa={estado === ""} onAlternar={() => filtrar("")} />
          <TarjetaMetrica
            etiqueta="En cola"
            cifra={p.cantidadPendientes}
            pendiente={p.cantidadPendientes > 0}
            pie={p.cantidadPendientes > 0 ? "Se actualiza sola" : undefined}
            activa={enCola}
            onAlternar={(siguiente) => filtrar(siguiente ? "Pendiente" : "")}
          />
          {FILTROS.map(([valor, etiqueta, cifra]) => (
            <TarjetaMetrica
              key={valor}
              etiqueta={etiqueta}
              cifra={cifra}
              colorCifra={COLOR_CIFRA_DOCUMENTO[valor]}
              activa={estado === valor}
              onAlternar={(siguiente) => filtrar(siguiente ? valor : "")}
            />
          ))}
        </div>
        {/* «En cola» suma dos estados y el filtro acepta uno: al activarla se elige cuál. */}
        {enCola && (
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs font-semibold text-muted-foreground">En cola:</span>
            <ChipsFiltro
              ariaLabel="Documentos en cola"
              valor={estado}
              onChange={filtrar}
              opciones={[
                { valor: "Pendiente", etiqueta: "Pendiente" },
                { valor: "EnProcesamiento", etiqueta: "En procesamiento" },
              ]}
            />
          </div>
        )}
      </div>

      {opera && !cerrado && lote.aviso && (
        <InlineAlert tone="warning" title="Hay números por verificar">
          {lote.aviso}
        </InlineAlert>
      )}

      {verResumen && enCurso && (
        <FranjaLote
          lote={enCurso}
          onVer={filtrar}
          onCerrar={() => {
            cerrarResumenLote(enCurso.loteId);
            setResumenesCerrados((c) => [...c, enCurso.loteId]);
          }}
        />
      )}

      {resumen && (
        <InlineAlert tone="success" title="Revisión de reemplazos">
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
            {resumen}
            <button type="button" className="font-semibold text-flit-brand-ink hover:underline" onClick={() => router.replace(`/ocr/paquetes/${id}`, { scroll: false })}>
              Cerrar
            </button>
          </span>
        </InlineAlert>
      )}

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
        <DataTable
          ariaLabel={estado ? `Documentos del paquete: ${ESTADO_DOCUMENTO[estado]?.texto ?? estado}` : "Documentos del paquete"}
          rows={pg.paginar(docs)}
          getRowKey={(d) => d.id}
          onRowClick={(d) => router.push(`/ocr/documentos/${d.id}`)}
          status={carga.estado === "loading" && !carga.datos ? "loading" : undefined}
          emptyMessage={estado ? "No hay documentos en ese estado. Toca «Documentos» para verlos todos." : "El paquete no tiene documentos."}
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
                  {d.avisoNumero && d.numeroPorConfirmar !== false && (
                    <span className="inline-flex items-center gap-1 text-xs font-semibold text-[var(--badge-warning-fg)]" title={d.avisoNumero}>
                      <Icono nombre="alerta" className="h-3 w-3" />
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
                const tono = d.estado === "Fallido" ? "text-destructive" : d.estado === "Duplicado" ? "text-[var(--badge-purple-fg)]" : d.estado === "Descartado" ? "text-muted-foreground" : d.estado === "Asociado" ? "text-[var(--badge-info-fg)]" : "text-[var(--badge-warning-fg)]";
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
                    className="text-xs font-semibold text-flit-brand-ink hover:underline"
                    onClick={(e) => {
                      e.stopPropagation();
                      setVerComparendo(d.comparendoId);
                    }}
                  >
                    Ver comparendo
                  </button>
                ) : (
                  <span className="text-xs font-semibold text-flit-brand-ink">{opera && ["Completo", "Incompleto", "Duplicado", "Fallido"].includes(d.estado) ? "Revisar" : "Ver"}</span>
                ),
            },
          ]}
          pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: docs.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
        />
      </div>


      {asociandoLote && vista.datos && (
        <AsociarLote
          paqueteId={id}
          vista={vista.datos}
          onCerrar={() => setAsociandoLote(false)}
          onError={(m) => toast.show(m, "error")}
          onEncolado={async (r) => {
            setAsociandoLote(false);
            recargarCargaYLote();
            if (r.encolados > 0) {
              toast.show(avisoLoteEncolado(r.encolados), "success");
              return;
            }
            // No encoló nada (doble clic o ya no había elegibles): si otro clic ya creó el lote, lo muestra la franja.
            try {
              const v = await apiFetch<VistaPreviaLote>(`/ocr/paquetes/${id}/asociacion-lote`);
              if (!v.enCurso) toast.show(AVISO_SIN_DOCUMENTOS_LOTE, "warning");
            } catch {
              toast.show(AVISO_SIN_DOCUMENTOS_LOTE, "warning");
            }
          }}
        />
      )}

      {reprocesando && (
        <Modal icono="reintento"
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
        <Modal icono="papelera"
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
