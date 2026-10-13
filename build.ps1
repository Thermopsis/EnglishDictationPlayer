param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $root 'vendor\roslyn\tools\csc.exe'
$library = Join-Path $root 'vendor\naudio\lib\net35\NAudio.dll'
$extraLibraries = @('soundtouch.net\lib\net461\SoundTouch.Net.dll', 'system.memory\lib\net461\System.Memory.dll', 'system.buffers\lib\net461\System.Buffers.dll', 'system.runtime.compilerservices.unsafe\lib\net461\System.Runtime.CompilerServices.Unsafe.dll', 'system.numerics.vectors\lib\net46\System.Numerics.Vectors.dll') | ForEach-Object { Join-Path (Join-Path $root 'vendor') $_ }
if ((Get-FileHash -LiteralPath (Join-Path $root 'vendor\naudio.1.10.0.nupkg') -Algorithm SHA256).Hash -ne 'E4A80EED41DAD794695F9C114432DE4667D20BDFA80825AA94D159708CF29217') { throw 'NAudio package checksum mismatch' }
$destination = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $root 'dist\PhrasePlayer' }
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+', '/codepage:65001',
    ('/out:' + (Join-Path $destination '英语听写播放器.exe')),
    ('/win32manifest:' + (Join-Path $root 'source\app.manifest')),
    ('/win32icon:' + (Join-Path $root 'source\AppIcon.ico')),
    ('/resource:' + (Join-Path $root 'source\AppIcon.ico') + ',PhrasePlayer.AppIcon.ico'),
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Xml.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',
    ('/reference:' + $library))
$arguments += Get-ChildItem -LiteralPath (Join-Path $root 'source') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$arguments += $extraLibraries | ForEach-Object { '/reference:' + $_ }
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item -LiteralPath $library -Destination (Join-Path $destination 'NAudio.dll') -Force
Copy-Item -LiteralPath (Join-Path $root 'source\app.config') -Destination (Join-Path $destination '英语听写播放器.exe.config') -Force
$extraLibraries | ForEach-Object { Copy-Item -LiteralPath $_ -Destination $destination -Force }
Copy-Item -LiteralPath (Join-Path $root 'vendor\soundtouch.net\LICENSE\LICENSE') -Destination (Join-Path $destination 'SoundTouch-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $root 'vendor\system.memory\LICENSE.TXT') -Destination (Join-Path $destination 'Microsoft-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $root 'vendor\NAudio-LICENSE.txt') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $destination -Force
Get-Item -LiteralPath (Join-Path $destination '英语听写播放器.exe') | Select-Object Name,Length
