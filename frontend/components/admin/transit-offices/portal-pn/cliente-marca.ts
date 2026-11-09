import {
  deletePortalMarcaBannerItem,
  deletePortalMarcaCarrusel,
  reorderPortalMarcaBanners,
  reorderPortalMarcaCarrusel,
  updatePortalMarcaBannerItem,
  updatePortalMarcaCarrusel,
  uploadPortalMarcaBannerItem,
  uploadPortalMarcaCarrusel,
  deletePortalMarcaLogo,
  fetchPortalMarcaImage,
  getPortalMarca,
  publishPortalMarca,
  retirePortalMarca,
  savePortalMarcaDraft,
  uploadPortalMarcaLogo,
  type PortalMarca,
  type PortalMarcaScope,
  type SavePortalMarcaDraftRequest,
} from "@/lib/api/admin-ot-portal-marca";

/** Operaciones de una lista de imágenes del portal (carrusel o banners). */
export interface ListaImagenesApi {
  subir(file: File, alt: string, linkUrl: string | null): Promise<PortalMarca>;
  editar(imageId: string, body: { alt: string; linkUrl: string | null }): Promise<PortalMarca>;
  ordenar(imageIds: string[]): Promise<PortalMarca>;
  quitar(imageId: string): Promise<PortalMarca>;
}

/**
 * Lo que `PortalPnDesignSection` necesita de la API de marca. Lo cumplen la marca del organismo (`clienteMarcaOt`) y la
 * de la web de persona, que comparten forma pero viven en rutas y ámbitos distintos. Sin `banners`, la sección no
 * ofrece banners.
 */
export interface ClienteMarcaPortal {
  obtener(signal?: AbortSignal): Promise<PortalMarca>;
  guardarBorrador(body: SavePortalMarcaDraftRequest): Promise<PortalMarca>;
  publicar(rowVersion: number): Promise<PortalMarca>;
  retirar(rowVersion: number): Promise<PortalMarca>;
  subirLogo(file: File): Promise<PortalMarca>;
  quitarLogo(): Promise<PortalMarca>;
  /** Banners del portal (hasta 6, el portal los rota). Sin ellos, la sección no los ofrece (p. ej. la web de persona). */
  banners?: ListaImagenesApi;
  /** Carrusel de imágenes del portal. Sin él, la sección no lo ofrece (p. ej. la web de persona). */
  carrusel?: ListaImagenesApi;
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
    banners: {
      subir: (file, alt, linkUrl) => uploadPortalMarcaBannerItem(file, alt, linkUrl, scope),
      editar: (imageId, body) => updatePortalMarcaBannerItem(imageId, body, scope),
      ordenar: (imageIds) => reorderPortalMarcaBanners(imageIds, scope),
      quitar: (imageId) => deletePortalMarcaBannerItem(imageId, scope),
    },
    carrusel: {
      subir: (file, alt, linkUrl) => uploadPortalMarcaCarrusel(file, alt, linkUrl, scope),
      editar: (imageId, body) => updatePortalMarcaCarrusel(imageId, body, scope),
      ordenar: (imageIds) => reorderPortalMarcaCarrusel(imageIds, scope),
      quitar: (imageId) => deletePortalMarcaCarrusel(imageId, scope),
    },
    imagen: (path, signal) => fetchPortalMarcaImage(path, scope, signal),
  };
}
