import {
  deletePortalMarcaBanner,
  deletePortalMarcaLogo,
  fetchPortalMarcaImage,
  getPortalMarca,
  publishPortalMarca,
  retirePortalMarca,
  savePortalMarcaDraft,
  uploadPortalMarcaBanner,
  uploadPortalMarcaLogo,
  type PortalMarca,
  type PortalMarcaScope,
  type SavePortalMarcaDraftRequest,
} from "@/lib/api/admin-ot-portal-marca";

/**
 * Lo que `PortalPnDesignSection` necesita de la API de marca. Lo cumplen la marca del organismo (`clienteMarcaOt`) y la
 * de la web de persona, que comparten forma pero viven en rutas y ámbitos distintos. Sin `banner`, la sección no
 * ofrece banner.
 */
export interface ClienteMarcaPortal {
  obtener(signal?: AbortSignal): Promise<PortalMarca>;
  guardarBorrador(body: SavePortalMarcaDraftRequest): Promise<PortalMarca>;
  publicar(rowVersion: number): Promise<PortalMarca>;
  retirar(rowVersion: number): Promise<PortalMarca>;
  subirLogo(file: File): Promise<PortalMarca>;
  quitarLogo(): Promise<PortalMarca>;
  banner?: { subir(file: File): Promise<PortalMarca>; quitar(): Promise<PortalMarca> };
  imagen(path: string, signal: AbortSignal): Promise<string>;
}

/** Cliente de la marca del portal de un organismo (`/api/v1/admin/ot/portal-marca`). */
export function clienteMarcaOt(scope: PortalMarcaScope): ClienteMarcaPortal {
  return {
    obtener: (signal) => getPortalMarca(scope, signal),
    guardarBorrador: (body) => savePortalMarcaDraft(body, scope),
    publicar: (rowVersion) => publishPortalMarca(rowVersion, scope),
    retirar: (rowVersion) => retirePortalMarca(rowVersion, scope),
    subirLogo: (file) => uploadPortalMarcaLogo(file, scope),
    quitarLogo: () => deletePortalMarcaLogo(scope),
    banner: {
      subir: (file) => uploadPortalMarcaBanner(file, scope),
      quitar: () => deletePortalMarcaBanner(scope),
    },
    imagen: (path, signal) => fetchPortalMarcaImage(path, scope, signal),
  };
}
