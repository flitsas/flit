"use client";

// HU #14076 AC1/AC5 — Preguntas frecuentes del borrador: tabla estándar con RowActions (editar, activar o desactivar,
// subir y bajar), orden editable, filtros por columna y los cuatro estados. Cada cambio avisa a la pestaña para
// refrescar el estado «borrador / publicado».
import { useCallback, useEffect, useMemo, useState } from "react";
import { ArrowDown, ArrowUp, HelpCircle, Pencil, Power } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { ActionsMenu } from "@/components/atom/ActionsMenu";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { EncabezadoFiltrable, TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { filtrarFilas, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";
import {
  actualizarFaqPn,
  crearFaqPn,
  establecerFaqActivaPn,
  fetchFaqsPn,
  ordenarFaqsPn,
  type FaqPn,
} from "@/lib/api/admin-pn-contenido";
import { PN_PRIMARY_BTN_CLS, PN_PRIMARY_BTN_STYLE } from "../pn-ui";
import { pnErrorMessage } from "../pnErrors";
import { PreguntaFormModal } from "./PreguntaFormModal";

const COLUMNAS_FILTRO: ColumnaFiltro[] = [
  { id: "pregunta", label: "Pregunta", tipo: "texto" },
  {
    id: "estado",
    label: "Estado",
    tipo: "seleccion",
    opciones: [
      { value: "activa", label: "Activa" },
      { value: "inactiva", label: "Inactiva" },
    ],
  },
];

/** Mueve el elemento `indice` una posición (`delta` ±1) y devuelve los ids en su orden nuevo. */
export function moverId(ids: readonly string[], indice: number, delta: -1 | 1): string[] {
  const destino = indice + delta;
  if (destino < 0 || destino >= ids.length) return [...ids];
  const copia = [...ids];
  [copia[indice], copia[destino]] = [copia[destino], copia[indice]];
  return copia;
}

function resumen(texto: string, max = 90): string {
  return texto.length > max ? `${texto.slice(0, max).trimEnd()}…` : texto;
}

export function PreguntasSection({ onCambio }: { onCambio: () => void }) {
  const { show } = useToast();
  const [faqs, setFaqs] = useState<FaqPn[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [form, setForm] = useState<{ faq?: FaqPn } | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const pg = usePaginacion("pn.contenido.preguntas");
  const { setPage } = pg;
  const volverAPrimeraPagina = useCallback(() => setPage(1), [setPage]);
  const filtros = useTablaFiltros({ columnas: COLUMNAS_FILTRO, onCambio: volverAPrimeraPagina });

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const list = await fetchFaqsPn(signal);
      if (signal?.aborted) return;
      setFaqs([...list].sort((a, b) => a.orden - b.orden));
      setStatus(list.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setStatus("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial: skeleton intencional
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const visibles = useMemo(
    () =>
      filtrarFilas(faqs, filtros.condicionesTotales, "", {
        columnas: { pregunta: (f) => [f.pregunta, f.respuesta], estado: (f) => (f.activa ? "activa" : "inactiva") },
      }),
    [faqs, filtros.condicionesTotales],
  );

  async function mover(faq: FaqPn, delta: -1 | 1) {
    const ids = faqs.map((f) => f.id);
    const nuevo = moverId(ids, ids.indexOf(faq.id), delta);
    setOcupado(true);
    try {
      await ordenarFaqsPn(nuevo);
      setFaqs((prev) => nuevo.map((id, i) => ({ ...(prev.find((f) => f.id === id) as FaqPn), orden: i + 1 })));
      onCambio();
    } catch (err) {
      show(pnErrorMessage(err), "error");
      void load();
    } finally {
      setOcupado(false);
    }
  }

  async function alternar(faq: FaqPn) {
    setOcupado(true);
    try {
      const actualizada = await establecerFaqActivaPn(faq.id, !faq.activa);
      setFaqs((prev) => prev.map((f) => (f.id === faq.id ? actualizada : f)));
      show(`Pregunta ${actualizada.activa ? "activada" : "desactivada"}.`, "success");
      onCambio();
    } catch (err) {
      show(pnErrorMessage(err), "error");
    } finally {
      setOcupado(false);
    }
  }

  function actionsFor(faq: FaqPn, indice: number): RowAction[] {
    return [
      { icon: Pencil, label: `Editar la pregunta «${resumen(faq.pregunta, 40)}»`, tone: "primary", onClick: () => setForm({ faq }) },
      {
        icon: Power,
        label: `${faq.activa ? "Desactivar" : "Activar"} la pregunta «${resumen(faq.pregunta, 40)}»`,
        tone: faq.activa ? "danger" : "primary",
        disabled: ocupado,
        onClick: () => void alternar(faq),
      },
      { icon: ArrowUp, label: `Subir la pregunta «${resumen(faq.pregunta, 40)}»`, disabled: ocupado || indice === 0, disabledTitle: "Ya es la primera", onClick: () => void mover(faq, -1) },
      {
        icon: ArrowDown,
        label: `Bajar la pregunta «${resumen(faq.pregunta, 40)}»`,
        disabled: ocupado || indice === faqs.length - 1,
        disabledTitle: "Ya es la última",
        onClick: () => void mover(faq, 1),
      },
    ];
  }

  const columns: DataTableColumn<FaqPn>[] = [
    { key: "orden", header: "Orden", cellClassName: "w-16 font-semibold", render: (f) => faqs.indexOf(f) + 1 },
    {
      key: "pregunta",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="pregunta">
          Pregunta
        </EncabezadoFiltrable>
      ),
      cellClassName: "max-w-[320px]",
      render: (f) => f.pregunta,
    },
    { key: "respuesta", header: "Respuesta", cellClassName: "max-w-[360px] text-xs text-[#59677D] dark:text-white/78", render: (f) => resumen(f.respuesta, 140) },
    {
      key: "estado",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="estado">
          Estado
        </EncabezadoFiltrable>
      ),
      render: (f) => <StatusBadge label={f.activa ? "Activa" : "Inactiva"} tone={f.activa ? "success" : "neutral"} />,
    },
    { key: "acciones", header: "Acciones", align: "right", render: (f) => <RowActions actions={actionsFor(f, faqs.indexOf(f))} /> },
  ];

  const nueva = () => setForm({});

  return (
    <section aria-labelledby="pn-faqs-titulo" className="flex flex-col gap-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 id="pn-faqs-titulo" className="text-base font-bold text-flit-primary dark:text-white">
            Preguntas frecuentes
          </h2>
          <p className="text-xs text-[#59677D] dark:text-white/70">
            Se publican en este orden. Las inactivas no aparecen en el portal.
          </p>
        </div>
        <ActionsMenu
          ariaLabel="Acciones de preguntas frecuentes"
          items={[{ key: "nueva", label: "Nueva pregunta", icon: HelpCircle, onSelect: nueva }]}
        />
      </div>
      {status === "loading" ? (
        <CarLoaderModal label="Cargando preguntas frecuentes…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="Aún no hay preguntas frecuentes. Cree la primera."
          emptyCta={
            <button type="button" onClick={nueva} className={PN_PRIMARY_BTN_CLS} style={PN_PRIMARY_BTN_STYLE}>
              Nueva pregunta
            </button>
          }
          errorMessage="No se pudieron cargar las preguntas frecuentes."
          onRetry={() => void load()}
        >
          <DataTable<FaqPn>
            ariaLabel="Preguntas frecuentes del portal"
            columns={columns}
            rows={pg.paginar(visibles)}
            getRowKey={(f) => f.id}
            emptyContent={<TablaFiltrosVacio onLimpiar={filtros.limpiar} />}
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: visibles.length,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
      )}

      {form && (
        <PreguntaFormModal
          faq={form.faq}
          onClose={() => setForm(null)}
          onSave={async (body) => {
            if (form.faq) {
              const guardada = await actualizarFaqPn(form.faq.id, body);
              setFaqs((prev) => prev.map((f) => (f.id === guardada.id ? guardada : f)));
            } else {
              const creada = await crearFaqPn(body);
              setFaqs((prev) => [...prev, creada]);
              setStatus("ready");
            }
            setForm(null);
            show("Pregunta guardada en el borrador.", "success");
            onCambio();
          }}
        />
      )}
    </section>
  );
}
