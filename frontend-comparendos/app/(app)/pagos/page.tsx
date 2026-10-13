"use client";

import { useRef, useState } from "react";
import { Download, FileSpreadsheet, Upload } from "lucide-react";
import { DataTable, InlineAlert, usePaginacion } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { Chip } from "@/components/ui/Chip";
import { BOTON_PRIMARIO_CLS, controlCls } from "@/components/ui/estilos";
import { apiDownload, apiFetch } from "@/lib/api";
import type { Etiqueta } from "@/lib/etiquetas";
import { formatoFecha, formatoFechaHora, formatoPesos } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { errorArchivoCarga, ESTADO_CARGA, leerDatosFila } from "@/lib/pagos";
import { useCarga } from "@/lib/useCarga";

interface Carga {
  id: string;
  nombreArchivo: string;
  estado: string;
  totalFilas: number;
  filasValidas: number;
  filasInvalidas: number;
  cargadoEn: string;
  cargadoPorNombre: string | null;
}

interface ResumenCarga extends Carga {
  filas: { numeroFila: number; valida: boolean; errores: string[]; datos: string }[];
}

const estado = (v: string): Etiqueta => ESTADO_CARGA[v] ?? { texto: v, tono: "neutral" };

function Resultado({ id }: { id: string }) {
  const toast = useToast();
  const carga = useCarga(`carga-pagos:${id}`, (signal) => apiFetch<ResumenCarga>(`/pagos/cargas/${id}`, { signal }));
  const [soloErrores, setSoloErrores] = useState(false);
  const pg = usePaginacion(25);
  const c = carga.datos;
  const filas = (c?.filas ?? []).filter((f) => !soloErrores || !f.valida);

  const descargarErrores = () =>
    apiDownload(`/pagos/cargas/${id}/errores`, "pagos-con-error.xlsx").catch((e: unknown) =>
      toast.show(e instanceof Error ? e.message : "No se pudo descargar.", "error"),
    );

  if (carga.estado === "loading") return <p className="text-sm opacity-60">Cargando resultado…</p>;
  if (carga.estado === "error") return <InlineAlert tone="error">{carga.error}</InlineAlert>;
  if (!c) return null;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-3 text-xs">
        <h2 className="text-sm font-semibold">{c.nombreArchivo}</h2>
        <Chip e={estado(c.estado)} />
        <span>
          <b>{c.filasValidas}</b> pagos registrados · <b>{c.filasInvalidas}</b> filas con error de <b>{c.totalFilas}</b>
        </span>
        {c.filasInvalidas > 0 && (
          <>
            <button type="button" className={controlCls(soloErrores)} onClick={() => { setSoloErrores((v) => !v); pg.setPage(1); }}>
              Solo filas con error
            </button>
            <button type="button" className={controlCls(false)} onClick={descargarErrores}>
              <Download className="h-3.5 w-3.5" aria-hidden />
              Excel de errores
            </button>
          </>
        )}
      </div>
      {c.estado === "Rechazada" ? (
        <InlineAlert tone="error" title="El archivo se rechazó completo">
          {c.filas.length === 0 ? "Revisa que sea la plantilla de pagos." : "Revisa el archivo y vuelve a cargarlo."}
        </InlineAlert>
      ) : (
        <DataTable
          ariaLabel="Filas de la carga"
          rows={pg.paginar(filas)}
          getRowKey={(f) => String(f.numeroFila)}
          emptyMessage={soloErrores ? "No hay filas con error." : "El archivo no tenía filas."}
          minWidth={900}
          columns={[
            { key: "fila", header: "Fila", align: "right", render: (f) => f.numeroFila },
            {
              key: "resultado",
              header: "Resultado",
              render: (f) => <Chip e={f.valida ? { texto: "Registrado", tono: "success" } : { texto: "Con error", tono: "danger" }} />,
            },
            { key: "tipo", header: "Tipo", render: (f) => leerDatosFila(f.datos).tipoPago ?? "—" },
            { key: "placa", header: "Placa", render: (f) => leerDatosFila(f.datos).placa ?? "—" },
            { key: "numero", header: "Comparendo", render: (f) => leerDatosFila(f.datos).numeroComparendo ?? "—" },
            { key: "valor", header: "Valor", align: "right", render: (f) => formatoPesos(leerDatosFila(f.datos).valor) },
            { key: "fecha", header: "Fecha de pago", render: (f) => formatoFecha(leerDatosFila(f.datos).fechaPago) },
            { key: "errores", header: "Errores", render: (f) => (f.errores.length ? <span className="text-[#B42318]">{f.errores.join("; ")}</span> : "—") },
          ]}
          pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: filas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
        />
      )}
    </div>
  );
}

function CargaPagos() {
  const toast = useToast();
  const { sesion } = useSesion();
  const soloLectura = !!sesion?.usuario.esSuperAdmin;
  const input = useRef<HTMLInputElement>(null);
  const [subiendo, setSubiendo] = useState(false);
  const [elegida, setElegida] = useState<string | null>(null);
  const [rechazo, setRechazo] = useState<string | null>(null);
  const historial = useCarga("cargas-pagos", (signal) => apiFetch<Carga[]>("/pagos/cargas", { signal }));
  const pg = usePaginacion(10);
  const cargas = historial.datos ?? [];

  const plantilla = () =>
    apiDownload("/pagos/plantilla", "plantilla-pagos.xlsx").catch((e: unknown) => toast.show(e instanceof Error ? e.message : "No se pudo descargar.", "error"));

  const subir = async (archivo: File) => {
    const invalido = errorArchivoCarga(archivo);
    setRechazo(invalido);
    if (invalido) return;
    setSubiendo(true);
    try {
      const form = new FormData();
      form.append("archivo", archivo);
      const r = await apiFetch<ResumenCarga>("/pagos/cargas", { method: "POST", body: form });
      setElegida(r.id);
      toast.show(r.filasInvalidas === 0 ? `${r.filasValidas} pagos registrados.` : `${r.filasValidas} pagos registrados y ${r.filasInvalidas} filas con error.`, r.filasInvalidas === 0 ? "success" : "error");
    } catch (e) {
      // CARGA_RECHAZADA: el archivo completo no sirve (columnas, más de 1.000 filas, no es Excel); igual queda en el historial.
      setRechazo(e instanceof Error ? e.message : "No se pudo cargar el archivo.");
      setElegida(null);
    } finally {
      setSubiendo(false);
      historial.recargar();
      if (input.current) input.current.value = "";
    }
  };

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Carga de pagos"
        descripcion="Registra pagos de muchos comparendos con la plantilla de Excel (hasta 1.000 filas). Las filas válidas se registran y las que tienen error vuelven con su motivo."
        acciones={
          <button type="button" className={controlCls(false)} onClick={plantilla}>
            <FileSpreadsheet className="h-3.5 w-3.5" aria-hidden />
            Descargar plantilla
          </button>
        }
      />

      {!soloLectura && (
        <div className="flex flex-col gap-2">
          <div className="flex flex-wrap items-center gap-3">
            <input
              ref={input}
              type="file"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              className="hidden"
              onChange={(e) => e.target.files?.[0] && subir(e.target.files[0])}
            />
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={subiendo} onClick={() => input.current?.click()}>
              <Upload className="h-3.5 w-3.5" aria-hidden />
              {subiendo ? "Procesando…" : "Cargar Excel de pagos"}
            </button>
            <span className="text-xs opacity-70">
              Columnas: tipo_pago (CC, PC o BC), placa, numero_comparendo, valor, fecha_pago. Un pago por comparendo; no hay anulación.
            </span>
          </div>
          {rechazo && (
            <InlineAlert tone="error" title="No se cargó el archivo">
              {rechazo}
            </InlineAlert>
          )}
        </div>
      )}

      {elegida && <Resultado key={elegida} id={elegida} />}

      <div className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold">Cargas anteriores</h2>
        <DataTable
          ariaLabel="Cargas de pagos"
          rows={pg.paginar(cargas)}
          getRowKey={(c) => c.id}
          onRowClick={(c) => setElegida(c.id)}
          status={historial.estado}
          errorMessage={historial.error ?? undefined}
          onRetry={historial.recargar}
          emptyMessage="Todavía no hay cargas de pagos."
          columns={[
            { key: "fecha", header: "Cargada", render: (c) => formatoFechaHora(c.cargadoEn) },
            { key: "archivo", header: "Archivo", render: (c) => <span className="font-semibold">{c.nombreArchivo}</span> },
            { key: "por", header: "Cargada por", render: (c) => c.cargadoPorNombre ?? "—" },
            { key: "estado", header: "Estado", render: (c) => <Chip e={estado(c.estado)} /> },
            { key: "total", header: "Filas", align: "right", render: (c) => c.totalFilas },
            { key: "validas", header: "Registradas", align: "right", render: (c) => c.filasValidas },
            { key: "invalidas", header: "Con error", align: "right", render: (c) => c.filasInvalidas },
          ]}
          pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: cargas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
        />
      </div>
    </section>
  );
}

/** Carga masiva de pagos por Excel (HU #13494 sobre la API de la HU #13493, §6.19). */
export default function Pagos() {
  return (
    <Requiere permiso={P.pagosCargarMasivo}>
      <CargaPagos />
    </Requiere>
  );
}
