"use client";

import { useId } from "react";
import { iconoComo } from "@flit/ui/iconoComo";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { MenuAcciones, type MenuAccionesItem } from "@/components/ui/MenuAcciones";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, FILA_TABLA_CLS, TEXTO_SECUNDARIO_CLS } from "@/components/tablero/vistasDetalle/comun";
import { TEXTO_MOTIVO, type AccionUsuario, type UsuarioDiagnostico } from "@/lib/usuarios/tipos";
import { EtiquetaEstadoUsuario } from "./EtiquetaEstadoUsuario";

const COLUMNAS = ["Nombre", "Correo", "Rol", "Estado", "Acciones"] as const;
const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;
/** Columna «Acciones» fija a la derecha cuando hay tabla (≥ 1024 px), como en la tabla por defecto (spec-ui §1). */
const FIJA_CLS = "lg:sticky lg:right-0 lg:bg-card";

const IconoRol = iconoComo("rol");
const IconoPausa = iconoComo("pausa");
const IconoReintento = iconoComo("reintento");

/** Texto e icono de cada acción. El texto no lleva el correo (SP-20); el menú ya se llama «Acciones de {nombre}». */
const ACCION: Readonly<Record<AccionUsuario, Pick<MenuAccionesItem, "label" | "icon" | "tone">>> = {
  CAMBIAR_ROL: { label: "Cambiar rol", icon: IconoRol },
  SUSPENDER: { label: "Suspender", icon: IconoPausa, tone: "danger" },
  REACTIVAR: { label: "Reactivar", icon: IconoReintento },
};

export interface TablaUsuariosProps {
  usuarios: UsuarioDiagnostico[];
  /** Acción elegida en el menú de una fila; `disparador` recibe el foco al cerrar el diálogo. */
  onAccion: (accion: AccionUsuario, usuario: UsuarioDiagnostico, disparador: HTMLButtonElement | null) => void;
  /** Id de la fila con una acción en vuelo (aria-busy en su menú). */
  ocupado?: string | null;
}

/**
 * Tabla de usuarios de Diagnóstico (HU #14588, spec-ui §10.1 con los ajustes H-UI del contrato): la tabla por defecto del
 * kit (las mismas recetas que el listado del inicio) con Nombre · Correo · Rol · Estado · Acciones; sin «Último ingreso»
 * (SP-18). El menú de cada fila sale solo de `acciones` del servidor: la fila propia, la invitación o el administrador de
 * la compañía no tienen menú y muestran el motivo (SP-20, H-U2). El correo va solo en su celda. Por debajo de 1024 px
 * cada usuario es una tarjeta con su nombre de título.
 */
export function TablaUsuarios({ usuarios, onAccion, ocupado = null }: TablaUsuariosProps) {
  return (
    <div className="overflow-x-auto rounded-xl border border-border">
      <table aria-label="Usuarios de Diagnóstico" className={`w-full min-w-[720px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            {COLUMNAS.map((t) => (
              <th key={t} scope="col" className={`${CABECERA_TABLA_CLS} ${t === "Acciones" ? FIJA_CLS : ""}`} style={CABECERA_TABLA_ESTILO}>
                {t}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {usuarios.map((u) => (
            <FilaUsuario key={u.id} usuario={u} onAccion={onAccion} ocupado={ocupado === u.id} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * Una fila. El disparador del menú se llama «Acciones del usuario» y se describe con el nombre visible de su celda
 * (`aria-describedby`): ni el nombre ni el correo se copian en `aria-label` o `title` (SP-19, SP-20; CR-1).
 */
function FilaUsuario({ usuario: u, onAccion, ocupado }: { usuario: UsuarioDiagnostico; onAccion: TablaUsuariosProps["onAccion"]; ocupado: boolean }) {
  const nombreId = useId();
  return (
    <tr
      className={`${FILA_TABLA_CLS} bg-card transition-colors duration-[var(--dx-motion-rapido)] hover:bg-[var(--table-row-hover)] motion-reduce:transition-none ${TABLA_MOVIL_ROW_CLS}`}
    >
      <td data-label="Nombre" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} font-semibold`}>
        <span id={nombreId}>{u.nombre}</span>
      </td>
      <td data-label="Correo" className={`${CELDA_CLS} break-all`}>
        {u.correo}
      </td>
      <td data-label="Rol" className={CELDA_CLS}>
        {u.rol_etiqueta}
      </td>
      <td data-label="Estado" className={CELDA_CLS}>
        <EtiquetaEstadoUsuario estado={u.estado} />
      </td>
      <td data-label="Acciones" className={`${CELDA_CLS} ${FIJA_CLS}`}>
        {u.acciones.length > 0 ? (
          <MenuAcciones
            ariaLabel="Acciones del usuario"
            ariaDescribedBy={nombreId}
            ocupado={ocupado}
            items={u.acciones.map((accion) => ({
              key: accion,
              ...ACCION[accion],
              onSelect: ({ disparador }) => onAccion(accion, u, disparador),
            }))}
          />
        ) : (
          <span className={TEXTO_SECUNDARIO_CLS}>{u.motivo_sin_acciones ? TEXTO_MOTIVO[u.motivo_sin_acciones] : "Sin acciones"}</span>
        )}
      </td>
    </tr>
  );
}

/**
 * Cargando (spec-ui §10.4): cinco filas grises con la forma de la tabla y un `role="status"` con su texto. Sin animación
 * si el usuario pide menos movimiento.
 */
export function EsqueletoTabla({ etiqueta, filas = 5 }: { etiqueta: string; filas?: number }) {
  return (
    <div role="status" aria-label={etiqueta} className="overflow-hidden rounded-xl border border-border">
      <span className="sr-only">{etiqueta}</span>
      <div aria-hidden="true" className="h-9" style={CABECERA_TABLA_ESTILO} />
      {Array.from({ length: filas }, (_, i) => (
        <div key={i} aria-hidden="true" className={`${FILA_TABLA_CLS} flex items-center gap-4 bg-card px-3 py-3`}>
          <span className="h-3 w-1/4 rounded-full bg-[var(--di-surface-sunken)] motion-safe:animate-pulse" />
          <span className="h-3 w-1/3 rounded-full bg-[var(--di-surface-sunken)] motion-safe:animate-pulse" />
          <span className="h-3 w-1/6 rounded-full bg-[var(--di-surface-sunken)] motion-safe:animate-pulse" />
        </div>
      ))}
    </div>
  );
}
