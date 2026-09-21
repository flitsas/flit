import type { ManualNavSection } from "../types";
import { COPY } from "@/lib/copy/copy-catalog";

export const MANUAL_NAV_SECTIONS: readonly ManualNavSection[] = [
  { id: "introduccion", label: "Introducción", order: 0 },
  { id: "gestor", label: COPY.A06, order: 1 },
  { id: "ot", label: COPY.A05, order: 2 },
] as const;

export const MANUAL_HOME_SLUG = "0-introduccion/1-bienvenida";

export const MANUAL_VERSION = "1.1.0";
export const MANUAL_VERSION_DATE = "Agosto 2026";
