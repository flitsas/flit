---
name: flit-inline-security-detector
description: Detecta en el diff de PR patrones de seguridad evidentes (SQLi por concatenación, credenciales hardcodeadas, logs de secretos, dangerouslySetInnerHTML sin sanitizar, eval con input de usuario, CSRF ausente, acceso directo a BD en controladores). Marca BLOQUEANTE solo con confianza alta. Usar en cada revisión de PR backend/frontend. Triggers seguridad inline, PR review, OWASP, BLOQUEANTE, flit-inline-security-detector.
---

Primera línea de defensa en el **diff visible**. No reemplaza SAST/SCA del Security Agent.

## Alcance

**Sí:** patrones obvios en líneas cambiadas.  
**No:** SonarQube, Semgrep, CVEs en dependencias, gitleaks histórico, Habeas Data, arquitectura profunda.

## Umbral

Marcar **BLOQUEANTE** solo si un revisor senior lo vería al instante. Si hay ambigüedad → Security Agent.

## Los 7 patrones

Detalle, ejemplos y referencias OWASP/CWE en `./patrones-seguridad-inline.md`.

1. SQL injection por concatenación — CRÍTICO  
2. Credenciales hardcodeadas — CRÍTICO  
3. Log de secretos — CRÍTICO  
4. `dangerouslySetInnerHTML` sin sanitizar — CRÍTICO  
5. `eval` / `new Function` con input de usuario — CRÍTICO  
6. Formularios POST sin CSRF (contexto server-rendered) — ALTO  
7. Acceso directo a BD en controladores — ALTO  

## Formato de comentario inline

```markdown
🚨 **BLOQUEANTE — [Categoría]**: <descripción>

**Línea {N}**:
\`\`\`ts
{código del diff}
\`\`\`

**Riesgo**: ...
**Recomendación**: ...
**Referencia**: OWASP / CWE

— flit-inline-security-detector
```

## Resumen por PR

Tabla por categoría con total bloqueantes y nota de que Security Agent corre en paralelo.

## Exclusiones

No reportar en: `*.test.*`, `*.spec.*`, `__mocks__/`, `fixtures/`, `*.env.example`

## Prohibido

- BLOQUEANTE sin confianza alta
- Modificar código (solo lectura)
- Escanear fuera del diff
- Duplicar el mismo hallazgo
- Lenguaje condescendiente
