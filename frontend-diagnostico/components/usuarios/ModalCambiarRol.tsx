"use client";

import { useId, useState } from "react";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Modal } from "@flit/ui/Modal";
import { AYUDA_CLS, ETIQUETA_CLS, MARCA_CLS } from "@/components/configuracion/estilos";
import { cambiarRolUsuario, traducirErrorUsuarios } from "@/lib/usuarios/api";
import { DESCRIPCION_ROL, ETIQUETA_ROL, ROLES_DIAGNOSTICO, type RolDiagnostico, type UsuarioDiagnostico } from "@/lib/usuarios/tipos";

export interface ModalCambiarRolProps {
  usuario: UsuarioDiagnostico;
  empresa: string | null;
  /** `roles_asignables` de la lista (SP-11=a). El rol actual se muestra marcado aunque el actor no pueda darlo. */
  rolesAsignables: RolDiagnostico[];
  onCerrar: () => void;
  /** 200 con la fila actualizada (acciones recalculadas por el servidor). */
  onCambiado: (fila: UsuarioDiagnostico) => void;
}

/**
 * «Cambiar rol de {Nombre}» (spec-ui §10.3): radios con los roles de Diagnóstico que el actor puede dar; «Guardar rol»
 * deshabilitado mientras no cambie. Solo toca el rol del producto Diagnóstico (SP-9=a). Los errores de la API (último
 * administrador, objetivo protegido, rol no asignable) quedan dentro del diálogo con su texto fijo.
 */
export function ModalCambiarRol({ usuario, empresa, rolesAsignables, onCerrar, onCambiado }: ModalCambiarRolProps) {
  const id = useId();
  const roles = ROLES_DIAGNOSTICO.filter((r) => r === usuario.rol || rolesAsignables.includes(r));
  const [rol, setRol] = useState<RolDiagnostico>(usuario.rol);
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  async function guardar() {
    if (guardando || rol === usuario.rol) return;
    setGuardando(true);
    setError(null);
    try {
      onCambiado(await cambiarRolUsuario(empresa, usuario.id, rol));
    } catch (e) {
      setError(traducirErrorUsuarios(e, "No se pudo cambiar el rol. Intenta de nuevo."));
      setGuardando(false);
    }
  }

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={guardando}
      title={`Cambiar rol de ${usuario.nombre}`}
      icono="rol"
      size="sm"
      footer={
        <div className="flex flex-wrap justify-end gap-3">
          <Button variant="secondary" onClick={onCerrar} disabled={guardando}>
            Cancelar
          </Button>
          <Button onClick={() => void guardar()} disabled={rol === usuario.rol} cargando={guardando}>
            Guardar rol
          </Button>
        </div>
      }
    >
      <fieldset className="flex flex-col gap-2">
        <legend className={`${ETIQUETA_CLS} mb-1`}>Rol en Diagnóstico</legend>
        {roles.map((r) => {
          const asignable = rolesAsignables.includes(r);
          return (
            <label key={r} className="flex min-h-11 cursor-pointer items-start gap-3 rounded-[9px] border border-border px-3 py-2">
              <input
                type="radio"
                name={`${id}-rol`}
                value={r}
                checked={rol === r}
                disabled={!asignable && r !== usuario.rol}
                onChange={() => setRol(r)}
                className={`mt-1 ${MARCA_CLS}`}
              />
              <span className="flex flex-col">
                <span className="text-sm font-semibold text-flit-primary">{ETIQUETA_ROL[r]}</span>
                <span className={AYUDA_CLS}>{DESCRIPCION_ROL[r]}</span>
              </span>
            </label>
          );
        })}
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
