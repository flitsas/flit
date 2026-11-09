"use client";

import { createContext, useContext } from "react";
import type { DockGroup } from "./nav";

/**
 * Los grupos del dock ya filtrados por permiso, para quien dentro del producto necesite la misma navegación que ve el
 * usuario (el buscador Ctrl K y el acordeón de módulos de Inicio, HU #14480/#14481). Fuera de `SuiteShell`, vacío.
 */
export interface ShellNav {
  groups: DockGroup[];
  homeHref: string;
}

const ShellNavContext = createContext<ShellNav>({ groups: [], homeHref: "/" });

export const ShellNavProvider = ShellNavContext.Provider;

export function useShellNav(): ShellNav {
  return useContext(ShellNavContext);
}
