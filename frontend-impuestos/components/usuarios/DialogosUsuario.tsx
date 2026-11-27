"use client";

import { useId, useState } from "react";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Modal } from "@flit/ui/Modal";
import { AYUDA_CLS, MARCA_CLS } from "@/components/comun/estilos";
import { ApiError } from "@/lib/api";
import { mensajeErrorFijo } from "@/lib/errores";
import {
  DESCRIPCION_ROL,
  ERRORES_USUARIOS,
  ETIQUETA_ROL,
  cambiarRolUsuario,
  etiquetaRol,
  reactivarUsuario,
  suspenderUsuario,
  type RolImpuestos,
  type UsuarioImpuestos,
} from "@/lib/usuarios/api";

export interface DialogoCambiarRolProps {
  usuario: UsuarioImpuestos;
  empresa: string | null;
  roles: readonly RolImpuestos[];
  onCerrar: () => void;
  /** 204 recibido: aviso para la región de la pantalla. */
  onCambiado: (aviso: string) => void;
}

/**
 * «Cambiar rol de {Nombre}» (spec-visual §3.8, molde ModalCambiarRol de Diagnóstico). Solo los roles que quien gestiona
 * puede dar; «Guardar rol» queda deshabilitado mientras no cambie. Errores de la API dentro del diálogo.
 */
export function DialogoCambiarRol({ usuario, empresa, roles, onCerrar, onCambiado }: DialogoCambiarRolProps) {
  const id = useId();
  // El rol actual siempre se ve, aunque quien gestiona no pueda darlo (p. ej. un Administrador visto por otro).
  const opciones = roles.includes(usuario.rol as RolImpuestos) ? roles : ([usuario.rol, ...roles] as RolImpuestos[]);
  const [rol, setRol] = useState<string>(usuario.rol);
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const sinCambio = rol === usuario.rol || !roles.includes(rol as RolImpuestos);

  async function guardar() {
    if (enviando || sinCambio) return;
    setEnviando(true);
    setError(null);
    try {
      await cambiarRolUsuario(usuario.id, rol as RolImpuestos, empresa);
      onCambiado(`${usuario.nombre} ahora es ${etiquetaRol(rol)}.`);
    } catch (e) {
      setError(mensajeErrorFijo(e, "No se pudo cambiar el rol. Intenta de nuevo.", ERRORES_USUARIOS));
      setEnviando(false);
    }
  }

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={enviando}
      closeOnBackdrop={false}
      title={`Cambiar rol de ${usuario.nombre}`}
      icono="rol"
      size="md"
      footer={
        <div className="flex flex-wrap justify-end gap-3">
          <Button variant="secondary" onClick={onCerrar} disabled={enviando}>
            Cancelar
          </Button>
          <Button onClick={() => void guardar()} disabled={sinCambio} cargando={enviando}>
            Guardar rol
          </Button>
        </div>
      }
    >
      <fieldset className="flex flex-col gap-2">
        <legend className="sr-only">Rol</legend>
        {opciones.map((r) => (
          <label key={r} className="flex min-h-11 cursor-pointer items-start gap-3 rounded-[9px] border border-border px-3 py-2 has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-60">
            <input
              type="radio"
              name={`${id}-rol`}
              value={r}
              checked={rol === r}
              disabled={!roles.includes(r)}
              onChange={() => setRol(r)}
              className={`mt-1 ${MARCA_CLS}`}
            />
            <span className="flex flex-col">
              <span className="text-sm font-semibold text-flit-primary">{ETIQUETA_ROL[r] ?? etiquetaRol(r)}</span>
              {DESCRIPCION_ROL[r] && <span className={AYUDA_CLS}>{DESCRIPCION_ROL[r]}</span>}
            </span>
          </label>
        ))}
      </fieldset>
      {error && (
        <div className="mt-4">
          <InlineAlert tone="error" title="No se pudo cambiar el rol">
            {error}
          </InlineAlert>
        </div>
      )}
    </Modal>
  );
}

export type AccionEstado = "SUSPENDER" | "REACTIVAR";

export interface DialogoEstadoUsuarioProps {
  usuario: UsuarioImpuestos;
  accion: AccionEstado;
  empresa: string | null;
  onCerrar: () => void;
  /** Hecho (o 409 ROL_YA_VIGENTE al reactivar): aviso para la región de la pantalla; quien abre relee la lista. */
  onHecho: (aviso: string) => void;
}

/**
 * Confirmación de «Suspender» y «Reactivar» (spec-visual §3.8, molde DialogoConfirmarEstado de Diagnóstico). Suspender
 * solo quita el acceso a Impuestos. Foco inicial en «Cancelar»; la acción destructiva va con el botón de alerta. Un error
 * de la API queda dentro del diálogo, que no se cierra.
 */
export function DialogoEstadoUsuario({ usuario, accion, empresa, onCerrar, onHecho }: DialogoEstadoUsuarioProps) {
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const suspender = accion === "SUSPENDER";

  async function confirmar() {
    if (enviando) return;
    setEnviando(true);
    setError(null);
    try {
      if (suspender) await suspenderUsuario(usuario.id, empresa);
      else await reactivarUsuario(usuario.id, empresa);
      onHecho(suspender ? `${usuario.nombre} quedó suspendido en Impuestos.` : `${usuario.nombre} volvió a tener acceso a Impuestos.`);
    } catch (e) {
      // 409 ROL_YA_VIGENTE al reactivar: la plataforma ya le dio un rol; se relee la lista con el literal como aviso.
      if (!suspender && e instanceof ApiError && e.code === "ROL_YA_VIGENTE") {
        onHecho(ERRORES_USUARIOS.ROL_YA_VIGENTE);
        return;
      }
      setError(mensajeErrorFijo(e, suspender ? "No se pudo suspender al usuario. Intenta de nuevo." : "No se pudo reactivar al usuario. Intenta de nuevo.", ERRORES_USUARIOS));
      setEnviando(false);
    }
  }

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={enviando}
      title={suspender ? "Suspender usuario" : "Reactivar usuario"}
      icono={suspender ? "alerta" : "check"}
      size="sm"
      footer={
        <div className="flex flex-wrap justify-end gap-3">
          <Button variant="secondary" onClick={onCerrar} disabled={enviando} data-autofocus>
            Cancelar
          </Button>
          <Button variant={suspender ? "alerta" : "primary"} onClick={() => void confirmar()} cargando={enviando}>
            {suspender ? "Suspender" : "Reactivar"}
          </Button>
        </div>
      }
    >
      <p className="text-sm text-flit-primary">
        {suspender ? "Pierde el acceso a Impuestos. Su cuenta y los demás productos no cambian." : `${usuario.nombre} podrá volver a entrar a Impuestos con su rol de antes.`}
      </p>
      {error && (
        <div className="mt-4">
          <InlineAlert tone="error" title={suspender ? "No se pudo suspender" : "No se pudo reactivar"}>
            {error}
          </InlineAlert>
        </div>
      )}
    </Modal>
  );
}
