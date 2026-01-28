# ======================================================
# Brightness Auto-Installer v1.0
# Автоматическая установка регулировки яркости по восходу/закату
# ======================================================

#Requires -RunAsAdministrator

param(
    [switch]$Uninstall,
    [switch]$Silent,
    [string]$InstallPath
)

# Настройки по умолчанию
$DefaultInstallPath = "$env:ProgramData\BrightnessController"
$ScriptName = "BrightnessAutoAdjust.ps1"
$TaskName = "Brightness Auto-Adjust"
$ServiceName = "lfsvc"  # Служба геолокации

# Цвета для вывода
$ErrorColor = "Red"
$SuccessColor = "Green"
$InfoColor = "Yellow"
$NormalColor = "White"

function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = $NormalColor
    )
    
    if ($Host.UI.RawUI) {
        $oldColor = $Host.UI.RawUI.ForegroundColor
        $Host.UI.RawUI.ForegroundColor = $Color
        Write-Output $Message
        $Host.UI.RawUI.ForegroundColor = $oldColor
    }
    else {
        Write-Output $Message
    }
}

function Show-Header {
    Clear-Host
    Write-ColorOutput "╔══════════════════════════════════════════════════╗" -Color Cyan
    Write-ColorOutput "║    Brightness Auto-Adjust Installer v1.0         ║" -Color Cyan
    Write-ColorOutput "║    Автоматическая регулировка яркости            ║" -Color Cyan
    Write-ColorOutput "║    по времени восхода и заката                   ║" -Color Cyan
    Write-ColorOutput "╚══════════════════════════════════════════════════╝" -Color Cyan
    Write-Output ""
}

function Get-InstallPath {
    if ($InstallPath) {
        return $InstallPath
    }
    
    Show-Header
    Write-ColorOutput "Выберите папку для установки:" -Color $InfoColor
    Write-ColorOutput "1. $DefaultInstallPath (рекомендуется, для всех пользователей)" -Color $NormalColor
    Write-ColorOutput "2. $env:APPDATA\BrightnessController (только для текущего пользователя)" -Color $NormalColor
    Write-ColorOutput "3. Указать другой путь" -Color $NormalColor
    Write-Output ""
    
    $choice = Read-Host "Введите номер выбора (1-3)"
    
    switch ($choice) {
        "1" { return $DefaultInstallPath }
        "2" { return "$env:APPDATA\BrightnessController" }
        "3" { 
            $customPath = Read-Host "Введите полный путь для установки"
            if ([string]::IsNullOrWhiteSpace($customPath)) {
                Write-ColorOutput "Используется путь по умолчанию" -Color $WarningColor
                return $DefaultInstallPath
            }
            return $customPath
        }
        default { return $DefaultInstallPath }
    }
}

function Create-BrightnessScript {
    param(
        [string]$Path
    )
    
    $scriptContent = @'
# ======================================================
# Brightness Auto-Adjust Script
# Автоматическая регулировка яркости по восходу/закату
# ======================================================

# Конфигурация
$DAY_BRIGHTNESS = 80    # Яркость днем (%)
$NIGHT_BRIGHTNESS = 33  # Яркость ночью (%)
$LOG_FILE = "$env:TEMP\BrightnessAdjustment.log"

# Функция получения геопозиции
function Get-Geolocation {
    # Метод 1: Windows Location API
    try {
        $geoWatcher = New-Object System.Device.Location.GeoCoordinateWatcher
        $geoWatcher.Start()
        
        # Ждем получения позиции до 5 секунд
        $timeout = 5000
        $startTime = Get-Date
        while (-not $geoWatcher.Position.Location.IsUnknown -and 
               ((Get-Date) - $startTime).TotalMilliseconds -lt $timeout) {
            Start-Sleep -Milliseconds 100
        }
        
        if (-not $geoWatcher.Position.Location.IsUnknown) {
            return @{
                Latitude = $geoWatcher.Position.Location.Latitude
                Longitude = $geoWatcher.Position.Location.Longitude
                Source = "Windows Location API"
            }
        }
    }
    catch {
        Write-Verbose "Windows Location API недоступен"
    }
    
    # Метод 2: IP-геолокация
    try {
        $response = Invoke-RestMethod -Uri "http://ip-api.com/json/" -TimeoutSec 3
        if ($response.status -eq "success") {
            return @{
                Latitude = [double]$response.lat
                Longitude = [double]$response.lon
                City = $response.city
                Country = $response.country
                Source = "IP Geolocation"
            }
        }
    }
    catch {
        Write-Verbose "IP-геолокация недоступна"
    }
    
    # Метод 3: Резервные координаты (Центральная Европа)
    Write-Warning "Не удалось определить местоположение. Использую координаты по умолчанию (Берлин)."
    return @{
        Latitude = 52.5200
        Longitude = 13.4050
        City = "Berlin"
        Country = "Germany"
        Source = "Default"
    }
}

# Функция получения времени восхода/заката
function Get-SunTimes {
    param(
        [double]$Latitude,
        [double]$Longitude
    )
    
    $date = Get-Date
    $dateStr = $date.ToString("yyyy-MM-dd")
    
    try {
        $url = "https://api.sunrise-sunset.org/json?lat=$Latitude&lng=$Longitude&date=$dateStr&formatted=0"
        $response = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 5
        
        if ($response.status -eq "OK") {
            $sunrise = [datetime]::Parse($response.results.sunrise).ToLocalTime()
            $sunset = [datetime]::Parse($response.results.sunset).ToLocalTime()
            
            return @{
                Sunrise = $sunrise
                Sunset = $sunset
                Success = $true
            }
        }
    }
    catch {
        Write-Verbose "API sunrise-sunset недоступно: $_"
    }
    
    # Резервный расчет по фиксированному времени
    return @{
        Sunrise = $date.Date.AddHours(7)  # 7:00
        Sunset = $date.Date.AddHours(19)  # 19:00
        Success = $false
    }
}

# Основная функция
function Set-BrightnessByTime {
    # Получаем геопозицию
    $location = Get-Geolocation
    
    # Логируем местоположение
    $locationInfo = "Местоположение: $($location.City), $($location.Country) [$($location.Latitude), $($location.Longitude)]"
    Write-Output $locationInfo
    
    # Получаем время восхода/заката
    $sunTimes = Get-SunTimes -Latitude $location.Latitude -Longitude $location.Longitude
    
    # Определяем текущий период
    $currentTime = Get-Date
    if ($currentTime -ge $sunTimes.Sunrise -and $currentTime -lt $sunTimes.Sunset) {
        $brightness = $DAY_BRIGHTNESS
        $period = "день"
        $nextChange = $sunTimes.Sunset
    }
    else {
        $brightness = $NIGHT_BRIGHTNESS
        $period = "ночь"
        $nextChange = $sunTimes.Sunrise.AddDays(1)
    }
    
    # Устанавливаем яркость
    try {
        $monitor = Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightnessMethods -ErrorAction Stop
        $monitor.WmiSetBrightness(1, $brightness)
        
        # Формируем сообщение
        $message = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - Яркость: $brightness% ($period). Восход: $($sunTimes.Sunrise.ToString('HH:mm')), Закат: $($sunTimes.Sunset.ToString('HH:mm'))"
        Write-Output $message
        
        # Логируем в файл
        Add-Content -Path $LOG_FILE -Value "$message | $locationInfo"
        
        return $true
    }
    catch {
        $errorMsg = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - Ошибка: $_"
        Write-Error $errorMsg
        Add-Content -Path $LOG_FILE -Value $errorMsg
        return $false
    }
}

# Запуск
try {
    $result = Set-BrightnessByTime
    if (-not $result) {
        exit 1
    }
}
catch {
    Write-Error "Критическая ошибка: $_"
    exit 1
}
'@
    
    try {
        New-Item -Path $Path -ItemType Directory -Force | Out-Null
        $scriptPath = Join-Path $Path $ScriptName
        $scriptContent | Out-File -FilePath $scriptPath -Encoding UTF8
        
        # Добавляем исполнение от имени администратора (опционально)
        $shortcutScript = @"
Start-Process powershell -ArgumentList "-ExecutionPolicy Bypass -File `"$scriptPath`"" -Verb RunAs
"@
        $shortcutPath = Join-Path $Path "RunAsAdmin.ps1"
        $shortcutScript | Out-File -FilePath $shortcutPath -Encoding UTF8
        
        return $scriptPath
    }
    catch {
        Write-ColorOutput "Ошибка при создании скрипта: $_" -Color $ErrorColor
        return $null
    }
}

function Enable-LocationServices {
    Write-ColorOutput "`nНастройка служб геолокации..." -Color $InfoColor
    
    # Включаем службу геолокации
    try {
        # Проверяем, существует ли служба
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        
        if ($service) {
            # Устанавливаем тип запуска "Автоматически"
            Set-Service -Name $ServiceName -StartupType Automatic -ErrorAction SilentlyContinue
            
            # Запускаем службу
            Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
            
            # Разрешаем доступ к местоположению в реестре
            $locationKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location"
            if (Test-Path $locationKey) {
                Set-ItemProperty -Path $locationKey -Name "Value" -Value "Allow" -Type String -Force -ErrorAction SilentlyContinue
            }
            
            # Для текущего пользователя
            $userLocationKey = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}"
            if (Test-Path $userLocationKey) {
                Set-ItemProperty -Path $userLocationKey -Name "Value" -Value "Allow" -Type String -Force -ErrorAction SilentlyContinue
            }
            
            Write-ColorOutput "✓ Службы геолокации настроены" -Color $SuccessColor
            return $true
        }
        else {
            Write-ColorOutput "⚠ Служба геолокации не найдена в системе" -Color $InfoColor
            return $false
        }
    }
    catch {
        Write-ColorOutput "⚠ Не удалось настроить службы геолокации: $_" -Color $InfoColor
        Write-ColorOutput "   Вы можете включить их вручную в Параметры → Конфиденциальность → Расположение" -Color $InfoColor
        return $false
    }
}

function Create-ScheduledTask {
    param(
        [string]$ScriptPath,
        [string]$TaskPath = "\"
    )
    
    Write-ColorOutput "`nСоздание задачи в Планировщике заданий..." -Color $InfoColor
    
    try {
        # Удаляем существующую задачу (если есть)
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
        
        # Создаем действие
        $action = New-ScheduledTaskAction -Execute "powershell.exe" `
            -Argument "-ExecutionPolicy Bypass -WindowStyle Hidden -File `"$ScriptPath`""
        
        # Создаем триггеры
        $triggers = @()
        
        # Триггер 1: При запуске системы
        $triggers += New-ScheduledTaskTrigger -AtStartup
        
        # Триггер 2: При разблокировке рабочей станции
        $triggers += New-ScheduledTaskTrigger -AtLogOn
        
        # Триггер 3: Ежедневно в 8:00 и 20:00
        $triggers += New-ScheduledTaskTrigger -Daily -At "8:00AM"
        $triggers += New-ScheduledTaskTrigger -Daily -At "8:00PM"
        
        # Триггер 4: Каждые 2 часа с 6:00 до 22:00
        $startTime = (Get-Date).Date.AddHours(6)
        $endTime = (Get-Date).Date.AddHours(22)
        $triggers += New-ScheduledTaskTrigger -Once -At $startTime `
            -RepetitionInterval (New-TimeSpan -Hours 2) `
            -RepetitionDuration (New-TimeSpan -Hours 16)
        
        # Настройки задачи
        $settings = New-ScheduledTaskSettingsSet `
            -AllowStartIfOnBatteries `
            -DontStopIfGoingOnBatteries `
            -StartWhenAvailable `
            -RestartInterval (New-TimeSpan -Minutes 1) `
            -RestartCount 3
        
        # Принципал (права)
        $principal = New-ScheduledTaskPrincipal `
            -UserId "SYSTEM" `
            -LogonType ServiceAccount `
            -RunLevel Highest
        
        # Регистрируем задачу
        $task = New-ScheduledTask `
            -Action $action `
            -Trigger $triggers `
            -Settings $settings `
            -Principal $principal
        
        Register-ScheduledTask `
            -TaskName $TaskName `
            -InputObject $task `
            -TaskPath $TaskPath `
            -Force | Out-Null
        
        Write-ColorOutput "✓ Задача создана: Планировщик заданий → Библиотека планировщика заданий → $TaskName" -Color $SuccessColor
        return $true
    }
    catch {
        Write-ColorOutput "✗ Ошибка при создании задачи: $_" -Color $ErrorColor
        return $false
    }
}

function Test-Installation {
    param(
        [string]$ScriptPath
    )
    
    Write-ColorOutput "`nТестирование установки..." -Color $InfoColor
    
    try {
        # Проверяем существование файла
        if (Test-Path $ScriptPath) {
            Write-ColorOutput "✓ Скрипт найден: $ScriptPath" -Color $SuccessColor
        }
        else {
            Write-ColorOutput "✗ Скрипт не найден" -Color $ErrorColor
            return $false
        }
        
        # Проверяем задачу в планировщике
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        if ($task) {
            Write-ColorOutput "✓ Задача найдена в планировщике" -Color $SuccessColor
        }
        else {
            Write-ColorOutput "✗ Задача не найдена в планировщике" -Color $ErrorColor
            return $false
        }
        
        # Тестовый запуск скрипта
        Write-ColorOutput "`nТестовый запуск скрипта..." -Color $InfoColor
        $testResult = powershell -ExecutionPolicy Bypass -Command "& {. '$ScriptPath'; exit `$LASTEXITCODE}"
        
        if ($LASTEXITCODE -eq 0) {
            Write-ColorOutput "✓ Скрипт работает корректно" -Color $SuccessColor
        }
        else {
            Write-ColorOutput "⚠ Скрипт завершился с ошибкой (код: $LASTEXITCODE)" -Color $InfoColor
        }
        
        return $true
    }
    catch {
        Write-ColorOutput "✗ Ошибка при тестировании: $_" -Color $ErrorColor
        return $false
    }
}

function Uninstall-Application {
    Show-Header
    
    Write-ColorOutput "УДАЛЕНИЕ Brightness Auto-Adjust" -Color $InfoColor
    Write-ColorOutput "=========================================" -Color $InfoColor
    
    # Удаляем задачу из планировщика
    try {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
        Write-ColorOutput "✓ Задача удалена из планировщика" -Color $SuccessColor
    }
    catch {
        Write-ColorOutput "⚠ Не удалось удалить задачу: $_" -Color $InfoColor
    }
    
    # Удаляем файлы
    $possiblePaths = @(
        $DefaultInstallPath,
        "$env:APPDATA\BrightnessController",
        "$env:ProgramFiles\BrightnessController"
    )
    
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            try {
                Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
                Write-ColorOutput "✓ Удалена папка: $path" -Color $SuccessColor
            }
            catch {
                Write-ColorOutput "⚠ Не удалось удалить папку $path" -Color $InfoColor
            }
        }
    }
    
    # Удаляем лог-файлы
    $logFiles = @(
        "$env:TEMP\BrightnessAdjustment.log",
        "$env:APPDATA\BrightnessController\log.txt"
    )
    
    foreach ($logFile in $logFiles) {
        if (Test-Path $logFile) {
            Remove-Item -Path $logFile -Force -ErrorAction SilentlyContinue
        }
    }
    
    Write-ColorOutput "`n✓ Удаление завершено!" -Color $SuccessColor
    Write-ColorOutput "  Перезагрузите компьютер для завершения удаления." -Color $InfoColor
    
    pause
    exit 0
}

function Show-InstallSummary {
    param(
        [string]$InstallPath,
        [string]$ScriptPath
    )
    
    Show-Header
    
    Write-ColorOutput "УСТАНОВКА ЗАВЕРШЕНА УСПЕШНО!" -Color $SuccessColor
    Write-ColorOutput "=========================================" -Color $SuccessColor
    Write-Output ""
    Write-ColorOutput "Установленные компоненты:" -Color $InfoColor
    Write-ColorOutput "  ✓ Скрипт регулировки яркости: $ScriptPath" -Color $NormalColor
    Write-ColorOutput "  ✓ Задача в планировщике: $TaskName" -Color $NormalColor
    Write-ColorOutput "  ✓ Лог-файл: $env:TEMP\BrightnessAdjustment.log" -Color $NormalColor
    Write-Output ""
    Write-ColorOutput "Что было сделано:" -Color $InfoColor
    Write-ColorOutput "  1. Создан скрипт автоматической регулировки яркости" -Color $NormalColor
    Write-ColorOutput "  2. Настроены службы геолокации Windows" -Color $NormalColor
    Write-ColorOutput "  3. Создана задача в Планировщике заданий Windows" -Color $NormalColor
    Write-ColorOutput "  4. Задача будет запускаться:" -Color $NormalColor
    Write-ColorOutput "     • При запуске компьютера" -Color $NormalColor
    Write-ColorOutput "     • При входе в систему" -Color $NormalColor
    Write-ColorOutput "     • Ежедневно в 8:00 и 20:00" -Color $NormalColor
    Write-ColorOutput "     • Каждые 2 часа с 6:00 до 22:00" -Color $NormalColor
    Write-Output ""
    Write-ColorOutput "Для проверки установки:" -Color $InfoColor
    Write-ColorOutput "  • Откройте Планировщик заданий (taskschd.msc)" -Color $NormalColor
    Write-ColorOutput "  • Найдите задачу '$TaskName'" -Color $NormalColor
    Write-ColorOutput "  • Запустите задачу вручную для проверки" -Color $NormalColor
    Write-Output ""
    Write-ColorOutput "Для удаления программы запустите:" -Color $InfoColor
    Write-ColorOutput "  powershell -ExecutionPolicy Bypass -File `"$ScriptPath`" -Uninstall" -Color $NormalColor
    Write-Output ""
    Write-ColorOutput "Или повторно запустите этот инсталлятор с параметром -Uninstall" -Color $NormalColor
    Write-Output ""
    
    # Предложение запустить скрипт сейчас
    $choice = Read-Host "Запустить скрипт сейчас для проверки? (Y/N)"
    if ($choice -match "^[YyДд]") {
        Write-ColorOutput "`nЗапуск скрипта..." -Color $InfoColor
        powershell -ExecutionPolicy Bypass -File $ScriptPath
    }
}

# ======================================================
# ГЛАВНЫЙ БЛОК УСТАНОВКИ
# ======================================================

# Проверка прав администратора
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-ColorOutput "Требуются права администратора!" -Color $ErrorColor
    Write-ColorOutput "Перезапуск с повышенными привилегиями..." -Color $InfoColor
    
    # Перезапуск с правами администратора
    $newProcess = New-Object System.Diagnostics.ProcessStartInfo "PowerShell"
    $newProcess.Arguments = "-ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`""
    if ($InstallPath) { $newProcess.Arguments += " -InstallPath `"$InstallPath`"" }
    if ($Uninstall) { $newProcess.Arguments += " -Uninstall" }
    if ($Silent) { $newProcess.Arguments += " -Silent" }
    
    $newProcess.Verb = "runas"
    [System.Diagnostics.Process]::Start($newProcess) | Out-Null
    exit
}

# Удаление
if ($Uninstall) {
    Uninstall-Application
}

# Установка
Show-Header
Write-ColorOutput "УСТАНОВКА Brightness Auto-Adjust" -Color $InfoColor
Write-ColorOutput "=========================================" -Color $InfoColor

# Получаем путь установки
$installPath = Get-InstallPath
Write-ColorOutput "`nПуть установки: $installPath" -Color $InfoColor

# Создаем скрипт
Write-ColorOutput "`nСоздание скрипта регулировки яркости..." -Color $InfoColor
$scriptPath = Create-BrightnessScript -Path $installPath

if (-not $scriptPath) {
    Write-ColorOutput "Ошибка: Не удалось создать скрипт" -Color $ErrorColor
    pause
    exit 1
}

Write-ColorOutput "✓ Скрипт создан: $scriptPath" -Color $SuccessColor

# Включаем службы геолокации
Enable-LocationServices

# Создаем задачу в планировщике
$taskCreated = Create-ScheduledTask -ScriptPath $scriptPath

if (-not $taskCreated) {
    Write-ColorOutput "`n⚠ Внимание: Задача не была создана в планировщике" -Color $InfoColor
    Write-ColorOutput "  Вы можете создать ее вручную:" -Color $InfoColor
    Write-ColorOutput "  1. Откройте Планировщик заданий" -Color $NormalColor
    Write-ColorOutput "  2. Создайте задачу, которая запускает:" -Color $NormalColor
    Write-ColorOutput "     powershell -ExecutionPolicy Bypass -File `"$scriptPath`"" -Color $NormalColor
}

# Тестируем установку
if (-not $Silent) {
    Test-Installation -ScriptPath $scriptPath
}

# Показываем результаты
Show-InstallSummary -InstallPath $installPath -ScriptPath $scriptPath

# Пауза для просмотра результатов (если не silent режим)
if (-not $Silent) {
    pause
}

exit 0