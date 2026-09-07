Add-Type -AssemblyName System.Drawing
$root=Split-Path $PSScriptRoot -Parent
$images=@()
foreach($size in @(16,24,32,48,64,128,256)) {
 $bitmap=New-Object Drawing.Bitmap($size,$size)
 $g=[Drawing.Graphics]::FromImage($bitmap)
 $g.SmoothingMode='AntiAlias'; $g.ScaleTransform($size/256.0,$size/256.0)
 $dark=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(24,29,42))
 $gold=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(245,193,75))
 $white=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(240,245,250))
 $g.FillEllipse($dark,4,4,248,248)
 $points=[Drawing.PointF[]]@([Drawing.PointF]::new(66,49),[Drawing.PointF]::new(96,69),[Drawing.PointF]::new(128,35),[Drawing.PointF]::new(160,69),[Drawing.PointF]::new(190,49),[Drawing.PointF]::new(178,95),[Drawing.PointF]::new(78,95))
 $g.FillPolygon($gold,$points)
 $g.FillRectangle($white,48,108,160,99)
 $g.FillEllipse($dark,90,119,76,76)
 $g.FillEllipse($gold,106,135,44,44)
 $g.FillRectangle($gold,180,120,16,12)
 $memory=New-Object IO.MemoryStream
 $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
 $images+=,@{Size=$size;Bytes=$memory.ToArray()}
 if($size -eq 256) { $bitmap.Save((Join-Path $root 'Windows\Resources\camera.png'),[Drawing.Imaging.ImageFormat]::Png) }
 $memory.Dispose();$g.Dispose();$bitmap.Dispose();$dark.Dispose();$gold.Dispose();$white.Dispose()
}
$stream=[IO.File]::Create((Join-Path $root 'Windows\Resources\camera.ico'))
$writer=New-Object IO.BinaryWriter($stream)
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$images.Count)
$offset=6+16*$images.Count
foreach($img in $images) {
 $dimension=if($img.Size -eq 256){0}else{$img.Size}
 $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
 $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$img.Bytes.Length);$writer.Write([uint32]$offset)
 $offset+=$img.Bytes.Length
}
foreach($img in $images) { $writer.Write([byte[]]$img.Bytes) }
$writer.Dispose();$stream.Dispose()
