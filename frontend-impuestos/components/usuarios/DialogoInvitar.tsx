"use client";

import { useId, useRef, useState, type FormEvent } from "react";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Modal } from "@flit/ui/Modal";
import { AYUDA_CLS, CAMPO_CLS, CAMPO_INVALIDO_CLS, ERROR_CLS, ETIQUETA_CLS, MARCA_CLS } from "@/components/comun/estilos";
import { campoDeError, mensajeErrorFijo } from "@/lib/errores";
import { DESCRIPCION_ROL, ERRORES_USUARIOS, ETIQUETA_ROL, invitarUsuario, type RolImpuestos } from "@/lib/usuarios/api";

type Campo = "nombre" | "correo" | "rol";
type Errores = Partial<Record<Campo, string>>;

const NOMBRE_CAMPO: Readonly<Record<Campo, string>> = { nombre: "Nombre completo", correo: "Correo electrónico", rol: "Rol" };
/** Campo del cuerpo (InvitacionUsuario) → campo del formulario, para un 400/422 con `campo`. */
const CAMPO_DE_API: Readonly<Record<string, Campo>> = { nombre: "nombre", email: "correo", rol: "rol" };

/** Forma mínima de un correo (la API vuelve a validarlo: 400 CORREO_INVALIDO). */
const CORREO_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
/** Límites del contrato (InvitacionUsuario). */
const NOMBRE_MAXIMO = 150;
const CORREO_MAXIMO = 254;

function validar(nombre: string, correo: string, rol: RolImpuestos | null): Errores {
  const e: Errores = {};
  if (!nombre.trim()) e.nombre = "Escribe el nombre completo.";
  if (!correo.trim()) e.correo = "Escribe el correo electrónico.";
  else if (!CORREO_RE.test(correo.trim())) e.correo = "Escribe un correo electrónico válido, como nombre@empresa.com.";
  if (!rol) e.rol = "Elige el rol.";
  return e;
}

export interface DialogoInvitarProps {
  /** Compañía elegida por el Super Admin, o null para la de la sesión. */
  empresa: string | null;
  /** Roles que quien invita puede dar (`rolesAsignables`). */
  roles: readonly RolImpuestos[];
  onCerrar: () => void;
  /** 201 recibido: quien abre cierra, avisa y relee la lista. */
  onInvitado: () => void;
}

/**
 * «Invitar usuario» (spec-visual §3.8, molde ModalInvitarUsuario de Diagnóstico): Nombre completo, Correo electrónico y
 * Rol (radios con descripción, solo los que se pueden dar). Errores por campo con aria-invalid y aria-describedby y un
 * resumen que recibe el foco. Los errores de la API (literales del contrato) quedan dentro del diálogo, que no se cierra
 * ni con un clic fuera.
 */
export function DialogoInvitar({ empresa, roles, onCerrar, onInvitado }: DialogoInvitarProps) {
  const id = useId();
  // Mínimo privilegio: Gestor Impuestos va marcado de entrada si se puede dar.
  const [rol, setRol] = useState<RolImpuestos | null>(roles.includes("gestor_impuestos") ? "gestor_impuestos" : (roles[0] ?? null));
  const [nombre, setNombre] = useState("");
  const [correo, setCorreo] = useState("");
  const [errores, setErrores] = useState<Errores>({});
  const [errorGeneral, setErrorGeneral] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const resumenRef = useRef<HTMLDivElement>(null);

  const errorId = (c: Campo) => `${id}-${c}-error`;

  async function enviar(ev?: FormEvent) {
    ev?.preventDefault();
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
      await invitarUsuario({ email: correo.trim(), nombre: nombre.trim(), rol }, empresa);
      onInvitado();
    } catch (e) {
      const texto = mensajeErrorFijo(e, "No se pudo enviar la invitación. Intenta de nuevo.", ERRORES_USUARIOS);
      const campo = CAMPO_DE_API[campoDeError(e) ?? ""];
      if (campo) setErrores({ [campo]: texto });
      else setErrorGeneral(texto);
      setEnviando(false);
    }
  }

  const listaErrores = (Object.keys(NOMBRE_CAMPO) as Campo[]).filter((c) => errores[c]);

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={enviando}
      closeOnBackdrop={false}
      title="Invitar usuario"
      icono="usuarios"
      size="md"
      description="Le llegará un correo para entrar a Impuestos con el rol que elijas."
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
      <form id={`${id}-form`} noValidate onSubmit={(ev) => void enviar(ev)} className="flex flex-col gap-4">
        {/* Una sola región role="alert" (el contenedor); el InlineAlert va sin región propia. */}
        {listaErrores.length > 0 && (
          <div ref={resumenRef} tabIndex={-1} role="alert" className="outline-none">
            <InlineAlert tone="error" title="Revisa los datos de la invitación" vivo={false}>
              <ul className="list-disc pl-5">
                {listaErrores.map((c) => (
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
            aria-describedby={errores.nombre ? errorId("nombre") : undefined}
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
            autoCapitalize="none"
            value={correo}
            maxLength={CORREO_MAXIMO}
            onChange={(e) => setCorreo(e.target.value)}
            aria-invalid={errores.correo ? true : undefined}
            aria-describedby={errores.correo ? errorId("correo") : undefined}
            className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS}`}
          />
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
