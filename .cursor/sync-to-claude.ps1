<#
.SYNOPSIS
    Sincroniza .cursor/ (canónico, versionado) -> .claude/ (espejo local, gitignored).

.DESCRIPTION
    .cursor/ es la fuente de verdad: está versionada y la comparte todo el equipo.
    .claude/ está en .gitignore, por lo que se desactualiza en silencio cuando alguien
    edita un agente o skill en .cursor/. Este script regenera el espejo.

    Transformaciones aplicadas a cada archivo de texto:
      1. Reescribe rutas .cursor/ -> .claude/ (y la variante con backslash).
      2. Restaura .cursor/state/ : ese buzón debe seguir apuntando al árbol versionado,
         porque .claude/ es local y nada escrito ahí es visible para el equipo.
      3. Normaliza finales de línea a LF, para que la comparación sea por contenido real
         y no por CRLF vs LF.

    IMPORTANTE — solo se sincroniza el CUERPO de los archivos con frontmatter.
    El frontmatter de .claude/ se preserva tal cual, porque los dos árboles usan
    dialectos distintos y el de Claude Code lleva correcciones que Cursor no tiene:
      - description debe ir entre comillas (los textos contienen "Úsame cuando:",
        y un ": " dentro de un escalar YAML sin comillas rompe el parseo);
      - .cursor declara model: claude-sonnet-4-6[] en algunos agentes, que no es un
        id de modelo válido para Claude Code (allí debe ser sonnet).
    Al crear un archivo nuevo el frontmatter sí se copia, pero normalizado.

    Divergencias intencionales (ver $Exclude) NO se sincronizan.
    El script nunca borra: los archivos huérfanos en .claude/ solo se reportan.

.PARAMETER DryRun
    Muestra qué cambiaría sin escribir nada.

.EXAMPLE
    pwsh .cursor/sync-to-claude.ps1 -DryRun
    pwsh .cursor/sync-to-claude.ps1
#>
[CmdletBinding()]
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$CursorRoot = Join-Path $RepoRoot '.cursor'
$ClaudeRoot = Join-Path $RepoRoot '.claude'

# Carpetas de configuración que se replican.
$SyncDirs = @('agents', 'rules', 'skills', 'workflows')

# Divergencias intencionales — rutas relativas a la raíz de cada árbol.
# orchestrator-routing: en Cursor el orquestador entra en CADA mensaje y toma la
# terminal; en Claude Code la terminal ya es nativa y el routing es solo end-to-end.
$Exclude = @(
    'rules/orchestrator-routing.mdc'
)

# Extensiones tratadas como texto (se transforman). El resto se copia binario.
$TextExt = @('.md', '.mdc', '.txt', '.json', '.yaml', '.yml')

$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Convert-Content {
    param([string]$Text)
    $t = $Text.Replace('.cursor/', '.claude/').Replace('.cursor\', '.claude\')
    # El buzón de estado compartido permanece en el árbol versionado.
    $t = $t.Replace('.claude/state/', '.cursor/state/').Replace('.claude\state\', '.cursor\state\')
    return $t.Replace("`r`n", "`n")
}

# Separa un documento en frontmatter (con delimitadores) y cuerpo.
# Devuelve $null en FrontMatter si el archivo no abre con '---'.
function Split-FrontMatter {
    param([string]$Text)
    $t = $Text.Replace("`r`n", "`n")
    if (-not $t.StartsWith("---`n")) {
        return [pscustomobject]@{ FrontMatter = $null; Body = $t }
    }
    $end = $t.IndexOf("`n---", 3)
    if ($end -lt 0) { return [pscustomobject]@{ FrontMatter = $null; Body = $t } }
    $stop = $t.IndexOf("`n", $end + 1)
    if ($stop -lt 0) { $stop = $t.Length - 1 }
    return [pscustomobject]@{
        FrontMatter = $t.Substring(0, $stop + 1)
        Body        = $t.Substring($stop + 1)
    }
}

# Normaliza frontmatter de Cursor al dialecto de Claude Code. Solo se usa
# al CREAR un archivo nuevo; nunca sobre uno que ya existe en .claude/.
function Normalize-FrontMatter {
    param([string]$Fm)
    $lines = $Fm.Split("`n")
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        # description sin comillas -> con comillas (los textos llevan ": ").
        if ($l -match '^description:\s*(.+)$') {
            $v = $Matches[1].Trim()
            if (-not ($v.StartsWith('"') -or $v.StartsWith("'"))) {
                $lines[$i] = 'description: "' + $v.Replace('"', '\"') + '"'
            }
        }
        # ids de modelo propios de Cursor -> alias válido en Claude Code.
        elseif ($l -match '^model:\s*(.+)$') {
            $v = $Matches[1].Trim()
            if ($v -notin @('sonnet', 'opus', 'haiku', 'inherit')) {
                $lines[$i] = 'model: sonnet'
                Write-Warning "frontmatter: model '$v' no es válido en Claude Code -> se usó 'sonnet'"
            }
        }
    }
    return ($lines -join "`n")
}

$added = @(); $updated = @(); $unchanged = 0; $skipped = @(); $orphans = @(); $fmKept = 0

foreach ($dir in $SyncDirs) {
    $srcDir = Join-Path $CursorRoot $dir
    if (-not (Test-Path $srcDir)) { continue }

    foreach ($src in Get-ChildItem $srcDir -Recurse -File) {
        $rel = $src.FullName.Substring($CursorRoot.Length + 1).Replace('\', '/')

        if ($Exclude -contains $rel) { $skipped += $rel; continue }

        $dst = Join-Path $ClaudeRoot ($rel -replace '/', '\')
        $isText = $TextExt -contains $src.Extension.ToLower()

        if ($isText) {
            $srcConv = Convert-Content ([System.IO.File]::ReadAllText($src.FullName))
            $exists  = Test-Path $dst
            $old     = if ($exists) { [System.IO.File]::ReadAllText($dst).Replace("`r`n", "`n") } else { $null }

            $s = Split-FrontMatter $srcConv
            if ($exists) {
                $d = Split-FrontMatter $old
                if ($null -ne $d.FrontMatter -and $null -ne $s.FrontMatter) {
                    # Solo el cuerpo se sincroniza; el frontmatter local manda.
                    $new = $d.FrontMatter + $s.Body
                    $fmKept++
                }
                else { $new = $srcConv }
            }
            elseif ($null -ne $s.FrontMatter) {
                $new = (Normalize-FrontMatter $s.FrontMatter) + $s.Body
            }
            else { $new = $srcConv }

            if (-not $exists)      { $added += $rel }
            elseif ($old -ne $new) { $updated += $rel }
            else                   { $unchanged++; continue }

            if (-not $DryRun) {
                $parent = Split-Path -Parent $dst
                if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force $parent | Out-Null }
                [System.IO.File]::WriteAllText($dst, $new, $Utf8NoBom)
            }
        }
        else {
            $exists = Test-Path $dst
            $same = $exists -and ((Get-FileHash $src.FullName).Hash -eq (Get-FileHash $dst).Hash)
            if ($same) { $unchanged++; continue }
            if ($exists) { $updated += $rel } else { $added += $rel }

            if (-not $DryRun) {
                $parent = Split-Path -Parent $dst
                if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force $parent | Out-Null }
                Copy-Item $src.FullName $dst -Force
            }
        }
    }

    # Huérfanos: existen en .claude/ pero ya no en .cursor/ (solo se reportan).
    $dstDir = Join-Path $ClaudeRoot $dir
    if (Test-Path $dstDir) {
        foreach ($d in Get-ChildItem $dstDir -Recurse -File) {
            $rel = $d.FullName.Substring($ClaudeRoot.Length + 1).Replace('\', '/')
            if (-not (Test-Path (Join-Path $CursorRoot ($rel -replace '/', '\')))) { $orphans += $rel }
        }
    }
}

$mode = if ($DryRun) { 'DRY-RUN (no se escribió nada)' } else { 'APLICADO' }
Write-Host ""
Write-Host "sync .cursor/ -> .claude/  [$mode]"
Write-Host ("-" * 52)
Write-Host ("  nuevos      : {0}" -f $added.Count)
foreach ($f in $added)   { Write-Host "      + $f" }
Write-Host ("  actualizados: {0}" -f $updated.Count)
foreach ($f in $updated) { Write-Host "      ~ $f" }
Write-Host ("  sin cambios : {0}" -f $unchanged)
Write-Host ("  frontmatter : {0} archivos conservaron el suyo (solo se sincronizó el cuerpo)" -f $fmKept)
Write-Host ("  excluidos   : {0}  (divergencia intencional)" -f $skipped.Count)
foreach ($f in $skipped) { Write-Host "      = $f" }

if ($orphans.Count -gt 0) {
    Write-Host ("  huérfanos   : {0}  (en .claude/ sin origen en .cursor/ — revisar a mano)" -f $orphans.Count)
    foreach ($f in $orphans) { Write-Host "      ? $f" }
}
Write-Host ""
