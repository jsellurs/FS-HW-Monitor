# Signs files with Azure Trusted Signing and checks the result. Used by build.ps1 -Sign and by the
# installer build (Inno Setup calls it for the setup and uninstaller).
#
# Needs, once per machine:
#   Install-Module TrustedSigning -Scope CurrentUser    (in Windows PowerShell)
#   az login                                           (an account with the Certificate Profile Signer role)
# and a signing.json next to build.ps1 (it's git-ignored):
#   { "Endpoint": "https://eus.codesigning.azure.net/",
#     "CodeSigningAccountName": "<account>", "CertificateProfileName": "<profile>" }

param([Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Files)

$ErrorActionPreference = 'Stop'
# When this runs under a PowerShell 7 parent, it inherits PowerShell 7 module paths; load the 5.1 ones.
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security')
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility')
Import-Module TrustedSigning -MinimumVersion 0.5.8

$configPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'signing.json'
if (-not (Test-Path -LiteralPath $configPath)) { throw "Signing settings not found: $configPath" }
$config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json

$paths = $Files | ForEach-Object { (Resolve-Path -LiteralPath $_).Path }

# Sign in through the Azure CLI only, so a missing login fails fast instead of opening a browser.
Invoke-TrustedSigning -Endpoint $config.Endpoint `
    -CodeSigningAccountName $config.CodeSigningAccountName -CertificateProfileName $config.CertificateProfileName `
    -Files ($paths -join ',') -FileDigest SHA256 -TimestampRfc3161 'http://timestamp.acs.microsoft.com' -TimestampDigest SHA256 `
    -Description 'FS HW Monitor' -DescriptionUrl 'https://github.com/jsellurs/FS-HW-Monitor' `
    -ExcludeEnvironmentCredential -ExcludeWorkloadIdentityCredential -ExcludeManagedIdentityCredential `
    -ExcludeSharedTokenCacheCredential -ExcludeVisualStudioCredential -ExcludeVisualStudioCodeCredential `
    -ExcludeAzurePowerShellCredential -ExcludeAzureDeveloperCliCredential -ExcludeInteractiveBrowserCredential

foreach ($path in $paths) {
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) {
        throw "$path is not validly signed and timestamped ($($signature.Status))."
    }
    Write-Host ("Signed {0} ({1})" -f (Split-Path -Leaf $path), $signature.SignerCertificate.GetNameInfo('SimpleName', $false))
}
