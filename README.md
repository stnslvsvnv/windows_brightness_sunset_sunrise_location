# windows_brightness_sunset_sunrise_location
Switches the brightness of the laptop display to 80% after sunrise and lowers it to 30% after sunset. 

Using the installer:
1. Quick installation (with administrator rights):
powershell
powershell -ExecutionPolicy Bypass -File “BrightnessInstaller.ps1”
2. Silent installation (without dialog boxes):
powershell
powershell -ExecutionPolicy Bypass -File “BrightnessInstaller.ps1” -Silent
3. Installation in a specific folder:
powershell
powershell -ExecutionPolicy Bypass -File “BrightnessInstaller.ps1” -InstallPath “C:\MyScripts”
4. Uninstallation:
powershell
powershell -ExecutionPolicy Bypass -File “BrightnessInstaller.ps1” -Uninstall
What the installer does:
Requests administrator rights (restarts automatically)

Selects the installation folder (offers options or accepts a user-specified path)

Creates the main script with the following functions:

Automatic location detection (Windows API → IP geolocation → backup coordinates)

Obtaining sunrise/sunset times via API

Brightness adjustment via WMI

Logging to a file

Configures Windows geolocation services

Creates a task in Task Scheduler with triggers:

At system startup

When the user logs in

Daily at 8:00 and 20:00

Every 2 hours from 6:00 to 22:00

Tests the installation and displays a report
