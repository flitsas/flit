"use client";

import { useId, useRef, useState, type FormEvent } from "react";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Modal } from "@flit/ui/Modal";
import { AYUDA_CLS, CAMPO_CLS, ETIQUETA_CLS, MARCA_CLS } from "@/components/configuracion/estilos";
import { CAMPO_INVALIDO_CLS, ERROR_CLS } from "@/components/parametrizacion/estilos";
import { campoDeError, invitarUsuario, traducirErrorUsuarios } from "@/lib/usuarios/api";
import { CORREO_MAXIMO, DESCRIPCION_ROL, ETIQUETA_ROL, NOMBRE_MAXIMO, ROLES_DIAGNOSTICO, type RolDiagnostico } from "@/lib/usuarios/tipos";

type Campo = "nombre" | "correo" | "rol";
type Errores = Partial<Record<Campo, string>>;

const NOMBRE_CAMPO: Readonly<Record<Campo, string>> = { nombre: "Nombre completo", correo: "Correo electrónico", rol: "Rol" };

/** Forma mínima de un correo (la API vuelve a validarlo: 400 CORREO_INVALIDO). */
const CORREO_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export interface ModalInvitarUsuarioProps {
  abierto: boolean;
  /** Compañía del backoffice (SuperAdmin) o null para la del token. */
  empresa: string | null;
  /** `roles_asignables` de la lista: los únicos que se ofrecen (SP-11=a). */
  rolesAsignables: RolDiagnostico[];
  onCerrar: () => void;
  /** 202 recibido: quien abre muestra el aviso uniforme, refresca la lista y cierra. */
  onInvitado: () => void;
}

function validar(nombre: string, correo: string, rol: RolDiagnostico | null): Errores {
  const e: Errores = {};
  if (!nombre.trim()) e.nombre = "Escribe el nombre completo.";
  else if (nombre.trim().length > NOMBRE_MAXIMO) e.nombre = `El nombre admite hasta ${NOMBRE_MAXIMO} caracteres.`;
  if (!correo.trim()) e.correo = "Escribe el correo electrónico.";
  else if (correo.trim().length > CORREO_MAXIMO || !CORREO_RE.test(correo.trim())) e.correo = "Escribe un correo electrónico válido, como nombre@empresa.com.";
  if (!rol) e.rol = "Elige el rol.";
  return e;
}

/**
 * «Invitar usuario» (HU #14588, spec-ui §10.3): Nombre completo, Correo electrónico y Rol, con los roles que el servidor
 * deja dar (`roles_asignables`, por código: SP-10/SP-11). Errores por campo con aria-invalid y aria-describedby y un
 * resumen arriba que recibe el foco. Los errores de la API quedan dentro del diálogo, que no se cierra. La respuesta es
 * el 202 uniforme (SP-15): no se distingue si el correo ya existía.
 */
export function ModalInvitarUsuario({ abierto, empresa, rolesAsignables, onCerrar, onInvitado }: ModalInvitarUsuarioProps) {
  const id = useId();
  const roles = ROLES_DIAGNOSTICO.filter((r) => rolesAsignables.includes(r));
  // Mínimo privilegio: si se puede dar «Usuario de Diagnóstico», va marcado de entrada.
  const inicial = roles.includes("usuario_diagnostico") ? "usuario_diagnostico" : (roles[0] ?? null);
  const [nombre, setNombre] = useState("");
  const [correo, setCorreo] = useState("");
  const [rol, setRol] = useState<RolDiagnostico | null>(inicial);
  const [errores, setErrores] = useState<Errores>({});
  const [errorGeneral, setErrorGeneral] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const resumenRef = useRef<HTMLDivElement>(null);

  const errorId = (c: Campo) => `${id}-${c}-error`;
  const ayudaId = (c: Campo) => `${id}-${c}-ayuda`;
  const describe = (c: Campo, ayuda = false) => [ayuda ? ayudaId(c) : null, errores[c] ? errorId(c) : null].filter(Boolean).join(" ") || undefined;

  async function enviar(e?: FormEvent) {
    e?.preventDefault();
    if (enviando) return;
    const encontrados = validar(nombre, correo, rol);
    setErrores(encontrados);
    setErrorGeneral(null);
    if (Object.keys(encontrados).length > 0 || !rol) {
      requestAnimationFrame(() => resumenRef.current?.focus());
      return;
    }
    setEnviando(true);
    try {
      await invitarUsuario(empresa, { correo, nombre, rol });
      onInvitado();
    } catch (error) {
      const texto = traducirErrorUsuarios(error, "No se pudo enviar la invitación. Intenta de nuevo.");
      const campo = campoDeError(error);
      if (campo) setErrores({ [campo]: texto });
      else setErrorGeneral(texto);
      setEnviando(false);
    }
  }

  const listaErrores = (Object.keys(NOMBRE_CAMPO) as Campo[]).filter((c) => errores[c]).map((c) => [c, errores[c]!] as const);

  return (
    <Modal
      open={abierto}
      onClose={onCerrar}
      busy={enviando}
      title="Invitar usuario"
      icono="usuarios"
      size="md"
      description="Le llegará un correo para entrar a Diagnóstico con el rol que elijas."
      footer={
        <div className="flex flex-wrap justify-end gap-3">
          <Button variant="secondary" onClick={onCerrar} disabled={enviando}>
            Cancelar
          </Button>
          <Button type="submit" form={`${id}-form`} cargando={enviando}>
            Enviar invitación
          </Button>
        </div>
      }
    >
      <form id={`${id}-form`} noValidate onSubmit={enviar} className="flex flex-col gap-4">
        {/* Una sola región role="alert" (el contenedor); el InlineAlert va sin región propia (CR-3). */}
        {listaErrores.length > 0 && (
          <div ref={resumenRef} tabIndex={-1} role="alert" className="outline-none">
            <InlineAlert tone="error" title="Revisa los datos de la invitación" vivo={false}>
              {/* El resumen nombra los campos; el motivo va junto a cada uno (aria-describedby), sin repetirlo aquí. */}
              <ul className="list-disc pl-5">
                {listaErrores.map(([c]) => (
                  <li key={c}>
                    <a href={`#${c === "rol" ? `${id}-rol-0` : `${id}-${c}`}`} className="underline underline-offset-2">
                      {NOMBRE_CAMPO[c]}
                    </a>
                  </li>
                ))}
              </ul>
            </InlineAlert>
          </div>
        )}
        <div className="flex flex-col gap-1">
          <label htmlFor={`${id}-nombre`} className={ETIQUETA_CLS}>
            Nombre completo
          </label>
          <input
            id={`${id}-nombre`}
            type="text"
            autoComplete="off"
            value={nombre}
            maxLength={NOMBRE_MAXIMO}
            onChange={(e) => setNombre(e.target.value)}
            aria-invalid={errores.nombre ? true : undefined}
            aria-describedby={describe("nombre")}
            className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS}`}
          />
          {errores.nombre && (
            <p id={errorId("nombre")} className={ERROR_CLS}>
              {errores.nombre}
            </p>
          )}
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor={`${id}-correo`} className={ETIQUETA_CLS}>
            Correo electrónico
          </label>
          <input
            id={`${id}-correo`}
            type="text"
            inputMode="email"
            autoComplete="off"
            spellCheck={false}
            value={correo}
            maxLength={CORREO_MAXIMO}
            onChange={(e) => setCorreo(e.target.value)}
            aria-invalid={errores.correo ? true : undefined}
            aria-describedby={describe("correo", true)}
            className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS}`}
          />
          <p id={ayudaId("correo")} className={AYUDA_CLS}>
            Si ya tiene cuenta en la compañía, recibirá el acceso a Diagnóstico.
          </p>
          {errores.correo && (
            <p id={errorId("correo")} className={ERROR_CLS}>
              {errores.correo}
            </p>
          )}
        </div>
        <fieldset className="flex flex-col gap-2" aria-describedby={errores.rol ? errorId("rol") : undefined}>
          <legend className={`${ETIQUETA_CLS} mb-1`}>Rol</legend>
          {roles.map((r, i) => (
            <label key={r} className="flex min-h-11 cursor-pointer items-start gap-3 rounded-[9px] border border-border px-3 py-2">
              <input id={`${id}-rol-${i}`} type="radio" name={`${id}-rol`} value={r} checked={rol === r} onChange={() => setRol(r)} className={`mt-1 ${MARCA_CLS}`} />
              <span className="flex flex-col">
                <span className="text-sm font-semibold text-flit-primary">{ETIQUETA_ROL[r]}</span>
                <span className={AYUDA_CLS}>{DESCRIPCION_ROL[r]}</span>
              </span>
            </label>
          ))}
          {errores.rol && (
            <p id={errorId("rol")} className={ERROR_CLS}>
              {errores.rol}
            </p>
          )}
        </fieldset>
        {errorGeneral && (
          <InlineAlert tone="error" title="No se pudo enviar la invitación">
            {errorGeneral}
          </InlineAlert>
        )}
      </form>
    </Modal>
  );
}
