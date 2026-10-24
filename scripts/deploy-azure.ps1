<#
 Provisions Dokumenta DMS on Azure (low-cost layout) in two phases.
 Usage: .\scripts\deploy-azure.ps1 -ResourceGroup rg-dokumenta -Location westeurope [-Iac bicep|terraform]
 Requires: az CLI (logged in), PowerShell 5.1+, and terraform on PATH when -Iac terraform.
 Secrets are generated once and written to .azure-secrets.json (git-ignored) - keep it safe.
#>
param(
  [ValidateSet('bicep', 'terraform')][string]$Iac = 'bicep',
  [string]$ResourceGroup = 'rg-dokumenta',
  [string]$Location = 'westeurope',
  [string]$ImageTag = '1',
  [string]$GeminiKey = $env:GEMINI_API_KEY,
  [string]$OpenAiKey = $env:OPENAI_API_KEY,
  [string]$GeminiModel = 'gemini-3.8-flash',
  [string]$DocumentIntelligenceEndpoint = '',
  [string]$DocumentIntelligenceKey = '',
  [string]$SecretsFile = (Join-Path $PSScriptRoot '..\.azure-secrets.json')
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

function New-Secret { [Convert]::ToBase64String((1..32 | ForEach-Object { [byte](Get-Random -Max 256) })).Replace('+','a').Replace('/','b').Replace('=','c') }

if (Test-Path $SecretsFile) { $s = Get-Content $SecretsFile -Raw | ConvertFrom-Json }
else {
  $s = [pscustomobject]@{
    pgAdminPassword = (New-Secret); rabbitPassword = (New-Secret)
    downloadApiKey = (New-Secret); artifactApiKey = (New-Secret); searchApiKey = (New-Secret)
  }
  $s | ConvertTo-Json | Set-Content $SecretsFile
}

$common = @(
  '--resource-group', $ResourceGroup, '--template-file', "$root\infra\main.bicep",
  '--parameters', "pgAdminPassword=$($s.pgAdminPassword)", "rabbitPassword=$($s.rabbitPassword)",
  "downloadApiKey=$($s.downloadApiKey)", "artifactApiKey=$($s.artifactApiKey)", "searchApiKey=$($s.searchApiKey)",
  "imageTag=$ImageTag", "geminiKey=$GeminiKey", "openAiKey=$OpenAiKey", "geminiModel=$GeminiModel",
  "documentIntelligenceEndpoint=$DocumentIntelligenceEndpoint", "documentIntelligenceKey=$DocumentIntelligenceKey"
)

az group create -n $ResourceGroup -l $Location | Out-Null

# Returns a normalized outputs object (acrName, postgresHost, staticWebAppName, staticWebAppUrl, apiUrl).
function Invoke-Infra([bool]$DeployApps) {
  $flag = $DeployApps.ToString().ToLower()
  if ($Iac -eq 'bicep') {
    $o = az deployment group create @common deployApps=$flag --query properties.outputs -o json | ConvertFrom-Json
    return [pscustomobject]@{
      acrName = $o.acrName.value; postgresHost = $o.postgresHost.value
      staticWebAppName = $o.staticWebAppName.value; staticWebAppUrl = $o.staticWebAppUrl.value
      apiUrl = $o.apiUrl.value
    }
  }

  $tfDir = "$root\infra\terraform"
  $env:TF_VAR_pg_admin_password = $s.pgAdminPassword
  $env:TF_VAR_rabbit_password = $s.rabbitPassword
  $env:TF_VAR_download_api_key = $s.downloadApiKey
  $env:TF_VAR_artifact_api_key = $s.artifactApiKey
  $env:TF_VAR_search_api_key = $s.searchApiKey
  $env:TF_VAR_gemini_key = $GeminiKey
  $env:TF_VAR_openai_key = $OpenAiKey
  $env:TF_VAR_document_intelligence_key = $DocumentIntelligenceKey
  $subscriptionId = az account show --query id -o tsv
  terraform -chdir="$tfDir" init -input=false
  if ($LASTEXITCODE) { throw 'terraform init failed' }
  terraform -chdir="$tfDir" apply -auto-approve -input=false `
    -var "subscription_id=$subscriptionId" -var "resource_group_name=$ResourceGroup" `
    -var "image_tag=$ImageTag" -var "gemini_model=$GeminiModel" `
    -var "document_intelligence_endpoint=$DocumentIntelligenceEndpoint" -var "deploy_apps=$flag"
  if ($LASTEXITCODE) { throw 'terraform apply failed' }
  $o = terraform -chdir="$tfDir" output -json | ConvertFrom-Json
  return [pscustomobject]@{
    acrName = $o.acr_name.value; postgresHost = $o.postgres_host.value
    staticWebAppName = $o.static_web_app_name.value; staticWebAppUrl = $o.static_web_app_url.value
    apiUrl = $o.api_url.value
  }
}

Write-Host "== Phase 1 ($Iac): infrastructure (ACR, Postgres, Storage, Container Apps env, RabbitMQ, Static Web App)"
$out = Invoke-Infra $false
$acr = $out.acrName

Write-Host '== Building images in ACR'
$images = @{ docmanager = 'DocuManagementApp'; orchestrator = 'OrchestratorService'; extraction = 'ExtractionService'; search = 'SearchService' }
foreach ($name in $images.Keys) {
  az acr build -r $acr -f "$root\Dockerfile.service" --build-arg PROJECT=$($images[$name]) -t "${name}:$ImageTag" $root
}

Write-Host '== Applying EF migrations to documanagementdb (temporarily allowing this machine)'
$myIp = (Invoke-RestMethod https://api.ipify.org)
$pgName = $out.postgresHost.Split('.')[0]
az postgres flexible-server firewall-rule create -g $ResourceGroup -n $pgName --rule-name TempDeploy --start-ip-address $myIp --end-ip-address $myIp | Out-Null
$conn = "Host=$($out.postgresHost);Port=5432;Database=documanagementdb;Username=dokumentaadmin;Password=$($s.pgAdminPassword);Ssl Mode=Require"
dotnet ef database update --project "$root\DocuManagementApp\DocuManagementApp.csproj" --connection $conn
az postgres flexible-server firewall-rule delete -g $ResourceGroup -n $pgName --rule-name TempDeploy --yes

Write-Host '== Phase 2: container apps'
$out = Invoke-Infra $true

Write-Host "API:      $($out.apiUrl)"
Write-Host "Frontend: $($out.staticWebAppUrl)"
Write-Host 'Next: register the frontend URL as an SPA redirect URI in Entra, then build the frontend:'
Write-Host "  cd angClientapp; `$env:API_URL='$($out.apiUrl)/api'; `$env:SPA_REDIRECT_URI='$($out.staticWebAppUrl)'; npm ci; npm run build:prod"
Write-Host "  az staticwebapp secrets list -n $($out.staticWebAppName) --query properties.apiKey -o tsv   # deployment token"
Write-Host "  npx @azure/static-web-apps-cli deploy dist/angClientapp/browser --deployment-token <token> --env production"
