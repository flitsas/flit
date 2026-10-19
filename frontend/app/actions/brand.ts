"use server";

// Publicar o retirar la marca de una red cambia lo que devuelve `/public/branding`, pero el layout
// la guarda 60 s en la caché de Next. Esta acción la expira para que el cambio se vea al instante.
//
// Uso de ejemplo (componente cliente, tras publicar):
//   await refreshBrand();
//   router.refresh();
import { updateTag } from "next/cache";
import { BRAND_CACHE_TAG } from "@/lib/brand/types";

export async function refreshBrand(): Promise<void> {
  updateTag(BRAND_CACHE_TAG);
}
