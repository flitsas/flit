# Patrones de seguridad inline (detalle)

## 1. SQL injection — CRÍTICO

Concatenación o template literals con input de usuario en SQL.  
**Fix:** consultas parametrizadas / ORM. CWE-89.

## 2. Credenciales hardcodeadas — CRÍTICO

`password = "..."`, `apiKey`, connection strings con secretos, prefijos `AKIA`, `sk_live_`, `ghp_`.  
**No flag:** `.env.example`, tests, mocks. CWE-798.

## 3. Log de secretos — CRÍTICO

`console.log` / `logger` con `password`, `token`, `jwt`, `authorization`, etc. CWE-532.

## 4. dangerouslySetInnerHTML — CRÍTICO

`__html` con variable de usuario sin `DOMPurify.sanitize` visible. CWE-79.

## 5. eval / Function dinámica — CRÍTICO

`eval(req.body...)`, `new Function(userInput)`, `setTimeout("code", n)`. CWE-95.

## 6. CSRF ausente — ALTO

`<form method="POST">` sin token ni header CSRF (no aplica a SPA solo JWT). CWE-352.

## 7. BD en controlador — ALTO

Queries/ORM directos en handler cuando el módulo usa capa de servicio. Extraer a service.
