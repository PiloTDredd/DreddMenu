# Рисует иконку Dredd Menu и собирает Assets\app.ico (+ app.png).
# Запуск: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 256.0

    # Фон: скруглённый квадрат с градиентом
    $bg = RoundRect (8*$k) (8*$k) (240*$k) (240*$k) (56*$k)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $size, $size),
        [System.Drawing.Color]::FromArgb(255, 56, 120, 255), [System.Drawing.Color]::FromArgb(255, 150, 60, 255))
    $g.FillPath($grad, $bg)

    # Блик сверху
    $shine = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, ($size + 1)),
        [System.Drawing.Color]::FromArgb(55, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillPath($shine, $bg)

    # Карточка меню
    $card = RoundRect (58*$k) (50*$k) (140*$k) (156*$k) (20*$k)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 255, 255, 255))), $card)

    $lineBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 170, 180, 215))
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 100, 90, 255))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    # Строки меню: точка + линия
    foreach ($row in @(0, 2)) {
        $y = (78 + $row * 34) * $k
        $g.FillEllipse($lineBrush, 76*$k, $y, 14*$k, 14*$k)
        $g.FillPath($lineBrush, (RoundRect (98*$k) ($y + 2*$k) (80*$k) (10*$k) (5*$k)))
    }
    # Средняя строка — выделенная, с тумблером
    $y = 112 * $k
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 100, 90, 255))), (RoundRect (68*$k) ($y - 6*$k) (120*$k) (26*$k) (8*$k)))
    $g.FillPath($accent, (RoundRect (76*$k) ($y + 2*$k) (60*$k) (10*$k) (5*$k)))
    $g.FillPath($accent, (RoundRect (146*$k) ($y - 1*$k) (32*$k) (16*$k) (8*$k)))
    $g.FillEllipse($white, 163*$k, $y + 1*$k, 12*$k, 12*$k)

    # Нижняя строка
    $y = 180 * $k
    $g.FillEllipse($lineBrush, 76*$k, $y - 4*$k, 14*$k, 14*$k)
    $g.FillPath($lineBrush, (RoundRect (98*$k) ($y - 2*$k) (56*$k) (10*$k) (5*$k)))

    # Курсор
    if ($size -ge 32) {
        $cur = New-Object System.Drawing.Drawing2D.GraphicsPath
        $pts = @(
            (New-Object System.Drawing.PointF (170*$k), (150*$k)),
            (New-Object System.Drawing.PointF (170*$k), (222*$k)),
            (New-Object System.Drawing.PointF (187*$k), (205*$k)),
            (New-Object System.Drawing.PointF (199*$k), (230*$k)),
            (New-Object System.Drawing.PointF (211*$k), (224*$k)),
            (New-Object System.Drawing.PointF (199*$k), (199*$k)),
            (New-Object System.Drawing.PointF (222*$k), (197*$k))
        )
        $cur.AddPolygon($pts)
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 25, 25, 40))), $cur)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), (7*$k)
        $pen.LineJoin = 'Round'
        $g.DrawPath($pen, $cur)
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 25, 25, 40))), $cur)
    }

    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($s -eq 256) { $bmp.Save((Join-Path $assets 'app.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# Собираем .ico из PNG-кадров
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $out.ToArray())
"OK: $(Join-Path $assets 'app.ico')"
