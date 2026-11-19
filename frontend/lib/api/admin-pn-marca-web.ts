// Cliente tipado de la marca de la WEB de persona (Épica #14297, Feature #14400, HU #14402).
// Contrato: docs/design/EPICA-14297-contrato-resto.md → «Diseño web»: `/api/v1/admin/plataforma/persona-natural/marca-web`,
// solo SuperAdmin, con la misma forma que la marca del organismo (`PortalMarca`) y sin banner: los de la web siguen en
// Contenido. No lleva `transitOfficeId`: la web de persona no pertenece a ningún organismo. Los errores `{ code, message }`
// conservan el motivo exacto en español, igual que en la marca del organismo.
import {
  fetchPortalMarcaImage,
  solicitudMarca,
  type PortalMarca,
  type SavePortalMarcaDraftRequest,
} from "./admin-ot-portal-marca";
import { PN_BASE } from "./admin-pn-tarifas";

export const MARCA_WEB_BASE = `${PN_BASE}/marca-web`;

/** GET — marca de la web (borrador, publicada y faltantes). */
export function getMarcaWeb(signal?: AbortSignal): Promise<PortalMarca> {
  return solicitudMarca<PortalMarca>("GET", MARCA_WEB_BASE, undefined, { signal });
}

/** PUT — guarda el borrador. `rowVersion` evita pisar un cambio ajeno (409 `PORTAL_MARCA_CONFLICTO`). */
export function saveMarcaWebDraft(body: SavePortalMarcaDraftRequest): Promise<PortalMarca> {
  return solicitudMarca<PortalMarca>("PUT", MARCA_WEB_BASE, undefined, { json: body });
}

/** POST /logo — multipart; el logo queda en el borrador. */
export function uploadMarcaWebLogo(file: File): Promise<PortalMarca> {
  const form = new FormData();
  form.append("file", file);
  return solicitudMarca<PortalMarca>("POST", `${MARCA_WEB_BASE}/logo`, undefined, { form });
}

/** DELETE /logo — la web usa el logo FLIT cuando se publique. */
export function deleteMarcaWebLogo(): Promise<PortalMarca> {
  return solicitudMarca<PortalMarca>("DELETE", `${MARCA_WEB_BASE}/logo`, undefined);
}

/** POST /publish — el servidor revalida nombre, colores y contraste. */
export function publishMarcaWeb(rowVersion: number): Promise<PortalMarca> {
  return solicitudMarca<PortalMarca>("POST", `${MARCA_WEB_BASE}/publish`, undefined, {
    json: { rowVersion },
  });
}

/** POST /retire — la web vuelve a la marca FLIT; el borrador se conserva. */
export function retireMarcaWeb(rowVersion: number): Promise<PortalMarca> {
  return solicitudMarca<PortalMarca>("POST", `${MARCA_WEB_BASE}/retire`, undefined, {
    json: { rowVersion },
  });
}

/** Imagen del borrador: exige sesión, así que se pide con el token y se entrega como URL de objeto. */
export function fetchMarcaWebImage(path: string, signal?: AbortSignal): Promise<string> {
  return fetchPortalMarcaImage(path, undefined, signal);
}
