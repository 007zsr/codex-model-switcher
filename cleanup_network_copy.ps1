param(
    [Parameter(Mandatory = $true)]
    [string]$Repo,

    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [switch]$DeleteRepo
)

$ErrorActionPreference = 'Continue'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Warning 'GitHub CLI was not found. Network cleanup must be done manually.'
    exit 0
}

gh auth status | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Warning 'GitHub CLI is not authenticated. Network cleanup must be done manually.'
    exit 0
}

Write-Host "Deleting encrypted release payload: $Repo / $Tag"
gh release delete $Tag --repo $Repo --cleanup-tag --yes
if ($LASTEXITCODE -ne 0) {
    Write-Warning 'Release cleanup failed. Check GitHub permissions and delete the release manually.'
}

if ($DeleteRepo) {
    Write-Host "Attempting to delete temporary repository: $Repo"
    gh repo delete $Repo --yes
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'Repository deletion failed. This usually means the token lacks delete_repo permission. The encrypted release may already be gone; delete the temporary repository manually if needed.'
    }
}

