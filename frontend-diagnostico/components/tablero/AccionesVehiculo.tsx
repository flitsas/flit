"use client";

import { useId } from "react";
import { MenuAcciones, type MenuAccionesItem } from "@/components/ui/MenuAcciones";
import { iconoComo } from "@flit/ui/iconoComo";
import { IconoDxBandera } from "@/components/dx/iconos/IconoDxBandera";
import { IconoDxCrearTramite } from "@/components/dx/iconos/IconoDxCrearTramite";
import { IconoDxDeuda } from "@/components/dx/iconos/IconoDxDeuda";
import { useAccionSanear } from "@/components/saneamiento/BotonSanear";
import type { ModoDiagnostico, EstadoDiagnostico } from "@/lib/diagnosticos/api";
import type { SaneamientoFila } from "@/lib/saneamiento/api";
import { deLaPlaca } from "@/lib/saneamiento/estados";
import type { FilaVehiculo, ResultadoReintento, TramiteFila } from "@/lib/tablero/api";
import { MensajeTramite, useAccionTramite } from "./BotonCrearTramite";
import { useAccionPdf } from "./BotonPdfVehiculo";
import { useReintento } from "./BotonReintentar";

export interface AccionesVehiculoProps {
  diagnosticoId: string;
  vehiculo: FilaVehiculo;
  estadoLote: EstadoDiagnostico;
  modo?: ModoDiagnostico;
  tramiteAnalizado?: string | null;
  tramitesUrl: string;
  empresaId?: string | null;
  /** Capacidades de la sesión: sin la capacidad, el ítem no existe. */
  capacidades: { sanear: boolean; derivar: boolean; descargar: boolean; reprocesar: boolean };
  /** La grilla trae el dato de saneamiento (habilita «Ver deuda» en los terminados). */
  conDatosSaneamiento: boolean;
  /** Abre PS-2 «Deuda y saneamiento»; el disparador «Acciones» recibe el foco al cerrarlo. */
  onAbrirDeuda: (disparador: HTMLElement | null) => void;
  onCambioSaneamiento: (saneamiento: SaneamientoFila) => void;
  onCreadoTramite: (vehiculoId: string, tramite: TramiteFila) => void;
  onRefrescar: () => void;
  onAnunciar?: (mensaje: string) => void;
  /** Atajo «Crear trámite» del pie del dictamen (F-05). */
  solicitudCrear?: number;
  reintentar: () => Promise<ResultadoReintento>;
  onReintentado: (resultado: ResultadoReintento) => void;
  onErrorReintento: (mensaje: string, refrescar: boolean) => void;
  /**
   * HU #14584 §6.4: «Ver dictamen» abre el panel del dictamen con el disparador «Acciones» (recibe el foco al cerrarlo). Va
   * primero, en su grupo, en toda fila. Sin él, no hay ítem.
   */
  onVerDictamen?: (disparador: HTMLElement | null) => void;
  /** HU #14584 §6.4: «Auditoría del dictamen» tras «Ver dictamen»; quien pinta lo pasa solo con la capacidad verAuditoria. */
  onVerAuditoria?: (disparador: HTMLElement | null) => void;
}

type IconoItem = NonNullable<MenuAccionesItem["icon"]>;
const BanderaApagada: IconoItem = ({ className }) => <IconoDxBandera tamano={16} className={className} />;
const BanderaEncendida: IconoItem = ({ className }) => <IconoDxBandera tamano={16} encendida className={className} />;
const CrearTramite: IconoItem = ({ className }) => <IconoDxCrearTramite tamano={16} className={className} />;
const Deuda: IconoItem = ({ className }) => <IconoDxDeuda tamano={16} className={className} />;
const Externo = iconoComo("externo") as IconoItem;
const NubeBajar = iconoComo("nube-bajar") as IconoItem;
const Reintento = iconoComo("reintento") as IconoItem;
const Dictamen = iconoComo("documento") as IconoItem;
const Auditoria = iconoComo("portapapeles") as IconoItem;

/** Grupos del menú (spec-ui §4): separador entre «Dictamen» y el resto. */
const GRUPO_DICTAMEN = "dictamen";
const GRUPO_ACCIONES = "acciones";

const MENSAJE_CLS = "max-w-[12rem] text-xs font-semibold";

/**
 * Columna «Acciones» de una fila del tablero (ola Diagnóstico UX 2026-11-15 · FE-2, spec-ui §2.3–§2.5): el menú
 * «Acciones» propio de Diagnóstico (`MenuAcciones`, Kit 2026) con los ítems en el orden D-103 (saneamiento · trámite ·
 * deuda · PDF · reintento; el estado del saneamiento lo informa la columna Dictamen, auditoría UI UA-1) y, debajo del disparador, los mensajes de resultado. Sin la capacidad el ítem no existe; si la
 * acción no aplica ahora, sale deshabilitado con su motivo visible. Los modales de crear trámite van fuera del menú.
 * La lógica de cada acción vive en su hook (`useAccionSanear`, `useAccionTramite`, `useAccionPdf`, `useReintento`).
 */
export function AccionesVehiculo({
  diagnosticoId,
  vehiculo: v,
  estadoLote,
  modo,
  tramiteAnalizado,
  tramitesUrl,
  empresaId,
  capacidades,
  conDatosSaneamiento,
  onAbrirDeuda,
  onCambioSaneamiento,
  onCreadoTramite,
  onRefrescar,
  onAnunciar,
  solicitudCrear,
  reintentar,
  onReintentado,
  onErrorReintento,
  onVerDictamen,
  onVerAuditoria,
}: AccionesVehiculoProps) {
  const placa = v.placa || v.vin || v.identificador;
  // Disparador «Acciones» de la fila: destino del foco al cancelar un modal de crear trámite (se busca al cerrar el modal).
  const celdaId = useId();
  const disparador = () => document.getElementById(celdaId)?.querySelector<HTMLElement>('[aria-haspopup="menu"]') ?? null;

  const sanear = useAccionSanear({
    diagnosticoId,
    vehiculo: v,
    saneamiento: v.saneamiento,
    puedeSanear: capacidades.sanear,
    onCambio: (_estado, saneamiento) => onCambioSaneamiento(saneamiento),
    onRefrescar,
    onAnunciar,
  });
  const tramite = useAccionTramite({
    diagnosticoId,
    vehiculo: v,
    modo,
    tramiteAnalizado,
    tramitesUrl,
    empresaId,
    onCreado: onCreadoTramite,
    onRefrescar,
    solicitudCrear,
    focoAlCancelar: disparador,
  });
  const pdf = useAccionPdf({ diagnosticoId, estadoLote, vehiculo: v });
  const reintento = useReintento({ reintentar, onReintentado, onError: onErrorReintento });

  const items: MenuAccionesItem[] = [];

  // 0 — dictamen (HU #14584 §6.4): «Ver dictamen» al principio, en su grupo. Como el botón de la columna que reemplaza, va en
  // toda fila: el panel ya resuelve «sin dictamen vigente» (Pendiente o modo Simple).
  if (onVerDictamen) {
    items.push({
      key: "dictamen",
      label: "Ver dictamen",
      ariaLabel: `Ver dictamen de ${placa}`,
      icon: Dictamen,
      grupo: GRUPO_DICTAMEN,
      onSelect: ({ disparador: d }) => onVerDictamen(d),
    });
  }
  if (onVerAuditoria) {
    items.push({
      key: "auditoria",
      label: "Auditoría del dictamen",
      ariaLabel: `Auditoría del dictamen de ${placa}`,
      icon: Auditoria,
      grupo: GRUPO_DICTAMEN,
      onSelect: ({ disparador: d }) => onVerAuditoria(d),
    });
  }
  const inicioAcciones = items.length;

  // 1 / 1′ — saneamiento (HU #14577 §2.1: la bandera va antes de «Crear trámite»).
  if (sanear.tipo === "alternar") {
    const accion = sanear.encendida ? "Quitar solicitud de saneamiento" : "Solicitar saneamiento";
    items.push({
      key: "sanear",
      label: sanear.enVuelo ? (sanear.encendida ? "Quitando…" : "Solicitando…") : accion,
      ariaLabel: `${accion} ${deLaPlaca(sanear.identificador)}`,
      icon: sanear.encendida ? BanderaEncendida : BanderaApagada,
      disabled: sanear.noElegible || sanear.enVuelo,
      disabledReason: sanear.noElegible ? sanear.motivo : undefined,
      onSelect: () => void sanear.alternar(),
    });
  } else if (sanear.tipo === "ver") {
    items.push({
      key: "sanear",
      label: "Ver saneamiento",
      ariaLabel: sanear.nombre,
      icon: BanderaEncendida,
      onSelect: ({ disparador: d }) => onAbrirDeuda(d),
    });
  }

  // 2 — trámite (Crear / Ver #N).
  if (capacidades.derivar) {
    if (tramite.tramite && tramite.urlVer) {
      items.push({ key: "tramite", label: tramite.textoVer, ariaLabel: tramite.nombreVer, icon: Externo, href: tramite.urlVer, target: "_blank" });
    } else if (tramite.derivacion.conocida) {
      items.push({
        key: "tramite",
        label: tramite.enviando ? "Creando…" : "Crear trámite",
        ariaLabel: tramite.nombreCrear,
        icon: CrearTramite,
        disabled: !tramite.derivacion.elegible || tramite.enviando,
        disabledReason: tramite.derivacion.elegible ? undefined : tramite.motivo,
        onSelect: () => tramite.alPulsar(),
      });
    }
  }

  // 3 — deuda (D-103: después de «Crear trámite»), solo en vehículos terminados.
  if (conDatosSaneamiento && v.terminado) {
    items.push({
      key: "deuda",
      label: "Ver deuda",
      ariaLabel: `Ver deuda de la placa ${placa}`,
      icon: Deuda,
      onSelect: ({ disparador: d }) => onAbrirDeuda(d),
    });
  }

  // 4 — PDF del vehículo.
  if (capacidades.descargar) {
    items.push({
      key: "pdf",
      label: pdf.descargando ? "Descargando…" : "Descargar PDF",
      ariaLabel: pdf.nombre,
      icon: NubeBajar,
      href: pdf.url,
      download: true,
      disabled: !pdf.habilitado,
      disabledReason: pdf.habilitado ? undefined : (pdf.ayuda ?? undefined),
      hint: pdf.habilitado ? (pdf.ayuda ?? undefined) : undefined,
      onSelect: ({ evento }) => pdf.alHacerClic(evento as React.MouseEvent<HTMLAnchorElement>),
    });
  }

  // 5 — reintento de las consultas pegadas.
  if (capacidades.reprocesar && v.pegada) {
    items.push({
      key: "reintento",
      label: reintento.enviando ? "Reintentando…" : "Reintentar consultas",
      ariaLabel: `Reintentar consultas de la placa ${placa}`,
      icon: Reintento,
      disabled: reintento.enviando,
      onSelect: () => void reintento.alPulsar(),
    });
  }

  for (const item of items.slice(inicioAcciones)) item.grupo = GRUPO_ACCIONES;

  const ocupado = tramite.enviando || reintento.enviando || (sanear.tipo === "alternar" && sanear.enVuelo);

  return (
    <div id={celdaId} className="flex flex-col items-start gap-1.5">
      {items.length > 0 ? (
        <MenuAcciones ariaLabel={`Acciones de la placa ${placa}`} triggerLabel="Acciones" items={items} ocupado={ocupado} />
      ) : (
        <span>
          <span aria-hidden="true">—</span>
          <span className="sr-only">{`Sin acciones para la placa ${placa}`}</span>
        </span>
      )}
      {sanear.error && (
        <p role="alert" className={MENSAJE_CLS} style={{ color: "var(--badge-danger-fg)" }}>
          {sanear.error}
        </p>
      )}
      {sanear.tipo === "alternar" && sanear.exito && (
        <p role="status" className={MENSAJE_CLS} style={{ color: "var(--badge-success-fg)" }}>
          {sanear.exito}
        </p>
      )}
      <MensajeTramite mensaje={tramite.mensaje} identificador={tramite.identificador} />
      {pdf.error && (
        <p role="alert" className={MENSAJE_CLS} style={{ color: "var(--badge-danger-fg)" }}>
          {pdf.error}
        </p>
      )}
      {tramite.modales}
    </div>
  );
}
