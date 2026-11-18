# Smoke test of the running system, through the gateway. The PowerShell twin of smoke-test.sh.
#   1. /health of the gateway, the monolith and Inventory must answer 200.
#   2. The order saga: 1 x item 1 must become Confirmed, 4 x item 2 (only 3 in stock) must become Cancelled.
# Usage: .\scripts\smoke-test.ps1            (-Gateway http://host:port to test another address)
# Exit code 0 = all good, 1 = something failed. Works in Windows PowerShell 5.1 and PowerShell 7.
param([string]$Gateway = "http://localhost:5000")

$ErrorActionPreference = "Stop"
$script:failures = 0
function Fail([string]$message) { Write-Host "FAIL: $message" -ForegroundColor Red; $script:failures++ }

# --- 1. Health -------------------------------------------------------------------------------
function Get-StatusCode([string]$path) {
    try { return (Invoke-WebRequest "$Gateway$path" -UseBasicParsing -TimeoutSec 10).StatusCode }
    catch { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode } else { return 0 } }
}

function Wait-ForHealth([string]$path) {
    for ($i = 0; $i -lt 60; $i++) {
        if ((Get-StatusCode $path) -eq 200) { Write-Host "ok   $path answers 200"; return }
        Start-Sleep -Seconds 3
    }
    Fail "$path did not answer 200 within 3 minutes"
}

Wait-ForHealth "/health"            # the gateway itself
Wait-ForHealth "/health/monolith"   # the monolith's /health (its databases), through the gateway
Wait-ForHealth "/health/inventory"  # Inventory's /health (InventoryDb), through the gateway

# --- 2. The order saga -------------------------------------------------------------------------
# One web session keeps the cookies, like a browser. The shop's forms need the anti-forgery token.
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

function Get-Page([string]$path) {
    (Invoke-WebRequest "$Gateway$path" -WebSession $session -UseBasicParsing).Content
}

function Get-Token([string]$html) {
    if ($html -match '__RequestVerificationToken" type="hidden" value="([^"]*)"') { return $Matches[1] }
    throw "No anti-forgery token found"
}

function Send-Form([string]$path, [hashtable]$fields) {
    Invoke-WebRequest "$Gateway$path" -Method Post -Body $fields -WebSession $session -UseBasicParsing | Out-Null
}

# My Orders as objects with Id and Status, newest last.
function Get-MyOrders {
    $html = Get-Page "/order/my-orders"
    $cells = [regex]::Matches($html, 'esh-orders-item col-xs-2">([^<]*)<') | ForEach-Object { $_.Groups[1].Value.Trim() }
    $orders = for ($i = 0; $i + 2 -lt $cells.Count; $i += 3) {
        [pscustomobject]@{ Id = [int]$cells[$i]; Status = $cells[$i + 2] }
    }
    $orders | Sort-Object Id
}

function Test-Order([int]$item, [int]$quantity, [string]$expected) {
    for ($n = 0; $n -lt $quantity; $n++) {
        $token = Get-Token (Get-Page "/")
        Send-Form "/Basket" @{ id = $item; name = "x"; pictureUri = "x"; price = 1; __RequestVerificationToken = $token }
    }
    $token = Get-Token (Get-Page "/Basket/Checkout")
    Send-Form "/Basket/Checkout" @{ __RequestVerificationToken = $token }

    # The answer from Inventory arrives asynchronously, so poll My Orders for up to 30 seconds.
    $order = $null
    for ($i = 0; $i -lt 15; $i++) {
        $order = Get-MyOrders | Select-Object -Last 1
        if ($order -and $order.Status -ne "Pending") { break }
        Start-Sleep -Seconds 2
    }
    if ($order -and $order.Status -eq $expected) {
        Write-Host "ok   order $($order.Id) ($quantity x item $item) is $($order.Status)"
    } else {
        Fail "order $($order.Id) ($quantity x item $item) is $($order.Status), expected $expected"
    }
}

$token = Get-Token (Get-Page "/Identity/Account/Login")
Send-Form "/Identity/Account/Login" @{
    "Input.Email" = "demouser@microsoft.com"; "Input.Password" = "Pass@word1"
    "Input.RememberMe" = "false"; __RequestVerificationToken = $token
}

Test-Order 1 1 "Confirmed"
Test-Order 2 4 "Cancelled"

if ($script:failures -gt 0) { Write-Host "$($script:failures) check(s) failed" -ForegroundColor Red; exit 1 }
Write-Host "All checks passed" -ForegroundColor Green
exit 0
