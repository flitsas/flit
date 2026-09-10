"use client";

import { useEffect, useMemo, useState } from "react";
import type { ModuleId } from "@/components/atom/Shell";
import { getToken } from "@/lib/api/client";
import type { JwtPayload } from "@/lib/auth/jwt";
import {
  canReadIctLogs,
  canReadLogQx,
  decodeJwtPayload,
  isOtAdmin,
  isSuperAdmin,
} from "@/lib/auth/jwt";
import { resolveNavigableModuleIds } from "@/lib/nav/modules";
import { useAccessibleModules } from "./useAccessibleModules";

/**
 * HU #12196 — módulos SPA a los que ESTE usuario puede navegar, resueltos con la MISMA función que
 * gobierna el dock y el gate de `?m=` (`resolveNavigableModuleIds`).
 *
 * <p>Existe para que una pantalla que no es el Shell —p. ej. el listado de trámites— pueda ofrecer
 * un atajo a otro módulo sin inventarse su propia regla de permisos. Preguntar «¿puedo enseñar el
 * enlace?» con un criterio distinto del que decide «¿puedo entrar?» es exactamente cómo se llega a
 * un atajo que rebota al dashboard con un aviso de "no tienes acceso": el atajo diría que sí y el
 * gate diría que no. Aquí la respuesta la da la misma función, así que no pueden discrepar.</p>
 *
 * <p>Sí implica una segunda lectura de `/api/v1/security/modules`: en App Router un layout no puede
 * pasarle props a la página que envuelve, así que el consumidor no tiene por dónde recibir los
 * códigos ya cargados. La fuente de verdad sigue siendo una (el RBAC del servidor); lo que se
 * duplica es la petición, no el criterio.</p>
 *
 * <p>`ready` distingue «todavía no sé» de «sé que no»: mientras sea false hay que tratar la lista
 * como indeterminada y NO pintar accesos —deny-by-default— en vez de mostrarlos y quitarlos.</p>
 */
export function useNavigableModules(enabled = true): {
  ids: ModuleId[];
  ready: boolean;
  loading: boolean;
} {
  const { modules, loading, ready } = useAccessibleModules(enabled);

  // Claims del JWT. Se resuelven TRAS montar (como el `isAdmin` del listado de trámites): en el
  // render del servidor `getToken()` no ve la cookie, así que leerlos durante el render daría
  // "null" y luego otra cosa — el clásico desajuste de hidratación.
  const [claims, setClaims] = useState<JwtPayload | null>(null);
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setClaims(decodeJwtPayload(getToken()));
  }, []);

  const codes = useMemo(() => modules.map((m) => m.code) as ModuleId[], [modules]);
  const codesKey = codes.join("\0");

  const ids = useMemo(
    () =>
      resolveNavigableModuleIds({
        accessibleCodes: codes,
        isSuperAdmin: isSuperAdmin(claims),
        isOtAdmin: isOtAdmin(claims),
        canReadLogQx: canReadLogQx(claims),
        canReadIctLogs: canReadIctLogs(claims),
      }),
    // `codesKey` mantiene estable la dependencia: `codes` es un array nuevo en cada render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [codesKey, claims],
  );

  return { ids, ready, loading };
}
