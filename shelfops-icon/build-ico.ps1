Add-Type -AssemblyName System.Drawing

$out = Split-Path -Parent $MyInvocation.MyCommand.Path

function Add-RoundRect([System.Drawing.Drawing2D.GraphicsPath]$p, [single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $d = $r * 2
    $p.StartFigure()
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
}

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 64.0
    $shapes = @(
        @{x=2;  y=2;    w=60; h=60; r=13;  c='#0f3057'},
        @{x=14; y=13;   w=36; h=9;  r=2.5; c='#ffffff'},
        @{x=14; y=13;   w=9;  h=20; r=2.5; c='#ffffff'},
        @{x=14; y=27.5; w=36; h=9;  r=2.5; c='#4da3ff'},
        @{x=41; y=31;   w=9;  h=20; r=2.5; c='#ffffff'},
        @{x=14; y=42;   w=36; h=9;  r=2.5; c='#ffffff'}
    )
    foreach ($sh in $shapes) {
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        Add-RoundRect $path ([single]($sh.x*$s)) ([single]($sh.y*$s)) ([single]($sh.w*$s)) ([single]($sh.h*$s)) ([single]($sh.r*$s))
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($sh.c))
        $g.FillPath($brush, $path)
        $brush.Dispose(); $path.Dispose()
    }
    $g.Dispose()
    return $bmp
}

# 1) render PNGs
$bitmaps = @{}
foreach ($sz in 16, 32, 48, 256) {
    $bmp = Draw-Icon $sz
    $bmp.Save("$out\shelfops-$sz.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmaps[$sz] = $bmp
}

# 2) ICO entry data (16/32/48 as BMP, 256 as PNG)
function Get-BmpEntryData([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $andRow = [int]([math]::Ceiling($w / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int32]40)               # BITMAPINFOHEADER size
    $bw.Write([int32]$w)
    $bw.Write([int32]($h * 2))         # height doubled (XOR + AND mask)
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int32]0)
    $bw.Write([int32]($w * $h * 4 + $andRow * $h))
    $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0)
    for ($y = $h - 1; $y -ge 0; $y--) {   # XOR data: BGRA, bottom-up
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $bw.Write((New-Object 'byte[]' ($andRow * $h)))   # AND mask: all zero (alpha used)
    $bw.Flush()
    return , $ms.ToArray()   # comma prevents pipeline unrolling of byte[]
}

$entries = @()
foreach ($sz in 16, 32, 48) {
    $entries += , @{ size = $sz; data = (Get-BmpEntryData $bitmaps[$sz]) }
}
$entries += , @{ size = 256; data = [System.IO.File]::ReadAllBytes("$out\shelfops-256.png") }

# 3) write ICO
$icoPath = "$out\ShelfOps.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $wb = if ($e.size -ge 256) { 0 } else { $e.size }
    $bw.Write([byte]$wb); $bw.Write([byte]$wb); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int32]$e.data.Length); $bw.Write([int32]$offset)
    $offset += $e.data.Length
}
foreach ($e in $entries) { $bw.Write([byte[]]$e.data) }
$bw.Flush(); $fs.Close()

# 4) verify: parse back at each size
$icon = New-Object System.Drawing.Icon($icoPath)
Write-Output "ICO parsed OK (default: $($icon.Width)x$($icon.Height))"
foreach ($sz in 16, 32, 48, 256) {
    $i = New-Object System.Drawing.Icon($icoPath, $sz, $sz)
    Write-Output "  request ${sz}px -> got $($i.Width)x$($i.Height)"
    $i.Dispose()
}
$icon.Dispose()
foreach ($b in $bitmaps.Values) { $b.Dispose() }
Write-Output "done: $icoPath ($((Get-Item $icoPath).Length) bytes)"
