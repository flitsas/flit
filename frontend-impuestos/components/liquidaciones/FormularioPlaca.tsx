"use client";

import { useId, useRef, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, CAMPO_CLS, CAMPO_INVALIDO_CLS, ERROR_CLS, ETIQUETA_CLS } from "@/components/comun/estilos";
import { ApiError } from "@/lib/api";
import { hrefConEmpresa } from "@/lib/empresaSeleccionada";
import { ERRORES_EMPRESA, esErrorDeEmpresa, mensajeErrorFijo } from "@/lib/errores";
import { crearIndividual } from "@/lib/liquidaciones/api";
import { ERRORES_LIQUIDACIONES, PANTALLA } from "@/lib/liquidaciones/estados";
import { normalizarPlaca, placaValida } from "@/lib/liquidaciones/formato";

type Error_ = { tipo: "campo"; texto: string } | { tipo: "aviso"; texto: string } | { tipo: "otro" } | null;

export interface FormularioPlacaProps {
  empresa: string | null;
  /** Literal del contrato por el que no se puede liquidar (suspendido / no listo); null = se puede. */
  bloqueo: string | null;
  /**
   * Super Admin: liquida para la compañía de ?empresa (contrato 1.1.0, delta 2). Sin compañía elegida, el formulario
   * queda deshabilitado con el literal EMPRESA_REQUERIDA («Elige una compañía.») y no llama a la API.
   */
  esSuperAdmin?: boolean;
  /** id de un texto ya visible con el motivo del bloqueo; sin él, el formulario pinta el suyo. */
  motivoId?: string;
  placaInicial?: string;
  autoFocus?: boolean;
  /** Botón «Cancelar» (pantalla /liquidaciones/nueva). */
  onCancelar?: () => void;
}

/**
 * Liquidación por placa (F2a-CF1, D-ENTRADA): el usuario da solo la placa; el resto sale del RUNT. Se normaliza como el
 * servidor y se valida antes de enviar (literal PLACA_INVALIDA junto al campo, con aria-invalid). Con el 201 navega al
 * detalle de la placa (`items[0]`). 409 APROVISIONAMIENTO_NO_LISTO / PRODUCTO_SUSPENDIDO en aviso warning con el literal
 * del contrato; otro error, «No se pudo crear la liquidación» + Reintentar. Nunca se pierde lo digitado. Los errores de
 * compañía (400 EMPRESA_REQUERIDA, 404 EMPRESA_NO_ENCONTRADA, 403 EMPRESA_SOLO_SUPER_ADMIN) van en el mismo aviso con su
 * literal: nunca a la 403.
 */
export function FormularioPlaca({
  empresa,
  bloqueo: bloqueoSesion,
  esSuperAdmin = false,
  motivoId,
  placaInicial = "",
  autoFocus = false,
  onCancelar,
}: FormularioPlacaProps) {
  // Para el Super Admin sin compañía manda ese motivo: su sesión no dice nada de la compañía sobre la que liquidaría.
  const bloqueo = esSuperAdmin && !empresa ? ERRORES_EMPRESA.EMPRESA_REQUERIDA : bloqueoSesion;
  const router = useRouter();
  const base = useId();
  const idCampo = `${base}-placa`;
  const idAyuda = `${base}-ayuda`;
  const idError = `${base}-error`;
  const idMotivo = motivoId ?? `${base}-motivo`;
  const [placa, setPlaca] = useState(placaInicial);
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<Error_>(null);
  const campo = useRef<HTMLInputElement>(null);

  async function enviar() {
    const normalizada = normalizarPlaca(placa);
    if (!placaValida(normalizada)) {
      setError({ tipo: "campo", texto: ERRORES_LIQUIDACIONES.PLACA_INVALIDA });
      campo.current?.focus();
      return;
    }
    setEnviando(true);
    setError(null);
    try {
      const creada = await crearIndividual(normalizada, { empresa });
      const itemId = creada.items?.[0]?.id;
      router.push(hrefConEmpresa(itemId ? `/liquidaciones/${creada.id}/items/${itemId}` : `/liquidaciones/${creada.id}`, empresa));
    } catch (e) {
      setEnviando(false);
      if (e instanceof ApiError && e.code === "PLACA_INVALIDA") setError({ tipo: "campo", texto: ERRORES_LIQUIDACIONES.PLACA_INVALIDA });
      else if (e instanceof ApiError && (e.code === "APROVISIONAMIENTO_NO_LISTO" || e.code === "PRODUCTO_SUSPENDIDO" || e.status === 403 || esErrorDeEmpresa(e)))
        setError({ tipo: "aviso", texto: mensajeErrorFijo(e, "", ERRORES_LIQUIDACIONES) });
      else setError({ tipo: "otro" });
    }
  }

  function alEnviar(e: FormEvent) {
    e.preventDefault();
    if (bloqueo || enviando) return;
    void enviar();
  }

  const describe = [idAyuda, error?.tipo === "campo" ? idError : null].filter(Boolean).join(" ");
  const describeBoton = bloqueo ? idMotivo : undefined;

  return (
    <form noValidate aria-label={PANTALLA.accionLiquidarPlaca} onSubmit={alEnviar} className="flex flex-col gap-3">
      {bloqueo && !motivoId ? (
        <p id={idMotivo} className={AYUDA_CLS}>
          {bloqueo}
        </p>
      ) : null}
      <div className="flex flex-col gap-1">
        <label htmlFor={idCampo} className={ETIQUETA_CLS}>
          Placa
        </label>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-start">
          <input
            ref={campo}
            id={idCampo}
            name="placa"
            type="text"
            inputMode="text"
            autoComplete="off"
            maxLength={10}
            // Pantalla de un solo campo (spec-visual §3.3): enfocado al cargar.
            autoFocus={autoFocus}
            value={placa}
            disabled={enviando}
            aria-invalid={error?.tipo === "campo" ? true : undefined}
            aria-describedby={describe}
            onChange={(e) => {
              setPlaca(e.target.value);
              if (error?.tipo === "campo") setError(null);
            }}
            className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS} uppercase tracking-[0.08em] sm:max-w-xs`}
          />
          <div className="flex flex-wrap gap-2">
            <Button type="submit" size="sm" cargando={enviando} disabled={Boolean(bloqueo)} aria-describedby={describeBoton}>
              {PANTALLA.accionLiquidarPlaca}
            </Button>
            {onCancelar ? (
              <Button type="button" variant="secondary" size="sm" onClick={onCancelar}>
                Cancelar
              </Button>
            ) : null}
          </div>
        </div>
        {error?.tipo === "campo" ? (
          <p id={idError} className={ERROR_CLS}>
            {error.texto}
          </p>
        ) : null}
        <p id={idAyuda} className={AYUDA_CLS}>
          Solo la placa: los datos del vehículo y del propietario se consultan en el RUNT.
        </p>
        {enviando ? <p className={AYUDA_CLS}>Creando la liquidación…</p> : null}
      </div>
      {error?.tipo === "aviso" ? (
        <InlineAlert tone="warning" title="No se pudo liquidar la placa">
          {error.texto}
        </InlineAlert>
      ) : null}
      {error?.tipo === "otro" ? (
        <InlineAlert
          tone="error"
          title="No se pudo crear la liquidación"
          action={
            <button type="button" onClick={() => void enviar()} className={BOTON_REINTENTAR_CLS}>
              Reintentar
            </button>
          }
        >
          Lo que escribiste sigue en el campo. Intenta de nuevo.
        </InlineAlert>
      ) : null}
    </form>
  );
}
