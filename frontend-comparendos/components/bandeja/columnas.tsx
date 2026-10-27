import type { DataTableColumn } from "@flit/ui";
import { StatusBadge, TABLA_CELDA_SECUNDARIA_CLS } from "@flit/ui";
import { Chip } from "@/components/ui/Chip";
import {
  cambioContraventor,
  consultaContraventor,
  estadoDp,
  estadoProceso,
  fuente,
  resultadoDp,
  textoSemaforo,
  tonoSemaforo,
  ultimoHito,
  ventanaDescuento,
} from "@/lib/etiquetas";
import { formatoFecha, formatoFechaHora, formatoPesos } from "@/lib/format/date";
import type { FilaBandeja } from "@/lib/tipos";

/**
 * Columnas de la bandeja (§7.1). Las claves son las mismas del Excel (ExportacionBandeja.cs): lo visible es lo que se
 * exporta. Ventana de descuento, Fin del descuento, Activo y Cobro coactivo vienen ocultas (HU #13871).
 */
export const COLUMNAS_BANDEJA: DataTableColumn<FilaBandeja>[] = [
  {
    key: "numero",
    header: "Número",
    render: (f) => (
      <div className="flex flex-col">
        <span className="font-semibold">{f.numero}</span>
        {!f.esFotomulta && <span className={TABLA_CELDA_SECUNDARIA_CLS}>Comparendo físico</span>}
      </div>
    ),
  },
  { key: "placa", header: "Placa", render: (f) => f.placa ?? "—" },
  {
    key: "titular",
    header: "NIT o titular",
    render: (f) => (
      <div className="flex flex-col">
        <span>{f.titular ?? "—"}</span>
        {f.nitConsultado && <span className={TABLA_CELDA_SECUNDARIA_CLS}>{f.nitConsultado}</span>}
      </div>
    ),
  },
  {
    key: "ot",
    header: "Organismo",
    render: (f) => (
      <span title={f.secretariaTexto ?? undefined}>{f.organismo ?? (f.secretariaTexto ? <em className="opacity-70">Sin mapear: {f.secretariaTexto}</em> : "—")}</span>
    ),
  },
  { key: "imposicion", header: "Imposición", render: (f) => formatoFecha(f.fechaImposicion) },
  { key: "notificacion", header: "Notificación", render: (f) => formatoFecha(f.fechaNotificacion) },
  { key: "valor", header: "Valor", align: "right", render: (f) => formatoPesos(f.valorTotal) },
  { key: "fuente", header: "Fuente", render: (f) => <Chip e={fuente(f.fuente)} /> },
  {
    key: "semaforo",
    header: "Estado legal",
    render: (f) => <StatusBadge label={textoSemaforo(f.estadoLegal, f.diasHabilesRestantes)} tone={tonoSemaforo(f.estadoLegal, f.proximoAVencer)} />,
  },
  { key: "ventana", header: "Ventana de descuento", render: (f) => <Chip e={ventanaDescuento(f.ventanaDescuento)} /> },
  { key: "finDescuento", header: "Fin del descuento", render: (f) => formatoFecha(f.finDescuentoVigente) },
  { key: "proceso", header: "Proceso", render: (f) => <Chip e={estadoProceso(f.estadoProceso)} /> },
  {
    key: "contraventor",
    header: "Contraventor",
    render: (f) =>
      f.contraventorNombre ? (
        <span className="inline-flex items-center gap-1.5">
          <span aria-hidden className="h-2 w-2 shrink-0 rounded-full bg-[#16A34A]" />
          <span>{f.contraventorNombre}</span>
          <span className="sr-only">(asignado)</span>
        </span>
      ) : (
        <Chip e={consultaContraventor(f.estadoConsultaContraventor)} />
      ),
  },
  { key: "aviso", header: "Último hito de notificación", render: (f) => <Chip e={ultimoHito(f.ultimoHito)} /> },
  { key: "cambio", header: "Cambio de contraventor", render: (f) => <Chip e={cambioContraventor(f.cambioContraventor)} /> },
  {
    key: "dp",
    header: "DP",
    render: (f) =>
      f.dpRadicado ? (
        <div className="flex flex-col gap-1">
          <span className="font-mono text-[11px]">{f.dpRadicado}</span>
          <Chip e={f.dpResultado ? resultadoDp(f.dpResultado) : estadoDp(f.dpEstado)} />
        </div>
      ) : (
        "—"
      ),
  },
  { key: "pagado", header: "Pago", render: (f) => (f.pagado ? <StatusBadge label="Pagado" tone="success" /> : "—") },
  { key: "activo", header: "Activo", render: (f) => (f.activo ? "Sí" : "No") },
  { key: "cobroCoactivo", header: "Cobro coactivo", render: (f) => (f.enCobroCoactivo ? "Sí" : "No") },
  { key: "actividad", header: "Última actividad", render: (f) => <span className="whitespace-nowrap">{formatoFechaHora(f.ultimoEventoEn)}</span> },
];
