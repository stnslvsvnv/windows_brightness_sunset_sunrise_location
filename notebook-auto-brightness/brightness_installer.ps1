# ======================================================
# Brightness Auto-Installer v1.0
# Automatic installation of sunrise/sunset brightness control
# ======================================================

#Requires -RunAsAdministrator

param(
    [switch]$Uninstall,
    [switch]$Silent,
    [string]$InstallPath
)

# Default settings
$DefaultInstallPath = "$env:ProgramData\BrightnessController"
$ScriptName = "BrightnessAutoAdjust.ps1"
$TaskName = "Brightness Auto-Adjust"
$ServiceName = "lfsvc"  # Geolocation service

# Output colors
$ErrorColor = "Red"
$SuccessColor = "Green"
$InfoColor = "Yellow"
$WarningColor = "Yellow"
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
    Write-ColorOutput "║    Automatic brightness control                  ║" -Color Cyan
    Write-ColorOutput "║    based on sunrise/sunset                        ║" -Color Cyan
    Write-ColorOutput "╚══════════════════════════════════════════════════╝" -Color Cyan
    Write-Output ""
}

function Get-InstallPath {
    if ($InstallPath) {
        return $InstallPath
    }
    
    Show-Header
    Write-ColorOutput "Choose installation folder:" -Color $InfoColor
    Write-ColorOutput "1. $DefaultInstallPath (recommended, for all users)" -Color $NormalColor
    Write-ColorOutput "2. $env:APPDATA\BrightnessController (current user only)" -Color $NormalColor
    Write-ColorOutput "3. Enter a different path" -Color $NormalColor
    Write-Output ""
    
    $choice = Read-Host "Enter choice number (1-3)"
    
    switch ($choice) {
        "1" { return $DefaultInstallPath }
        "2" { return "$env:APPDATA\BrightnessController" }
        "3" { 
            $customPath = Read-Host "Enter full installation path"
            if ([string]::IsNullOrWhiteSpace($customPath)) {
                Write-ColorOutput "Using the default path" -Color $WarningColor
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
# Automatic brightness control based on sunrise/sunset
# ======================================================

# Configuration
$DAY_BRIGHTNESS = 80    # Daytime brightness (%)
$NIGHT_BRIGHTNESS = 33  # Nighttime brightness (%)
$LOG_FILE = "$env:TEMP\BrightnessAdjustment.log"

# Get geolocation
function Get-Geolocation {
    # Method 1: Windows Location API
    try {
        $geoWatcher = New-Object System.Device.Location.GeoCoordinateWatcher
        $geoWatcher.Start()
        
        # Wait for a position up to 5 seconds
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
        Write-Verbose "Windows Location API not available"
    }
    
    # Method 2: IP geolocation
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
        Write-Verbose "IP geolocation not available"
    }
    
    # Method 3: Fallback coordinates (Central Europe)
    Write-Warning "Could not determine location. Using default coordinates (Berlin)."
    return @{
        Latitude = 52.5200
        Longitude = 13.4050
        City = "Berlin"
        Country = "Germany"
        Source = "Default"
    }
}

# Get sunrise/sunset times
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
        Write-Verbose "Sunrise-sunset API not available: $_"
    }
    
    # Fallback to fixed times
    return @{
        Sunrise = $date.Date.AddHours(7)  # 7:00
        Sunset = $date.Date.AddHours(19)  # 19:00
        Success = $false
    }
}

# Main function
function Set-BrightnessByTime {
    # Get geolocation
    $location = Get-Geolocation
    
    # Log location
    $locationInfo = "Location: $($location.City), $($location.Country) [$($location.Latitude), $($location.Longitude)]"
    Write-Output $locationInfo
    
    # Get sunrise/sunset
    $sunTimes = Get-SunTimes -Latitude $location.Latitude -Longitude $location.Longitude
    
    # Determine current period
    $currentTime = Get-Date
    if ($currentTime -ge $sunTimes.Sunrise -and $currentTime -lt $sunTimes.Sunset) {
        $brightness = $DAY_BRIGHTNESS
        $period = "day"
        $nextChange = $sunTimes.Sunset
    }
    else {
        $brightness = $NIGHT_BRIGHTNESS
        $period = "night"
        $nextChange = $sunTimes.Sunrise.AddDays(1)
    }
    
    # Set brightness
    try {
        $monitor = Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightnessMethods -ErrorAction Stop
        $monitor.WmiSetBrightness(1, $brightness)
        
        # Build message
        $message = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - Brightness: $brightness% ($period). Sunrise: $($sunTimes.Sunrise.ToString('HH:mm')), Sunset: $($sunTimes.Sunset.ToString('HH:mm'))"
        Write-Output $message
        
        # Log to file
        Add-Content -Path $LOG_FILE -Value "$message | $locationInfo"
        
        return $true
    }
    catch {
        $errorMsg = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - Error: $_"
        Write-Error $errorMsg
        Add-Content -Path $LOG_FILE -Value $errorMsg
        return $false
    }
}

# Run
try {
    $result = Set-BrightnessByTime
    if (-not $result) {
        exit 1
    }
}
catch {
    Write-Error "Critical error: $_"
    exit 1
}
'@
    
    try {
        New-Item -Path $Path -ItemType Directory -Force | Out-Null
        $scriptPath = Join-Path $Path $ScriptName
        $scriptContent | Out-File -FilePath $scriptPath -Encoding UTF8
        
        # Optional admin run helper
        $shortcutScript = @"
Start-Process powershell -ArgumentList "-ExecutionPolicy Bypass -File `"$scriptPath`"" -Verb RunAs
"@
        $shortcutPath = Join-Path $Path "RunAsAdmin.ps1"
        $shortcutScript | Out-File -FilePath $shortcutPath -Encoding UTF8
        
        return $scriptPath
    }
    catch {
        Write-ColorOutput "Error creating script: $_" -Color $ErrorColor
        return $null
    }
}

function Enable-LocationServices {
    Write-ColorOutput "`nConfiguring location services..." -Color $InfoColor
    
    # Enable geolocation service
    try {
        # Check if the service exists
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        
        if ($service) {
            # Set startup type to Automatic
            Set-Service -Name $ServiceName -StartupType Automatic -ErrorAction SilentlyContinue
            
            # Start the service
            Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
            
            # Allow location access in registry
            $locationKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location"
            if (Test-Path $locationKey) {
                Set-ItemProperty -Path $locationKey -Name "Value" -Value "Allow" -Type String -Force -ErrorAction SilentlyContinue
            }
            
            # For current user
            $userLocationKey = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}"
            if (Test-Path $userLocationKey) {
                Set-ItemProperty -Path $userLocationKey -Name "Value" -Value "Allow" -Type String -Force -ErrorAction SilentlyContinue
            }
            
            Write-ColorOutput "✓ Location services configured" -Color $SuccessColor
            return $true
        }
        else {
            Write-ColorOutput "⚠ Location service not found on this system" -Color $InfoColor
            return $false
        }
    }
    catch {
        Write-ColorOutput "⚠ Failed to configure location services: $_" -Color $InfoColor
        Write-ColorOutput "   You can enable them manually in Settings → Privacy → Location" -Color $InfoColor
        return $false
    }
}

function Create-ScheduledTask {
    param(
        [string]$ScriptPath,
        [string]$TaskPath = "\"
    )
    
    Write-ColorOutput "`nCreating a task in Task Scheduler..." -Color $InfoColor
    
    try {
        # Remove existing task (if any)
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
        
        # Create action
        $action = New-ScheduledTaskAction -Execute "powershell.exe" `
            -Argument "-ExecutionPolicy Bypass -WindowStyle Hidden -File `"$ScriptPath`""
        
        # Create triggers
        $triggers = @()
        
        # Trigger 1: At system startup
        $triggers += New-ScheduledTaskTrigger -AtStartup
        
        # Trigger 2: At user logon
        $triggers += New-ScheduledTaskTrigger -AtLogOn
        
        # Trigger 3: Daily at 8:00 and 20:00
        $triggers += New-ScheduledTaskTrigger -Daily -At "8:00AM"
        $triggers += New-ScheduledTaskTrigger -Daily -At "8:00PM"
        
        # Trigger 4: Every 2 hours from 6:00 to 22:00
        $startTime = (Get-Date).Date.AddHours(6)
        $endTime = (Get-Date).Date.AddHours(22)
        $triggers += New-ScheduledTaskTrigger -Once -At $startTime `
            -RepetitionInterval (New-TimeSpan -Hours 2) `
            -RepetitionDuration (New-TimeSpan -Hours 16)
        
        # Task settings
        $settings = New-ScheduledTaskSettingsSet `
            -AllowStartIfOnBatteries `
            -DontStopIfGoingOnBatteries `
            -StartWhenAvailable `
            -RestartInterval (New-TimeSpan -Minutes 1) `
            -RestartCount 3
        
        # Principal (permissions)
        $principal = New-ScheduledTaskPrincipal `
            -UserId "SYSTEM" `
            -LogonType ServiceAccount `
            -RunLevel Highest
        
        # Register task
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
        
        Write-ColorOutput "✓ Task created: Task Scheduler → Task Scheduler Library → $TaskName" -Color $SuccessColor
        return $true
    }
    catch {
        Write-ColorOutput "✗ Error creating task: $_" -Color $ErrorColor
        return $false
    }
}

function Test-Installation {
    param(
        [string]$ScriptPath
    )
    
    Write-ColorOutput "`nTesting installation..." -Color $InfoColor
    
    try {
        # Check script existence
        if (Test-Path $ScriptPath) {
            Write-ColorOutput "✓ Script found: $ScriptPath" -Color $SuccessColor
        }
        else {
            Write-ColorOutput "✗ Script not found" -Color $ErrorColor
            return $false
        }
        
        # Check task existence
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        if ($task) {
            Write-ColorOutput "✓ Task found in Task Scheduler" -Color $SuccessColor
        }
        else {
            Write-ColorOutput "✗ Task not found in Task Scheduler" -Color $ErrorColor
            return $false
        }
        
        # Test run
        Write-ColorOutput "`nTest running script..." -Color $InfoColor
        $testResult = powershell -ExecutionPolicy Bypass -Command "& {. '$ScriptPath'; exit `$LASTEXITCODE}"
        
        if ($LASTEXITCODE -eq 0) {
            Write-ColorOutput "✓ Script runs correctly" -Color $SuccessColor
        }
        else {
            Write-ColorOutput "⚠ Script finished with error (code: $LASTEXITCODE)" -Color $InfoColor
        }
        
        return $true
    }
    catch {
        Write-ColorOutput "✗ Error during testing: $_" -Color $ErrorColor
        return $false
    }
}

function Uninstall-Application {
    Show-Header
    
    Write-ColorOutput "UNINSTALL Brightness Auto-Adjust" -Color $InfoColor
    Write-ColorOutput "=========================================" -Color $InfoColor
    
    # Remove task from scheduler
    try {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
        Write-ColorOutput "✓ Task removed from Task Scheduler" -Color $SuccessColor
    }
    catch {
        Write-ColorOutput "⚠ Failed to remove task: $_" -Color $InfoColor
    }
    
    # Remove files
    $possiblePaths = @(
        $DefaultInstallPath,
        "$env:APPDATA\BrightnessController",
        "$env:ProgramFiles\BrightnessController"
    )
    
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            try {
                Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
                Write-ColorOutput "✓ Folder removed: $path" -Color $SuccessColor
            }
            catch {
                Write-ColorOutput "⚠ Failed to remove folder $path" -Color $InfoColor
            }
        }
    }
    
    # Remove log files
    $logFiles = @(
        "$env:TEMP\BrightnessAdjustment.log",
        "$env:APPDATA\BrightnessController\log.txt"
    )
    
    foreach ($logFile in $logFiles) {
        if (Test-Path $logFile) {
            Remove-Item -Path $logFile -Force -ErrorAction SilentlyContinue
        }
    }
    
    Write-ColorOutput "`n✓ Uninstall completed!" -Color $SuccessColor
    Write-ColorOutput "  Please restart your computer to complete the removal." -Color $InfoColor
    
    pause
    exit 0
}

function Show-InstallSummary {
    param(
        [string]$InstallPath,
        [string]$ScriptPath
    )
    
    Show-Header
    
    Write-ColorOutput "INSTALLATION COMPLETED SUCCESSFULLY!" -Color $SuccessColor
    Write-ColorOutput "=========================================" -Color $SuccessColor
    Write-Output ""
    Write-ColorOutput "Installed components:" -Color $InfoColor
    Write-ColorOutput "  ✓ Brightness control script: $ScriptPath" -Color $NormalColor
    Write-ColorOutput "  ✓ Scheduled task: $TaskName" -Color $NormalColor
    Write-ColorOutput "  ✓ Log file: $env:TEMP\BrightnessAdjustment.log" -Color $NormalColor
    Write-Output ""
    Write-ColorOutput "What was done:" -Color $InfoColor
    Write-ColorOutput "  1. Created the automatic brightness control script" -Color $NormalColor
    Write-ColorOutput "  2. Configured Windows location services" -Color $NormalColor
    Write-ColorOutput "  3. Created a Windows Task Scheduler task" -Color $NormalColor
    Write-ColorOutput "  4. The task will run:" -Color $NormalColor
    Write-ColorOutput "     • At system startup" -Color $NormalColor
    Write-ColorOutput "     • At user logon" -Color $NormalColor
    Write-ColorOutput "     • Daily at 8:00 and 20:00" -Color $NormalColor
    Write-ColorOutput "     • Every 2 hours from 6:00 to 22:00" -Color $NormalColor
    Write-Output ""
    Write-ColorOutput "To verify installation:" -Color $InfoColor
    Write-ColorOutput "  • Open Task Scheduler (taskschd.msc)" -Color $NormalColor
    Write-ColorOutput "  • Find the task '$TaskName'" -Color $NormalColor
    Write-ColorOutput "  • Run the task manually to test" -Color $NormalColor
    Write-Output ""
    Write-ColorOutput "To uninstall, run:" -Color $InfoColor
    Write-ColorOutput "  powershell -ExecutionPolicy Bypass -File `"$ScriptPath`" -Uninstall" -Color $NormalColor
    Write-Output ""
    Write-ColorOutput "Or run this installer again with -Uninstall" -Color $NormalColor
    Write-Output ""
    
    # Offer to run the script now
    $choice = Read-Host "Run the script now for testing? (Y/N)"
    if ($choice -match "^[Yy]") {
        Write-ColorOutput "`nRunning script..." -Color $InfoColor
        powershell -ExecutionPolicy Bypass -File $ScriptPath
    }
}

# ======================================================
# MAIN INSTALLATION BLOCK
# ======================================================

# Check admin privileges
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-ColorOutput "Administrator privileges are required!" -Color $ErrorColor
    Write-ColorOutput "Restarting with elevated privileges..." -Color $InfoColor
    
    # Restart as admin
    $newProcess = New-Object System.Diagnostics.ProcessStartInfo "PowerShell"
    $newProcess.Arguments = "-ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`""
    if ($InstallPath) { $newProcess.Arguments += " -InstallPath `"$InstallPath`"" }
    if ($Uninstall) { $newProcess.Arguments += " -Uninstall" }
    if ($Silent) { $newProcess.Arguments += " -Silent" }
    
    $newProcess.Verb = "runas"
    [System.Diagnostics.Process]::Start($newProcess) | Out-Null
    exit
}

# Uninstall
if ($Uninstall) {
    Uninstall-Application
}

# Install
Show-Header
Write-ColorOutput "INSTALLING Brightness Auto-Adjust" -Color $InfoColor
Write-ColorOutput "=========================================" -Color $InfoColor

# Get install path
$installPath = Get-InstallPath
Write-ColorOutput "`nInstallation path: $installPath" -Color $InfoColor

# Create script
Write-ColorOutput "`nCreating brightness control script..." -Color $InfoColor
$scriptPath = Create-BrightnessScript -Path $installPath

if (-not $scriptPath) {
    Write-ColorOutput "Error: Failed to create script" -Color $ErrorColor
    pause
    exit 1
}

Write-ColorOutput "✓ Script created: $scriptPath" -Color $SuccessColor

# Enable location services
Enable-LocationServices

# Create scheduled task
$taskCreated = Create-ScheduledTask -ScriptPath $scriptPath

if (-not $taskCreated) {
    Write-ColorOutput "`n⚠ Warning: Task was not created in Task Scheduler" -Color $InfoColor
    Write-ColorOutput "  You can create it manually:" -Color $InfoColor
    Write-ColorOutput "  1. Open Task Scheduler" -Color $NormalColor
    Write-ColorOutput "  2. Create a task that runs:" -Color $NormalColor
    Write-ColorOutput "     powershell -ExecutionPolicy Bypass -File `"$scriptPath`"" -Color $NormalColor
}

# Test installation
if (-not $Silent) {
    Test-Installation -ScriptPath $scriptPath
}

# Show results
Show-InstallSummary -InstallPath $installPath -ScriptPath $scriptPath

# Pause to view results (if not silent)
if (-not $Silent) {
    pause
}

exit 0
