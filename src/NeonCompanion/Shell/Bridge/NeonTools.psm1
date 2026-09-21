# NeonCompanion's tool bridge for a PowerShell script run by execute_code.
#
# Every tool the chat offers is one call away: `Invoke-NeonTool read_file @{ path = 'notes.txt' }`
# returns the tool's text, and a tool that answers "Error: ..." throws that sentence. Each call is
# one short connection to the app over loopback, authenticated by the run's token; both come from
# the environment execute_code set. Windows PowerShell 5.1 and pwsh alike, nothing to install.

function Invoke-NeonTool {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)] [string] $Name,
        [Parameter(Position = 1)] [hashtable] $Arguments = @{}
    )
    $address = $env:NEONCOMPANION_BRIDGE_ADDRESS
    $token = $env:NEONCOMPANION_BRIDGE_TOKEN
    if (-not $address -or -not $token) {
        throw "Error: the bridge is not configured (run this script through execute_code)"
    }
    $at = $address.LastIndexOf(':')
    $target = $address.Substring(0, $at)
    $port = [int] $address.Substring($at + 1)
    $ordered = [ordered] @{ token = $token; tool = $Name; arguments = $Arguments }
    $line = (ConvertTo-Json -InputObject $ordered -Compress -Depth 10) + "`n"
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect($target, $port)
        $stream = $client.GetStream()
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($line)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush()
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
        $reply = $reader.ReadLine()
    }
    finally {
        $client.Close()
    }
    $parsed = ConvertFrom-Json -InputObject $reply
    if ($null -ne $parsed.error) {
        throw $parsed.error
    }
    if ($null -eq $parsed.result) { return "" }
    return [string] $parsed.result
}

Export-ModuleMember -Function Invoke-NeonTool
