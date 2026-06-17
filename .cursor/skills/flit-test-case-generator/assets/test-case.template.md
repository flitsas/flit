# Plantilla: Test Case — FLIT

## Formato del título (obligatorio)

```
QA_TC{##}_{MODULO}_{ALCANCE} - {ESCENARIO}
```

| Parte | Regla |
|-------|-------|
| `QA_TC` | Prefijo fijo, siempre en mayúsculas |
| `{##}` | Número de 2 dígitos con zero-padding: 01, 02, ..., 99 |
| `{MODULO}` | Nombre del módulo en MAYÚSCULAS sin espacios ni guiones |
| `{ALCANCE}` | Sub-funcionalidad en MAYÚSCULAS |
| ` - ` | Espacio guión espacio (literal) |
| `{ESCENARIO}` | Descripción en sentence case |

### Ejemplos válidos
- `QA_TC01_PERSONAS_REGISTRO - Registro exitoso con documento válido`
- `QA_TC02_PERSONAS_REGISTRO - Fallo por documento duplicado`
- `QA_TC03_PERSONAS_BUSQUEDA - Búsqueda retorna resultados filtrados`
- `QA_TC04_AUTH_LOGIN - Fallo por credenciales incorrectas`

### Ejemplos inválidos (rechazados)
- `QA-TC01-PERSONAS-Registro` — guiones incorrectos
- `qa_tc01_personas_registro - ...` — minúsculas
- `QA_TC1_PERSONAS_REGISTRO` — sin zero-padding
- `TC01_PERSONAS` — falta prefijo QA_

---

## Cuerpo del Test Case

```markdown
# {QA_TCNN_MODULO_ALCANCE - Escenario}

**ID**: QA_TCNN
**US vinculada**: #{US-ID}
**Módulo**: {MODULO}
**Tipo**: Positivo | Negativo | Borde
**Prioridad**: Alta | Media | Baja

## Precondiciones

- {Estado del sistema antes del test}
- {Fixtures necesarios}
- {Usuario/rol requerido}

## Pasos

1. {Acción específica}
2. {Siguiente acción}
3. {Verificación}

## Datos de prueba

| Campo | Valor |
|-------|-------|
| {campo1} | {valor sintético — nunca datos reales} |
| {campo2} | {valor} |

## Resultado esperado

{Descripción clara y verificable del estado del sistema después del test}

## Postcondiciones (limpieza)

- {Cómo restablecer el estado para no afectar otros tests}

## Notas

{Consideraciones adicionales, variantes, contexto}
```
