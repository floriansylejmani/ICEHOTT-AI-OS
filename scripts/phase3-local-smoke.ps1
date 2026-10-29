# Phase 3 offline E2E: disposable PostgreSQL, local Mailpit, .NET API,
# Next BFF, HMAC-protected FastAPI runtime, no external provider or billing.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\phase2-local-smoke.ps1"
$wsId = $workspace.id
$create = Post-Json "$web/api/workspaces/$wsId/ai/conversations" @{
  title = "Phase 3 acceptance"
} $ownerSession
if ([int]$create.StatusCode -ne 201) {throw "AI conversation creation failed."}
$chat = $create.Content | ConvertFrom-Json
$identifier = [Guid]::NewGuid()
$reply = Post-Json "$web/api/workspaces/$wsId/ai/conversations/$($chat.id)/send" @{
  requestId = $identifier; message = "Explain the release process for this workspace"
} $ownerSession
if ([int]$reply.StatusCode -ne 200) {throw "AI message failed."}
$answer = $reply.Content | ConvertFrom-Json
if ($answer.provider -ne "demo-offline" -or $answer.answer -notlike "DEMO MODE*") {
  throw "Demo must disclose offline status."
}
if ([string]::IsNullOrEmpty($answer.traceId) -or $answer.usedTokens -lt 1) {
  throw "Tracing/usage missing."
}
$replayed = Post-Json "$web/api/workspaces/$wsId/ai/conversations/$($chat.id)/send" @{
  requestId = $identifier; message = "Explain the release process for this workspace"
} $ownerSession
if (($replayed.Content | ConvertFrom-Json).traceId -ne $answer.traceId) {throw "Idempotency violated."}
$messages = Invoke-RestMethod -Uri "$web/api/workspaces/$wsId/ai/conversations/$($chat.id)/messages" -WebSession $ownerSession
if (@($messages).Count -ne 2) {throw "Duplicate turns persisted."}
$usage = Invoke-RestMethod -Uri "$web/api/workspaces/$wsId/ai/usage" -WebSession $ownerSession
if ($usage.requestsToday -ne 1 -or $usage.reservedTokensToday -lt 1) {throw "Ledger not persistent."}
Write-Output "SIGNED_RUNTIME_DEMO_IDEMPOTENCY_USAGE=PASS"

# In Phase 2 script member account is reauthenticated with Member role.
$memberConversations = Invoke-RestMethod -Uri "$web/api/workspaces/$wsId/ai/conversations" -WebSession $memberSession
if (@($memberConversations).Count -ne 1) {throw "Member cannot read team chat."}
$otherSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$otherEmail = "other-" + [Guid]::NewGuid().ToString("N") + "@example.invalid"
$null = Post-Json "$web/api/session/register" @{email=$otherEmail;password=$password} $otherSession
$token = Find-Token $otherEmail "verify-email"
$null = Post-Json "$web/api/session/verify-email" @{token=$token} $otherSession
$null = Post-Json "$web/api/session/login" @{email=$otherEmail;password=$password} $otherSession
try {
  $null = Invoke-WebRequest -UseBasicParsing -Uri "$web/api/workspaces/$wsId/ai/conversations" -WebSession $otherSession
  throw "Outsider accessed AI chats."
} catch [System.Net.WebException] {
  if ([int]$_.Exception.Response.StatusCode -ne 403) {throw "Expected outside tenant 403."}
}
Write-Output "AI_TENANT_MEMBERSHIP_DENY=PASS"
Write-Output "PHASE3_SMOKE=PASS"
