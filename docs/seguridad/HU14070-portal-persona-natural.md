# Revisión de seguridad y Habeas Data — Portal Persona Natural

HU #14070 · Épica #14021 · 2026-10-29

Alcance: BFF `services/core-personanatural`, fachada gRPC de persona natural y front `frontend-personanatural`. Esta corrida no abre el portal a producción. No quedan hallazgos abiertos en el código revisado.

## Resumen

| Capa | Estado | Critical | High | Medium | Low |
|---|---|---|---|---|---|
| SAST (Semgrep / ESLint security) | No ejecutada | — | — | — | — |
| SCA | Parcial | 0 en el BFF | 0 en el BFF | — | — |
| Secretos (gitleaks) | No ejecutada | — | — | — | — |
| Habeas Data | Revisión de código y pruebas existentes | 0 | 0 | 0 | 0 |

Veredicto de esta corrida: **FAIL-WITH-EXCEPTIONS**. El texto de autorización quedó resuelto con la política pública de FLIT 2. Faltan Semgrep y gitleaks en la máquina, así que AC1 no queda cerrado.

## AC1 — SAST, dependencias y secretos

- `semgrep` y `gitleaks` no están instalados. No hubo escaneo SAST ni de secretos en el historial.
- `dotnet list package --vulnerable --include-transitive` sobre `Flit.PersonaNatural.Api` (2026-10-29, nuget.org): ningún paquete vulnerable.
- `npm audit` en `frontend-personanatural` no corrió: el proyecto no tiene lockfile.

## AC2 — Enumeración y acceso ajeno

Cubierto por pruebas ya escritas. Esta corrida no las reejecutó.

- Un radicado inexistente y una cédula distinta responden igual (`RadicadosTests.RadicadoInexistenteYCedulaDistinta_RespondenExactamenteLoMismo`). Un radicado que no existe también se bloquea tras fallos, para no delatar cuáles existen.
- La sesión de otra solicitud recibe 403 al leer, radicar, consultar persona, adjuntos o identidad (`SolicitudesTests`, `RadicacionTests`, `AdjuntosTests`, `IdentidadTests`, `ConsultaPersonaDetalleTests`).
- La fachada gRPC responde `solicitud_no_encontrada` tanto si la solicitud es de otra empresa como si no existe (`RadicacionGrpcTests`).

## AC3 — Captcha y límites

Cubierto por `AbusoTests` y `RadicadosTests`. Sin captcha o con un token que el proveedor de prueba no acepta, la acción no corre (`captcha_required`). Superar el límite por IP responde 429 `rate_limited` con `Retry-After`. El recorrido local `scripts/e2e-personanatural/recorrido.mjs` ahora incluye ese caso (AC5 de #14069) para cuando el stack esté levantado.

## AC4 — Habeas Data

| Control | Evidencia |
|---|---|
| Consentimiento al abrir el trámite | El BFF rechaza la sesión sin consentimiento y guarda la constancia en `personanatural.consent`. Versión `v1` en `frontend-personanatural/lib/consentimiento.ts`. |
| Cifrado en reposo | Documento, nombre, correo, celular, dirección y ciudad se cifran en la aplicación (AES-GCM) en `PersonaService`. En la base queda la huella HMAC del documento, no el número. |
| Retención | `RetencionJob` depura datos según `retention_config`. |
| Acceso auditado | Cada lectura de personas queda en `pii_access_log`. |
| PII en logs | `RadicacionTests.RadicarNoDejaDatosPersonalesEnLosLogs`. Los logs del BFF usan mensajes fuente (`LoggerMessage`) con identificadores, no con el documento ni el correo. |
| Consulta anónima | `VehiculoMinimo` sigue sin nombre ni documento del propietario. |

**Alto, resuelto.** El consentimiento y el pie abren la misma política pública que FLIT 2: `https://flitsas.com.co/privacy-policy/` (`URL_POLITICA_FLIT`, el `ProcedureTermsOptions.DefaultUrl` del wizard). Si la configuración de #14073 publica otra URL http(s) válida, esa reemplaza a la de FLIT. Una URL `javascript:`, `ftp:` o `data:` se descarta y vuelve la de FLIT 2. El ciudadano puede leerla antes de marcar «Acepto el tratamiento de mis datos personales».

**Medio, resuelto.** Las claves de laboratorio salieron de `levantar.ps1`, `observar-bus.cs` y `docs/suite/local-personanatural.md`. El script las lee de `scripts/e2e-personanatural/.env.local`, que no se versiona. La plantilla commiteada es `.env.example`, con marcadores. Si esas claves ya estaban en commits anteriores, siguen en el historial de git: rotarlas en el broker local es suficiente porque nunca fueron de un ambiente compartido.

**Decisión de producto ya aplicada.** Dentro del trámite, el portal muestra completos el nombre y el documento del propietario y de los actores. La consulta anónima de placa no. Dirección, teléfono, póliza y acreedor no salen en la proyección del trámite.

## AC5 — Entrega

Este archivo es el informe. No sustituye la corrida de Semgrep y gitleaks ni la certificación de QA. La HU permanece en Active.
