# ТОиР-Проф — сквозная проверка: свежая база из сида, сборка, тесты,
# запуск API и основной сценарий за три роли. Итог — smoke-log.txt рядом
# со скриптом. Запуск из корня репозитория (Windows PowerShell 5.1+):
#   powershell -ExecutionPolicy Bypass -File tools\smoke.ps1
# Нужны: PostgreSQL с базой toirprof (пароль toirprof), .NET 10, Node.js.
param([switch]$Unattended)
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$app  = if (Test-Path (Join-Path $here 'app')) { Join-Path $here 'app' } else { Split-Path -Parent $here }
$log  = Join-Path $here 'smoke-log.txt'
$base = 'http://localhost:5080'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Set-Content -Path $log -Value "=== ToirProf smoke $(Get-Date -Format s) ===" -Encoding UTF8

$script:ok = 0; $script:fail = 0
function Say($t) { Write-Host $t; Add-Content -Path $log -Value $t -Encoding UTF8 }
function Pass($n, $d) { $script:ok++;  Say "[OK]   $n $d" }
function Fail($n, $d) { $script:fail++; Say "[FAIL] $n $d" }

Add-Type -AssemblyName System.Net.Http
$script:http = New-Object System.Net.Http.HttpClient
$script:http.Timeout = [TimeSpan]::FromSeconds(30)
function Call($method, $path, $token, $body) {
    $req = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($method)), "$base$path"
    if ($token) { $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $token) }
    if ($null -ne $body) {
        $json = $body | ConvertTo-Json -Depth 6
        $req.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, 'application/json')
    }
    try {
        $resp = $script:http.SendAsync($req).GetAwaiter().GetResult()
        $text = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return @{ Code = [int]$resp.StatusCode; Json = ($text | ConvertFrom-Json -ErrorAction SilentlyContinue); Raw = "$text" }
    } catch {
        return @{ Code = 0; Json = $null; Raw = $_.Exception.InnerException.Message + ' ' + $_.Exception.Message }
    }
}
function Check($name, $r, [int[]]$expect) {
    $msg = if ($r.Json -and $r.Json.message) { $r.Json.message } else { ($r.Raw -replace '\s+',' ').Substring(0, [Math]::Min(200, $r.Raw.Length)) }
    if ($expect -contains $r.Code) { Pass $name "(HTTP $($r.Code))" } else { Fail $name "(HTTP $($r.Code), ждали $($expect -join '/')): $msg" }
    return ($expect -contains $r.Code)
}

# ---------- остановить прошлый запуск: иначе сборка упрётся в занятые DLL ----------
Get-Process -Name 'ToirProf.Desktop', 'ToirProf.Api' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
& taskkill.exe /F /IM ToirProf.Desktop.exe 2>&1 | Out-Null
Start-Sleep -Seconds 1

# ---------- свежая база из сида ----------
$psql = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
$env:PGPASSWORD = 'toirprof'; $env:PGCLIENTENCODING = 'UTF8'
Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
& $psql -h 127.0.0.1 -U toirprof -d toirprof -q -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;' 2>&1 | Out-Null
$dbOk = $true
foreach ($f in 'schema','triggers','functions','seed') {
    $out = & $psql -h 127.0.0.1 -U toirprof -d toirprof -v ON_ERROR_STOP=1 -q -f (Join-Path $app "db\$f.sql") 2>&1
    if ($LASTEXITCODE -ne 0) { $dbOk = $false; Say "SQL $f.sql: $($out | Select-Object -Last 3)" }
}
$verify = (& $psql -h 127.0.0.1 -U toirprof -d toirprof -q -f (Join-Path $app 'db\verify.sql') 2>&1) -join "`n"
if ($dbOk -and $verify -match 'ИТОГО\s*\|\s*(\d+)\s*\|\s*(\d+)\s*\|\s*0') { Pass 'База пересоздана из сида, verify.sql' "$($Matches[2]) из $($Matches[1])" } else { Fail 'База пересоздана из сида, verify.sql' '' }

# ---------- сборка и тесты ----------
function Build($name, [scriptblock]$block) {
    $out = & $block 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -eq 0) { Pass $name '' } else { Fail $name ''; $out | Select-Object -Last 25 | ForEach-Object { Say "   $_" } }
    return $out
}
Push-Location $app
Build 'dotnet build (все проекты)' { dotnet build src\ToirProf.slnx -nologo -v q -clp:ErrorsOnly } | Out-Null
$t = Build 'dotnet test' { dotnet test src\Tests -nologo --no-build }
$t | Where-Object { $_ -match 'пройдено|Passed|Failed|не пройдено' } | ForEach-Object { Say "   $_" }
Pop-Location
Push-Location (Join-Path $app 'src\web')
if (-not (Test-Path node_modules)) { Build 'npm install' { npm.cmd install --no-audit --no-fund } | Out-Null }
Build 'web: tsc + vite build' { npm.cmd run build } | Out-Null
Pop-Location

# ---------- запуск API ----------
Say 'Запуск API…'
Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
$apiLog = Join-Path $here 'api-console.txt'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$api = Start-Process dotnet -ArgumentList @('run','--project',"`"$app\src\api`"",'--launch-profile','http') `
        -PassThru -WindowStyle Minimized -RedirectStandardOutput $apiLog -RedirectStandardError "$apiLog.err"
$up = $false
for ($i = 0; $i -lt 120; $i++) {
    Start-Sleep -Seconds 2
    $h = Call GET '/api/health' $null $null
    if ($h.Code -eq 200) { $up = $true; break }
    if ($api.HasExited) { break }
}
if (-not $up) {
    Fail 'Запуск API' 'не ответил за 4 минуты'
    Say '--- хвост консоли API ---'
    Get-Content $apiLog, "$apiLog.err" -Tail 40 -ErrorAction SilentlyContinue | ForEach-Object { Say $_ }
    if (-not $Unattended) { Read-Host 'Enter — закрыть' }; exit 1
}
Pass 'Запуск API' "$base"
Say ("health: " + $h.Raw)
$sw = Call GET '/swagger/v1/swagger.json' $null $null
Check 'Swagger доступен' $sw @(200) | Out-Null

# ---------- аутентификация ----------
$tokens = @{}
foreach ($u in @(@('admin','Admin123!'), @('manager','Manager123!'), @('master','Master123!'))) {
    $r = Call POST '/api/auth/login' $null @{ username = $u[0]; password = $u[1] }
    if (Check "Вход $($u[0])" $r @(200)) { $tokens[$u[0]] = $r.Json.data.token }
}
$bad = Call POST '/api/auth/login' $null @{ username = 'admin'; password = 'wrong' }
Check 'Вход с неверным паролем отклонён' $bad @(400,401) | Out-Null
$anon = Call GET '/api/work-orders' $null $null
Check 'Без токена — 401' $anon @(401) | Out-Null
$me = Call GET '/api/auth/me' $tokens['master'] $null
Check 'Профиль мастера (/me)' $me @(200) | Out-Null

# ---------- справочники ----------
foreach ($p in '/api/equipment','/api/crews','/api/materials','/api/maintenance-types','/api/maintenance-schedule','/api/work-orders','/api/materials/shortages','/api/dashboard/summary') {
    Check "GET $p" (Call GET $p $tokens['manager'] $null) @(200) | Out-Null
}

# ---------- права ролей ----------
$eqBody = @{ inventoryNumber = 'SMOKE-1'; name = 'Тест'; workshopId = 'aaaaaaaa-0000-0000-0000-000000000002' }
Check 'Мастер не может создать оборудование (403)' (Call POST '/api/equipment' $tokens['master'] $eqBody) @(403) | Out-Null
Check 'Мастер не видит оборудование чужого цеха (403)' (Call GET '/api/equipment/44444444-4444-4444-4444-444444444444' $tokens['master'] $null) @(403) | Out-Null
$ml = Call GET '/api/equipment?pageSize=100' $tokens['master'] $null
$otherWs = @($ml.Json.data.items | Where-Object { $_.workshopId -ne 'aaaaaaaa-0000-0000-0000-000000000001' }).Count
if ($ml.Code -eq 200 -and $otherWs -eq 0) { Pass 'Мастер получает только оборудование своего цеха' "$(@($ml.Json.data.items).Count) ед." } else { Fail 'Мастер получает только своё оборудование' "чужих: $otherWs" }
$byEmail = Call POST '/api/auth/login' $null @{ username = 'sokolov@toirprof.ru'; password = 'Master123!' }
Check 'Вход по email' $byEmail @(200) | Out-Null
$badReg = Call POST '/api/auth/register' $null @{ username = 'newuser'; password = 'Password1!'; fullName = 'Новый Пользователь'; email = '' }
Check 'Регистрация с пустым email отклонена (400)' $badReg @(400) | Out-Null
$badType = Call POST '/api/work-orders' $tokens['master'] @{ equipmentId = 'abc'; title = 'X'; plannedStart = '31.12.2026' }
if ($badType.Code -eq 400 -and $badType.Json.success -eq $false -and $badType.Json.message) { Pass 'Неверные типы → 400 с указанием поля' $badType.Json.message } else { Fail 'Неверные типы → 400' "HTTP $($badType.Code): $($badType.Raw)" }
$badPrio = Call POST '/api/work-orders' $tokens['master'] @{ equipmentId = '44444444-4444-4444-4444-444444444445'; title = 'X'; description = 'Y'; priority = 'Срочный' }
Check 'Приоритет вне списка → 400' $badPrio @(400) | Out-Null
Check 'Мастер не может назначить бригаду (403)' (Call POST '/api/work-orders/00000000-0000-0000-0000-000000000000/crew' $tokens['master'] @{ crewId = 'cccccccc-0000-0000-0000-000000000001' }) @(403) | Out-Null

# ---------- жизненный цикл заявки ----------
$equipment = '44444444-4444-4444-4444-444444444445'   # ТС-11, токарный участок — цех мастера
$crew      = 'cccccccc-0000-0000-0000-000000000003'
$material  = 'eeeeeeee-0000-0000-0000-000000000001'

$eqBefore = Call GET "/api/equipment/$equipment" $tokens['manager'] $null
$stockBefore = (Call GET "/api/materials/$material" $tokens['manager'] $null).Json.data.stock

$create = Call POST '/api/work-orders' $tokens['master'] @{
    equipmentId = $equipment; title = 'Смоук: течь масла в гидросистеме'
    description = 'Обнаружена течь под гидроцилиндром, нужна замена уплотнений'
    priority = 'Высокий'; plannedStart = (Get-Date).ToString('yyyy-MM-dd'); plannedEnd = (Get-Date).AddDays(1).ToString('yyyy-MM-dd') }
if (Check 'Мастер создаёт заявку' $create @(200,201)) {
    $wo = $create.Json.data
    Say "   номер: $($wo.number), статус: $($wo.status)"
    if ($wo.number -match '^Z-\d{4}-\d{4}$') { Pass 'Номер по шаблону Z-ГГГГ-NNNN' $wo.number } else { Fail 'Номер по шаблону' $wo.number }
    $id = $wo.id

    Check 'Пустая заявка отклонена валидацией' (Call POST '/api/work-orders' $tokens['master'] @{ equipmentId = $equipment; title = '' }) @(400) | Out-Null

    $as = Call POST "/api/work-orders/$id/crew" $tokens['manager'] @{ crewId = $crew }
    if (Check 'Руководитель назначает бригаду' $as @(200)) { Say "   статус: $($as.Json.data.status)" }

    $start = Get-Date
    $st = Call POST "/api/work-orders/$id/start" $tokens['master'] @{ actualStart = $start.ToString('o') }
    if (Check 'Мастер начинает работу' $st @(200)) { Say "   статус: $($st.Json.data.status)" }
    $eqMid = Call GET "/api/equipment/$equipment" $tokens['manager'] $null
    Say "   статус оборудования во время работ: $($eqMid.Json.data.status)"
    if ($eqMid.Json.data.status -eq 'В ремонте') { Pass 'Триггер: оборудование «В ремонте»' '' } else { Fail 'Триггер: оборудование «В ремонте»' $eqMid.Json.data.status }

    $mo = Call POST "/api/work-orders/$id/materials" $tokens['master'] @{ materialId = $material; quantity = 2 }
    Check 'Списание материала (2 шт)' $mo @(200,201) | Out-Null
    $tooMuch = Call POST "/api/work-orders/$id/materials" $tokens['master'] @{ materialId = $material; quantity = 100000 }
    Check 'Списание сверх остатка отклонено' $tooMuch @(400,409,422) | Out-Null
    $stockAfter = (Call GET "/api/materials/$material" $tokens['manager'] $null).Json.data.stock
    if ([decimal]$stockBefore - [decimal]$stockAfter -eq 2) { Pass 'Остаток уменьшился на 2' "$stockBefore → $stockAfter" } else { Fail 'Остаток уменьшился на 2' "$stockBefore → $stockAfter" }
    $cost = Call GET "/api/work-orders/$id/cost" $tokens['master'] $null
    if ((Check 'Стоимость материалов по заявке' $cost @(200)) -and [decimal]$cost.Json.data.totalCost -eq 1700) { Pass 'Стоимость = 2 × 850 = 1700' '' } elseif ($cost.Code -eq 200) { Fail 'Стоимость = 1700' $cost.Json.data.totalCost }

    $badEnd = Call POST "/api/work-orders/$id/complete" $tokens['master'] @{ actualEnd = $start.AddHours(-1).ToString('o'); failureReason = 'x' }
    Check 'Окончание раньше начала отклонено' $badEnd @(400,409,422) | Out-Null
    $cp = Call POST "/api/work-orders/$id/complete" $tokens['master'] @{ actualEnd = $start.AddHours(3).ToString('o'); failureReason = 'Износ уплотнений'; comment = 'Уплотнения заменены'; materials = @(@{ materialId = $material; quantity = 1 }) }
    $stockFinal = (Call GET "/api/materials/$material" $tokens['manager'] $null).Json.data.stock
    if ([decimal]$stockAfter - [decimal]$stockFinal -eq 1) { Pass 'Завершение списало материалы из отчёта' "$stockAfter → $stockFinal" } else { Fail 'Завершение списало материалы' "$stockAfter → $stockFinal" }
    if (Check 'Мастер завершает заявку' $cp @(200)) { Say "   статус: $($cp.Json.data.status), простой: $($cp.Json.data.downtimeHours) ч" }
    if ($cp.Json.data.status -eq 'Выполнена') { Pass 'После завершения статус «Выполнена»' '' } else { Fail 'После завершения статус «Выполнена»' $cp.Json.data.status }
    $cl = Call POST "/api/work-orders/$id/close" $tokens['master'] @{}
    if ((Check 'Мастер закрывает заявку (POST /close)' $cl @(200)) -and $cl.Json.data.status -eq 'Закрыта') { Pass 'Статус «Закрыта», простой зафиксирован' "$($cl.Json.data.downtimeHours) ч" } elseif ($cl.Code -eq 200) { Fail 'Статус «Закрыта»' $cl.Json.data.status }
    Check 'Закрытая заявка не редактируется (409)' (Call POST "/api/work-orders/$id/materials" $tokens['manager'] @{ materialId = $material; quantity = 1 }) @(409) | Out-Null
    $eqAfter = Call GET "/api/equipment/$equipment" $tokens['manager'] $null
    Say "   статус оборудования после работ: $($eqAfter.Json.data.status)"
    if ($eqAfter.Json.data.status -eq 'В работе') { Pass 'Триггер: оборудование снова «В работе»' '' } else { Fail 'Триггер: оборудование снова «В работе»' $eqAfter.Json.data.status }

    $byNum = Call GET "/api/work-orders/by-number/$($wo.number)" $tokens['manager'] $null
    Check 'Поиск заявки по номеру' $byNum @(200) | Out-Null
}

# ---------- график ТО → плановая заявка ----------
$sched = Call GET '/api/maintenance-schedule?status=%D0%9F%D1%80%D0%BE%D1%81%D1%80%D0%BE%D1%87%D0%B5%D0%BD%D0%BE' $tokens['manager'] $null
$entry = @($sched.Json.data | Where-Object { $_.inventoryNumber -notin @('КП-9', 'КР-14') })[0]
if ($entry) {
    $co = Call POST "/api/schedule/$($entry.id)/create-order" $tokens['manager'] @{}
    if ((Check 'Заявка из графика ТО (POST /schedule/{id}/create-order)' $co @(201)) -and $co.Json.data.orderType -eq 'Плановая') { Pass 'Тип заявки «Плановая», вид ТО перенесён' "$($co.Json.data.number) · $($co.Json.data.maintenanceTypeName)" } elseif ($co.Code -eq 201) { Fail 'Тип «Плановая»' $co.Json.data.orderType }
    $after = Call GET "/api/maintenance-schedule/$($entry.id)" $tokens['manager'] $null
    if ($after.Json.data.status -eq 'Назначено') { Pass 'Запись графика получила статус «Назначено»' '' } else { Fail 'Статус записи графика' $after.Json.data.status }
    Check 'Повторное создание заявки по записи → 409' (Call POST "/api/schedule/$($entry.id)/create-order" $tokens['manager'] @{}) @(409) | Out-Null
} else { Fail 'Есть просроченная запись графика для теста' '' }

# ---------- новые эндпоинты ----------
foreach ($p in '/api/analytics/dashboard-summary','/api/analytics/equipment-status','/api/analytics/orders-by-workshop',
               '/api/analytics/orders-by-type','/api/analytics/orders-by-priority','/api/analytics/orders-by-status',
               '/api/analytics/downtime-dynamics','/api/analytics/top-downtime','/api/analytics/materials-top?from=2025-01-01',
               '/api/analytics/plan-vs-fact','/api/analytics/violations','/api/analytics/upcoming-maintenance',
               '/api/analytics/workshop-report?workshopId=aaaaaaaa-0000-0000-0000-000000000001&from=2025-01-01',
               '/api/workshops','/api/users','/api/users/roles','/api/notifications','/api/search?q=%D0%A2%D0%A1') {
    Check "GET $p" (Call GET $p $tokens['admin'] $null) @(200) | Out-Null
}
$kpi = (Call GET '/api/analytics/dashboard-summary' $tokens['manager'] $null).Json.data
Say "   KPI: оборудование $($kpi.equipmentTotal), в ремонте $($kpi.equipmentInRepair), активных заявок $($kpi.activeOrders), просрочено $($kpi.overdueOrders), простой $($kpi.downtimeHours) ч"
$wr = (Call GET '/api/analytics/workshop-report?workshopId=aaaaaaaa-0000-0000-0000-000000000001&from=2025-01-01' $tokens['manager'] $null).Json.data
if (@($wr | Where-Object { $_.section -like '5.*' }).Count -gt 0) { Pass 'Отчёт по цеху содержит топ-5 материалов' "$(@($wr).Count) строк" } else { Fail 'Отчёт по цеху: топ-5 материалов' '' }
$hits = (Call GET '/api/search?q=%D0%A2%D0%A1' $tokens['admin'] $null).Json.data
if (@($hits).Count -gt 0) { Pass 'Глобальный поиск находит «ТС»' "$(@($hits).Count) совпадений" } else { Fail 'Глобальный поиск' 'пусто' }
Check 'Пользователи — только администратору (403 мастеру)' (Call GET '/api/users' $tokens['master'] $null) @(403) | Out-Null

# ---------- аналитика и отчёты ----------
$from = (Get-Date).AddDays(-365).ToString('yyyy-MM-dd'); $to = (Get-Date).ToString('yyyy-MM-dd')
$an = Call GET "/api/analytics?from=$from&to=$to" $tokens['manager'] $null
if (Check 'Аналитика за год' $an @(200)) { Say "   заявок: $($an.Json.data.orders.total), строк простоя: $(@($an.Json.data.downtime).Count)" }
foreach ($k in 'downtime','crews','materials','plan') {
    $rl = Call POST '/api/reports' $tokens['manager'] @{ kind = $k; from = $from; to = $to }
    if (Check "Ссылка на PDF-отчёт ($k)" $rl @(200,201)) {
        $url = $rl.Json.data.url; if ($url -notmatch '^http') { $url = "$base$url" }
        try {
            $f = Join-Path $here "smoke-report-$k.pdf"
            Invoke-WebRequest -Uri $url -OutFile $f -UseBasicParsing -TimeoutSec 60
            $head = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($f)[0..3])
            if ($head -eq '%PDF') { Pass "PDF скачан ($k)" "$((Get-Item $f).Length) байт, TTL $($rl.Json.data.ttlSeconds) с" } else { Fail "PDF скачан ($k)" "не PDF: $head" }
        } catch { Fail "PDF скачан ($k)" $_.Exception.Message }
    }
}
$sum = Call GET '/api/dashboard/summary' $tokens['manager'] $null
Say ("   дашборд: " + (($sum.Raw -replace '\s+',' ').Substring(0, [Math]::Min(400, $sum.Raw.Length))))

# ---------- экраны desktop-клиента без монитора (tools/ui-check) ----------
$uiProj = Join-Path $app 'tools\ui-check'
if (Test-Path $uiProj) {
    $uiDir = Join-Path $here 'ui-shots'
    $uiOut = & dotnet run --project $uiProj -- $uiDir 2>&1 | ForEach-Object { "$_" }
    foreach ($l in $uiOut) {
        if ($l -match '^\[UI-OK\]\s*(.*)$') { Pass 'Desktop UI:' $Matches[1] }
        elseif ($l -match '^\[UI-FAIL\]\s*(.*)$') { Fail 'Desktop UI:' $Matches[1] }
        elseif ($l -match '^    ' -or $l -match ' error ') { Say "   $l" }
    }
    if (-not ($uiOut -match 'UI-SUMMARY')) { Fail 'Desktop UI' 'ui-check не запустился'; $uiOut | Select-Object -Last 25 | ForEach-Object { Say "   $_" } }
}

# ---------- мобильный клиент бригады без телефона (tools/mobile-check) ----------
$mobProj = Join-Path $app 'tools\mobile-check'
if (Test-Path $mobProj) {
    $mobDir = Join-Path $here 'mobile-shots'
    $mobOut = & dotnet run --project $mobProj -- $mobDir 2>&1 | ForEach-Object { "$_" }
    foreach ($l in $mobOut) {
        if ($l -match '^\[MOB-OK\]\s*(.*)$') { Pass 'Mobile:' $Matches[1] }
        elseif ($l -match '^\[MOB-FAIL\]\s*(.*)$') { Fail 'Mobile:' $Matches[1] }
        elseif ($l -match '^    ' -or $l -match ' error ') { Say "   $l" }
    }
    if (-not ($mobOut -match 'MOB-SUMMARY')) { Fail 'Mobile' 'mobile-check не запустился'; $mobOut | Select-Object -Last 25 | ForEach-Object { Say "   $_" } }
}

Say "`n=== ИТОГ: успешно $script:ok, ошибок $script:fail ==="
Say 'API оставлен работать (окно свёрнуто) — для web и desktop.'

# ---------- клиенты для ручной проверки ----------
if (-not $Unattended) {
    Start-Process cmd.exe -ArgumentList '/c', 'npm.cmd run dev' -WorkingDirectory (Join-Path $app 'src\web') -WindowStyle Minimized
    Start-Process dotnet -ArgumentList @('run', '--project', "`"$app\src\desktop`"") -WindowStyle Hidden
    Say 'Запущены: web — http://localhost:5173, desktop-клиент — отдельным окном.'
}
