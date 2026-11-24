"use client";

import { useId, useState, type FormEvent } from "react";
import { Button } from "@flit/ui/Button";
import { ICONO_TONO, rutaKit } from "@flit/ui/iconos-mapas";
import { deleteTenantProductCredential, setTenantProductCredential } from "@/lib/api/platform";
import { ApiError } from "@/lib/api/types";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { FIELD_CLASS, FIELD_LABEL, HELP_TEXT } from "../ConfigUi";

// Ola impuesto-fuente-real (diseno.md §1.4–§1.5 y §5.1) — API key de impuesto vehicular de Diagnóstico por compañía.
// La UI nunca recibe ni muestra el valor guardado: solo sabe si está «Configurada». Lo escrito vive solo en el estado
// local del formulario y se vacía al guardar, cancelar o cerrar; no se registra en consola ni se persiste en el navegador.

const PRODUCT_CODE = "diagnostico";
const CREDENTIAL_CODE = "impuesto_api_key";
/** Misma regla que el backend (contrato A2): ASCII imprimible sin espacios, de 16 a 256 caracteres. */
const PATRON_LLAVE = /^[\x21-\x7E]{16,256}$/;
const MSG_LONGITUD = "La API key debe tener entre 16 y 256 caracteres, sin espacios.";
const AYUDA =
  "Opcional. Sin ella, la consulta de impuesto de esta compañía queda en error «Credencial de impuesto no configurada».";

function mensajeError(e: unknown, porDefecto: string): string {
  if (e instanceof ApiError && e.status === 400 && (e.body as { code?: string } | undefined)?.code === "CREDENTIAL_INVALID") {
    return MSG_LONGITUD;
  }
  return e instanceof Error && e.message ? e.message : porDefecto;
}

export interface CredencialImpuestoDiagnosticoProps {
  tenantId: string;
  configurada: boolean;
  /** Abre el formulario sin que lo pidan (al encender Diagnóstico sin llave); el botón de cierre dice «Ahora no». */
  sugerirAbierto?: boolean;
  /** Se llama tras guardar o borrar con éxito para releer los productos. */
  onChanged: () => Promise<void> | void;
}

export function CredencialImpuestoDiagnostico({ tenantId, configurada, sugerirAbierto = false, onChanged }: CredencialImpuestoDiagnosticoProps) {
  const uid = useId();
  const inputId = `${uid}-llave`;
  const ayudaId = `${uid}-ayuda`;
  const errorCampoId = `${uid}-error`;
  const tituloBorrarId = `${uid}-borrar`;

  const [abierto, setAbierto] = useState(sugerirAbierto);
  const [porSugerencia, setPorSugerencia] = useState(sugerirAbierto);
  const [valor, setValor] = useState("");
  const [visible, setVisible] = useState(false);
  const [errorCampo, setErrorCampo] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [confirmarBorrar, setConfirmarBorrar] = useState(false);
  const [borrando, setBorrando] = useState(false);

  // Si la sugerencia llega después de montar (se acaba de encender Diagnóstico), se abre en el mismo render.
  const [sugerenciaPrevia, setSugerenciaPrevia] = useState(sugerirAbierto);
  if (sugerirAbierto !== sugerenciaPrevia) {
    setSugerenciaPrevia(sugerirAbierto);
    if (sugerirAbierto) {
      setAbierto(true);
      setPorSugerencia(true);
    }
  }

  const cerrar = () => {
    setAbierto(false);
    setPorSugerencia(false);
    setValor("");
    setVisible(false);
    setErrorCampo(null);
  };

  const abrir = () => {
    setExito(null);
    setError(null);
    setConfirmarBorrar(false);
    setPorSugerencia(false);
    setAbierto(true);
  };

  const guardar = async (ev: FormEvent<HTMLFormElement>) => {
    ev.preventDefault();
    setError(null);
    setExito(null);
    if (!PATRON_LLAVE.test(valor)) {
      setErrorCampo(MSG_LONGITUD);
      return;
    }
    setErrorCampo(null);
    setGuardando(true);
    try {
      await setTenantProductCredential(tenantId, PRODUCT_CODE, CREDENTIAL_CODE, valor);
      cerrar();
      setExito("API key de impuesto guardada.");
      await onChanged();
    } catch (e) {
      setError(mensajeError(e, "No se pudo guardar la API key de impuesto."));
    } finally {
      setGuardando(false);
    }
  };

  const borrar = async () => {
    setError(null);
    setExito(null);
    setBorrando(true);
    try {
      await deleteTenantProductCredential(tenantId, PRODUCT_CODE, CREDENTIAL_CODE);
      setConfirmarBorrar(false);
      setExito("API key de impuesto borrada.");
      await onChanged();
    } catch (e) {
      setConfirmarBorrar(false);
      setError(mensajeError(e, "No se pudo borrar la API key de impuesto."));
    } finally {
      setBorrando(false);
    }
  };

  const ocupado = guardando || borrando;

  return (
    <div role="group" aria-label="API key de impuesto de Diagnóstico" className="ml-12 space-y-2 rounded-xl border px-4 py-3 text-xs">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-semibold">API key de impuesto:</span>
        <StatusBadge
          tone={configurada ? "success" : "neutral"}
          icono={rutaKit(configurada ? ICONO_TONO.ok : ICONO_TONO[""])}
          ariaLabel={`API key de impuesto: ${configurada ? "Configurada" : "No configurada"}`}
          label={configurada ? "Configurada" : "No configurada"}
        />
        {!abierto && (
          <div className="ml-auto flex gap-2">
            <Button variant="secondary" size="sm" onClick={abrir} disabled={ocupado}>
              {configurada ? "Reemplazar" : "Configurar"}
            </Button>
            {configurada && (
              <Button
                variant="alerta"
                size="sm"
                onClick={() => {
                  setExito(null);
                  setError(null);
                  setConfirmarBorrar(true);
                }}
                disabled={ocupado}
              >
                Borrar
              </Button>
            )}
          </div>
        )}
      </div>

      {abierto && (
        <form onSubmit={(ev) => void guardar(ev)} noValidate className="space-y-2">
          <label htmlFor={inputId} className={FIELD_LABEL}>
            API key de impuesto vehicular
          </label>
          <div className="flex gap-2">
            <input
              id={inputId}
              type={visible ? "text" : "password"}
              autoComplete="off"
              spellCheck={false}
              autoCapitalize="none"
              autoCorrect="off"
              value={valor}
              onChange={(e) => setValor(e.target.value)}
              aria-invalid={errorCampo ? "true" : undefined}
              aria-describedby={errorCampo ? `${ayudaId} ${errorCampoId}` : ayudaId}
              className={FIELD_CLASS}
              style={errorCampo ? { borderColor: "var(--destructive)" } : undefined}
              disabled={guardando}
            />
            <Button
              variant="ghost"
              size="sm"
              aria-pressed={visible}
              onClick={() => setVisible((v) => !v)}
              className="shrink-0"
            >
              {visible ? "Ocultar API key" : "Mostrar API key"}
            </Button>
          </div>
          <p id={ayudaId} className={HELP_TEXT}>
            {AYUDA}
          </p>
          {errorCampo && (
            <p id={errorCampoId} className="font-medium" style={{ color: "var(--destructive)" }}>
              {errorCampo}
            </p>
          )}
          <div className="flex gap-2">
            <Button type="submit" size="sm" cargando={guardando}>
              {guardando ? "Guardando…" : "Guardar"}
            </Button>
            <Button variant="navy" size="sm" onClick={cerrar} disabled={guardando}>
              {porSugerencia ? "Ahora no" : "Cancelar"}
            </Button>
          </div>
        </form>
      )}

      {confirmarBorrar && (
        <div role="alertdialog" aria-labelledby={tituloBorrarId} className="rounded-xl border px-4 py-3" style={{ borderColor: "var(--destructive)" }}>
          <p id={tituloBorrarId} className="font-semibold">
            ¿Borrar la API key de impuesto de esta compañía?
          </p>
          <p className="mt-1 opacity-70">
            Mientras no se configure otra, la consulta de impuesto de esta compañía queda en error «Credencial de impuesto no
            configurada».
          </p>
          <div className="mt-3 flex gap-2">
            <Button variant="alerta" size="sm" cargando={borrando} onClick={() => void borrar()}>
              Borrar API key
            </Button>
            <Button variant="navy" size="sm" onClick={() => setConfirmarBorrar(false)} disabled={borrando}>
              Cancelar
            </Button>
          </div>
        </div>
      )}

      <p role="status" aria-live="polite" aria-atomic="true" className="font-medium">
        {exito ?? ""}
      </p>
      {error && (
        <p role="alert" className="font-medium" style={{ color: "var(--destructive)" }}>
          {error}
        </p>
      )}
    </div>
  );
}
