# Technical Assessment Worklog

## Containerize, Scan, and Ship a .NET Service via GitHub Actions

## Overview

This worklog documents the implementation of the technical assessment,
including commands executed, design decisions, test results, issues encountered,
and lessons learned.

The solution will demonstrate:

- ASP.NET Core Web API development using .NET 8
- Application health and version endpoints
- NuGet dependency vulnerability scanning
- Docker containerization
- Multi-stage Docker builds
- Non-root container execution
- Container image vulnerability scanning with Trivy
- CI/CD implementation using GitHub Actions
- GitHub Container Registry (GHCR) publishing
- Automated and manual deployment
- Post-deployment verification

---

# Environment

Development environment:

- Operating System: Windows
- .NET SDK: 8.0.303
- Git: 2.55.0.windows.5
- Docker: 27.2.0
- IDE: Visual Studio Code
- Application: ASP.NET Core Web API
- Target Framework: .NET 8




Environment validation commands:

Do **not** continue until you've captured the actual result.

---
# Environment Observations

WARNING: daemon is not using the default seccomp profile


# Step 2: Deliberately add the vulnerable package

The assessment specifically suggests `Newtonsoft.Json 9.0.1` as a known vulnerable package for this exercise. :contentReference[oaicite:1]{index=1}

Run:

```cmd
dotnet add package Newtonsoft.Json --version 9.0.1

```cmd
dotnet --version
git --version
docker --version

## Part 2 - Vulnerability Scanning

### Baseline Dependency Scan

Before intentionally adding a vulnerable dependency, I ran:

```cmd
dotnet list package --vulnerable --include-transitive


### Deliberately Introduced Vulnerable Dependency

To validate that the dependency scanning process could detect a real
security issue, I intentionally added an older vulnerable version of
Newtonsoft.Json.

Command:

```cmd
dotnet add package Newtonsoft.Json --version 9.0.1



### Issue Encountered - Docker Engine Initially Unavailable

When I first attempted to build the Docker image:

```cmd
docker build -t orderservice:vulnerable .

error during connect:
open //./pipe/dockerDesktopLinuxEngine:
The system cannot find the file specified.


docker info

Server Version: 27.2.0
Operating System: Docker Desktop
OSType: linux
Architecture: x86_64
Kernel: WSL2


## Now retred the image build
docker build -t orderservice:vulnerable .


### Container Healthcheck Verification

After updating the runtime image to install `curl` and changing the Docker
HEALTHCHECK to use the `/health` endpoint, I rebuilt and restarted the container.

Docker reported:

```text
orderservice:vulnerable
Up 2 minutes (healthy)



## Next: Trivy vulnerability scan

Now we intentionally scan the image **before fixing Newtonsoft.Json 9.0.1**. That sequence is important because the assessment wants you to demonstrate the vulnerability first, remediate it, rebuild, and prove the scan is clean afterward. :contentReference[oaicite:1]{index=1}

First check whether Trivy is already installed:

```cmd
trivy --version



### Vulnerable Container Image Scan

After successfully building and running the deliberately vulnerable container,
I scanned the image using Trivy.

Command:

```cmd
trivy image orderservice:vulnerable



Trivy scanned both operating-system packages and application dependencies.

The application dependency scan detected:

app/OrderService.deps.json (dotnet-core)

Total: 1
HIGH: 1
CRITICAL: 0

Library:           Newtonsoft.Json
Vulnerability:     CVE-2024-21907
Severity:          HIGH
Installed Version: 9.0.1
Fixed Version:     13.0.1



## Next — fix the application vulnerability

Now we're finally ready to remove `Newtonsoft.Json 9.0.1`.

From:

```text
C:\Users\roger\Desktop\TechnicalAssessment\OrderService>


### Dependency Remediation

The intentionally vulnerable Newtonsoft.Json package was upgraded from
version 9.0.1 to version 13.0.1.

Command:

```cmd
dotnet add package Newtonsoft.Json --version 13.0.1




Now we need to prove the **container image** is also clean from that .NET vulnerability.

Run these commands next:

```cmd
docker build -t orderservice:fixed .



### Container Rescan After Dependency Remediation

After upgrading Newtonsoft.Json from 9.0.1 to 13.0.1, I rebuilt the
container image and rescanned it with Trivy.

Commands:

```cmd
docker build -t orderservice:fixed .
trivy image orderservice:fixed




### Next: run the actual CI-style security gate

Now run exactly this:

```cmd
trivy image --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 orderservice:fixed


### Final Container Security Gate

After upgrading Newtonsoft.Json to 13.0.1 and rebuilding the image, I ran
the same type of vulnerability gate that will be used in CI/CD.

Command:

```cmd
trivy image --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 orderservice:fixed


### Fixed Container Runtime Verification

The remediated container image was started with the application version
provided through the APP_VERSION environment variable.

Command:

```cmd
docker run -d --name orderservice-fixed -p 8080:8080 -e APP_VERSION=fixed orderservice:fixed


At this point, **Part 2 is complete**.

The next phase is **Part 3: GitHub Actions CI/CD**. We should start with just one small step: create the GitHub workflow directory.

From:

```text
C:\Users\roger\Desktop\TechnicalAssessment\OrderService>



## Part 3 – GitHub Actions CI/CD Pipeline

### Initial GitHub Actions Setup

Created a GitHub Actions workflow at:

`.github/workflows/ci.yml`

The workflow is configured to run on:

- Pushes to `main`
- Pull requests targeting `main`
- Manual execution using `workflow_dispatch`

The first pipeline job implements a formatting quality gate using
`dotnet format --verify-no-changes --no-restore`.

An `.editorconfig` file was also added to establish consistent formatting
rules between local development and the CI environment.

### Initial CI Formatting Failure

The first GitHub Actions execution failed during the `Format Check` job.

GitHub Actions reported:

```text
Program.cs(14,11): error FINALNEWLINE:
Fix final newline. Insert '\n'.

Process completed with exit code 2.




There is also another failure worth documenting: the **first Git push of the workflow was rejected** because the cached GitHub Personal Access Token didn't have workflow permission.

I recommend adding this immediately after the section above:

```markdown
### GitHub Workflow Authentication Issue

The initial attempt to push `.github/workflows/ci.yml` was rejected by GitHub:

```text
refusing to allow a Personal Access Token to create or update workflow
`.github/workflows/ci.yml` without `workflow` scope



That's exactly the kind of troubleshooting trail the evaluator is asking for when they say **"silence is not good."**

Now fix the newline in `Program.cs`, save it, and run:

```cmd
dotnet format --verify-no-changes --no-restore
echo %ERRORLEVEL%
git status


### Formatting Gate Remediation Verified

After correcting the final newline and converting `Program.cs` to LF line
endings, the local formatting check passed:

```cmd
dotnet format --verify-no-changes --no-restore
echo %ERRORLEVEL%


### Build and Test Stage Added

A second GitHub Actions job was added for application build and test validation.

The job uses:

```yaml
needs: format


### Dependency Vulnerability Scan Stage

A dedicated dependency scanning job was added to the GitHub Actions pipeline.

The job runs:

```text
dotnet list package --vulnerable --include-transitive



### CI Validation Result

The updated GitHub Actions pipeline completed successfully with all three stages passing:

```text
Format Check       PASSED
Build and Test     PASSED
Dependency Scan    PASSED


### Docker Build and Trivy Security Gate Validation

The Docker build and Trivy image security scan were added to the GitHub Actions pipeline.

The container image is tagged using the Git commit SHA:

```text
orderservice:${{ github.sha }}


URGENT NOTE:
This confirms that the production image can be built successfully and that the current image passes the configured HIGH/CRITICAL Trivy security gate.

Because the Docker/Trivy job depends on the dependency scan, and future publishing/deployment jobs will depend on the Docker/Trivy job, a failed security scan will stop the pipeline before release.


### GHCR Publishing Validation

The CI/CD workflow was extended to publish the scanned container image to
GitHub Container Registry (GHCR).

The image is tagged using the Git commit SHA:

```text
ghcr.io/shrogers45/orderservice:${{ github.sha }}



URGENT NOTES FOR CACHE:
I implemented NuGet package caching as a step in each .NET job rather than as a separate job. Since GitHub-hosted jobs run on separate ephemeral runners, actions/cache@v4 restores the NuGet package cache for each job. The cache is keyed using the project file hash, so changes to package references generate a new cache key.

### NuGet Cache Implementation and Validation

Added NuGet package caching to the Format Check, Build and Test, and
Dependency Scan jobs using `actions/cache@v4`.

The cache stores:

`~/.nuget/packages`

The cache key is based on the runner operating system and the hash of
the project files:

`${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}`

This allows unchanged NuGet dependencies to be reused between workflow
runs instead of downloading all packages again.

Caching was implemented inside each .NET job because GitHub-hosted jobs
run on separate ephemeral runners and do not automatically share their
local filesystems.

After adding the cache steps, the complete CI/CD workflow was executed
successfully. Format Check, Build and Test, Dependency Scan, container
security scanning, GHCR publishing, and deployment all completed
successfully.