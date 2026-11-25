"use client";

import { useId, useState, type FormEvent } from "react";
import { Button } from "@flit/ui/Button";
import { ICONO_TONO, rutaKit } from "@flit/ui/iconos-mapas";
import {
  deleteTenantProductCredential,
  obtenerCredencialImpuestoDiagnostico,
  setTenantProductCredential,
  type CredencialImpuestoObtenida,
} from "@/lib/api/platform";
import { ApiError } from "@/lib/api/types";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { InlineAlert } from "@/components/atom/InlineAlert";
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

// HU #14711 · AC-DC-13 — alta idempotente con el proveedor. La respuesta solo trae metadatos (prefijo y banderas); la UI no
// recibe la llave ni el secreto del webhook, y en estado guarda solo esa copia de metadatos (AC-DC-12).
const MSG_NIT = "La empresa no tiene un NIT válido; complétalo o ingresa la llave manualmente";
const MSG_PROVEEDOR = "El proveedor no respondió; intenta de nuevo";
const MENSAJES_OBTENER: Record<string, string> = {
  NIT_AUSENTE: MSG_NIT,
  NIT_AMBIGUO: MSG_NIT,
  NIT_IGUAL_A_CABEZA: MSG_NIT,
  PRODUCTO_NO_HABILITADO: "Diagnóstico no está habilitado para esta empresa; actívalo antes de obtener la llave",
  PROVEEDOR_NO_CONFIGURADO: "La integración con el proveedor no está configurada en este ambiente",
  PROVEEDOR_NO_DISPONIBLE: MSG_PROVEEDOR,
  PROVEEDOR_TOKEN_INVALIDO: "El proveedor rechazó la autorización de este ambiente; revisa la configuración de la integración",
  PROVEEDOR_INTEGRACION_DESHABILITADA: "El proveedor tiene deshabilitada la integración; ingresa la llave manualmente",
  PROVEEDOR_SOLICITUD_INVALIDA: "El proveedor rechazó los datos de la empresa; revisa el NIT o ingresa la llave manualmente",
};
const MSG_LIMITE = "Se hicieron demasiadas solicitudes al proveedor; espera un momento e intenta de nuevo";
const MSG_OBTENER_DEFECTO = "No se pudo obtener la llave del proveedor; intenta de nuevo";
const MSG_VACIO = "Sin credencial del proveedor. Se obtiene del proveedor de impuesto con el NIT de la empresa.";
const MSG_GUARDADA = "API key de impuesto guardada.";
const MSG_BORRADA = "API key de impuesto borrada.";
const MSG_OBTENIDA = "Llave obtenida del proveedor.";
const MSG_RELECTURA = "No se pudo actualizar el listado; recarga la página para verlo.";
const MSG_ROTADA ="El proveedor reemplazó la llave anterior; Impuestos se actualiza automáticamente";

/** El mensaje del servidor no se muestra (puede traer detalle técnico): solo los literales por código. */
function mensajeObtener(e: unknown): string {
  if (e instanceof ApiError) {
    const code = (e.body as { code?: string } | undefined)?.code;
    if (code && Object.hasOwn(MENSAJES_OBTENER, code)) return MENSAJES_OBTENER[code];
    if (e.status === 429) return MSG_LIMITE;
  }
  return MSG_OBTENER_DEFECTO;
}

function mensajeError(e: unknown, porDefecto: string): string {
  if (e instanceof ApiError && e.status === 400 && (e.body as { code?: string } | undefined)?.code === "CREDENTIAL_INVALID") {
    return MSG_LONGITUD;
  }
  return e instanceof Error && e.message ? e.message : porDefecto;
}

export interface CredencialImpuestoDiagnosticoProps {
  tenantId: string;
  configurada: boolean;
  /** HU #14711. Si la compañía tiene secreto de webhook del proveedor (del listado); `null` o ausente: no se muestra. */
  webhookSecretConfigurada?: boolean | null;
  /** Abre el formulario sin que lo pidan (al encender Diagnóstico sin llave); el botón de cierre dice «Ahora no». */
  sugerirAbierto?: boolean;
  /** Se llama tras guardar o borrar con éxito para releer los productos. */
  onChanged: () => Promise<void> | void;
}

export function CredencialImpuestoDiagnostico({
  tenantId,
  configurada,
  webhookSecretConfigurada = null,
  sugerirAbierto = false,
  onChanged,
}: CredencialImpuestoDiagnosticoProps) {
  const uid = useId();
  const inputId = `${uid}-llave`;
  const ayudaId = `${uid}-ayuda`;
  const errorCampoId = `${uid}-error`;
  const tituloBorrarId = `${uid}-borrar`;
  const manualId = `${uid}-manual`;

  // La captura manual vive plegada en «Ingresar manualmente»; la sugerencia al encender Diagnóstico la despliega.
  const [manual, setManual] = useState(sugerirAbierto);
  const [obteniendo, setObteniendo] = useState(false);
  const [obtenida, setObtenida] = useState<CredencialImpuestoObtenida | null>(null);
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
      setManual(true);
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
      // La llave escrita a mano reemplaza la obtenida del proveedor: su prefijo ya no corresponde a la llave en uso.
      setObtenida(null);
      setExito(MSG_GUARDADA);
    } catch (e) {
      setError(mensajeError(e, "No se pudo guardar la API key de impuesto."));
      setGuardando(false);
      return;
    }
    await releer(MSG_GUARDADA);
    setGuardando(false);
  };

  // M-3: la relectura del listado va aparte de la operación. Si falla, la operación ya se hizo: se conserva el éxito y se
  // avisa en la misma región de estado (sin role=alert), en vez de mostrar el error de la operación junto al éxito.
  const releer = async (mensajeExito: string) => {
    try {
      await onChanged();
    } catch {
      setExito(`${mensajeExito} ${MSG_RELECTURA}`);
    }
  };

  const borrar = async () => {
    setError(null);
    setExito(null);
    setBorrando(true);
    try {
      await deleteTenantProductCredential(tenantId, PRODUCT_CODE, CREDENTIAL_CODE);
      setConfirmarBorrar(false);
      // Lo obtenido del proveedor ya no existe: sin esto, el estado efectivo seguiría «Configurada» hasta releer.
      setObtenida(null);
      setExito(MSG_BORRADA);
    } catch (e) {
      setConfirmarBorrar(false);
      setError(mensajeError(e, "No se pudo borrar la API key de impuesto."));
      setBorrando(false);
      return;
    }
    await releer(MSG_BORRADA);
    setBorrando(false);
  };

  const obtener = async () => {
    setError(null);
    setExito(null);
    setObteniendo(true);
    try {
      const r = await obtenerCredencialImpuestoDiagnostico(tenantId);
      // Copia explícita de los metadatos del contrato: un campo de más en la respuesta no entra al estado (AC-DC-12).
      setObtenida({
        apiKeyPrefix: r.apiKeyPrefix,
        created: r.created,
        rotated: r.rotated,
        hasWebhookSecret: r.hasWebhookSecret,
        changed: r.changed,
        updatedAt: r.updatedAt,
      });
      setExito(MSG_OBTENIDA);
    } catch (e) {
      setError(mensajeObtener(e));
      setObteniendo(false);
      return;
    }
    await releer(MSG_OBTENIDA);
    setObteniendo(false);
  };

  const alternarManual = () => {
    if (manual) {
      // Plegar descarta lo escrito y la confirmación de borrado: nada queda vivo fuera de la vista.
      cerrar();
      setConfirmarBorrar(false);
    }
    setManual((m) => !m);
  };

  const ocupado = guardando || borrando || obteniendo;
  const llaveConfigurada = configurada || obtenida !== null;
  const secretoWebhook = obtenida ? obtenida.hasWebhookSecret : webhookSecretConfigurada;
  const textoSecreto =
    typeof secretoWebhook === "boolean" ? (secretoWebhook ? "Secreto de webhook configurado" : "Sin secreto de webhook") : null;

  return (
    <div role="group" aria-label="API key de impuesto de Diagnóstico" className="ml-12 space-y-2 rounded-xl border px-4 py-3 text-xs">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-semibold">API key de impuesto:</span>
        <StatusBadge
          tone={llaveConfigurada ? "success" : "neutral"}
          icono={rutaKit(llaveConfigurada ? ICONO_TONO.ok : ICONO_TONO[""])}
          ariaLabel={`API key de impuesto: ${llaveConfigurada ? "Configurada" : "No configurada"}`}
          label={llaveConfigurada ? "Configurada" : "No configurada"}
        />
        <Button size="sm" className="ml-auto" cargando={obteniendo} disabled={ocupado} onClick={() => void obtener()}>
          Obtener del proveedor
        </Button>
      </div>

      {llaveConfigurada ? (
        <ul className="space-y-0.5">
          <li>{obtenida ? `Llave configurada · prefijo ${obtenida.apiKeyPrefix}…` : "Llave configurada"}</li>
          {textoSecreto && <li>{textoSecreto}</li>}
        </ul>
      ) : (
        <>
          <p className={HELP_TEXT}>{MSG_VACIO}</p>
          {textoSecreto && <p>{textoSecreto}</p>}
        </>
      )}

      {obtenida?.rotated && (
        <InlineAlert tone="warning" compact vivo={false}>
          {MSG_ROTADA}
        </InlineAlert>
      )}

      <div>
        <Button variant="ghost" size="sm" aria-expanded={manual} aria-controls={manual ? manualId : undefined} onClick={alternarManual}>
          Ingresar manualmente
        </Button>
      </div>

      {manual && (
        <div id={manualId} className="space-y-2">
          {!abierto && (
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" size="sm" onClick={abrir} disabled={ocupado}>
                {llaveConfigurada ? "Reemplazar" : "Configurar"}
              </Button>
              {llaveConfigurada && (
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
