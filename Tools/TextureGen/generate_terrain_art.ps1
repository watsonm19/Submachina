<#
  generate_terrain_art.ps1 — reusable Terrain Object Generator art baker (Google Nano Banana).

  Generates tileable MATERIALS and stamped DECALS for the Terrain Object Generator's PaintLayer /
  DecalLayer slots, from a JSON manifest. Materials are saved as-is; decals are generated on a
  magenta background and chroma-keyed to alpha. Framing/lighting/stylization constraints and the
  IMAGE_RECITATION-avoiding "stylized game art" phrasing are baked into the shared style tails, so a
  manifest only needs a subject line per texture (+ an optional global style flavour).

  USAGE
    $env:GEMINI_KEY='...'; ./generate_terrain_art.ps1 -Manifest my.json
    ./generate_terrain_art.ps1 -Manifest my.json -Key '...'         # explicit key
    ./generate_terrain_art.ps1 -Manifest my.json -Force             # overwrite existing

  KEY RESOLUTION (first hit wins): -Key arg  ->  $env:GEMINI_KEY  ->  Tools/TextureGen/gemini_key.local.txt
  The key MUST belong to a BILLING-ENABLED Google AI project (image gen is not free-tier). ~$0.039/image.

  MANIFEST SCHEMA (JSON)
    {
      "style":      "optional global style flavour weaved into every prompt (e.g. 'dark volcanic basalt, ashy')",
      "resolution": 1024,                       // optional, default 1024
      "materials":  { "RockBare": "bare seabed rock, tight grain", ... },   // Name -> subject
      "decals":     { "CoralBranch": "branching staghorn coral clump", ... }// Name -> subject
    }
  Files land as Art/Terrain/Materials/Mat_<Name>.png and Art/Terrain/Decals/Dec_<Name>.png.
#>
param(
  [Parameter(Mandatory=$true)][string]$Manifest,
  [string]$Key,
  [string]$Model = 'gemini-2.5-flash-image',
  [string]$OutRoot,
  [string]$Tag = '',      # optional filename suffix so variant sets coexist, e.g. -Tag '_Gritty'
  [switch]$Force
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# --- paths ---
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = (Resolve-Path (Join-Path $ScriptDir '..\..')).Path
if (-not $OutRoot) { $OutRoot = Join-Path $RepoRoot 'Assets/Submachina/Art/Terrain' }
$MatDir = Join-Path $OutRoot 'Materials'
$DecDir = Join-Path $OutRoot 'Decals'

# --- key resolution ---
if (-not $Key) { $Key = $env:GEMINI_KEY }
if (-not $Key) {
  $keyFile = Join-Path $ScriptDir 'gemini_key.local.txt'
  if (Test-Path $keyFile) { $Key = (Get-Content $keyFile -Raw).Trim() }
}
if (-not $Key) {
  throw "No API key. Pass -Key, set `$env:GEMINI_KEY, or create Tools/TextureGen/gemini_key.local.txt (a billing-enabled Google AI key)."
}
$Uri = "https://generativelanguage.googleapis.com/v1beta/models/${Model}:generateContent?key=$Key"

# --- manifest ---
if (-not (Test-Path $Manifest)) { throw "Manifest not found: $Manifest" }
$m = Get-Content $Manifest -Raw | ConvertFrom-Json
$resolution = if ($m.resolution) { [int]$m.resolution } else { 1024 }
$styleFlavour = if ($m.style) { " " + $m.style } else { "" }

# --- shared style tails ---
# ART DIRECTION: gritty industrial hard-sci-fi undersea mining, in the spirit of Factorio and
# Delta V: Rings of Saturn — grounded/believable, "realistic painterly" (real materials rendered
# with visible confident brushwork; NOT cartoonish, NOT photographic — which also dodges the
# IMAGE_RECITATION filter). Muted desaturated industrial palette, weathered and tactile.
$Aesthetic = 'Gritty, grounded hard-sci-fi undersea mood in the spirit of Factorio and Delta V: Rings of Saturn. Realistic painterly hand-painted game art: believable NATURAL materials rendered with confident concept-art brushwork and rich painterly detail, physically grounded, weathered and tactile; NOT cartoonish, NOT photographic, NOT mechanical or robotic. Muted, desaturated, moody palette with deep grimy crevices, fine grit and settled dust; sparse cold mineral glints in the stone. Cohesive, readable, deep-water gloom.'
# Framing/lighting constraints so the rock bake can apply its own URP 2D lighting + normal relief.
$MatFrame = 'Render it as a flat repeating SURFACE material (not a scene, not an object, no buildings or structures). Top-down orthographic overhead view, camera straight down. Perfectly flat even diffuse lighting, NO directional shadows, NO glossy highlights, NO vignette. Uniform detail filling the whole square frame edge to edge, no border, no text. Keep the overall tone fairly neutral grey so it can be recoloured at runtime. Square 1:1.'
$Chroma   = 'a solid flat uniform bright magenta #FF00FF background filling every pixel behind it, absolutely no checkerboard and no transparency, no cast shadow on the background'
$MatStyle = " $Aesthetic $MatFrame$styleFlavour"
$DecStyle = " $Aesthetic One single isolated object as a cut-out game prop, NOT a square tile, NOT a slab, NOT a full-frame texture. The subject's own irregular silhouette sits with clear empty margin around it, centered, top-down orthographic view, flat even diffuse lighting, no cast shadow, on $Chroma. Square 1:1. Nothing else in the frame.$styleFlavour"

$log = Join-Path $ScriptDir 'gen_progress.log'
function Log($msg){ $line = "[{0}] {1}" -f (Get-Date -Format HH:mm:ss), $msg; Write-Host $line; Add-Content $log $line }

# Calls the image API; retries with a fresh variation nudge to clear IMAGE_RECITATION blocks.
function CallImage($prompt){
  for($try=1; $try -le 4; $try++){
    $p = if($try -eq 1){ $prompt } else { "$prompt Unique stylized variation number $try, a distinctly different composition and layout." }
    $body = @{ contents=@(@{parts=@(@{text=$p})}); generationConfig=@{responseModalities=@('IMAGE')} } | ConvertTo-Json -Depth 8
    try {
      $r = Invoke-RestMethod -Uri $Uri -Method Post -ContentType 'application/json' -Body $body
      $fr = $r.candidates[0].finishReason
      $part = $r.candidates[0].content.parts | Where-Object { $_.inlineData } | Select-Object -First 1
      if($part){ return [Convert]::FromBase64String($part.inlineData.data) }
      Log "    (no image, finishReason=$fr, retry ${try})"
    } catch { Log "    (error try ${try}: $($_.Exception.Message))"; Start-Sleep -Seconds (2*$try) }
  }
  return $null
}

# Chroma-keys magenta -> alpha (+ mild despill) and writes an RGBA PNG.
function KeyDecalToFile($bytes,$outPath){
  $ms=New-Object IO.MemoryStream(,$bytes); $src=[System.Drawing.Image]::FromStream($ms); $bmp=New-Object System.Drawing.Bitmap($src)
  $w=$bmp.Width; $h=$bmp.Height
  $out=New-Object System.Drawing.Bitmap($w,$h,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  for($y=0;$y -lt $h;$y++){ for($x=0;$x -lt $w;$x++){
    $c=$bmp.GetPixel($x,$y)
    $mag=[Math]::Min($c.R,$c.B)-$c.G
    $a=255; if($mag -gt 60){ $a=0 } elseif($mag -gt 10){ $a=[int](255*(1-($mag-10)/50.0)) }
    $spill=[Math]::Max(0,(($c.R+$c.B)/2)-$c.G)
    $R=[int][Math]::Max(0,$c.R-$spill*0.5); $B=[int][Math]::Max(0,$c.B-$spill*0.5); $G=[int]$c.G
    $out.SetPixel($x,$y,[System.Drawing.Color]::FromArgb($a,$R,$G,$B))
  }}
  $out.Save($outPath,[System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose();$src.Dispose();$ms.Dispose();$out.Dispose()
}

function GenSet($section, $dir, $prefix, $styleTail, $isDecal){
  if (-not $section) { return }
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  $props = @($section.PSObject.Properties)
  Log "=== $prefix set ($($props.Count)) ==="
  foreach($prop in $props){
    $name = $prop.Name; $subject = $prop.Value
    $path = Join-Path $dir "$prefix$name$Tag.png"
    if((Test-Path $path) -and -not $Force){ Log "  skip $prefix$name$Tag (exists)"; continue }
    Log "  $prefix$name$Tag ..."
    $b = CallImage ("$subject$styleTail")
    if(-not $b){ Log "  FAILED $prefix$name$Tag"; continue }
    if($isDecal){ KeyDecalToFile $b $path } else { [IO.File]::WriteAllBytes($path,$b) }
    Log "  saved $prefix$name$Tag"
  }
}

Log "=== generate_terrain_art: model=$Model res=$resolution out=$OutRoot ==="
GenSet $m.materials $MatDir 'Mat_' $MatStyle $false
GenSet $m.decals    $DecDir 'Dec_' $DecStyle $true
Log "=== DONE ==="
