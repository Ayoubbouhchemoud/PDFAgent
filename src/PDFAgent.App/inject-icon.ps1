param([string]$ExePath, [string]$IcoPath)

if (-not ([Management.Automation.PSTypeName]'Win32Resource').Type) {
    $src = 'using System; using System.Runtime.InteropServices;' +
           'public static class Win32Resource {' +
           '[DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Auto)]' +
           'public static extern IntPtr BeginUpdateResource(string f,bool d);' +
           '[DllImport("kernel32.dll",SetLastError=true)]' +
           'public static extern bool UpdateResource(IntPtr h,IntPtr t,IntPtr n,ushort l,byte[] b,uint s);' +
           '[DllImport("kernel32.dll",SetLastError=true)]' +
           'public static extern bool EndUpdateResource(IntPtr h,bool d);' +
           'public static IntPtr MIR(int id){return new IntPtr(id);}}'
    Add-Type -TypeDefinition $src
}

$RT_ICON       = 3
$RT_GROUP_ICON = 14
$LANG_NEUTRAL  = 0

$icoBytes = [System.IO.File]::ReadAllBytes($IcoPath)
$count    = [BitConverter]::ToUInt16($icoBytes, 4)

$images = @()
for ($i = 0; $i -lt $count; $i++) {
    $base  = 6 + $i * 16
    $size  = [BitConverter]::ToUInt32($icoBytes, $base + 8)
    $off   = [BitConverter]::ToUInt32($icoBytes, $base + 12)
    $images += @{
        Width      = $icoBytes[$base]
        Height     = $icoBytes[$base + 1]
        ColorCount = $icoBytes[$base + 2]
        Reserved   = $icoBytes[$base + 3]
        Planes     = [BitConverter]::ToUInt16($icoBytes, $base + 4)
        BitCount   = [BitConverter]::ToUInt16($icoBytes, $base + 6)
        Data       = $icoBytes[$off..($off + $size - 1)]
    }
}

$groupData = New-Object byte[] (6 + $count * 14)
[BitConverter]::GetBytes([uint16]0).CopyTo($groupData, 0)
[BitConverter]::GetBytes([uint16]1).CopyTo($groupData, 2)
[BitConverter]::GetBytes([uint16]$count).CopyTo($groupData, 4)

for ($i = 0; $i -lt $count; $i++) {
    $img = $images[$i]
    $o   = 6 + $i * 14
    $groupData[$o]   = $img.Width
    $groupData[$o+1] = $img.Height
    $groupData[$o+2] = $img.ColorCount
    $groupData[$o+3] = $img.Reserved
    [BitConverter]::GetBytes([uint16]$img.Planes).CopyTo($groupData, $o+4)
    [BitConverter]::GetBytes([uint16]$img.BitCount).CopyTo($groupData, $o+6)
    [BitConverter]::GetBytes([uint32]$img.Data.Length).CopyTo($groupData, $o+8)
    [BitConverter]::GetBytes([uint16]($i+1)).CopyTo($groupData, $o+12)
}

$handle = [Win32Resource]::BeginUpdateResource($ExePath, $false)
if ($handle -eq [IntPtr]::Zero) { Write-Error "BeginUpdateResource failed"; exit 1 }

# Write individual icon images (RT_ICON, ordinals 1..n)
for ($i = 0; $i -lt $count; $i++) {
    $d = $images[$i].Data
    [Win32Resource]::UpdateResource($handle, [Win32Resource]::MIR($RT_ICON), [Win32Resource]::MIR($i+1), $LANG_NEUTRAL, $d, [uint32]$d.Length) | Out-Null
}

# Replace ALL known icon group ordinals (1 = injected, 32512 = original apphost IDI_APPLICATION)
foreach ($groupId in @(1, 32512)) {
    [Win32Resource]::UpdateResource($handle, [Win32Resource]::MIR($RT_GROUP_ICON), [Win32Resource]::MIR($groupId), $LANG_NEUTRAL, $groupData, [uint32]$groupData.Length) | Out-Null
}

[Win32Resource]::EndUpdateResource($handle, $false) | Out-Null
Write-Host "Icon injected into $ExePath"
