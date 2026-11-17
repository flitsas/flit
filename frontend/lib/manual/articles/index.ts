import { ADMIN_COMPANY_ARTICLES } from "./admin-company";
import { DIAGNOSTICO_ARTICLES } from "./diagnostico";
import { DIAGNOSTICO_REPORTES_ARTICLES } from "./diagnostico-reportes";
import { GESTOR_ARTICLES } from "./gestor";
import { INTRO_ARTICLES } from "./intro";
import { NORMATIVA_ARTICLES } from "./normativa";
import { OT_ARTICLES } from "./ot";
import { SUPERADMIN_ARTICLES } from "./superadmin";
import type { ManualArticle } from "../types";

export const MANUAL_ARTICLES: readonly ManualArticle[] = [
  ...INTRO_ARTICLES,
  ...NORMATIVA_ARTICLES,
  ...GESTOR_ARTICLES,
  ...OT_ARTICLES,
  ...ADMIN_COMPANY_ARTICLES,
  ...SUPERADMIN_ARTICLES,
  ...DIAGNOSTICO_ARTICLES,
  // HU #14580 — Dashboard, Reportes y vista global de FLIT, después de los 12 de la HU #14577.
  ...DIAGNOSTICO_REPORTES_ARTICLES,
];
