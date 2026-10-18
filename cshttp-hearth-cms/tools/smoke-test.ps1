<#
  End-to-end smoke test for a running cshttp Hearth CMS instance.

    powershell -ExecutionPolicy Bypass -File tools\smoke-test.ps1 -BaseUrl http://127.0.0.1:5080 -User admin -Password <pwd>

  Works against any deployment: direct process, IIS (HttpPlatformHandler) or nginx.
  Uses curl.exe (built into Windows 10+). Creates one test post + one test image
  and removes the post again (the uploaded image stays in the media library).
#>
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$User,
    [Parameter(Mandatory = $true)][string]$Password
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')
$work = Join-Path $env:TEMP ("hearth-smoke-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null
$jar = Join-Path $work 'cookies.txt'
$script:fail = 0
$script:pass = 0

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { $script:pass++; Write-Host ("  PASS  " + $name) -ForegroundColor Green }
    else { $script:fail++; Write-Host ("  FAIL  " + $name + "  " + $detail) -ForegroundColor Red }
}

# curl wrapper: returns @{ Status; Body; Headers }
function Http([string]$method, [string]$path, [string[]]$extra = @(), [string]$cookieJar = $jar) {
    $hdr = Join-Path $work 'h.txt'; $bodyFile = Join-Path $work 'b.txt'
    $args = @('-s', '-S', '-X', $method, '-D', $hdr, '-o', $bodyFile, '-w', '%{http_code}', '-b', $cookieJar, '-c', $cookieJar) + $extra + @($BaseUrl + $path)
    if ($method -eq 'HEAD') { $args = @('-s', '-S', '-I', '-o', $hdr, '-w', '%{http_code}', '-b', $cookieJar, '-c', $cookieJar) + $extra + @($BaseUrl + $path) }
    $code = & curl.exe @args
    $body = if (Test-Path $bodyFile) { [IO.File]::ReadAllText($bodyFile) } else { '' }
    $headers = if (Test-Path $hdr) { [IO.File]::ReadAllText($hdr) } else { '' }
    Remove-Item $bodyFile, $hdr -ErrorAction SilentlyContinue
    return @{ Status = [int]$code; Body = $body; Headers = $headers }
}

Write-Host "Hearth CMS smoke test -> $BaseUrl"

# ---- public site ---------------------------------------------------------
$r = Http GET '/'
Check 'GET / returns 200 html' ($r.Status -eq 200 -and $r.Body -match '<html') "status $($r.Status)"
Check 'response comes from cshttp host' ($r.Headers -match '(?im)^Server: .*hearth-cshttp' -or $r.Headers -match '(?im)^Server: (nginx|Microsoft-IIS)') ''
foreach ($p in '/latest-post', '/categories-latest-post', '/sitemap.xml', '/robots.txt') {
    $r = Http GET $p
    Check "GET $p returns 200" ($r.Status -eq 200) "status $($r.Status)"
}
$r = Http GET '/this-page-does-not-exist-xyz'
Check 'unknown slug returns 404' ($r.Status -eq 404) "status $($r.Status)"
$r = Http HEAD '/'
Check 'HEAD / returns 200' ($r.Status -eq 200) "status $($r.Status)"

# ---- static files + caching ------------------------------------------------
$r = Http GET '/css/admin.css'
Check 'static /css/admin.css 200' ($r.Status -eq 200 -and $r.Body.Length -gt 1000) "status $($r.Status)"
$etag = ([regex]::Match($r.Headers, '(?im)^ETag:\s*(.+?)\s*$')).Groups[1].Value
if ($etag) {
    # Windows PowerShell 5.1 drops embedded double quotes when calling native exes - escape them.
    $r2 = Http GET '/css/admin.css' @('-H', ("If-None-Match: " + $etag.Replace('"', '\"')))
    Check 'conditional GET returns 304' ($r2.Status -eq 304) "status $($r2.Status)"
}
$r = Http GET '/fonts/fontawesome/css/all.min.css'
Check 'static font css 200' ($r.Status -eq 200) "status $($r.Status)"

# ---- security probes -------------------------------------------------------
foreach ($p in '/App_Data/hearth-cms.sqlite', '/App_Data/config.txt', '/HearthCms.dll', '/web.config', '/css/../App_Data/config.txt', '/media/%2e%2e/App_Data/config.txt') {
    $r = Http GET $p @('--path-as-is')
    Check "blocked: $p" ($r.Status -eq 404 -or $r.Status -eq 400) "status $($r.Status)"
}

# ---- admin login -----------------------------------------------------------
$r = Http GET '/admin'
Check 'GET /admin (anonymous) shows login' ($r.Body -match "type=['""]password['""]") "status $($r.Status)"
$r = Http POST '/admin/api/login' @('--data-urlencode', 'action=login', '--data-urlencode', "username=$User", '--data-urlencode', 'password=wrong-password')
Check 'wrong password rejected (401)' ($r.Status -eq 401) "status $($r.Status)"
$r = Http POST '/admin/api/login' @('--data-urlencode', 'action=login', '--data-urlencode', "username=$User", '--data-urlencode', "password=$Password", '--data-urlencode', 'remember=1')
Check 'login succeeds' ($r.Status -eq 200 -and $r.Body -match '"success":\s*true') "status $($r.Status) $($r.Body)"
$r = Http GET '/admin'
Check 'GET /admin shows dashboard after login' ($r.Body -match 'Dashboard' -and $r.Body -notmatch "type=['""]password['""]") "status $($r.Status)"
foreach ($p in '/admin/posts', '/admin/pages', '/admin/media', '/admin/categories', '/admin/settings', '/admin/themes', '/admin/users', '/admin/nav', '/admin/analytics') {
    $r = Http GET $p
    Check "admin page $p 200" ($r.Status -eq 200) "status $($r.Status)"
}

# ---- media upload (multipart) + thumbnail -----------------------------------
Add-Type -AssemblyName System.Drawing
$png = Join-Path $work 'smoke-image.png'
$bmp = New-Object System.Drawing.Bitmap 1200, 800
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, 200, 120, 40))
$g.FillEllipse([System.Drawing.Brushes]::White, 300, 150, 600, 500)
$g.Dispose(); $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

$r = Http POST '/api/admin/media' @('-F', 'action=upload', '-F', "file=@$png;type=image/png")
$uploadOk = $r.Status -eq 200 -and $r.Body -match '"success":\s*true'
Check 'multipart media upload' $uploadOk "status $($r.Status) $($r.Body)"
$imgUrl = ([regex]::Match($r.Body, '"(/media/[^"]+?\.png)"')).Groups[1].Value
if ($imgUrl) {
    $r = Http GET $imgUrl
    Check "uploaded file served ($imgUrl)" ($r.Status -eq 200) "status $($r.Status)"
}

# ---- post create / view / delete -------------------------------------------
$slug = 'cshttp-smoke-' + (Get-Date -Format 'yyyyMMddHHmmss')
$r = Http POST '/api/admin/posts' @('--data-urlencode', 'action=save', '--data-urlencode', 'id=0',
    '--data-urlencode', 'title=cshttp smoke test post', '--data-urlencode', "slug=$slug",
    '--data-urlencode', 'content_format=markdown', '--data-urlencode', "content=# Hello from cshttp`n`nUnicode check: caf$([char]0x00e9) $([char]0x4e2d)$([char]0x6587)",
    '--data-urlencode', 'is_published=1', '--data-urlencode', "cover_image=$imgUrl")
Check 'create post' ($r.Status -eq 200 -and $r.Body -match '"success":\s*true') "status $($r.Status) $($r.Body)"
$postId = ([regex]::Match($r.Body, '"id":\s*(\d+)')).Groups[1].Value

$r = Http GET "/$slug"
Check 'new post is publicly visible' ($r.Status -eq 200 -and $r.Body -match 'Hello from cshttp') "status $($r.Status)"
Check 'UTF-8 round trip' ($r.Body -match ('caf' + [char]0x00e9)) ''

if ($imgUrl) {
    Start-Sleep -Seconds 2
    $thumb = $imgUrl -replace '^/media/', '/media/thumbnail/'
    $r = Http GET $thumb
    Check "cover thumbnail generated ($thumb)" ($r.Status -eq 200) "status $($r.Status)"
}

if ($postId) {
    $r = Http POST '/api/admin/posts' @('--data-urlencode', 'action=delete', '--data-urlencode', "id=$postId")
    Check 'soft-delete post' ($r.Body -match '"success":\s*true') $r.Body
    $r = Http POST '/api/admin/posts' @('--data-urlencode', 'action=purge', '--data-urlencode', "id=$postId")
    Check 'purge post' ($r.Body -match '"success":\s*true') $r.Body
    $r = Http GET "/$slug"
    Check 'purged post returns 404' ($r.Status -eq 404) "status $($r.Status)"
}

# ---- remember-me rehydration (simulates a server restart) ------------------
$jar2 = Join-Path $work 'cookies2.txt'
$rmt = Get-Content $jar | Where-Object { $_ -match "`trmt(_\d+)?`t" }
if ($rmt) {
    Set-Content $jar2 ("# Netscape HTTP Cookie File`n" + ($rmt -join "`n"))
    $r = Http GET '/admin' @() $jar2
    Check 'remember-me cookie alone restores login' ($r.Body -match 'Dashboard' -and $r.Body -notmatch "type=['""]password['""]") "status $($r.Status)"
}
else { Check 'remember-me cookie issued' $false 'no rmt cookie in jar' }

# ---- logout ----------------------------------------------------------------
$r = Http GET '/logout'
$r = Http GET '/admin'
Check 'logout ends the session' ($r.Body -match "type=['""]password['""]") "status $($r.Status)"

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ""
Write-Host ("{0} passed, {1} failed" -f $script:pass, $script:fail) -ForegroundColor $(if ($script:fail) { 'Red' } else { 'Green' })
exit $script:fail
