import { GESTOR_ARTICLES } from "./gestor";
import { INTRO_ARTICLES } from "./intro";
import { OT_ARTICLES } from "./ot";
import type { ManualArticle } from "../types";

export const MANUAL_ARTICLES: readonly ManualArticle[] = [
  ...INTRO_ARTICLES,
  ...GESTOR_ARTICLES,
  ...OT_ARTICLES,
];
