"use client";

// HU #14076 AC1/AC2/AC5 — Banners del borrador: tabla estándar con la imagen, el texto alternativo, la vigencia, RowActions
// (editar, activar o desactivar, subir y bajar), orden editable y los cuatro estados. La imagen del borrador se
// descarga con la sesión del administrador (el endpoint exige Authorization).
import { useCallback, useEffect, useMemo, useState } from "react";
import { ArrowDown, ArrowUp, EyeOff, ImageIcon, Pencil } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CreateButton } from "@/components/atom/CreateButton";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { EncabezadoFiltrable, TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { formatFechaHora } from "@/lib/format/date";
import { filtrarFilas, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";
import {
  actualizarBannerPn,
  crearBannerPn,
  establecerBannerActivoPn,
  fetchBannersPn,
  ordenarBannersPn,
  type BannerPn,
} from "@/lib/api/admin-pn-contenido";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import { PnConfirmDialog, PnSeccionHeader } from "../pn-componentes";
import { pnErrorMessage } from "../pnErrors";
import { BannerFormModal } from "./BannerFormModal";
import { BannerImagen } from "./BannerImagen";
import { moverId } from "./PreguntasSection";

const COLUMNAS_FILTRO: ColumnaFiltro[] = [
  { id: "banner", label: "Banner", tipo: "texto" },
  {
    id: "estado",
    label: "Estado",
    tipo: "seleccion",
    opciones: [
      { value: "activo", label: "Visible" },
      { value: "inactivo", label: "Oculto" },
    ],
  },
];

/** Vigente, Programado (aún no empieza), Vencido (ya terminó) u Oculto. */
export function estadoBanner(b: BannerPn, ahora: Date = new Date()): { label: string; tone: "success" | "info" | "warning" | "neutral" } {
  if (!b.activo) return { label: "Oculto", tone: "neutral" };
  if (b.vigenteDesde && new Date(b.vigenteDesde) > ahora) return { label: "Programado", tone: "info" };
  if (b.vigenteHasta && new Date(b.vigenteHasta) < ahora) return { label: "Vencido", tone: "warning" };
  return { label: "Vigente", tone: "success" };
}

function plural(n: number, uno: string, varios: string): string {
  return `${n} ${n === 1 ? uno : varios}`;
}

function vigencia(b: BannerPn): string {
  if (!b.vigenteDesde && !b.vigenteHasta) return "Siempre";
  return `${b.vigenteDesde ? formatFechaHora(b.vigenteDesde) : "Sin inicio"} → ${b.vigenteHasta ? formatFechaHora(b.vigenteHasta) : "Sin fin"}`;
}

export function BannersSection({ onCambio }: { onCambio: () => void }) {
  const { show } = useToast();
  const [banners, setBanners] = useState<BannerPn[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [form, setForm] = useState<{ banner?: BannerPn } | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [ocultando, setOcultando] = useState<BannerPn | null>(null);
  const pg = usePaginacion("pn.contenido.banners");
  const { setPage } = pg;
  const volverAPrimeraPagina = useCallback(() => setPage(1), [setPage]);
  const filtros = useTablaFiltros({ columnas: COLUMNAS_FILTRO, onCambio: volverAPrimeraPagina });

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const list = await fetchBannersPn(signal);
      if (signal?.aborted) return;
      setBanners([...list].sort((a, b) => a.orden - b.orden));
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
      filtrarFilas(banners, filtros.condicionesTotales, "", {
        columnas: { banner: (b) => [b.titulo, b.textoAlternativo], estado: (b) => (b.activo ? "activo" : "inactivo") },
      }),
    [banners, filtros.condicionesTotales],
  );

  async function mover(banner: BannerPn, delta: -1 | 1) {
    const ids = banners.map((b) => b.id);
    const nuevo = moverId(ids, ids.indexOf(banner.id), delta);
    setOcupado(true);
    try {
      await ordenarBannersPn(nuevo);
      setBanners((prev) => nuevo.map((id, i) => ({ ...(prev.find((b) => b.id === id) as BannerPn), orden: i + 1 })));
      onCambio();
    } catch (err) {
      show(pnErrorMessage(err), "error");
      void load();
    } finally {
      setOcupado(false);
    }
  }

  async function alternar(banner: BannerPn, propagar = false) {
    setOcupado(true);
    try {
      const actualizado = await establecerBannerActivoPn(banner.id, !banner.activo);
      setBanners((prev) => prev.map((b) => (b.id === banner.id ? actualizado : b)));
      show(`Banner ${actualizado.activo ? "visible" : "oculto"}. El ciudadano verá el cambio cuando publiques.`, "success");
      onCambio();
    } catch (err) {
      if (propagar) throw err;
      show(pnErrorMessage(err), "error");
    } finally {
      setOcupado(false);
    }
  }

  function actionsFor(b: BannerPn, indice: number): RowAction[] {
    const n = `«${b.titulo}»`;
    return [
      { icon: Pencil, label: `Editar el banner ${n}`, tone: "primary", onClick: () => setForm({ banner: b }) },
      { icon: ArrowUp, label: `Subir el banner ${n}`, disabled: ocupado || indice === 0, disabledTitle: "Ya es el primero", onClick: () => void mover(b, -1) },
      { icon: ArrowDown, label: `Bajar el banner ${n}`, disabled: ocupado || indice === banners.length - 1, disabledTitle: "Ya es el último", onClick: () => void mover(b, 1) },
    ];
  }

  const columns: DataTableColumn<BannerPn>[] = [
    { key: "orden", header: "Orden", cellClassName: "w-16 font-semibold", render: (b) => banners.indexOf(b) + 1 },
    {
      key: "imagen",
      header: "Imagen",
      render: (b) => <BannerImagen key={b.imagenSha256} id={b.id} alt={b.textoAlternativo} className="h-12 w-24 rounded-lg border border-[#DFE5ED] object-cover" />,
    },
    {
      key: "banner",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="banner">
          Banner
        </EncabezadoFiltrable>
      ),
      cellClassName: "max-w-[320px]",
      render: (b) => (
        <>
          <span className="block font-semibold">{b.titulo}</span>
          <span className="block text-xs text-[#59677D] dark:text-white/70">Descripción: {b.textoAlternativo}</span>
        </>
      ),
    },
    { key: "vigencia", header: "Vigencia", cellClassName: "text-xs", render: (b) => vigencia(b) },
    {
      key: "estado",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="estado">
          Estado
        </EncabezadoFiltrable>
      ),
      render: (b) => {
        const e = estadoBanner(b);
        return (
          <div className="flex items-start gap-2">
            <SwitchToggle
              checked={b.activo}
              disabled={ocupado}
              label={`${b.activo ? "Ocultar" : "Mostrar"} el banner «${b.titulo}» en el portal`}
              onChange={(next) => (next ? void alternar(b) : setOcultando(b))}
            />
            <StatusBadge label={e.label} tone={e.tone} />
          </div>
        );
      },
    },
    { key: "acciones", header: "Acciones", align: "right", render: (b) => <RowActions actions={actionsFor(b, banners.indexOf(b))} /> },
  ];

  const nuevo = () => setForm({});

  return (
    <section aria-labelledby="pn-banners-titulo" className="flex flex-col gap-3">
      <PnSeccionHeader
        id="pn-banners-titulo"
        contador={status === "ready" || status === "empty" ? `${plural(banners.length, "banner", "banners")} · ${plural(banners.filter((b) => b.activo).length, "visible", "visibles")}` : undefined}
        titulo="Banners"
        descripcion="Imágenes destacadas del portal, en este orden (usa las flechas para subir o bajar). Usa JPG, PNG o WEBP de hasta 2 MB. Los ocultos o vencidos no aparecen."
        accion={<CreateButton label="Nuevo banner" icon={ImageIcon} onClick={nuevo} />}
      />
      {status === "loading" ? (
        <CarLoaderModal label="Cargando banners…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="Aún no hay banners. Crea el primero."
          errorMessage="No se pudieron cargar los banners."
          onRetry={() => void load()}
        >
          <DataTable<BannerPn>
            ariaLabel="Banners del portal"
            columns={columns}
            rows={pg.paginar(visibles)}
            getRowKey={(b) => b.id}
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
          title="Ocultar este banner"
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
            «{ocultando.titulo}» dejará de mostrarse en el portal cuando publiques los cambios. Puedes volver a mostrarlo
            cuando quieras.
          </p>
        </PnConfirmDialog>
      )}

      {form && (
        <BannerFormModal
          banner={form.banner}
          onClose={() => setForm(null)}
          onSave={async (input) => {
            if (form.banner) {
              const guardado = await actualizarBannerPn(form.banner.id, input);
              setBanners((prev) => prev.map((b) => (b.id === guardado.id ? guardado : b)));
            } else {
              const creado = await crearBannerPn(input);
              setBanners((prev) => [...prev, creado]);
              setStatus("ready");
            }
            setForm(null);
            show("Banner guardado. El ciudadano lo verá cuando publiques.", "success");
            onCambio();
          }}
        />
      )}
    </section>
  );
}
