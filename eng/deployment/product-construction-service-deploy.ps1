# This script deploys the Product Construction Service using the blue/green deployment pattern.
# The script determines the color of the currently active revision, deactivates the old inactive revision, 
# and deploys the new revision, switching all traffic to it if the health probes pass.
param(
    [Parameter(Mandatory=$true)][string]$resourceGroupName,
    [Parameter(Mandatory=$true)][string]$containerappName,
    [Parameter(Mandatory=$true)][string]$newImageTag,
    [Parameter(Mandatory=$true)][string]$containerRegistryName,
    [Parameter(Mandatory=$true)][string]$imageName,
    [Parameter(Mandatory=$true)][string]$pcsUrl
)

function StopAndWait([string]$pcsUrl) {
    try {
        $pcsStopUrl = $pcsUrl + "/status/stop"
        $stopResponse = Invoke-WebRequest -Uri $pcsStopUrl -Method Put

        if ($stopResponse.StatusCode -ne 200) {
            Write-Host "Service isn't responding to the stop request. Deploying the new revision without stopping the service."
            return
        }

        # wait for the service to finish processing the current job
        $sleep = $false
        $pcsStatusUrl = $pcsUrl + "/status"
        DO
        {
            if ($sleep -eq $true) 
            {
                Start-Sleep -Seconds 30
            }
            $pcsStateResponse = Invoke-WebRequest -Uri $pcsStatusUrl -Method Get
            if ($pcsStateResponse.StatusCode -ne 200) {
                Write-Host "Service isn't responding to the status request. Deploying the new revision without stopping the service."
                return
            }
            Write-Host "Product Construction Service state: $($pcsStateResponse.Content)"
            $sleep = $true
        } While ($pcsStateResponse.Content -notmatch "Stopped")
    }
    catch {
        Write-Host "An error occurred: $($_.Exception.Message).  Deploying the new revision without stopping the service."
    }
    return
}

function Compare-Properties($before, $after) {
    mkdir temp
    cd temp
    git config --global user.email "temp@example.com"
    git config --global user.name "temp"
    Set-Content -Path output.json -Value $before -Encoding UTF8
    git init
    git add .\
    git commit -m "Before deployment"
    Set-Content -Path output.json -Value $after -Encoding UTF8
    git diff .
}

az extension add --name containerapp --upgrade

$before = az containerapp show --name $containerappName --resource-group $resourceGroupName --output json

Write-Host "Fetching all revisions to determine the active label"
$containerappTraffic = az containerapp ingress traffic show --name $containerappName --resource-group $resourceGroupName | ConvertFrom-Json
# find the currently active revision
$activeRevision = $containerappTraffic | Where-Object { $_.weight -eq 100 } 

Write-Host "Currently active revision: $($activeRevision.revisionName) with label $($activeRevision.label)"

# detirmine the label of the inactive revision
if ($activeRevision.label -eq "blue") {
    $inactiveLabel = "green"
} else {
    $inactiveLabel = "blue"
}

Write-Host "Next revision will be deployed with label $inactiveLabel"
Write-Host "Removing label $inactiveLabel from the inactive revision"
# remove the label from the inactive revision
$revisionRemovalOutput = az containerapp revision label remove --label $inactiveLabel --name $containerappName --resource-group $resourceGroupName 2>&1

if ($revisionRemovalOutput -match "Please specify a label name with an associated traffic weight") {
    Write-Host "Couldn't find a revision with label $inactiveLabel. Skipping deactivation of inactive revision"
} 
else
{
    Write-Host "Deactivating inactive revision"
    # deactivate the inactive revision

    $inactiveRevision = $containerappTraffic | Where-Object { $_.label -eq $inactiveLabel }

    az containerapp revision deactivate --revision $inactiveRevision.revisionName --name $containerappName --resource-group $resourceGroupName
}

# Tell the service to stop processing jobs after it finishes the current one
Write-Host "Stopping the service from processing new jobs"
StopAndWait $pcsUrl

# deploy the new image
$newImage = "$containerRegistryName.azurecr.io/$imageName`:$newImageTag"
Write-Host "Deploying new image $newImage"
# We should used a managed identity to authenticate to the container registry once https://github.com/dotnet/arcade-services/issues/3180 is resolved
az containerapp update --name $containerappName --resource-group $resourceGroupName --image $newImage --revision-suffix $newImageTag | Out-Null

$newRevisionName = "$containerappName--$newImageTag"

try
{
    Write-Host "Waiting for new revision $newRevisionName to become active"
    # wait for the new revision to pass health probes and become active
    $sleep = $false
    DO
    {
        if ($sleep -eq $true) 
        {
            Start-Sleep -Seconds 60
        }
        $newRevisionRunningState = az containerapp revision show --name $containerappName --resource-group $resourceGroupName --revision $newRevisionName --query "properties.runningState"
        Write-Host "New revision running state: $newRevisionRunningState"
        $sleep = $true
    } While ($newRevisionRunningState -notmatch "Running" -and $newRevisionRunningState -notmatch "Failed")

    if ($newRevisionRunningState -match "Running") {
        Write-Host "Assigning label $inactiveLabel to the new revision"
        # assign the label to the new revision
        az containerapp revision label add --label $inactiveLabel --name $containerappName --resource-group $resourceGroupName --revision $newRevisionName | Out-Null

        # transfer all traffic to the new revision
        az containerapp ingress traffic set --name $containerappName --resource-group $resourceGroupName --label-weight "$inactiveLabel=100" | Out-Null
        Write-Host "All traffic has been redirected to label $inactiveLabel"
    }
    else {
        Write-Host "New revision is not running. Check revision $newRevisionName logs in the inactive revisions. Deactivating the new revision"
        az containerapp revision deactivate --revision $newRevisionName --name $containerappName --resource-group $resourceGroupName
        exit 1
    }
}
finally {
    # Start the service. This either starts the new revision or the old one if the new one failed to start
    Write-Host "Starting the product construction service"
    $pcsStartUrl = $pcsUrl + "/status/start"
    Invoke-WebRequest -Uri $pcsStartUrl -Method Put
    $after = az containerapp show --name $containerappName --resource-group $resourceGroupName --output json

    Compare-Properties $before $after
}
