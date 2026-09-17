/**
 * HU #12578 (Feature #12565) — tipos de la vista dedicada "Revocatorias": UNA fila por intento de
 * solicitud de revocatoria, en cualquier sub-estado (activo o cerrado). Forma IDÉNTICA en las dos
 * respuestas del backend (`GET /api/v1/tramites/revocation-requests` del lado gestor y
 * `GET /api/v1/admin/ot/revocation-requests` del lado OT — ver `RevocationRequestListItemDto` en
 * `Flit.Tramites.Application`), así que un solo tipo sirve a los dos clientes
 * (`tramites-client.ts` y `admin-ot.ts`).
 */
export interface RevocationRequestListItem {
  revocationRequestId: string;
  procedureInstanceId: string;
  referenceNumber: string;
  placa: string | null;
  transitOfficeId: string | null;
  transitOfficeName: string | null;
  /** 'solicitada' | 'en_revision' | 'aprobada' | 'rechazada' — ver `lib/tramites/estados.ts`. */
  status: string;
  attemptNumber: number;
  requestedAt: string;
  decidedAt: string | null;
}

/** Página de resultados, ya paginada por el servidor (`skip`/`take`, no `page`/`pageSize`). */
export interface RevocationRequestListResponse {
  items: RevocationRequestListItem[];
  total: number;
  skip: number;
  take: number;
}

/**
 * Filtros de la vista dedicada, compartidos por gestor y OT (AC1: "los mismos filtros del listado
 * general" — fecha, OT, estado). `transitOfficeId` solo tiene efecto del lado gestor: del lado OT el
 * alcance ya es un único organismo (resuelto por sesión/URL), igual que el resto de la bandeja OT.
 */
export interface RevocationRequestListParams {
  /** Sub-estados a incluir (OR). Vacío/omitido = todos. */
  statuses?: string[];
  /** `yyyy-mm-dd` o ISO — filtra sobre `requestedAt`. */
  requestedFrom?: string;
  requestedTo?: string;
  transitOfficeId?: string;
  skip?: number;
  take?: number;
}
