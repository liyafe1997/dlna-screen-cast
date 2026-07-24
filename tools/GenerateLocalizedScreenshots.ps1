param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\screenshot')
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stringsRoot = Join-Path $repoRoot 'src\DesktopDlnaCast.App\Strings'
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if (-not (Test-Path -LiteralPath $edge)) {
    throw 'Microsoft Edge was not found.'
}

$languageNames = [ordered]@{
    'ar' = '阿拉伯语'; 'da' = '丹麦语'; 'de' = '德语'; 'el' = '希腊语'
    'en-US' = '英语'; 'es' = '西班牙语'; 'fi' = '芬兰语'; 'fr' = '法语'
    'he' = '希伯来语'; 'hi' = '印地语'; 'hu' = '匈牙利语'; 'id' = '印度尼西亚语'
    'it' = '意大利语'; 'ja' = '日语'; 'ko' = '韩语'; 'ms' = '马来语'
    'my-MM' = '缅甸语'; 'nb-NO' = '挪威语'; 'pl' = '波兰语'; 'pt-BR' = '巴西葡萄牙语'
    'ru' = '俄语'; 'sv' = '瑞典语'; 'th' = '泰语'; 'tr' = '土耳其语'
    'uk' = '乌克兰语'; 'vi' = '越南语'; 'zh-Hans' = '简体中文'; 'zh-Hant' = '繁体中文'
}

function ConvertTo-HtmlText([string]$value) {
    return [System.Net.WebUtility]::HtmlEncode($value)
}

function Format-Resource([string]$value, [object[]]$arguments) {
    for ($index = 0; $index -lt $arguments.Count; $index++) {
        $value = $value.Replace("{$index}", [string]$arguments[$index])
    }
    return $value
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$temporaryDirectory = Join-Path $env:TEMP ('desktop-dlna-screenshots-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

try {
    foreach ($entry in $languageNames.GetEnumerator()) {
        [xml]$document = Get-Content -Raw -Encoding UTF8 (Join-Path $stringsRoot "$($entry.Key)\Resources.resw")
        $resources = @{}
        foreach ($data in $document.root.data) {
            $resources[[string]$data.name] = [string]$data.value
        }

        $rtl = $resources.LayoutDirection -eq 'RTL'
        $direction = if ($rtl) { 'rtl' } else { 'ltr' }
        $bodyClass = if ($entry.Key -eq 'my-MM') { 'compact-script' } else { '' }
        $display = Format-Resource $resources.PrimaryDisplayName @(1, 2576, 1712)
        $quality = Format-Resource $resources.QualityMedium @('3')
        $status = Format-Resource $resources.DiscoveryCompleted @(0)

        $html = @"
<!doctype html>
<html lang="$($entry.Key)">
<head>
<meta charset="utf-8">
<style>
*{box-sizing:border-box} html,body{margin:0;width:1136px;height:790px;overflow:hidden}
body{background:#f3f3f3;color:#202020;font-family:"Segoe UI Variable Text","Segoe UI","Nirmala UI","Microsoft YaHei UI",sans-serif;font-size:18px}
.titlebar{height:40px;display:flex;align-items:center;padding-left:19px;font-size:18px;direction:ltr}
.window-controls{margin-left:auto;height:40px;display:flex;align-items:center;gap:48px;padding:0 22px;font-size:28px;font-family:"Segoe UI Symbol";line-height:1}
.app{direction:$direction;padding:4px 29px 0}
.label{font-weight:600;font-size:18px;margin-bottom:7px}
.combo{height:48px;border:2px solid #dedede;border-radius:7px;background:#fbfbfb;display:flex;align-items:center;padding:0 16px;font-size:21px;box-shadow:0 1px 0 #d1d1d1}
.combo .value{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;min-width:0}
.arrow{margin-inline-start:auto;font-size:22px;transform:translateY(-2px)}
.card{margin-top:12px;border:2px solid #e1e1e1;border-radius:14px;background:#fbfbfb;padding:15px 15px 16px}
.grid{display:grid;grid-template-columns:1fr 1fr;column-gap:18px;row-gap:13px}
.field .combo{margin-top:6px}
.checks{display:flex;align-items:flex-start;gap:27px;min-height:42px}
.check{display:flex;align-items:flex-start;gap:12px;font-size:20px;line-height:1.4;min-width:0}
.box{width:30px;height:30px;border:2px solid #969696;border-radius:7px;background:#fafafa;flex:0 0 auto;position:relative}
.box.checked{background:#076bc1;border-color:#076bc1}.box.checked:after{content:"";position:absolute;left:8px;top:3px;width:8px;height:15px;border:solid white;border-width:0 3px 3px 0;transform:rotate(45deg)}
.wide{grid-column:1/3}.actions{height:59px;display:flex;align-items:center;gap:12px;direction:$direction}
button{height:46px;border:2px solid #dfdfdf;border-radius:8px;background:#fafafa;padding:0 18px;font:20px "Segoe UI Variable Text","Segoe UI","Nirmala UI",sans-serif;color:#222;white-space:nowrap}
button.disabled{color:#a8a8a8;background:#f5f5f5}button.primary{color:white;background:#bdbdbd;border-color:#bdbdbd}
.about{margin-inline-start:auto}.volume{height:58px;display:flex;align-items:center;gap:12px;font-weight:600}
.slider{width:360px;height:6px;background:#8f8f8f;border-radius:3px;position:relative}.slider:after{content:"";position:absolute;left:166px;top:-11px;width:26px;height:26px;border:3px solid white;background:#c8c8c8;border-radius:50%;box-shadow:0 0 0 2px #d7d7d7}
.status{height:110px;border:2px solid #e1e1e1;border-radius:14px;background:#fbfbfb;padding:16px}.status .text{font-size:18px;margin-top:5px}
.compact-script{font-size:16px}.compact-script .label{font-size:16px}.compact-script .combo{font-size:18px;height:44px}
.compact-script .check,.compact-script button{font-size:17px}.compact-script .grid{row-gap:8px}.compact-script .card{padding-top:12px;padding-bottom:12px}
</style>
</head>
<body class="$bodyClass">
  <div class="titlebar"><span>$(ConvertTo-HtmlText $resources.WindowTitle)</span><div class="window-controls"><span>−</span><span style="font-size:21px">□</span><span>×</span></div></div>
  <main class="app">
    <div class="label">$(ConvertTo-HtmlText $resources.'DeviceListLabel.Text')</div>
    <div class="combo"><span class="value">$(ConvertTo-HtmlText $resources.DeviceListEmpty)</span><span class="arrow">⌄</span></div>
    <section class="card">
      <div class="grid">
        <div class="field"><div class="label">$(ConvertTo-HtmlText $resources.'CaptureDisplayLabel.Text')</div><div class="combo"><span class="value">$(ConvertTo-HtmlText $display)</span><span class="arrow">⌄</span></div></div>
        <div class="field"><div class="label">$(ConvertTo-HtmlText $resources.'OutputResolutionLabel.Text')</div><div class="combo"><span class="value">$(ConvertTo-HtmlText $resources.Resolution720p)</span><span class="arrow">⌄</span></div></div>
        <div class="field"><div class="label">$(ConvertTo-HtmlText $resources.'GopIntervalLabel.Text')</div><div class="combo"><span class="value">$(ConvertTo-HtmlText $resources.GopOneSecond)</span><span class="arrow">⌄</span></div></div>
        <div class="field"><div class="label">$(ConvertTo-HtmlText $resources.'QualityLabel.Text')</div><div class="combo"><span class="value">$(ConvertTo-HtmlText $quality)</span><span class="arrow">⌄</span></div></div>
        <div class="checks"><div class="check"><span class="box checked"></span><span>$(ConvertTo-HtmlText $resources.'IncludeCursorToggle.Content')</span></div></div>
        <div class="checks"><div class="check"><span class="box checked"></span><span>$(ConvertTo-HtmlText $resources.'IncludeAudioToggle.Content')</span></div><div class="check"><span class="box"></span><span>$(ConvertTo-HtmlText $resources.'AudioOnlyToggle.Content')</span></div></div>
        <div class="checks"><div class="check"><span class="box"></span><span>$(ConvertTo-HtmlText $resources.'MuteLocalPlaybackToggle.Content')</span></div></div>
        <div class="checks"><div class="check"><span class="box checked"></span><span>$(ConvertTo-HtmlText $resources.'StartAtLiveEdgeToggle.Content')</span></div></div>
        <div class="field wide"><div class="label">$(ConvertTo-HtmlText $resources.'AspectRatioModeLabel.Text')</div><div class="combo"><span class="value">$(ConvertTo-HtmlText $resources.AspectRatioLetterbox)</span><span class="arrow">⌄</span></div></div>
      </div>
    </section>
    <div class="actions">
      <button class="primary">$(ConvertTo-HtmlText $resources.'StartCastButton.Content')</button>
      <button class="disabled">$(ConvertTo-HtmlText $resources.'StopButton.Content')</button>
      <button>$(ConvertTo-HtmlText $resources.'RefreshDevicesButton.Content')</button>
      <button class="disabled">$(ConvertTo-HtmlText $resources.'TestRendererButton.Content')</button>
      <button class="about">ⓘ&nbsp; $(ConvertTo-HtmlText $resources.'AboutButtonLabel.Text')</button>
    </div>
    <div class="volume"><span>$(ConvertTo-HtmlText $resources.'VolumeLabel.Text')</span><div class="slider"></div></div>
    <section class="status"><div class="label">$(ConvertTo-HtmlText $resources.'SessionStatusLabel.Text')</div><div class="text">$(ConvertTo-HtmlText $status)</div></section>
  </main>
</body>
</html>
"@
        $htmlPath = Join-Path $temporaryDirectory "$($entry.Key).html"
        $pngPath = Join-Path (Resolve-Path $OutputDirectory).Path "$($entry.Value).png"
        [System.IO.File]::WriteAllText($htmlPath, $html, [System.Text.UTF8Encoding]::new($false))
        $uri = [uri]$htmlPath
        $savedErrorPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & $edge --headless --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --window-size=1136,790 "--screenshot=$pngPath" $uri.AbsoluteUri 2>$null | Out-Null
        $ErrorActionPreference = $savedErrorPreference
        if (-not (Test-Path -LiteralPath $pngPath)) {
            throw "Failed to render $($entry.Key)."
        }
    }
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Generated $($languageNames.Count) screenshots in $OutputDirectory"




