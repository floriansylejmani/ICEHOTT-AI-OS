# Phase 2 local acceptance requires disposable PG/Mailpit/API/Next BFF only.
# Phase1 smoke creates randomized test users and an isolated workspace.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\phase1-local-smoke.ps1"
$null = Invoke-WebRequest -UseBasicParsing -Uri "$web/dashboard"
Write-Output "DASHBOARD_ROUTE=PASS"

$wsId = $workspace.id
$createdProject = Post-Json "$web/api/workspaces/$wsId/projects" @{name="  Phase 2 acceptance  ";description="  Browser BFF  ";status=1} $ownerSession
if ([int]$createdProject.StatusCode -ne 201) { throw "Project create failed." }
$project = $createdProject.Content | ConvertFrom-Json
if ($project.name -ne "Phase 2 acceptance") {throw "Project normalization failed."}
$overview = Invoke-RestMethod -Uri "$web/api/workspaces/$wsId/overview" -WebSession $ownerSession
if ($overview.memberCount -ne 2 -or $overview.projectCount -ne 1) {throw "Dashboard counts failed."}
$null = Post-Json "$web/api/session/login" @{email=$memberEmail;password=$password} $memberSession
$memberOverview = Invoke-RestMethod -Uri "$web/api/workspaces/$wsId/overview" -WebSession $memberSession
if ($memberOverview.memberCount -ne 2 -or $memberOverview.role -ne 2) {throw "Member overview invalid."}
try {
  $null = Invoke-WebRequest -UseBasicParsing -Method Put -Uri "$web/api/workspaces/$wsId/settings" -WebSession $memberSession -ContentType "application/json" -Body '{"name":"Unauthorized"}'
  throw "Member renamed workspace."
} catch [System.Net.WebException] {
  if ([int]$_.Exception.Response.StatusCode -ne 403) {throw "Expected settings deny."}
}
$updated = Invoke-RestMethod -Method Put -Uri "$web/api/workspaces/$wsId/projects/$($project.id)" -WebSession $memberSession -ContentType "application/json" -Body '{"name":"Phase 2 shipped","description":"Done","status":4}'
if ($updated.status -ne 4) {throw "Member project update failed."}
try {
  $null = Invoke-WebRequest -UseBasicParsing -Method Delete -Uri "$web/api/workspaces/$wsId/projects/$($project.id)" -WebSession $memberSession
  throw "Member archived without Admin."
} catch [System.Net.WebException] {
  if ([int]$_.Exception.Response.StatusCode -ne 403) {throw "Expected archive deny."}
}
Write-Output "ROLE_MATRIX_PROJECTS=PASS"

$settings = Invoke-RestMethod -Method Put -Uri "$web/api/workspaces/$wsId/settings" -WebSession $ownerSession -ContentType "application/json" -Body '{"name":"Phase 2 Dashboard"}'
if ($settings.displayName -ne "Phase 2 Dashboard") {throw "Owner rename failed."}
$deleted = Invoke-WebRequest -UseBasicParsing -Method Delete -Uri "$web/api/workspaces/$wsId/projects/$($project.id)" -WebSession $ownerSession
if ([int]$deleted.StatusCode -ne 204) {throw "Owner archive failed."}
$summary = Invoke-RestMethod -Uri "$web/api/workspaces/$wsId/overview" -WebSession $ownerSession
if ($summary.projectCount -ne 0 -or $summary.memberCount -ne 2) {throw "Archived counts incorrect."}
if (@($summary.recentActivity | Where-Object {$_.action -eq 'project.archived'}).Count -lt 1) {throw "Audit missing."}
Write-Output "OWNER_SETTINGS_ARCHIVE_AUDIT=PASS"
Write-Output "PHASE2_SMOKE=PASS"
