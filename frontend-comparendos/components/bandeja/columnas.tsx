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
  ultimoAviso,
} from "@/lib/etiquetas";
import { formatoFecha, formatoFechaHora, formatoPesos } from "@/lib/format/date";
import type { FilaBandeja } from "@/lib/tipos";

/** Columnas de la bandeja (§7.1). */
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
  { key: "proceso", header: "Proceso", render: (f) => <Chip e={estadoProceso(f.estadoProceso)} /> },
  {
    key: "contraventor",
    header: "Contraventor",
    render: (f) => (f.contraventorNombre ? <span>{f.contraventorNombre}</span> : <Chip e={consultaContraventor(f.estadoConsultaContraventor)} />),
  },
  { key: "aviso", header: "Último aviso", render: (f) => <Chip e={ultimoAviso(f.ultimoAviso)} /> },
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
  { key: "actividad", header: "Última actividad", render: (f) => <span className="whitespace-nowrap">{formatoFechaHora(f.ultimoEventoEn)}</span> },
];
