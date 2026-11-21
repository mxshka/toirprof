<#
  Сборка APK мобильного клиента бригады (src/mobile/ToirProf.Mobile.Android).

  Права администратора не нужны: всё ставится в %LOCALAPPDATA%\toirprof-mobile
    dotnet  — .NET SDK 10 с workload android (системный dotnet не трогается);
    jdk     — Microsoft OpenJDK 17;
    android-sdk — platform-tools, build-tools, платформа API (ставит сам .NET-SDK).
  Первый запуск скачивает ~2 ГБ (нужен интернет), повторные — только собирают.

  Результат: ToirProf-mobile.apk в корне репозитория. Установка на телефон —
  скопировать файл и открыть (разрешить «установку из неизвестных источников»)
  или: adb install -r ToirProf-mobile.apk
#>
param([string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$root = Join-Path $env:LOCALAPPDATA 'toirprof-mobile'
$dn = Join-Path $root 'dotnet'; $jdk = Join-Path $root 'jdk'; $sdk = Join-Path $root 'android-sdk'
New-Item -ItemType Directory -Force $root | Out-Null

if (-not (Test-Path "$dn\dotnet.exe")) {
    Write-Host '[1/4] .NET SDK 10 (локально для пользователя)'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile "$root\dotnet-install.ps1" -UseBasicParsing
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$root\dotnet-install.ps1" -Channel 10.0 -InstallDir $dn -NoPath
}

if (-not (Test-Path "$jdk\bin\java.exe")) {
    Write-Host '[2/4] Microsoft OpenJDK 17'
    Invoke-WebRequest 'https://aka.ms/download-jdk/microsoft-jdk-17-windows-x64.zip' -OutFile "$root\jdk.zip" -UseBasicParsing
    Expand-Archive "$root\jdk.zip" "$root\jdk-tmp" -Force
    Move-Item (Get-ChildItem "$root\jdk-tmp" -Directory | Select-Object -First 1).FullName $jdk -Force
    Remove-Item "$root\jdk-tmp", "$root\jdk.zip" -Recurse -Force -ErrorAction SilentlyContinue
}

$env:DOTNET_ROOT = $dn; $env:DOTNET_MULTILEVEL_LOOKUP = '0'; $env:JAVA_HOME = $jdk
$dotnet = "$dn\dotnet.exe"
$project = Join-Path $repo 'src\mobile\ToirProf.Mobile.Android\ToirProf.Mobile.Android.csproj'

if (-not ((& $dotnet workload list) -match 'android')) {
    Write-Host '[3/4] workload android'
    & $dotnet workload install android --skip-sign-check
}

if (-not (Test-Path "$sdk\platform-tools")) {
    Write-Host '[3/4] Android SDK (лицензии принимаются автоматически)'
    & $dotnet build $project -t:InstallAndroidDependencies -f net10.0-android "-p:AndroidSdkDirectory=$sdk" "-p:JavaSdkDirectory=$jdk" -p:AcceptAndroidSdkLicenses=True -nologo -v:q
}

Write-Host "[4/4] сборка APK ($Configuration)"
& $dotnet build $project -c $Configuration "-p:AndroidSdkDirectory=$sdk" "-p:JavaSdkDirectory=$jdk" -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Сборка APK завершилась с ошибкой.' }

$apk = Get-ChildItem (Join-Path $repo "src\mobile\ToirProf.Mobile.Android\bin\$Configuration") -Recurse -Filter '*-Signed.apk' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
$target = Join-Path $repo 'ToirProf-mobile.apk'
Copy-Item $apk.FullName $target -Force
Write-Host ("Готово: {0} ({1:N1} МБ)" -f $target, ($apk.Length / 1MB))
