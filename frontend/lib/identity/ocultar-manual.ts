// El flujo manual de identidad (Épica #13202) es una herramienta interna del Super Admin FLIT. Para la compañía y
// el cliente una validación manual es una validación biométrica como cualquier otra: mismo proveedor, mismos
// estados y ninguna palabra «manual». Se aplica en un solo punto (la capa de red) a todo lo que llega del backend.

/** Estados propios del flujo manual → el estado biométrico normal equivalente. */
const ESTADO_NORMAL: Record<string, string> = {
  // Se le envió un enlace y aún no responde: igual que «Enviado» de cualquier validación.
  manual_activo: 'enviado',
  // Ya hay captura y se está validando: «En proceso».
  pendiente_revision_manual: 'en_proceso',
};

/** Proveedor con el que se muestra una validación manual (el de las validaciones biométricas de siempre). */
const PROVEEDOR_NORMAL = 'kyverum';

function ocultar(valor: unknown): unknown {
  if (Array.isArray(valor)) return valor.map(ocultar);
  if (valor === null || typeof valor !== 'object') return valor;
  const src = valor as Record<string, unknown>;
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(src)) out[k] = ocultar(v);

  if (typeof out.status === 'string' && out.status in ESTADO_NORMAL) out.status = ESTADO_NORMAL[out.status];
  if (out.provider === 'manual') out.provider = PROVEEDOR_NORMAL;
  if (out.approvalOrigin === 'manual') out.approvalOrigin = 'automatica';
  // El motivo homologado lo elige el Super Admin al rechazar: es interno.
  if ('rejectionReasonCode' in out) out.rejectionReasonCode = null;
  return out;
}

/**
 * Devuelve `data` tal cual para el Super Admin y, para cualquier otro usuario, una copia donde ninguna
 * validación se distingue de una biométrica normal. Recorre objetos y listas anidadas.
 */
export function ocultarManualAlCliente<T>(data: T, esSuperAdmin: boolean): T {
  if (esSuperAdmin || data === null || typeof data !== 'object') return data;
  return ocultar(data) as T;
}
