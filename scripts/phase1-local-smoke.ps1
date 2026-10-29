# ICEHOTT AI OS V2 - local Phase 1 smoke test.
# Run ONLY with disposable local PostgreSQL, local Next API BFF and loopback Mailpit.
# Does not print generated credentials or tokens.
$ErrorActionPreference = "Stop"
$web = "http://localhost:3000"
$api = "http://127.0.0.1:5035"
$mailpit = "http://127.0.0.1:8025"
$system = Invoke-RestMethod -Uri "$api/api/system"
if (-not $system.authEnabled) { throw "Auth must be enabled in disposable Development." }
$null = Invoke-WebRequest -UseBasicParsing -Uri "$web/login"
$prefix = [Guid]::NewGuid().ToString("N")
$ownerEmail = "owner-$prefix@example.invalid"
$memberEmail = "member-$prefix@example.invalid"
$password = [Guid]::NewGuid().ToString("N") + "Aa9912"
$ownerSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$memberSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession

function Post-Json($url, $body, $session) {
  Invoke-WebRequest -UseBasicParsing -Uri $url -Method Post -ContentType "application/json" -Body ($body | ConvertTo-Json -Compress -Depth 10) -WebSession $session
}
function Find-Token($recipient, $route) {
  for ($i=0; $i -lt 12; $i++) {
    $list = Invoke-RestMethod -Uri "$mailpit/api/v1/messages"
    $matchesForEmail = @($list.messages | Where-Object { ($_ | ConvertTo-Json -Compress -Depth 7) -match [regex]::Escape($recipient) })
    foreach ($message in $matchesForEmail) {
      $detail = Invoke-RestMethod -Uri "$mailpit/api/v1/message/$($message.ID)"
      $raw = $detail | ConvertTo-Json -Compress -Depth 10
      if ($raw.Contains("/$route") -and $raw -match "token=([A-Za-z0-9_-]+)") { return $Matches[1] }
    }
    Start-Sleep -Milliseconds 300
  }
  throw "Expected local verification/invitation email for $route not delivered."
}

foreach ($email in @($ownerEmail,$memberEmail)) {
  $session = if ($email -eq $ownerEmail) { $ownerSession } else { $memberSession }
  $registered = Post-Json "$web/api/session/register" @{email=$email;password=$password} $session
  if ([int]$registered.StatusCode -ne 202) { throw "Registration failed." }
  $verifyToken = Find-Token $email "verify-email"
  $verified = Post-Json "$web/api/session/verify-email" @{token=$verifyToken} $session
  if ([int]$verified.StatusCode -ne 204) { throw "Verification failed." }
  $login = Post-Json "$web/api/session/login" @{email=$email;password=$password} $session
  if ([int]$login.StatusCode -ne 200) { throw "Login failed." }
}
Write-Output "REGISTER_VERIFY_LOGIN=PASS"

$slug = "smoke-" + $prefix.Substring(0,16)
$created = Post-Json "$web/api/workspaces" @{slug=$slug;name="Smoke Team"} $ownerSession
if ([int]$created.StatusCode -ne 201) { throw "Workspace create failed." }
$workspace = $created.Content | ConvertFrom-Json
$otherList = Invoke-RestMethod -Uri "$web/api/workspaces" -WebSession $memberSession
if (@($otherList).Count -ne 0) { throw "Cross-tenant enumeration detected." }
$invite = Post-Json "$web/api/workspaces/$($workspace.id)/invitations" @{email=$memberEmail;role=2} $ownerSession
if ([int]$invite.StatusCode -ne 202) { throw "Invite failed." }
$inviteToken = Find-Token $memberEmail "accept-invite"
$accepted = Post-Json "$web/api/invitations/accept" @{token=$inviteToken} $memberSession
if ([int]$accepted.StatusCode -ne 200) { throw "Invitation acceptance failed." }
$members = Invoke-RestMethod -Uri "$web/api/workspaces/$($workspace.id)/members" -WebSession $ownerSession
if (@($members).Count -ne 2) { throw "Membership check failed." }
Write-Output "WORKSPACE_TENANT_INVITE=PASS"

$logout = Post-Json "$web/api/session/logout" @{} $memberSession
if ([int]$logout.StatusCode -ne 204) { throw "Logout failed." }
try {
  $null = Invoke-WebRequest -UseBasicParsing -Uri "$web/api/workspaces" -WebSession $memberSession
  throw "Logout did not revoke browser session."
} catch [System.Net.WebException] {
  if ([int]$_.Exception.Response.StatusCode -ne 401) { throw "Session not denied after logout." }
}
Write-Output "LOGOUT_SESSION_REVOCATION=PASS"
Write-Output "PHASE1_SMOKE=PASS"
