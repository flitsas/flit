"use client";

// HU #14076 AC1/AC5 — Preguntas frecuentes del borrador: tabla estándar con RowActions (editar, activar o desactivar,
// subir y bajar), orden editable, filtros por columna y los cuatro estados. Cada cambio avisa a la pestaña para
// refrescar el estado «borrador / publicado».
import { useCallback, useEffect, useMemo, useState } from "react";
import { ArrowDown, ArrowUp, EyeOff, HelpCircle, Pencil } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CreateButton } from "@/components/atom/CreateButton";
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
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import { PnConfirmDialog, PnSeccionHeader } from "../pn-componentes";
import { pnErrorMessage } from "../pnErrors";
import { PreguntaFormModal } from "./PreguntaFormModal";

const COLUMNAS_FILTRO: ColumnaFiltro[] = [
  { id: "pregunta", label: "Pregunta", tipo: "texto" },
  {
    id: "estado",
    label: "Estado",
    tipo: "seleccion",
    opciones: [
      { value: "activa", label: "Visible" },
      { value: "inactiva", label: "Oculta" },
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

function plural(n: number, uno: string, varios: string): string {
  return `${n} ${n === 1 ? uno : varios}`;
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
  const [ocultando, setOcultando] = useState<FaqPn | null>(null);
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

  async function alternar(faq: FaqPn, propagar = false) {
    setOcupado(true);
    try {
      const actualizada = await establecerFaqActivaPn(faq.id, !faq.activa);
      setFaqs((prev) => prev.map((f) => (f.id === faq.id ? actualizada : f)));
      show(`Pregunta ${actualizada.activa ? "visible" : "oculta"}. El ciudadano verá el cambio cuando publiques.`, "success");
      onCambio();
    } catch (err) {
      if (propagar) throw err;
      show(pnErrorMessage(err), "error");
    } finally {
      setOcupado(false);
    }
  }

  function actionsFor(faq: FaqPn, indice: number): RowAction[] {
    return [
      { icon: Pencil, label: `Editar la pregunta «${resumen(faq.pregunta, 40)}»`, tone: "primary", onClick: () => setForm({ faq }) },
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
      render: (f) => (
        <div className="flex items-start gap-2">
          <SwitchToggle
            checked={f.activa}
            disabled={ocupado}
            label={`${f.activa ? "Ocultar" : "Mostrar"} la pregunta «${resumen(f.pregunta, 40)}» en el portal`}
            onChange={(next) => (next ? void alternar(f) : setOcultando(f))}
          />
          <StatusBadge label={f.activa ? "Visible" : "Oculta"} tone={f.activa ? "success" : "neutral"} />
        </div>
      ),
    },
    { key: "acciones", header: "Acciones", align: "right", render: (f) => <RowActions actions={actionsFor(f, faqs.indexOf(f))} /> },
  ];

  const nueva = () => setForm({});

  return (
    <section aria-labelledby="pn-faqs-titulo" className="flex flex-col gap-3">
      <PnSeccionHeader
        id="pn-faqs-titulo"
        contador={status === "ready" || status === "empty" ? `${plural(faqs.length, "pregunta", "preguntas")} · ${plural(faqs.filter((f) => f.activa).length, "visible", "visibles")}` : undefined}
        titulo="Preguntas frecuentes"
        descripcion="Al empezar, aquí están las preguntas que el portal ya muestra al ciudadano. Se muestran en este orden (usa las flechas para subir o bajar); las ocultas no aparecen en el portal."
        accion={<CreateButton label="Nueva pregunta" icon={HelpCircle} onClick={nueva} />}
      />
      {status === "loading" ? (
        <CarLoaderModal label="Cargando preguntas frecuentes…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="Aún no hay preguntas frecuentes. Crea la primera."
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

      {ocultando && (
        <PnConfirmDialog
          title="Ocultar esta pregunta"
          icon={EyeOff}
          tone="danger"
          confirmLabel="Ocultar"
          onClose={() => setOcultando(null)}
          onConfirm={async () => {
            await alternar(ocultando, true);
            setOcultando(null);
          }}
        >
          <p>
            «{resumen(ocultando.pregunta, 80)}» dejará de mostrarse en el portal cuando publiques los cambios. Puedes volver a
            mostrarla cuando quieras.
          </p>
        </PnConfirmDialog>
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
            show("Pregunta guardada. El ciudadano la verá cuando publiques.", "success");
            onCambio();
          }}
        />
      )}
    </section>
  );
}
