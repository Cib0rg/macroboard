# Stop MacroKeyboard Backend processes
# Usage: .\stop-backend.ps1

Write-Host "Searching for MacroKeyboard.Backend processes..." -ForegroundColor Yellow

# Find all MacroKeyboard.Backend processes
$processes = Get-Process | Where-Object {$_.ProcessName -like "*MacroKeyboard.Backend*"}

if ($processes.Count -eq 0) {
    Write-Host "No MacroKeyboard.Backend processes found" -ForegroundColor Green
    
    # Check if port is still in use
    Write-Host "`nChecking port 28195..." -ForegroundColor Yellow
    $portCheck = netstat -ano | findstr :28195
    
    if ($portCheck) {
        Write-Host "Port 28195 is still in use:" -ForegroundColor Red
        Write-Host $portCheck
        
        # Extract PID from netstat output
        $portCheck | ForEach-Object {
            if ($_ -match '\s+(\d+)\s*$') {
                $pid = $matches[1]
                $process = Get-Process -Id $pid -ErrorAction SilentlyContinue
                if ($process) {
                    Write-Host "`nProcess using port 28195:" -ForegroundColor Yellow
                    Write-Host "  PID: $pid"
                    Write-Host "  Name: $($process.ProcessName)"
                    Write-Host "  Path: $($process.Path)"
                    
                    $response = Read-Host "`nKill this process? (y/n)"
                    if ($response -eq 'y') {
                        Stop-Process -Id $pid -Force
                        Write-Host "Process $pid killed" -ForegroundColor Green
                    }
                }
            }
        }
    } else {
        Write-Host "Port 28195 is free" -ForegroundColor Green
    }
    
    exit 0
}

Write-Host "Found $($processes.Count) process(es):" -ForegroundColor Yellow
$processes | ForEach-Object {
    Write-Host "  PID: $($_.Id), Name: $($_.ProcessName), Path: $($_.Path)"
}

$response = Read-Host "`nStop all MacroKeyboard.Backend processes? (y/n)"

if ($response -eq 'y') {
    $processes | ForEach-Object {
        Write-Host "Stopping process $($_.Id)..." -ForegroundColor Yellow
        Stop-Process -Id $_.Id -Force
    }
    Write-Host "All processes stopped" -ForegroundColor Green
} else {
    Write-Host "Operation cancelled" -ForegroundColor Yellow
}
