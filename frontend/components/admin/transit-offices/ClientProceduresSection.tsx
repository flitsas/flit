"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { tramitesClient } from "@/lib/api/tramites-client";
import {
  adjuntarOtLicenciaTransito,
  type AdjuntarLtResult,
  approveOtClientProcedure,
  fetchOtAttachmentPreviewUrl,
  fetchOtBandejaCounters,
  fetchOtBandejaFilterFields,
  fetchOtBandejaHealth,
  fetchOtDocuments,
  fetchOtProfile,
  generarOtConsolidadoMaestro,
  rejectOtClientProcedure,
  revokeOtClientProcedure,
  searchOtClientProcedures,
} from "@/lib/api/admin-ot";
import type {
  OtBandejaCounters,
  OtBandejaHealth,
  OtClientProcedure,
  OtClientProceduresParams,
  OtProfile,
  RejectionReason,
} from "@/lib/api/types-ot";
import { fetchRejectionReasons } from "@/lib/api/ot-metrics";
import { fetchMandateSigners, type MandateSigner } from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";
import { getToken } from "@/lib/api/client";
import { downloadFile } from "@/lib/api/download";
import { decodeJwtPayload, isSuperAdmin } from "@/lib/auth/jwt";
import { DocumentPreviewModal } from "@/components/shared/DocumentPreviewModal";
import { Download, RefreshCw } from "lucide-react";
import { ClientProceduresTable } from "./ClientProceduresTable";
import {
  ClientProcedureDetailModal,
  type OtDetalleSeccionId,
} from "./ClientProcedureDetailModal";
import {
  assignPlateToProcedure,
  listPlateDetails,
  revokeProcedurePlate,
  updateProcedurePlate,
  type PlateDetail,
} from "@/lib/api/admin-plate-ranges";
import { OT_INPUT_CLS } from "./ot-form-styles";
import { plateUpdateRemainingLabel } from "./ot-utils";
import {
  OtBandejaCountersStrip,
  filtrosDeContador,
  type OtCounterKey,
} from "./OtBandejaCounters";
import { formatDocumentWithType } from "@/lib/display/document-number";
import { ColumnSelector } from "@/components/atom/ColumnSelector";
import { useUiPreferences } from "@/hooks/useUiPreferences";
import {
  TramitesFiltrosBar,
  TramitesFiltrosChips,
  rangoDePeriodo,
  type RangoSobre,
} from "@/components/operacion/TramitesFiltrosBar";
import { controlCls } from "@/components/operacion/tramites-control-styles";
import type { QueryCondition, QueryField } from "@/lib/api/queries";
import {
  OT_PROCEDURES_COLUMNS,
  DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS,
} from "@/lib/admin/ot-procedures-columns";
import {
  otProceduresExportFields,
  nombreArchivoBandejaOt,
} from "@/lib/admin/ot-procedures-export";
import { buildWorkbook, type DataColumn } from "@/components/consultas/columns";
import { download, EXPORT_BATCH_SIZE, exportarPorLotes } from "@/components/consultas/export";
import { selloDeArchivo } from "@/components/operacion/tramites-export";
import { XLSX_MIME } from "@/lib/xlsx";

const PAGE_SIZE = 20;

/**
 * Tamaño de página del recorrido del export: el tope DURO del endpoint
 * (`ListOtClientProceduresHandler.MaxPageSize`). Pedir más no trae más, así que subirlo solo haría
 * que el servidor devolviera menos de lo pedido y el recorrido diera vueltas de más.
 */
const EXPORT_PAGE_SIZE = 100;

/**
 * Estados que el organismo puede filtrar en su bandeja (HU #11946).
 *
 * Es el espejo en frontend de `TramiteEstado.RecibidosPorOrganismo` (backend, HU #11945): el
 * organismo solo ve trámites que ya le fueron entregados, así que `borrador`, `preparado` y
 * `anulado` no tienen sitio aquí. El backend es quien manda —la lista sale vacía aunque se pida
 * un estado de fuera—; esta constante existe para que el desplegable y la precarga desde la URL
 * no puedan discrepar entre sí.
 */
const FILTROS_ESTADO_OT = [
  { value: "entregado", label: "Pendiente OT" },
  { value: "aprobado", label: "Aprobado OT" },
  { value: "rechazado", label: "Rechazado OT" },
  // HU #12166/#12168 (Feature #12156) — a diferencia de Anulado, Revocado SÍ entra en
  // TramiteEstado.RecibidosPorOrganismo (el OT revocó su propia aprobación; ver el comentario en
  // esa constante en el backend), así que también puede filtrarse aquí.
  { value: "revocado", label: "Revocado OT" },
] as const;

/** La bandeja abre por la cola de decisión: es el trabajo que el organismo tiene pendiente. */
const ESTADO_POR_DEFECTO = "entregado";

function esEstadoDeBandeja(valor: string): boolean {
  return FILTROS_ESTADO_OT.some((f) => f.value === valor);
}

/** Extrae el motivo de fallo al asignar placa (ProblemDetails.detail o fallback legible). */
export function readAssignPlateError(
  err: unknown,
  fallback = "No se pudo asignar la placa.",
): string {
  if (err instanceof ApiError) {
    const body = err.body as { detail?: unknown; title?: unknown } | null | undefined;
    if (typeof body?.detail === "string" && body.detail.trim()) return body.detail.trim();
    if (typeof body?.title === "string" && body.title.trim() && body.title !== "Conflict") {
      return body.title.trim();
    }
    // apiFetch (422 ProblemDetails, o cualquier no-ok) ya prioriza detail/title/error en message
    // y nunca filtra la ruta interna (Bug #11626) — se puede usar tal cual.
    if (err.message) return err.message;
  }
  if (err instanceof Error && err.message && err.message !== "Validación fallida") {
    // Errores técnicos de red/fetch no ayudan al operador OT.
    if (!/^(network|failed to fetch|load failed|aborted?)$/i.test(err.message.trim())) {
      return err.message;
    }
  }
  return fallback;
}

/**
 * Vista tenant admin — trámites de clientes OT (HU #10220).
 *
 * `transitOfficeId` (ruta /admin/transit-offices/[id]) scope-a la consulta para el
 * SuperAdmin: sin él, el backend resuelve el OT desde el tenant del token, que para
 * SuperAdmin no tiene perfil OT y la lista queda vacía (los trámites `entregado`
 * "desaparecen"). Para ot_admin el backend ignora el override (seguridad) y sigue
 * resolviendo por su propio tenant.
 */

/**
 * HU #11996 — mensaje del OCR de la Licencia de Tránsito para el toast del OT.
 *
 * El análisis NUNCA bloquea el adjunto: aquí solo se traduce el resultado a algo accionable. Tres
 * desenlaces posibles y ninguno impide que la LT quede guardada:
 *  - `ocr` en null  → no se pudo analizar (proveedor caído, sin key, archivo >10 MB) ⇒ silencio: el
 *                     OT no puede hacer nada al respecto y un aviso ahí solo sería ruido.
 *  - `es_valido` false → el archivo no parece una licencia (típicamente un recibo de derechos).
 *  - placa/VIN leídos  → se devuelven para que el OT coteje de un vistazo contra el trámite.
 */
type LtOcrEstado =
  | { fase: "analizando" }
  | { fase: "listo"; data: Record<string, unknown>; valido: boolean; motivo: string }
  | { fase: "sin_analisis"; nota: string };

/** Lee un campo de texto del JSON del OCR sin romperse si no viene o no es string. */
function campoOcr(data: Record<string, unknown>, clave: string): string {
  const v = data[clave];
  return typeof v === "string" ? v.trim() : typeof v === "number" ? String(v) : "";
}

/**
 * HU #12042 — traduce la respuesta del OCR a lo que el OT necesita ver ANTES de decidir.
 *
 * El defecto que corrige: antes esto se calculaba DESPUÉS de aprobar y se mostraba en un toast
 * efímero, así que el OT decidía a ciegas y se enteraba cuando ya no podía hacer nada. Ahora el
 * análisis ocurre al seleccionar el archivo y el resultado vive dentro de la modal hasta que el
 * usuario decide.
 *
 * Sigue sin bloquear nada: `sin_analisis` (proveedor caído, archivo >10 MB) informa y deja seguir.
 */
function evaluarLtOcr(data: Record<string, unknown> | null | undefined): LtOcrEstado {
  if (!data) {
    return {
      fase: "sin_analisis",
      nota: "No se pudo verificar el documento automáticamente. Puedes continuar igual.",
    };
  }
  const valido = data.es_valido !== false;
  const motivo = campoOcr(data, "observaciones");
  return { fase: "listo", data, valido, motivo };
}

/**
 * Pares de caracteres que el OCR confunde de forma sistemática al leer placas y VIN sobre un
 * escaneo. Existen para no gritar «otro vehículo» por un solo carácter mal leído: un VIN que
 * difiere en un `0` donde debía ir una `O` casi siempre es el mismo vehículo y una lectura
 * imperfecta, mientras que uno que difiere en seis caracteres es, sin ambigüedad, otro carro.
 */
const CARACTERES_CONFUNDIBLES: ReadonlyArray<string> = ["0O", "1I", "1L", "5S", "8B", "2Z", "6G", "4A"];

/** Deja el identificador comparable: sin espacios, guiones ni minúsculas. */
export function normalizarIdentificador(valor: string): string {
  return valor.toUpperCase().replace(/[^A-Z0-9]/g, "");
}

export type CotejoResultado = "coincide" | "posible_lectura" | "difiere";

/**
 * Compara lo que el OCR leyó contra lo que el trámite dice. Devuelve `null` cuando falta cualquiera
 * de los dos lados: sin las dos mitades no hay nada que afirmar, y callar es mejor que inventar.
 */
export function compararIdentificador(leido: string, esperado: string): CotejoResultado | null {
  const a = normalizarIdentificador(leido);
  const b = normalizarIdentificador(esperado);
  if (!a || !b) return null;
  if (a === b) return "coincide";
  if (a.length !== b.length) return "difiere";
  let distintos = 0;
  for (let i = 0; i < a.length; i += 1) {
    if (a[i] === b[i]) continue;
    distintos += 1;
    const par = `${a[i]}${b[i]}`;
    const confundible = CARACTERES_CONFUNDIBLES.some(
      (c) => c === par || c === `${par[1]}${par[0]}`,
    );
    if (!confundible) return "difiere";
  }
  return distintos <= 1 ? "posible_lectura" : "difiere";
}

export interface CotejoCampo {
  label: string;
  leido: string;
  esperado: string;
  resultado: CotejoResultado;
}

export interface CotejoVehiculo {
  campos: CotejoCampo[];
  /** Al menos un identificador es de otro vehículo: el aviso fuerte. */
  difiere: boolean;
  /** Todo cuadra salvo un carácter confundible: aviso suave, probablemente sea el mismo vehículo. */
  dudas: boolean;
}

/**
 * HU #12043 — coteja la licencia contra el vehículo del trámite.
 *
 * El defecto que corrige: el panel enseñaba la placa y el VIN leídos y esperaba que el OT los
 * comparara de memoria contra un trámite que ni siquiera tenía delante. En la prueba se adjuntó una
 * licencia legítima de un vehículo distinto —VIN 9F8HJD49RM640413 sobre un trámite de
 * LRWYGCEK7TC769623— y el sistema, teniendo los dos números, la dio por buena.
 *
 * Sigue sin bloquear: el OT puede adjuntarla igual. La diferencia es que ahora se le dice.
 */
export function cotejarVehiculo(
  data: Record<string, unknown>,
  tramite: { vin?: string | null; placa?: string | null } | null | undefined,
): CotejoVehiculo {
  const campos: CotejoCampo[] = [];
  const pares: ReadonlyArray<{ label: string; clave: string; esperado?: string | null }> = [
    { label: "VIN", clave: "vehiculo_vin", esperado: tramite?.vin },
    { label: "Placa", clave: "vehiculo_placa", esperado: tramite?.placa },
  ];
  for (const par of pares) {
    const leido = campoOcr(data, par.clave);
    const esperado = (par.esperado ?? "").trim();
    const resultado = compararIdentificador(leido, esperado);
    if (resultado) campos.push({ label: par.label, leido, esperado, resultado });
  }
  return {
    campos,
    difiere: campos.some((c) => c.resultado === "difiere"),
    dudas: campos.some((c) => c.resultado === "posible_lectura"),
  };
}

/** Los campos con los que el OT coteja la licencia contra el trámite de un vistazo. */
const LT_OCR_CAMPOS: ReadonlyArray<{ label: string; clave: string }> = [
  { label: "Placa", clave: "vehiculo_placa" },
  { label: "VIN", clave: "vehiculo_vin" },
  { label: "Vehículo", clave: "vehiculo_marca" },
  { label: "Propietario", clave: "propietario_nombre" },
  { label: "Organismo", clave: "organismo_transito" },
  { label: "Expedición", clave: "fecha_expedicion" },
];

/** Panel del veredicto dentro de la modal. Informa; nunca deshabilita el botón de confirmar. */
function LtOcrPanel({
  estado,
  tramite,
}: {
  estado: LtOcrEstado | null;
  tramite: { vin?: string | null; placa?: string | null } | null;
}) {
  if (!estado) return null;

  if (estado.fase === "analizando") {
    return (
      <p className="mt-3 rounded-xl border px-3 py-2 text-xs opacity-70">
        Verificando la Licencia de Tránsito…
      </p>
    );
  }

  if (estado.fase === "sin_analisis") {
    return (
      <p className="mt-3 rounded-xl border px-3 py-2 text-xs opacity-70">{estado.nota}</p>
    );
  }

  const campos = LT_OCR_CAMPOS.map((c) => ({ ...c, valor: campoOcr(estado.data, c.clave) })).filter(
    (c) => c.valor !== "",
  );
  const cotejo = cotejarVehiculo(estado.data, tramite);
  // Una licencia de otro vehículo es tan grave como un documento equivocado: manda el rojo.
  const tono = !estado.valido
    ? { borde: "#C81E1E", texto: "#C81E1E", titulo: "El documento NO parece una Licencia de Tránsito" }
    : cotejo.difiere
      ? { borde: "#C81E1E", texto: "#C81E1E", titulo: "Esta licencia es de OTRO vehículo" }
      : { borde: "#3B8A00", texto: "#3B8A00", titulo: "Parece una Licencia de Tránsito" };

  return (
    <div className="mt-3 rounded-xl border px-3 py-2" style={{ borderColor: tono.borde }}>
      <p className="text-xs font-semibold" style={{ color: tono.texto }}>
        {tono.titulo}
      </p>
      {!estado.valido && estado.motivo && (
        <p className="mt-1 text-[11px] opacity-80">{estado.motivo.slice(0, 220)}</p>
      )}
      {cotejo.difiere && (
        <div className="mt-2 rounded-lg px-2 py-1.5" style={{ backgroundColor: "#FDECEC" }}>
          {cotejo.campos
            .filter((c) => c.resultado === "difiere")
            .map((c) => (
              <p key={c.label} className="text-[11px] leading-relaxed" style={{ color: "#C81E1E" }}>
                <span className="font-semibold">{c.label}</span> — el trámite es{" "}
                <span className="font-semibold">{c.esperado}</span> y la licencia dice{" "}
                <span className="font-semibold">{c.leido}</span>
              </p>
            ))}
          <p className="mt-1 text-[11px]" style={{ color: "#C81E1E" }}>
            Revisa que sea la licencia de este trámite antes de continuar.
          </p>
        </div>
      )}
      {!cotejo.difiere && cotejo.dudas && (
        <p className="mt-1 text-[11px] font-medium" style={{ color: "#B77900" }}>
          La placa o el VIN coinciden salvo un carácter: puede ser un error de lectura, pero
          compruébalo.
        </p>
      )}
      {campoOcr(estado.data, "legibilidad") !== "" &&
        campoOcr(estado.data, "legibilidad") !== "buena" && (
          <p className="mt-1 text-[11px] font-medium" style={{ color: "#B77900" }}>
            El documento se leyó con dificultad: comprueba los datos antes de darlos por buenos.
          </p>
        )}
      {campos.length > 0 && (
        <dl className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1">
          {campos.map((c) => (
            <div key={c.clave} className="flex items-baseline gap-1 text-[11px]">
              <dt className="shrink-0 opacity-60">{c.label}:</dt>
              <dd className="font-medium">{c.valor}</dd>
            </div>
          ))}
        </dl>
      )}
      <p className="mt-2 text-[11px] opacity-60">
        Esta verificación es informativa: puedes continuar en cualquier caso.
      </p>
    </div>
  );
}

export function ClientProceduresSection({ transitOfficeId }: { transitOfficeId?: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [rows, setRows] = useState<OtClientProcedure[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  // N 03 — `entregado` reemplaza a pending_ot como estado en cola de decisión OT.
  const [statusFilter, setStatusFilter] = useState(ESTADO_POR_DEFECTO);
  /** Sub-estado de placa; lo fijan las tarjetas de la cabecera, no el panel de búsqueda. */
  const [plateFlowFilter, setPlateFlowFilter] = useState("");
  const [counters, setCounters] = useState<OtBandejaCounters | null>(null);
  const [contadorActivo, setContadorActivo] = useState<OtCounterKey | "">("");
  const [sortBy, setSortBy] = useState("createdAt");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("desc");

  // ── Filtros con la gramática de Consultas (HU #12218) ───────────────────────────────────────
  //
  // Sustituyen a los siete campos sueltos del antiguo formulario «Búsqueda avanzada». Se conserva
  // la separación borrador/aplicado: el panel se edita sin que la bandeja se mueva, y solo
  // «Aplicar» la recarga. En tiempo real, cada tecla serían dos llamadas (bandeja y contadores).
  const [queryFields, setQueryFields] = useState<QueryField[]>([]);
  const [fieldsError, setFieldsError] = useState(false);
  const [fieldsKey, setFieldsKey] = useState(0);
  const [draftCondiciones, setDraftCondiciones] = useState<QueryCondition[]>([]);
  const [appliedCondiciones, setAppliedCondiciones] = useState<QueryCondition[]>([]);

  const [search, setSearch] = useState("");
  const [busquedaAplicada, setBusquedaAplicada] = useState("");

  const [rangoSobre, setRangoSobre] = useState<RangoSobre>("created");
  const [periodo, setPeriodo] = useState<string>("Sin periodo");
  const [rangoPropioDesde, setRangoPropioDesde] = useState("");
  const [rangoPropioHasta, setRangoPropioHasta] = useState("");
  const [appliedCreatedFrom, setAppliedCreatedFrom] = useState("");
  const [appliedCreatedTo, setAppliedCreatedTo] = useState("");
  const [appliedUpdatedFrom, setAppliedUpdatedFrom] = useState("");
  const [appliedUpdatedTo, setAppliedUpdatedTo] = useState("");

  // Selector de columnas (HU #12218 AC7). El scope `ot.procedures.columns` ya existía sin usarse.
  // Degrada con elegancia: si la preferencia no carga o falla al guardar, la tabla sigue con todas
  // las columnas y el usuario no se queda sin bandeja.
  const {
    visible: visibleColumns,
    saving: savingColumns,
    setVisible: setVisibleColumns,
  } = useUiPreferences("ot.procedures.columns", DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS, {
    catalog: OT_PROCEDURES_COLUMNS.map((c) => c.key),
  });

  /** ¿La selección de columnas se apartó del default? Es lo que marca el control en azul. */
  const columnasPersonalizadas = useMemo(() => {
    const actual = [...visibleColumns].sort();
    const base = [...DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS].sort();
    return actual.length !== base.length || actual.some((k, i) => k !== base[i]);
  }, [visibleColumns]);

  // Export (HU #12220).
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);
  const [exportNotice, setExportNotice] = useState<string | null>(null);
  const [approveTarget, setApproveTarget] = useState<OtClientProcedure | null>(null);
  const [rejectTarget, setRejectTarget] = useState<OtClientProcedure | null>(null);
  // ADR-0036 §D9 (HU #10916) — cuando la aprobación devuelve 409 mandatario_requerido, se elige el
  // mandatario que firma el mandato y se reintenta la aprobación con él.
  const [mandatarioTarget, setMandatarioTarget] = useState<OtClientProcedure | null>(null);
  const [mandatarioOptions, setMandatarioOptions] = useState<MandateSigner[]>([]);
  const [mandatarioChoice, setMandatarioChoice] = useState("");
  // Feature #10587 — asignar placa (preasignado) / revocar preasignación.
  const [assignTarget, setAssignTarget] = useState<OtClientProcedure | null>(null);
  const [plateInput, setPlateInput] = useState("");
  // HU #10800 — placas disponibles del rango de la compañía (para el select) y modo de asignación.
  const [availablePlates, setAvailablePlates] = useState<PlateDetail[]>([]);
  const [assignMode, setAssignMode] = useState<"range" | "out">("range");
  const [revokeTarget, setRevokeTarget] = useState<OtClientProcedure | null>(null);
  const [revokePlateReason, setRevokePlateReason] = useState("");
  // HU #12166 (Feature #12156) — revocar la APROBACIÓN (aprobado→revocado), distinto del revoke de
  // preasignación de arriba.
  const [revokeAprobacionTarget, setRevokeAprobacionTarget] = useState<OtClientProcedure | null>(null);
  const [revokeAprobacionReason, setRevokeAprobacionReason] = useState("");
  // HU #12167 — corregir la placa dentro de la ventana de 1 hora.
  const [updatePlateTarget, setUpdatePlateTarget] = useState<OtClientProcedure | null>(null);
  const [updatePlateInput, setUpdatePlateInput] = useState("");
  // HU #12168 — contador EN VIVO del tiempo restante dentro del modal (a pedido explícito): un
  // tick por segundo mientras el modal está abierto, nada más (no corre en segundo plano sin
  // necesidad ni sigue vivo tras cerrar el modal).
  const [, setPlateCountdownTick] = useState(0);
  useEffect(() => {
    if (!updatePlateTarget) return;
    const id = setInterval(() => setPlateCountdownTick((t) => t + 1), 1000);
    return () => clearInterval(id);
  }, [updatePlateTarget]);
  const plateWindowJustExpired =
    !!updatePlateTarget?.plateAssignedAt &&
    plateUpdateRemainingLabel(updatePlateTarget.plateAssignedAt) === "00:00";
  const [rejectReason, setRejectReason] = useState("");
  // Causales del catálogo para el modal de rechazo. Se cargan según la familia del trámite: las
  // causales no son intercambiables entre matrícula y traspaso.
  const [rejectReasonCatalog, setRejectReasonCatalog] = useState<RejectionReason[]>([]);
  const [rejectReasonIds, setRejectReasonIds] = useState<string[]>([]);
  const [rejectCatalogError, setRejectCatalogError] = useState<string | null>(null);
  /** Trámite cuya carga de causales es la vigente; descarta respuestas de aperturas anteriores. */
  const rejectCatalogRequestRef = useRef<string | null>(null);
  // Licencia de Tránsito opcional al aprobar; también adjuntable después (fila aprobada).
  const [ltFile, setLtFile] = useState<File | null>(null);
  // HU #12042 — veredicto del OCR de la LT, calculado al SELECCIONAR el archivo para que el OT lo
  // vea antes de decidir. Vive mientras la modal esté abierta; nunca deshabilita el confirmar.
  const [ltOcr, setLtOcr] = useState<LtOcrEstado | null>(null);

  /**
   * Selección del archivo de la LT: se guarda y se manda a analizar de inmediato, igual que hace el
   * wizard del gestor. El `await` no bloquea al usuario —puede confirmar mientras tanto— y el
   * resultado se guarda para enviarlo con la aprobación, de modo que lo registrado sea lo mostrado.
   */
  const seleccionarLt = useCallback(async (file: File | null) => {
    setLtFile(file);
    setLtOcr(file ? { fase: "analizando" } : null);
    if (!file) return;
    try {
      const res = await tramitesClient.analyzeDocument("tarjeta_propiedad", file);
      setLtOcr(evaluarLtOcr(res?.data));
    } catch {
      // Proveedor caído, archivo > 10 MB o tipo no soportado: se informa y se sigue.
      setLtOcr(evaluarLtOcr(null));
    }
  }, []);
  const [ltTarget, setLtTarget] = useState<OtClientProcedure | null>(null);
  const [consolidadoActingId, setConsolidadoActingId] = useState<string | null>(null);
  const [acting, setActing] = useState(false);
  const [profile, setProfile] = useState<OtProfile | null>(null);
  // HU #10705 — panel de documentos del expediente
  // Sección con la que abre el modal de detalle: la bandeja tiene dos entradas al mismo trámite
  // («ver detalle» y «ver documentos») y las dos llevan al mismo modal.
  const [detailSection, setDetailSection] = useState<OtDetalleSeccionId>("vehiculo");
  // Panel lateral derecho — detalle del trámite
  const [detailProcedure, setDetailProcedure] = useState<OtClientProcedure | null>(null);
  // Previsualización inline del consolidado (sin forzar descarga).
  const [preview, setPreview] = useState<{
    open: boolean;
    title: string;
    mimetype: string | null;
    url: string | null;
    loading: boolean;
    error: string | null;
    download: { procId: string; attId: string; filename: string } | null;
  }>({ open: false, title: "Consolidado", mimetype: null, url: null, loading: false, error: null, download: null });
  // Diagnóstico de bandeja (R09): entregados hacia el OT que no aparecen por falta de grant.
  const [health, setHealth] = useState<OtBandejaHealth | null>(null);

  /**
   * Objeto estable, y NO un literal en el cuerpo del render.
   *
   * `scope` viaja como prop al modal de detalle y de ahí a los efectos que traen el trámite y su
   * expediente. Creado en cada render, cambiaba de identidad con CADA pulsación de tecla en los
   * diálogos de rechazo o de placa —que viven en este mismo componente—, así que el detalle de
   * detrás recargaba y replegaba sus acordeones a cada letra: eso era el salto que se veía.
   */
  const scope = useMemo(
    () => (transitOfficeId ? { transitOfficeId } : undefined),
    [transitOfficeId],
  );

  // El SuperAdmin supervisa la cola pero la decisión aprobar/rechazar es del OT admin
  // (los endpoints approve/reject YA soportan el override de organismo del SuperAdmin vía
  // ?transitOfficeId=; en esta bandeja OT nativa el SuperAdmin no decide, solo supervisa).
  const [superAdmin] = useState(() => isSuperAdmin(decodeJwtPayload(getToken())));

  const isReadOnly = Boolean(
    profile?.operationMode === "quipux" && profile?.quipuxReadOnly,
  );

  useEffect(() => {
    const controller = new AbortController();
    fetchOtProfile(controller.signal, transitOfficeId ? { transitOfficeId } : undefined)
      .then(setProfile)
      .catch(() => setProfile(null));
    return () => controller.abort();
  }, [transitOfficeId]);

  // El respiro tras la última tecla. La búsqueda se aplica SOLA, igual que en el listado del
  // gestor: la caja se ve idéntica en las dos pantallas y tiene que responder igual — obligar aquí
  // a abrir «Filtros» y pulsar «Aplicar» para que surtiera efecto era la clase de diferencia que
  // solo se descubre probando. 350 ms es el rango en que una pausa se lee como «terminé de
  // escribir» sin que la tabla se sienta perezosa.
  useEffect(() => {
    // El setState va dentro del temporizador, no en el cuerpo del efecto: es diferido.
    const id = setTimeout(() => {
      setBusquedaAplicada(search);
      setPage(1);
    }, 350);
    return () => clearTimeout(id);
  }, [search]);

  // HU #12218 — catálogo de campos filtrables. Degrada con elegancia (AC8): si no carga, la
  // bandeja se pinta igual con su listado y el panel ofrece reintentar. Nunca bloquea el render.
  useEffect(() => {
    const c = new AbortController();
    fetchOtBandejaFilterFields(c.signal, transitOfficeId ? { transitOfficeId } : undefined)
      .then((fields) => {
        if (c.signal.aborted) return;
        setQueryFields(fields);
        setFieldsError(false);
      })
      .catch(() => {
        if (!c.signal.aborted) setFieldsError(true);
      });
    return () => c.abort();
  }, [transitOfficeId, fieldsKey]);

  // Deep-link desde el drill-down de reportes OT (?placa=/?vin=/?status=): abrir la lista de un
  // bloque del panel debe aterrizar ya filtrado en el trámite, no en la bandeja completa. Se lee
  // window.location directo (en vez de useSearchParams) para no requerir un boundary de router en
  // este componente ni afectar los tests que lo montan fuera de una app real.
  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    const placaParam = params.get("placa")?.trim();
    const vinParam = params.get("vin")?.trim();
    const statusParam = params.get("status")?.trim();
    if (!placaParam && !vinParam && !statusParam) return;
    /* eslint-disable react-hooks/set-state-in-effect -- siembra desde la URL al montar: no hay otro momento para leerla */
    // La placa y el VIN entran como CONDICIONES aplicadas, no como campos sueltos: desde la
    // HU #12218 ese es el único camino de filtrado, y así el enlace profundo deja además su chip
    // a la vista — quien aterriza aquí ve por qué la lista viene acotada y puede quitarlo.
    const sembradas: QueryCondition[] = [];
    if (placaParam)
      sembradas.push({ fieldId: "placa", operator: "es_alguno", values: [placaParam] });
    if (vinParam) sembradas.push({ fieldId: "vin", operator: "es_alguno", values: [vinParam] });
    if (sembradas.length > 0) {
      setDraftCondiciones(sembradas);
      setAppliedCondiciones(sembradas);
    }
    // Un estado fuera de la bandeja (p. ej. `?status=borrador`) se ignora en vez de sembrarse:
    // el backend ya lo devuelve vacío, pero el <select> se quedaría en un valor sin <option> que
    // lo represente — se vería en blanco junto a una lista vacía, y eso se lee como un fallo de
    // carga en vez de como un filtro que no existe.
    if (statusParam && esEstadoDeBandeja(statusParam)) setStatusFilter(statusParam);
    setPage(1);
    /* eslint-enable react-hooks/set-state-in-effect */
    // Solo al montar: es una precarga desde la URL de entrada, no una sincronización continua.
  }, []);

  /**
   * Los criterios que resuelve el SERVIDOR, tal y como están aplicados ahora mismo.
   *
   * Vive aparte de `load` porque tiene DOS consumidores: la bandeja y el recorrido del export
   * (HU #12220). Con una copia en cada sitio, el día que se añada un filtro habría que acordarse
   * de ponerlo también en el export — y hasta que alguien lo notara, el Excel traería filas que
   * la pantalla no está mostrando. NO incluye la paginación: eso es cosa de cada consumidor.
   */
  const buildListQuery = useCallback(
    (): OtClientProceduresParams => ({
      status: statusFilter || undefined,
      plateFlowStatus: plateFlowFilter || undefined,
      condiciones: appliedCondiciones.length > 0 ? appliedCondiciones : undefined,
      busqueda: busquedaAplicada.trim() || undefined,
      createdFrom: appliedCreatedFrom || undefined,
      createdTo: appliedCreatedTo || undefined,
      updatedFrom: appliedUpdatedFrom || undefined,
      updatedTo: appliedUpdatedTo || undefined,
      sortBy: sortBy || undefined,
      sortDir,
    }),
    [
      statusFilter,
      plateFlowFilter,
      appliedCondiciones,
      busquedaAplicada,
      appliedCreatedFrom,
      appliedCreatedTo,
      appliedUpdatedFrom,
      appliedUpdatedTo,
      sortBy,
      sortDir,
    ],
  );

  const load = useCallback(
    async (signal?: AbortSignal, targetPage = page) => {
      setStatus("loading");
      try {
        // Por POST: las condiciones no caben en una query string cuando alguien pega una lista de
        // placas desde Excel.
        const result = await searchOtClientProcedures(
          {
            ...buildListQuery(),
            page: targetPage,
            pageSize: PAGE_SIZE,
          },
          signal,
          transitOfficeId ? { transitOfficeId } : undefined,
        );
        if (signal?.aborted) return;
        setRows(result.data);
        setTotalCount(result.totalCount);
        setPage(result.page);
        setStatus(result.data.length === 0 ? "empty" : "ready");
        // Diagnóstico (R09) y contadores de la cabecera: acompañan a la lista y NUNCA la bloquean.
        // Van en su propio `try` y no solo con `.catch`, porque un fallo SÍNCRONO —el módulo sin
        // esa función— caería en el catch de abajo y dejaría la bandeja en estado de error por no
        // haber podido pintar una decoración.
        try {
          fetchOtBandejaHealth(signal, transitOfficeId ? { transitOfficeId } : undefined)
            .then((h) => {
              if (!signal?.aborted) setHealth(h);
            })
            .catch(() => {
              /* el diagnóstico es informativo: su fallo no afecta la bandeja */
            });
          fetchOtBandejaCounters(signal, transitOfficeId ? { transitOfficeId } : undefined)
            .then((c) => {
              if (!signal?.aborted) setCounters(c);
            })
            .catch(() => {
              /* la tira es orientativa: su fallo la deja con guiones, no tumba la bandeja */
            });
        } catch {
          /* idem: ni el diagnóstico ni los contadores pueden romper el listado */
        }
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [buildListQuery, page, transitOfficeId],
  );

  useEffect(() => {
    const c = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(c.signal, page);
    return () => c.abort();
  }, [load, page]);

  /**
   * Pasa el borrador a aplicado: condiciones, búsqueda y periodo a la vez. El periodo se traduce
   * aquí a un rango concreto sobre la fecha elegida, que es lo que el servidor entiende.
   */
  const applyFilters = useCallback(() => {
    setAppliedCondiciones(draftCondiciones);
    setBusquedaAplicada(search);

    // Filtrar el estado a mano MANDA sobre la tarjeta y sobre el estado por defecto de la bandeja.
    // Son dos formas de acotar por lo mismo, y sin esta regla se combinarían con AND: pedir
    // «Aprobado» con la bandeja abierta en «Pendiente de decisión» devolvería siempre cero, sin
    // nada en pantalla que explicara por qué. Es la misma precedencia que ya tenía el desplegable
    // de estado del formulario retirado.
    if (draftCondiciones.some((c) => c.fieldId === "estado")) {
      setStatusFilter("");
      setPlateFlowFilter("");
      setContadorActivo("");
    }

    const rango =
      periodo === "Rango propio"
        ? { desde: rangoPropioDesde, hasta: rangoPropioHasta }
        : rangoDePeriodo(periodo, new Date());

    const desde = rango?.desde ?? "";
    const hasta = rango?.hasta ?? "";
    // El rango aplica a UNA de las dos fechas: la otra se limpia, o quedarían dos periodos
    // activos a la vez tras cambiar de «Rango sobre».
    setAppliedCreatedFrom(rangoSobre === "created" ? desde : "");
    setAppliedCreatedTo(rangoSobre === "created" ? hasta : "");
    setAppliedUpdatedFrom(rangoSobre === "updated" ? desde : "");
    setAppliedUpdatedTo(rangoSobre === "updated" ? hasta : "");
    setPage(1);
  }, [draftCondiciones, search, periodo, rangoPropioDesde, rangoPropioHasta, rangoSobre]);

  const hasActiveFilters =
    appliedCondiciones.length > 0 ||
    busquedaAplicada.trim() !== "" ||
    periodo !== "Sin periodo" ||
    statusFilter !== ESTADO_POR_DEFECTO;

  /** Quitar un chip aplica el cambio de inmediato: el chip describe lo que ya está filtrando. */
  const quitarCondicion = useCallback((fieldId: string) => {
    setDraftCondiciones((prev) => prev.filter((c) => c.fieldId !== fieldId));
    setAppliedCondiciones((prev) => prev.filter((c) => c.fieldId !== fieldId));
    setPage(1);
  }, []);

  const quitarPeriodo = useCallback(() => {
    setPeriodo("Sin periodo");
    setRangoPropioDesde("");
    setRangoPropioHasta("");
    setAppliedCreatedFrom("");
    setAppliedCreatedTo("");
    setAppliedUpdatedFrom("");
    setAppliedUpdatedTo("");
    setPage(1);
  }, []);

  /**
   * Pulsar una tarjeta fija SU juego de filtros y suelta el de la anterior. El panel de búsqueda no
   * se toca: son dos formas de acotar que conviven, y la tarjeta manda sobre el estado porque es la
   * que el operador acaba de pulsar.
   */
  const handleContadorSelect = (key: OtCounterKey | "") => {
    const { status, plateFlowStatus } = filtrosDeContador(key);
    setContadorActivo(key);
    setStatusFilter(key === "" ? ESTADO_POR_DEFECTO : status);
    setPlateFlowFilter(plateFlowStatus);
    setPage(1);
  };

  const clearFilters = useCallback(() => {
    setContadorActivo("");
    setPlateFlowFilter("");
    setStatusFilter(ESTADO_POR_DEFECTO);
    setDraftCondiciones([]);
    setAppliedCondiciones([]);
    setSearch("");
    setBusquedaAplicada("");
    setPeriodo("Sin periodo");
    setRangoPropioDesde("");
    setRangoPropioHasta("");
    setAppliedCreatedFrom("");
    setAppliedCreatedTo("");
    setAppliedUpdatedFrom("");
    setAppliedUpdatedTo("");
    setSortBy("createdAt");
    setSortDir("desc");
    setPage(1);
  }, []);

  /**
   * HU #12220 — descarga a Excel de TODO lo que cumple los filtros, no de la página a la vista.
   *
   * <p>Exportar solo las filas de pantalla sería una trampa: el archivo parecería completo y nadie
   * lo comprobaría. Por eso recorre el servidor página a página hasta agotar el total —el mismo
   * `total` que ya alimenta el pie— y reparte en archivos de {@link EXPORT_BATCH_SIZE} filas en vez
   * de truncar. Cada archivo se dispara apenas se arma, dentro de la misma interacción del clic.</p>
   *
   * <p>El recorrido usa {@link buildListQuery}, el MISMO que pinta la tabla: así un filtro nuevo
   * llega al archivo sin tocar el export, y el Excel no puede traer filas que la pantalla no está
   * mostrando.</p>
   */
  const handleExportExcel = useCallback(async () => {
    setExporting(true);
    setExportNotice(null);
    setExportError(null);
    try {
      const base = buildListQuery();
      const campos = otProceduresExportFields(visibleColumns);
      const columnasExcel: DataColumn<OtClientProcedure>[] = campos.map((campo) => ({
        id: campo.id,
        label: campo.label,
        group: "Bandeja",
        value: campo.value,
        raw: campo.raw,
        width: campo.width,
      }));
      const idsVisibles = campos.map((c) => c.id);
      const sello = selloDeArchivo(new Date());

      // La primera página se pide aparte porque de ella sale el `total` con el que se sabe cuántas
      // quedan. Se guarda para reusarla como página 1 en vez de volver a pedirla.
      const primeraPagina = await searchOtClientProcedures(
        { ...base, page: 1, pageSize: EXPORT_PAGE_SIZE },
        undefined,
        transitOfficeId ? { transitOfficeId } : undefined,
      );

      const { exportadas, archivos } = await exportarPorLotes<OtClientProcedure>({
        total: primeraPagina.totalCount,
        pageSize: EXPORT_PAGE_SIZE,
        traerPagina: async (pagina, pageSize) =>
          pagina === 1
            ? primeraPagina.data
            : (
                await searchOtClientProcedures(
                  { ...base, page: pagina, pageSize },
                  undefined,
                  transitOfficeId ? { transitOfficeId } : undefined,
                )
              ).data,
        volcar: (lote, parte) => {
          download(
            buildWorkbook("Bandeja OT", columnasExcel, lote, idsVisibles),
            nombreArchivoBandejaOt(sello, parte),
            XLSX_MIME,
          );
        },
      });

      if (exportadas === 0) {
        setExportNotice("Ningún trámite cumple los filtros activos: no se descargó ningún archivo.");
        return;
      }
      setExportNotice(
        archivos > 1
          ? `Se exportaron ${exportadas} trámites en ${archivos} archivos de hasta ${EXPORT_BATCH_SIZE} filas cada uno, con ${idsVisibles.length} columnas.`
          : `Se exportaron ${exportadas} trámites con ${idsVisibles.length} columnas.`,
      );
    } catch (err) {
      setExportError(
        err instanceof Error ? `No se pudo exportar: ${err.message}` : "No se pudo exportar.",
      );
    } finally {
      setExporting(false);
    }
  }, [buildListQuery, visibleColumns, transitOfficeId]);

  const handleSortChange = (nextSortBy: string, nextSortDir: "asc" | "desc") => {
    setSortBy(nextSortBy);
    setSortDir(nextSortDir);
    setPage(1);
  };

  // OT sobre el que se listan los mandatarios (SuperAdmin: prop de la ruta; ot_admin: su perfil).
  const otIdForSigners = transitOfficeId ?? profile?.transitOfficeId ?? null;

  /**
   * Aprueba el trámite (opcionalmente con el mandatario elegido) y, si se adjuntó, sube la LT.
   * ADR-0036 §D9 (HU #10916): un 409 `mandatario_requerido` abre el diálogo de selección de mandatario
   * (varios candidatos sin cotejo automático) para reintentar con el firmante elegido.
   */
  const runApprove = async (target: OtClientProcedure, mandateSignerId?: string) => {
    setActing(true);
    try {
      // Se aprueba PRIMERO y luego se adjunta la LT: el gate de la LT exige el trámite en
      // entregado/aprobado. En la ruta de placa (Feature #10587) el trámite llega a la aprobación
      // en 'asignado', así que adjuntar antes fallaba con estado_invalido; tras aprobar queda
      // 'aprobado' (válido para la LT). El consolidado se genera on-demand y toma la LT vigente.
      const updated = await approveOtClientProcedure(target.id, mandateSignerId);
      setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)));

      if (ltFile) {
        try {
          // Se manda el análisis que el OT ya vio: el backend no lo repite y lo registrado coincide
          // con lo mostrado. Dos análisis del mismo archivo pueden diferir, y esa incoherencia sería
          // peor que no mostrar nada.
          const analizado = ltOcr?.fase === "listo" ? ltOcr.data : null;
          await adjuntarOtLicenciaTransito(target.id, ltFile, scope, analizado);
        } catch {
          // La aprobación YA quedó firme; solo falló el adjunto. Se puede reintentar con la
          // acción dedicada de Licencia de Tránsito.
          setApproveTarget(null);
          setMandatarioTarget(null);
          setLtFile(null);
          setLtOcr(null);
          show(
            "Trámite aprobado, pero no se pudo adjuntar la Licencia de Tránsito. Reintenta la carga.",
            "error",
          );
          return;
        }
      }

      const traiaLt = ltFile !== null;
      setApproveTarget(null);
      // Si la decisión se tomó desde el detalle, ese modal quedaría enseñando el estado anterior:
      // se cierra con la acción resuelta (HU #12062).
      setDetailProcedure(null);
      setMandatarioTarget(null);
      setLtFile(null);
      setLtOcr(null);
      // El veredicto del OCR ya se mostró DENTRO de la modal, antes de que el OT decidiera; aquí
      // solo se acusa recibo de la aprobación. Antes se anunciaba en este toast, y llegaba tarde:
      // el usuario ya había aprobado sin saber qué decía el análisis.
      show(traiaLt ? "Trámite aprobado con Licencia de Tránsito adjunta." : "Trámite aprobado.", "success");
    } catch (err) {
      const errorCode =
        err instanceof ApiError && err.status === 409
          ? (err.body as { error?: string } | undefined)?.error
          : undefined;

      // ADR-0036 §D9 (HU #10911) — el mandatario resuelto no tiene identidad validada vigente.
      if (errorCode === "mandatario_identidad_requerida") {
        setApproveTarget(null);
        setMandatarioTarget(null);
        show(
          "El mandatario debe validar su identidad (vigente) antes de firmar el mandato. " +
            "La compañía se la envía desde la pestaña «Mandatarios» de su configuración.",
          "error",
        );
        return;
      }

      // ADR-0036 §D9 — hay varios mandatarios y ninguno cotejó: pedir que el OT elija uno.
      const needsMandatario = errorCode === "mandatario_requerido";
      const otId = target.transitOfficeId ?? otIdForSigners;
      if (needsMandatario && otId) {
        try {
          const signers = await fetchMandateSigners(otId);
          const options = signers.filter(
            (s) => s.isActive && s.companyTenantIds.includes(target.clientTenantId),
          );
          setMandatarioOptions(options);
          setMandatarioChoice(options[0]?.id ?? "");
          setApproveTarget(null);
          setMandatarioTarget(target);
          return;
        } catch {
          // cae al mensaje genérico
        }
      }
      show("No se pudo aprobar el trámite.", "error");
    } finally {
      setActing(false);
    }
  };

  const confirmApprove = () => {
    if (approveTarget) void runApprove(approveTarget);
  };

  const confirmMandatario = () => {
    if (mandatarioTarget && mandatarioChoice) void runApprove(mandatarioTarget, mandatarioChoice);
  };

  // HU #10800 — abre el modal de asignar placa y carga las placas disponibles del rango de la compañía;
  // si no hay, arranca en modo "fuera de rango".
  const openAssignPlate = (row: OtClientProcedure) => {
    setPlateInput("");
    setAssignMode("range");
    setAvailablePlates([]);
    setAssignTarget(row);
    listPlateDetails(row.clientTenantId, { state: "disponible", scope: { transitOfficeId } })
      .then((plates) => {
        setAvailablePlates(plates);
        setAssignMode(plates.length > 0 ? "range" : "out");
      })
      .catch(() => setAssignMode("out"));
  };

  const confirmAssignPlate = async () => {
    if (!assignTarget || !plateInput.trim()) return;
    setActing(true);
    try {
      // HU #10800 — del rango (outOfRange=false) o fuera de rango (outOfRange=true).
      const placaAsignada = plateInput.trim().toUpperCase();
      await assignPlateToProcedure(assignTarget.id, placaAsignada, assignMode === "out");
      // HU #10785 — el status global permanece 'entregado'; avanza el sub-estado interno de placa.
      // La placa se refleja YA: es el dato que el operador acaba de escribir y el que viene a ver.
      const conPlaca = (r: OtClientProcedure): OtClientProcedure => ({
        ...r,
        placa: placaAsignada,
        plateFlowStatus: "asignado",
      });
      setRows((prev) => prev.map((r) => (r.id === assignTarget.id ? conPlaca(r) : r)));
      // El detalle abierto es un objeto de estado APARTE del de la fila: sin esto seguía enseñando
      // «Sin preasignar» hasta cerrar el modal y recargar la bandeja, y el operador no sabía si la
      // asignación había cuajado.
      setDetailProcedure((prev) => (prev && prev.id === assignTarget.id ? conPlaca(prev) : prev));
      setAssignTarget(null);
      setPlateInput("");
      show("Placa asignada al trámite.", "success");
    } catch (err) {
      // El backend explica la causa en el `detail` del ProblemDetails (placa ya asignada, fuera de
      // los rangos, trámite en otro estado…). Antes se descartaba y el operador solo veía un toast
      // genérico sin saber por qué no avanzaba el formulario.
      show(readAssignPlateError(err), "error");
      // El modal queda abierto a propósito: la corrección es escribir otra placa.
    } finally {
      setActing(false);
    }
  };

  const confirmRevokePlate = async () => {
    if (!revokeTarget || !revokePlateReason.trim()) return;
    setActing(true);
    try {
      await revokeProcedurePlate(revokeTarget.id, revokePlateReason.trim());
      // HU #10785 — el status global permanece 'entregado'; el sub-estado vuelve a 'preasignado'.
      //
      // Aquí NO se limpia `placa`: revocar libera la placa en el inventario y devuelve el sub-estado,
      // pero deja el field_value 'plate' escrito, así que el trámite sigue trayendo la placa en la
      // siguiente lectura. Ponerla a null la borraría de la pantalla y la haría reaparecer al
      // refrescar — la UI no debe fingir un borrado que el servidor no hace. Esa asimetría del
      // backend está pendiente de definición por el equipo.
      const revocado = (r: OtClientProcedure): OtClientProcedure => ({
        ...r,
        plateFlowStatus: "preasignado",
      });
      setRows((prev) => prev.map((r) => (r.id === revokeTarget.id ? revocado(r) : r)));
      setDetailProcedure((prev) => (prev && prev.id === revokeTarget.id ? revocado(prev) : prev));
      setRevokeTarget(null);
      setRevokePlateReason("");
      show("Preasignación revocada.", "success");
    } catch {
      show("No se pudo revocar la preasignación.", "error");
    } finally {
      setActing(false);
    }
  };

  // HU #12166 (Feature #12156) — revocar la aprobación: libera la placa (el trámite ya no cuenta
  // como "en proceso" en el CF-01/CF-03 del backend) y marca el FUR/certificados como históricos.
  // AC4: confirmación previa (el modal en sí) + registro de usuario/fecha/hora (lo hace el backend).
  const confirmRevokeAprobacion = async () => {
    if (!revokeAprobacionTarget) return;
    setActing(true);
    try {
      const updated = await revokeOtClientProcedure(revokeAprobacionTarget.id, revokeAprobacionReason);
      setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)));
      setDetailProcedure((prev) => (prev && prev.id === updated.id ? updated : prev));
      setRevokeAprobacionTarget(null);
      setRevokeAprobacionReason("");
      show("Trámite revocado.", "success");
    } catch {
      show("No se pudo revocar el trámite.", "error");
    } finally {
      setActing(false);
    }
  };

  // HU #12167 (Feature #12156) — corregir la placa dentro de la ventana de 1 hora (una única vez).
  // El backend rechaza fuera de la ventana o si ya se usó la corrección; el modal solo evita el
  // roundtrip cuando el ítem del menú ya llegó deshabilitado (ver plateUpdateWindow).
  const confirmUpdatePlate = async () => {
    if (!updatePlateTarget || !updatePlateInput.trim()) return;
    setActing(true);
    try {
      await updateProcedurePlate(updatePlateTarget.id, updatePlateInput.trim());
      const nuevaPlaca = updatePlateInput.trim().toUpperCase();
      const corregido = (r: OtClientProcedure): OtClientProcedure => ({
        ...r,
        placa: nuevaPlaca,
        plateUpdatedAt: new Date().toISOString(),
      });
      setRows((prev) => prev.map((r) => (r.id === updatePlateTarget.id ? corregido(r) : r)));
      setDetailProcedure((prev) => (prev && prev.id === updatePlateTarget.id ? corregido(prev) : prev));
      setUpdatePlateTarget(null);
      setUpdatePlateInput("");
      show("Placa corregida.", "success");
    } catch (err) {
      show(readAssignPlateError(err, "No se pudo corregir la placa."), "error");
    } finally {
      setActing(false);
    }
  };

  const confirmAdjuntarLt = async () => {
    if (!ltTarget || !ltFile) return;
    setActing(true);
    try {
      const analizado = ltOcr?.fase === "listo" ? ltOcr.data : null;
      await adjuntarOtLicenciaTransito(ltTarget.id, ltFile, scope, analizado);
      setLtTarget(null);
      setLtFile(null);
      setLtOcr(null);
      // El veredicto ya se mostró en la modal antes de confirmar; aquí solo se acusa recibo.
      show("Licencia de Tránsito adjuntada.", "success");
    } catch {
      show("No se pudo adjuntar la Licencia de Tránsito.", "error");
    } finally {
      setActing(false);
    }
  };

  const closePreview = () => {
    setPreview((p) => {
      if (p.url) URL.revokeObjectURL(p.url);
      return { ...p, open: false, url: null, error: null, download: null };
    });
  };

  const handleConsolidado = async (row: OtClientProcedure, force = false) => {
    // Botón único (Feature #10701): abre el consolidado del expediente INLINE. Si el OT puede
    // generar, "asegura" el vigente — el backend regenera solo si la marca lo pide (nunca generado
    // o invalidado por CUALQUIER cambio del expediente: adjuntar o borrar un documento, editar
    // datos, la decisión del OT, una transición de estado…) y reutiliza si ya está vigente.
    // `force` se salta ese atajo y reconstruye: la salida manual del operador. En modo QX read-only
    // no se puede generar: solo se muestra el consolidado existente.
    setConsolidadoActingId(row.id);
    setPreview((p) => {
      if (p.url) URL.revokeObjectURL(p.url);
      return {
        open: true,
        title: `Consolidado — ${row.referenceNumber}`,
        mimetype: "application/pdf",
        url: null,
        loading: true,
        error: null,
        download: null,
      };
    });
    try {
      let attId: string;
      let filename: string;
      let mimetype = "application/pdf";
      if (!isReadOnly) {
        const res = await generarOtConsolidadoMaestro(row.id, scope, force);
        attId = res.document.attachmentId;
        filename = res.document.filename;
        if (res.regenerado) show("Consolidado generado.", "success");
      } else {
        const docs = await fetchOtDocuments(row.id, scope);
        const consol =
          docs.data.find((a) => a.tipo === "consolidado_maestro") ??
          docs.data.find((a) => a.tipo === "consolidado");
        if (!consol) {
          setPreview((p) => ({
            ...p,
            loading: false,
            error: "El trámite aún no tiene consolidado generado.",
          }));
          return;
        }
        attId = consol.id;
        filename = consol.filename;
        mimetype = consol.mimetype || "application/pdf";
      }
      const { url } = await fetchOtAttachmentPreviewUrl(row.id, attId, scope);
      // El file-manager sirve el objeto como binary/octet-stream: re-empaquetamos como Blob con el
      // mimetype real para forzar el render inline (S3 permite CORS GET).
      const blob = await fetch(url).then((r) => {
        if (!r.ok) throw new Error(String(r.status));
        return r.blob();
      });
      const objectUrl = URL.createObjectURL(new Blob([blob], { type: mimetype }));
      setPreview((p) => ({
        ...p,
        loading: false,
        url: objectUrl,
        mimetype,
        download: { procId: row.id, attId, filename },
      }));
    } catch {
      setPreview((p) => ({
        ...p,
        loading: false,
        error: "No se pudo abrir el consolidado. Intenta de nuevo.",
      }));
    } finally {
      setConsolidadoActingId(null);
    }
  };

  const handlePreviewDownload = async () => {
    const dl = preview.download;
    if (!dl) return;
    try {
      await downloadFile(
        `/api/v1/admin/ot/client-procedures/${dl.procId}/documents/${dl.attId}/download`,
        {
          query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
          fallbackFilename: dl.filename,
        },
      );
    } catch {
      show("No se pudo descargar el consolidado.", "error");
    }
  };

  // Abre el modal y trae las causales activas de la familia del trámite. Si el catálogo falla, el
  // rechazo NO se bloquea: la observación en texto libre basta para radicar la decisión, y dejar al
  // revisor sin poder rechazar por un catálogo caído sería peor que perder el dato del reporte.
  const openReject = async (procedure: OtClientProcedure) => {
    setRejectTarget(procedure);
    setRejectReason("");
    setRejectReasonIds([]);
    setRejectCatalogError(null);
    setRejectReasonCatalog([]);
    // El modal pudo cerrarse o reabrirse con otro trámite mientras esperábamos el catálogo: se
    // marca cuál es la carga vigente para descartar la respuesta de una anterior, que pintaría
    // las causales del trámite equivocado.
    rejectCatalogRequestRef.current = procedure.id;
    try {
      const catalog = await fetchRejectionReasons({ family: procedure.familia });
      if (rejectCatalogRequestRef.current !== procedure.id) return;
      setRejectReasonCatalog(catalog);
    } catch {
      if (rejectCatalogRequestRef.current !== procedure.id) return;
      setRejectCatalogError(
        "No se pudieron cargar las causales. Puedes rechazar describiendo el motivo.",
      );
    }
  };

  const toggleRejectReason = (id: string) => {
    setRejectReasonIds((prev) =>
      prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id],
    );
  };

  const confirmReject = async () => {
    if (!rejectTarget || !rejectReason.trim()) return;
    setActing(true);
    try {
      const updated = await rejectOtClientProcedure(rejectTarget.id, {
        reason: rejectReason.trim(),
        rejectionReasonIds: rejectReasonIds.length > 0 ? rejectReasonIds : undefined,
      });
      setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)));
      setRejectTarget(null);
      setDetailProcedure(null);
      setRejectReason("");
      setRejectReasonIds([]);
      show("Trámite rechazado.", "success");
    } catch {
      show("No se pudo rechazar el trámite.", "error");
    } finally {
      setActing(false);
    }
  };

  // HU #10805 — dígito de preferencia del trámite en asignación (solo guía). Las placas del rango que
  // terminan en ese dígito se ordenan primero y se marcan; el OT puede elegir esa u otra cualquiera.
  const preferredDigit = assignTarget?.platePreferredLastDigit?.trim() ?? "";
  const orderedPlates = preferredDigit
    ? [...availablePlates].sort(
        (a, b) =>
          Number(b.plate.endsWith(preferredDigit)) - Number(a.plate.endsWith(preferredDigit)),
      )
    : availablePlates;

  return (
    <div className="space-y-4">
      {isReadOnly && (
        <div className="flex items-center gap-2">
          <span
            className="rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide"
            style={{ background: "#FFF4EC", color: "#FF4E00" }}
          >
            Solo lectura
          </span>
          <span className="text-[11px] opacity-60">
            Este OT opera en Quipux — no se pueden aprobar ni rechazar trámites desde FLIT.
          </span>
        </div>
      )}
      {health?.hasDeliveredWithoutGrant && (
        <div
          role="alert"
          className="rounded-xl px-4 py-3 text-xs"
          style={{ background: "#FFF4EC", color: "#7A2E00", border: "1px solid #FFD9C2" }}
        >
          <span className="font-semibold">
            {health.deliveredWithoutGrant}{" "}
            {health.deliveredWithoutGrant === 1
              ? "trámite entregado sin grant vigente"
              : "trámites entregados sin grant vigente"}
          </span>{" "}
          no {health.deliveredWithoutGrant === 1 ? "aparece" : "aparecen"} en esta bandeja.
          Habilita el grant OT↔empresa correspondiente para que el organismo pueda recibirlos y
          aprobarlos.
        </div>
      )}
      {/*
        Cabecera de trabajo (HU #12218): PRIMERO la fila de controles y después la tira de
        contadores, el mismo orden que el listado del gestor («tabs + filtros ANTES de KPIs», la
        convención de flit-tramites-chrome). Los controles son con lo que se empieza a trabajar; los
        contadores describen lo que hay. Con la fila debajo, buscar quedaba escondido tras un bloque
        de seis tarjetas.

        Antes había aquí un botón «Búsqueda avanzada» que desplegaba una tarjeta con ocho campos
        sueltos: ocupaba media pantalla en reposo y no se parecía en nada a la barra que el mismo
        producto ya usa al otro lado del trámite.
      */}
      <div className="flex min-w-0 flex-col">
        <div className="flex flex-wrap items-center justify-end gap-2">
          <TramitesFiltrosBar
            rangoSobre={rangoSobre}
            onRangoSobreChange={setRangoSobre}
            periodo={periodo}
            onPeriodoChange={setPeriodo}
            rangoPropioDesde={rangoPropioDesde}
            rangoPropioHasta={rangoPropioHasta}
            onRangoPropioDesdeChange={setRangoPropioDesde}
            onRangoPropioHastaChange={setRangoPropioHasta}
            queryFields={queryFields}
            draftCondiciones={draftCondiciones}
            onDraftCondicionesChange={setDraftCondiciones}
            condicionesCount={appliedCondiciones.length}
            filtrosTestIdPrefix="ot-bandeja-filtros"
            fieldsError={
              fieldsError ? (
                <div className="p-1 text-xs">
                  <p className="mb-2 text-[#C2410C]">
                    No se pudieron cargar los filtros disponibles.
                  </p>
                  <button
                    type="button"
                    onClick={() => setFieldsKey((k) => k + 1)}
                    className="rounded-lg border border-[#DFE5ED] px-2.5 py-1.5 font-semibold text-[#557EFF] transition hover:bg-[#557EFF]/10"
                  >
                    Reintentar
                  </button>
                </div>
              ) : undefined
            }
            search={search}
            onSearchChange={setSearch}
            searchPlaceholder="Buscar radicado (FT1-0000012), placa, VIN..."
            searchAriaLabel="Buscar en la bandeja de trámites"
            onAplicar={applyFilters}
            onEmpezarDeCero={clearFilters}
            empezarDeCeroDisabled={!hasActiveFilters && draftCondiciones.length === 0}
            columnSelector={
              <ColumnSelector
                columns={OT_PROCEDURES_COLUMNS.map((c) => ({ key: c.key, label: c.label }))}
                visible={visibleColumns}
                onChange={setVisibleColumns}
                label="Columnas"
                disabled={savingColumns}
                buttonClassName={controlCls(columnasPersonalizadas)}
              />
            }
            exportAction={
              <button
                type="button"
                onClick={() => void handleExportExcel()}
                disabled={exporting || status === "loading" || totalCount === 0}
                aria-label="Exportar la bandeja de trámites a Excel"
                title="Exportar a Excel"
                className={controlCls(false)}
                data-testid="ot-bandeja-export-xlsx"
              >
                <Download
                  className={`h-3.5 w-3.5 ${exporting ? "animate-pulse" : ""}`}
                  aria-hidden="true"
                />
                {exporting ? "Exportando…" : "Exportar"}
              </button>
            }
          />
          {/* Actualizar cierra la fila, como en el listado del gestor: la bandeja cambia por lo que
              hacen los gestores al otro lado, y recargar la página entera para enterarse costaba
              perder los filtros puestos. */}
          <button
            type="button"
            onClick={() => void load()}
            disabled={status === "loading"}
            aria-label="Actualizar la bandeja de trámites"
            title="Actualizar"
            className={controlCls(false)}
          >
            <RefreshCw
              className={`h-3.5 w-3.5 ${status === "loading" ? "animate-spin" : ""}`}
              aria-hidden="true"
            />
            Actualizar
          </button>
        </div>

        <TramitesFiltrosChips
          periodo={periodo}
          onQuitarPeriodo={quitarPeriodo}
          condiciones={appliedCondiciones}
          fields={queryFields}
          onQuitarCondicion={quitarCondicion}
        />
      </div>

      {/* HU #12220 — resultado de la exportación. El reparto en varios archivos necesita decirse:
          tres descargas seguidas sin explicación se leen como un fallo, no como un archivo por
          lote. */}
      {exportError ? (
        <div
          role="alert"
          className="rounded-xl px-4 py-3 text-xs"
          style={{ background: "#FFF4EC", color: "#7A2E00", border: "1px solid #FFD9C2" }}
        >
          {exportError}
        </div>
      ) : null}
      {exportNotice ? (
        <p role="status" className="text-xs opacity-70">
          {exportNotice}
        </p>
      ) : null}

      {/* La tira de contadores ocupa ya todo el ancho: su acompañante de la derecha era el botón
          Actualizar, que se mudó a la fila de controles. */}
      <OtBandejaCountersStrip
        counters={counters}
        selected={contadorActivo}
        onSelect={handleContadorSelect}
        loading={status === "loading"}
      />

      <UiStateBoundary
        status={status}
        emptyMessage="No hay trámites pendientes de tus clientes."
        errorMessage="Error al cargar trámites de clientes."
        onRetry={() => void load()}
        skeletonRows={5}
      >
        <ClientProceduresTable
          rows={rows}
          totalCount={totalCount}
          page={page}
          pageSize={PAGE_SIZE}
          onPageChange={setPage}
          visibleColumns={visibleColumns}
          sortBy={sortBy}
          sortDir={sortDir}
          onSortChange={handleSortChange}
          onApprove={(row) => {
            setLtFile(null);
            setApproveTarget(row);
          }}
          onReject={(p) => void openReject(p)}
          onAssignPlate={!isReadOnly && !superAdmin ? openAssignPlate : undefined}
          onRevoke={!isReadOnly && !superAdmin ? (row) => { setRevokePlateReason(""); setRevokeTarget(row); } : undefined}
          onRevokeAprobacion={
            !isReadOnly && !superAdmin
              ? (row) => { setRevokeAprobacionReason(""); setRevokeAprobacionTarget(row); }
              : undefined
          }
          onUpdatePlate={
            !isReadOnly && !superAdmin
              ? (row) => { setUpdatePlateInput(""); setUpdatePlateTarget(row); }
              : undefined
          }
          showApprovalActions={!isReadOnly && !superAdmin}
          onConsolidado={handleConsolidado}
          onAdjuntarLt={
            !isReadOnly && !superAdmin
              ? (row) => {
                  setLtFile(null);
                  setLtTarget(row);
                }
              : undefined
          }
          consolidadoActingId={consolidadoActingId}
          onVerDocumentos={(row) => {
            setDetailSection("documentos");
            setDetailProcedure(row);
          }}
          onVerDetalle={(row) => {
            setDetailSection("vehiculo");
            setDetailProcedure(row);
          }}
        />
      </UiStateBoundary>

      {approveTarget && (
        <div
          className="fixed inset-0 z-[1200] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Confirmar aprobación"
        >
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-card p-6 shadow-2xl border">
            <h2 className="text-lg font-semibold text-foreground">
              ¿Aprobar este trámite?
            </h2>
            <p className="mt-2 text-sm opacity-80">{approveTarget.referenceNumber}</p>
            <label className="mt-4 block text-xs font-semibold text-foreground">
              Licencia de Tránsito (LT) — opcional
              <input
                type="file"
                accept="application/pdf,image/jpeg,image/png,image/webp"
                aria-label="Licencia de Tránsito (LT)"
                className={`mt-1 ${OT_INPUT_CLS}`}
                onChange={(e) => void seleccionarLt(e.target.files?.[0] ?? null)}
              />
              <span className="mt-1 block text-[11px] font-normal opacity-60">
                Se adjunta al expediente del trámite y entra al consolidado al generarlo o regenerarlo.
              </span>
            </label>
            <LtOcrPanel estado={ltOcr} tramite={approveTarget} />
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                onClick={() => {
                  setApproveTarget(null);
                  setLtFile(null);
                  setLtOcr(null);
                }}
                disabled={acting}
              >
                Cancelar
              </button>
              {/* HU #12042 — el veredicto del OCR existe para que el OT decida CON él, no a pesar
                  de él: mientras se analiza, confirmar queda cerrado. En cuanto termina —diga lo
                  que diga, incluso si no se pudo analizar— se abre: el análisis informa, no veta. */}
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#557EFF" }}
                disabled={acting || ltOcr?.fase === "analizando"}
                onClick={() => void confirmApprove()}
              >
                {acting
                  ? "Procesando…"
                  : ltOcr?.fase === "analizando"
                    ? "Analizando la LT…"
                    : "Confirmar"}
              </button>
            </div>
          </div>
        </div>
      )}

      {mandatarioTarget && (
        <div
          className="fixed inset-0 z-[1200] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Elegir mandatario del mandato"
        >
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-card p-6 shadow-2xl border">
            <h2 className="text-lg font-semibold text-foreground">Elige el mandatario que firma</h2>
            <p className="mt-2 text-sm opacity-80">
              Este trámite requiere contrato de mandato y la compañía tiene varios mandatarios. Elige quién
              firma para aprobar.
            </p>
            <p className="mt-1 text-xs opacity-60">{mandatarioTarget.referenceNumber}</p>

            {mandatarioOptions.length === 0 ? (
              <p className="mt-4 rounded-xl border p-3 text-center text-xs opacity-70">
                No hay mandatarios activos para esta compañía en el organismo. Ahora los registra la
                propia compañía, desde la pestaña «Mandatarios» de su configuración, marcando en qué
                organismos aplican.
              </p>
            ) : (
              <fieldset className="mt-4 space-y-2" data-testid="mandatario-options">
                <legend className="sr-only">Mandatarios disponibles</legend>
                {mandatarioOptions.map((s) => (
                  <label
                    key={s.id}
                    className="flex cursor-pointer items-center gap-3 rounded-xl border px-3 py-2 text-sm"
                  >
                    <input
                      type="radio"
                      name="mandatario"
                      value={s.id}
                      checked={mandatarioChoice === s.id}
                      onChange={() => setMandatarioChoice(s.id)}
                      className="h-4 w-4 accent-[#557EFF]"
                    />
                    <span className="flex-1">
                      <span className="font-semibold">{s.fullName}</span>
                      <span className="ml-2 font-mono text-xs opacity-60">
                        {formatDocumentWithType(s.documentType, s.documentNumber)}
                      </span>
                    </span>
                  </label>
                ))}
              </fieldset>
            )}

            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                onClick={() => setMandatarioTarget(null)}
                disabled={acting}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#557EFF" }}
                disabled={acting || !mandatarioChoice}
                onClick={() => void confirmMandatario()}
              >
                {acting ? "Procesando…" : "Aprobar con este mandatario"}
              </button>
            </div>
          </div>
        </div>
      )}

      {assignTarget && (
        <div
          className="fixed inset-0 z-[1200] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Asignar placa"
        >
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]" style={{ border: "1px solid #DFE5ED" }}>
            <h2 className="text-lg font-semibold" style={{ color: "#162744" }}>Asignar placa al trámite</h2>
            <p className="mt-2 text-sm opacity-80">{assignTarget.referenceNumber}</p>
            {/* HU #10805 — dígito de preferencia del gestor: SOLO guía. El OT puede asignar una placa
                que termine en ese dígito o cualquier otra. */}
            {preferredDigit && (
              <div
                className="mt-3 rounded-lg px-3 py-2 text-xs"
                style={{ background: "#EEF3FF", color: "#1E3A8A", border: "1px solid #C7D7FE" }}
              >
                Dígito de preferencia: <b>termina en {preferredDigit}</b> — solo guía. Las placas ★ del
                rango terminan en ese dígito; puedes asignar esa u otra cualquiera.
              </div>
            )}
            {/* HU #10800 — elegir del rango (select) o registrar una placa fuera de rango (input). */}
            <div className="mt-4 flex gap-2 text-xs font-semibold">
              <button
                type="button"
                disabled={availablePlates.length === 0}
                onClick={() => { setAssignMode("range"); setPlateInput(""); }}
                className={`rounded-lg border px-3 py-1.5 disabled:opacity-40 ${assignMode === "range" ? "text-white" : ""}`}
                style={assignMode === "range" ? { background: "#557EFF", borderColor: "#557EFF" } : undefined}
              >
                Del rango{availablePlates.length > 0 ? ` (${availablePlates.length})` : ""}
              </button>
              <button
                type="button"
                onClick={() => { setAssignMode("out"); setPlateInput(""); }}
                className={`rounded-lg border px-3 py-1.5 ${assignMode === "out" ? "text-white" : ""}`}
                style={assignMode === "out" ? { background: "#557EFF", borderColor: "#557EFF" } : undefined}
              >
                Fuera de rango
              </button>
            </div>
            {assignMode === "range" ? (
              <label className="mt-4 block text-xs font-semibold" style={{ color: "#162744" }}>
                Placa del rango
                <select
                  value={plateInput}
                  onChange={(e) => setPlateInput(e.target.value)}
                  aria-label="Placa del rango"
                  className={`mt-1 ${OT_INPUT_CLS}`}
                >
                  <option value="">
                    {availablePlates.length === 0 ? "No hay placas disponibles" : "Selecciona una placa"}
                  </option>
                  {orderedPlates.map((p) => (
                    <option key={p.id} value={p.plate}>
                      {p.plate}
                      {preferredDigit && p.plate.endsWith(preferredDigit) ? " ★" : ""}
                    </option>
                  ))}
                </select>
              </label>
            ) : (
              <label className="mt-4 block text-xs font-semibold" style={{ color: "#162744" }}>
                Placa (fuera de rango)
                <input
                  type="text"
                  value={plateInput}
                  onChange={(e) => setPlateInput(e.target.value)}
                  placeholder="ABC123"
                  aria-label="Placa fuera de rango"
                  className={`mt-1 uppercase ${OT_INPUT_CLS}`}
                />
                <span className="mt-1 block text-[11px] font-normal opacity-70">
                  Formato ABC123. Se validará que no esté registrada y quedará en el inventario de la compañía.
                </span>
              </label>
            )}
            <div className="mt-5 flex gap-3">
              <button type="button" className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60" onClick={() => setAssignTarget(null)} disabled={acting}>Cancelar</button>
              <button type="button" className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60" style={{ background: "#557EFF" }} disabled={acting || !plateInput.trim()} onClick={() => void confirmAssignPlate()}>{acting ? "Procesando…" : "Asignar"}</button>
            </div>
          </div>
        </div>
      )}

      {revokeTarget && (
        <div
          className="fixed inset-0 z-[1200] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Revocar preasignación"
        >
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]" style={{ border: "1px solid #DFE5ED" }}>
            <h2 className="text-lg font-semibold" style={{ color: "#162744" }}>Revocar preasignación</h2>
            <p className="mt-2 text-sm opacity-80">{revokeTarget.referenceNumber}</p>
            <textarea
              className={`mt-3 ${OT_INPUT_CLS}`}
              rows={3}
              value={revokePlateReason}
              onChange={(e) => setRevokePlateReason(e.target.value)}
              placeholder="Motivo de la revocación…"
              aria-label="Motivo de la revocación"
            />
            <div className="mt-5 flex gap-3">
              <button type="button" className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60" onClick={() => setRevokeTarget(null)} disabled={acting}>Cancelar</button>
              <button type="button" className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60" style={{ background: "#dc2626" }} disabled={acting || !revokePlateReason.trim()} onClick={() => void confirmRevokePlate()}>{acting ? "Procesando…" : "Revocar"}</button>
            </div>
          </div>
        </div>
      )}

      {/* HU #12166 (Feature #12156) AC4 — confirmación previa a revocar la APROBACIÓN. Distinto del
          modal de arriba (que revoca una preasignación de placa antes de aprobar). */}
      {revokeAprobacionTarget && (
        <div
          className="fixed inset-0 z-[1200] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Revocar trámite"
        >
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]" style={{ border: "1px solid #DFE5ED" }}>
            <h2 className="text-lg font-semibold" style={{ color: "#557EFF" }}>¿Revocar este trámite aprobado?</h2>
            <p className="mt-2 text-sm opacity-80">Trámite {revokeAprobacionTarget.referenceNumber}</p>
            <p className="mt-2 text-xs opacity-70">
              Se libera la placa/VIN para una nueva radicación y el FUR/certificados vigentes quedan
              marcados como históricos. Esta acción no se puede deshacer desde aquí.
            </p>
            <textarea
              className={`mt-3 ${OT_INPUT_CLS}`}
              rows={3}
              value={revokeAprobacionReason}
              onChange={(e) => setRevokeAprobacionReason(e.target.value)}
              placeholder="Motivo de la revocación (opcional)…"
              aria-label="Motivo de la revocación del trámite"
            />
            <div className="mt-5 flex gap-3">
              <button type="button" className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60" onClick={() => setRevokeAprobacionTarget(null)} disabled={acting}>Cancelar</button>
              <button type="button" className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60" style={{ background: "#dc2626" }} disabled={acting} onClick={() => void confirmRevokeAprobacion()}>{acting ? "Procesando…" : "Revocar"}</button>
            </div>
          </div>
        </div>
      )}

      {/* HU #12167 (Feature #12156) — corregir la placa dentro de la ventana de 1 hora. El ítem del
          menú ya llega deshabilitado con el motivo cuando no aplica (ver plateUpdateWindow); este
          modal solo se abre cuando SÍ aplica. */}
      {updatePlateTarget && (
        <div
          className="fixed inset-0 z-[1200] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Actualizar placa"
        >
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]" style={{ border: "1px solid #DFE5ED" }}>
            <h2 className="text-lg font-semibold" style={{ color: "#557EFF" }}>Corregir placa</h2>
            <p className="mt-2 text-sm opacity-80">
              Trámite {updatePlateTarget.referenceNumber} · placa actual {updatePlateTarget.placa?.trim() || "—"}
            </p>
            <p className="mt-2 text-xs opacity-70">
              Solo se puede usar una vez, dentro de la hora siguiente a la asignación original.
            </p>
            {/* HU #12168 — tiempo restante EN VIVO (tick de 1s mientras el modal está abierto). */}
            {updatePlateTarget.plateAssignedAt && (
              <p
                className="mt-2 text-sm font-semibold"
                style={{ color: plateWindowJustExpired ? "#dc2626" : "#557EFF" }}
              >
                {plateWindowJustExpired
                  ? "La ventana de 1 hora venció mientras tenías este cuadro abierto."
                  : `Tiempo restante: ${plateUpdateRemainingLabel(updatePlateTarget.plateAssignedAt)}`}
              </p>
            )}
            <label className="mt-3 block text-xs font-semibold" style={{ color: "#162744" }}>
              Placa correcta
              <input
                type="text"
                value={updatePlateInput}
                onChange={(e) => setUpdatePlateInput(e.target.value)}
                placeholder="ABC123"
                aria-label="Placa correcta"
                className={`mt-1 uppercase ${OT_INPUT_CLS}`}
              />
            </label>
            <div className="mt-5 flex gap-3">
              <button type="button" className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60" onClick={() => setUpdatePlateTarget(null)} disabled={acting}>Cancelar</button>
              <button type="button" className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60" style={{ background: "#557EFF" }} disabled={acting || !updatePlateInput.trim() || plateWindowJustExpired} onClick={() => void confirmUpdatePlate()}>{acting ? "Procesando…" : "Corregir"}</button>
            </div>
          </div>
        </div>
      )}

      {rejectTarget && (
        <div
          className="fixed inset-0 z-[1200] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Rechazar trámite"
        >
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-card p-6 shadow-2xl border">
            <h2 className="text-lg font-semibold text-foreground">
              Motivo del rechazo
            </h2>

            {/* Causales del catálogo: varias son válidas y esperadas. Un expediente puede llegar
                con improntas borrosas, sin impronta y sin pago de impuestos a la vez, y el gestor
                necesita saberlo todo para subsanar. */}
            {rejectReasonCatalog.length > 0 && (
              <fieldset className="mt-4" data-testid="reject-reason-catalog">
                <legend className="text-xs font-semibold text-foreground">
                  ¿Qué falló? Marca todo lo que aplique
                </legend>
                <div className="mt-2 flex flex-col gap-1.5">
                  {rejectReasonCatalog.map((reason) => (
                    <label
                      key={reason.id}
                      className="flex items-start gap-2 text-xs text-foreground"
                    >
                      <input
                        type="checkbox"
                        className="mt-0.5"
                        checked={rejectReasonIds.includes(reason.id)}
                        onChange={() => toggleRejectReason(reason.id)}
                      />
                      <span>{reason.description}</span>
                    </label>
                  ))}
                </div>
              </fieldset>
            )}
            {rejectCatalogError && (
              <p className="mt-3 text-[11px] text-amber-700 dark:text-amber-400">
                {rejectCatalogError}
              </p>
            )}

            <label className="mt-4 block text-xs font-semibold text-foreground">
              Observación para quien va a subsanar
            </label>
            {/* El texto libre NO lo sustituyen las causales: la causal dice QUÉ falló y esto dice
                CÓMO corregirlo — qué documento exactamente, qué dato no cuadra. */}
            <textarea
              className={`mt-2 ${OT_INPUT_CLS}`}
              rows={3}
              placeholder="Indica qué debe corregirse y con qué detalle"
              value={rejectReason}
              onChange={(e) => setRejectReason(e.target.value)}
            />
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                onClick={() => setRejectTarget(null)}
                disabled={acting}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#FF4E00" }}
                disabled={acting || !rejectReason.trim()}
                onClick={() => void confirmReject()}
              >
                Confirmar rechazo
              </button>
            </div>
          </div>
        </div>
      )}

      {ltTarget && (
        <div
          className="fixed inset-0 z-[1200] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Adjuntar Licencia de Tránsito"
        >
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-card p-6 shadow-2xl border">
            <h2 className="text-lg font-semibold text-foreground">
              Adjuntar Licencia de Tránsito (LT)
            </h2>
            <p className="mt-2 text-sm opacity-80">{ltTarget.referenceNumber}</p>
            <input
              type="file"
              accept="application/pdf,image/jpeg,image/png,image/webp"
              aria-label="Archivo de la Licencia de Tránsito"
              className={`mt-4 ${OT_INPUT_CLS}`}
              onChange={(e) => void seleccionarLt(e.target.files?.[0] ?? null)}
            />
            <p className="mt-1 text-[11px] opacity-60">
              Reemplaza la LT previa si existe; regenera el consolidado para incluirla.
            </p>
            <LtOcrPanel estado={ltOcr} tramite={ltTarget} />
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                onClick={() => {
                  setLtTarget(null);
                  setLtFile(null);
                  setLtOcr(null);
                }}
                disabled={acting}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#557EFF" }}
                disabled={acting || !ltFile || ltOcr?.fase === "analizando"}
                onClick={() => void confirmAdjuntarLt()}
              >
                {acting
                  ? "Adjuntando…"
                  : ltOcr?.fase === "analizando"
                    ? "Analizando la LT…"
                    : "Adjuntar LT"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* HU #11930 — modal de detalle del trámite; la sección Documentos absorbió el antiguo
          modal del expediente (HU #10705), que se abría encima de este. */}
      <ClientProcedureDetailModal
        open={!!detailProcedure}
        procedure={detailProcedure}
        onClose={() => setDetailProcedure(null)}
        scope={scope}
        readOnly={isReadOnly}
        initialSection={detailSection}
        // HU #12062 — LOS MISMOS manejadores que recibe la tabla: decidir desde el detalle abre el
        // mismo diálogo que decidir desde la fila, sin una segunda vía de aprobación.
        onApprove={(row) => {
          setLtFile(null);
          setLtOcr(null);
          setApproveTarget(row);
        }}
        onReject={(row) => void openReject(row)}
        onAssignPlate={!isReadOnly && !superAdmin ? openAssignPlate : undefined}
        showApprovalActions={!isReadOnly && !superAdmin}
      />

      {/* Previsualización inline del consolidado (botón "Ver consolidado" de la tabla) */}
      <DocumentPreviewModal
        open={preview.open}
        onClose={closePreview}
        title={preview.title}
        mimetype={preview.mimetype}
        url={preview.url}
        loading={preview.loading}
        error={preview.error}
        onDownload={preview.download ? () => void handlePreviewDownload() : undefined}
      />
    </div>
  );
}
