#Requires -Version 5.1

$ErrorActionPreference = 'Stop'
$repository = 'TangSong777/HanLin-ToDo'
$releaseWorkflow = '.github/workflows/release-source.yml'
$rulesFile = '.github/main-ruleset.json'

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments)

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Test-GitRef {
    param([string] $RefName)

    & git show-ref --verify --quiet $RefName
    if ($LASTEXITCODE -gt 1) {
        throw "Unable to check Git ref $RefName."
    }
    return ($LASTEXITCODE -eq 0)
}

Push-Location -LiteralPath $PSScriptRoot
try {
    if (-not (Test-Path -LiteralPath '.git')) {
        Invoke-Checked git @('init', '--initial-branch=main')
    }

    if (-not (Test-GitRef 'refs/heads/main')) {
        $currentBranch = Invoke-Checked git @('symbolic-ref', '--short', 'HEAD')
        if ($currentBranch -ne 'main') {
            throw 'An initial commit requires an unborn main branch.'
        }

        $files = @('.gitignore', $releaseWorkflow, $rulesFile)
        Invoke-Checked git (@('add', '--') + $files)
        Invoke-Checked git (@('commit', '-m', 'Initialize stable release rules', '--') + $files)
    }

    if (Test-GitRef 'refs/heads/snapshot') {
        Invoke-Checked git @('switch', 'snapshot')
    } else {
        Invoke-Checked git @('switch', '--create', 'snapshot', 'main')
    }

    if (-not (Invoke-Checked git @('ls-tree', '--name-only', 'snapshot', '--', 'README.md'))) {
        $files = @('README.md', 'Initialize-Repository.ps1')
        Invoke-Checked git (@('add', '--') + $files)
        Invoke-Checked git (@('commit', '-m', 'Record project goals and development workflow', '--') + $files)
    }

    Invoke-Checked git @('cat-file', '-e', "main:$releaseWorkflow")

    if (-not (Get-Command gh -CommandType Application -ErrorAction SilentlyContinue)) {
        throw 'Install GitHub CLI and sign in before configuring the remote.'
    }
    $login = Invoke-Checked gh @('api', '--hostname', 'github.com', 'user', '--jq', '.login')
    if ($login -ne 'TangSong777') {
        throw 'Sign in to GitHub CLI as TangSong777 to continue.'
    }

    $url = "https://github.com/$repository.git"
    $remotes = Invoke-Checked git @('remote')
    if ($remotes -contains 'origin') {
        $origin = Invoke-Checked git @('remote', 'get-url', 'origin')
        $expectedUrls = @($url, "https://github.com/$repository", "git@github.com:$repository.git")
        if ($origin -notin $expectedUrls) {
            throw 'The existing origin points to a different repository.'
        }
    }

    $remote = & {
        $ErrorActionPreference = 'Continue'
        $visibility = & gh repo view $repository --json visibility --jq .visibility 2>$null
        [pscustomobject]@{
            Visibility = $visibility
            ExitCode = $LASTEXITCODE
        }
    }
    if ($remote.ExitCode -ne 0) {
        Invoke-Checked gh @('repo', 'create', $repository, '--private')
    } elseif ($remote.Visibility -ne 'PRIVATE') {
        throw 'Check repository visibility before uploading the project.'
    }

    if ($remotes -notcontains 'origin') {
        Invoke-Checked git @('remote', 'add', 'origin', $url)
    }

    $gitAuth = @('-c', 'credential.helper=', '-c', 'credential.helper=!gh auth git-credential')
    Invoke-Checked git ($gitAuth + @('fetch', 'origin'))

    # Check any existing remote history before publishing missing branches.
    foreach ($branch in @('main', 'snapshot')) {
        $remoteRef = "refs/remotes/origin/$branch"
        if (Test-GitRef $remoteRef) {
            $null = Invoke-Checked git @('merge-base', $branch, $remoteRef)
        }
    }
    foreach ($branch in @('main', 'snapshot')) {
        if (Test-GitRef "refs/remotes/origin/$branch") {
            Invoke-Checked git @("branch", "--set-upstream-to=origin/$branch", $branch)
        } else {
            Invoke-Checked git ($gitAuth + @('push', '--set-upstream', 'origin', $branch))
        }
    }

    Invoke-Checked git @('cat-file', '-e', "origin/main:$releaseWorkflow")
    Invoke-Checked gh @('repo', 'edit', $repository, '--default-branch', 'main', '--delete-branch-on-merge=false')

    $rulesEndpoint = "repos/$repository/rulesets"
    $rulesetName = 'Stable releases from snapshot'
    $rulesets = Invoke-Checked gh @('api', $rulesEndpoint) | ConvertFrom-Json
    if (-not ($rulesets | Where-Object { $_.name -eq $rulesetName })) {
        $null = Invoke-Checked gh @('api', '--method', 'POST', $rulesEndpoint, '--input', $rulesFile)
    }

    $rules = Invoke-Checked gh @('api', "repos/$repository/rules/branches/main") | ConvertFrom-Json
    $pullRequestRule = $rules | Where-Object { $_.type -eq 'pull_request' }
    $statusRules = $rules | Where-Object { $_.type -eq 'required_status_checks' }
    if ('merge' -notin $pullRequestRule.parameters.allowed_merge_methods -or
        'squash' -in $pullRequestRule.parameters.allowed_merge_methods -or
        'rebase' -in $pullRequestRule.parameters.allowed_merge_methods -or
        'deletion' -notin $rules.type -or
        'non_fast_forward' -notin $rules.type -or
        'snapshot-release-source' -notin $statusRules.parameters.required_status_checks.context) {
        throw 'The repository exists. Verify the active main ruleset before releasing.'
    }

    Write-Host 'The main and snapshot branches track origin. Main requires the release source check.'
} finally {
    Pop-Location
}
