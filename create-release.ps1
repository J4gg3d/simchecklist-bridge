$source = "D:\DEV MSFS Checklist\bridge-server\MSFSBridge\bin\Release\net8.0"
$dest = "D:\MSFSBridge\MSFSBridge-v1.6.0.zip"

# Remove old zip
if (Test-Path $dest) { Remove-Item $dest }

# Get only files (no subdirectories), exclude .pdb
$files = Get-ChildItem -Path $source -File | Where-Object { $_.Extension -ne '.pdb' }

# Create temp folder
$temp = "$env:TEMP\MSFSBridge_Release"
if (Test-Path $temp) { Remove-Item $temp -Recurse }
New-Item -ItemType Directory -Path $temp | Out-Null

# Copy files to temp
foreach ($file in $files) {
    Copy-Item $file.FullName -Destination $temp
}

# Create zip
Compress-Archive -Path "$temp\*" -DestinationPath $dest

# Cleanup
Remove-Item $temp -Recurse

Write-Host "Created: $dest"
Write-Host "Size: $((Get-Item $dest).Length / 1MB) MB"
