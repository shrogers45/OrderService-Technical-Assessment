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



### Manual Redeploy Validation

Implemented the `workflow_dispatch` deployment path to allow an existing
container image in GitHub Container Registry (GHCR) to be redeployed without
performing another build or publishing a new image.

#### Identifying the Image to Redeploy

Images published by the normal CI/CD pipeline are tagged with the Git commit
SHA:

`ghcr.io/shrogers45/orderservice:<git-sha>`

The Git SHA therefore provides traceability between the source code, GitHub
Actions workflow run, container image, and deployed application.

To obtain the image tag for the most recently committed version locally, I used:

`git rev-parse HEAD`

The resulting SHA corresponds to the image tag created by the successful
main-branch pipeline.

The image tag can also be identified from the successful GitHub Actions
workflow run or from the container versions published in GitHub Container
Registry.

For the manual redeployment test, I opened:

`GitHub Repository -> Actions -> OrderService CI/CD -> Run workflow`

I selected the `main` branch and entered the existing Git SHA into the
`Existing GHCR image tag (Git SHA) to redeploy` field.

This identifies the previously built and scanned image that should be pulled
from GHCR.

#### Manual Redeploy Execution

During a manual `workflow_dispatch`, the following CI jobs are intentionally
skipped:

- Format Check
- Build and Test
- Dependency Scan
- Docker Build, Trivy Scan, and GHCR Push

Only the Deploy job executes.

The Deploy job:

1. Reads the supplied GHCR image tag.
2. Authenticates to GitHub Container Registry.
3. Pulls `ghcr.io/shrogers45/orderservice:<image-tag>`.
4. Removes any existing same-name container.
5. Starts the selected container image.
6. Sets `APP_VERSION` to the deployed image tag.
7. Calls `/health` to verify application health.
8. Calls `/version` to confirm that the running application reports the
   expected image tag.

The manual redeployment completed successfully.

The GitHub Actions results confirmed that Format Check, Build and Test,
Dependency Scan, and Docker Build/Trivy Scan/GHCR Push were skipped while
the Deploy job completed successfully.

This demonstrates that a previously built, security-scanned, and published
container image can be redeployed without rebuilding or republishing the
application.

The normal push-to-main pipeline was also retested after implementing the
manual deployment path, and all five jobs completed successfully.


### Deployment Risk and Smallest Mitigation

The assessment deployment uses `docker run -d` on the GitHub Actions runner
without a Docker restart policy or automatic health-based rollback.

The primary risk is that if the newly deployed container fails after startup,
Docker will not automatically restart it. In addition, the existing container
is removed before the new container is fully validated. If the new version
fails its health check, there is no automatic rollback to the previously known
good image, which could result in service downtime.

The smallest improvement for a persistent Docker host would be to add a restart
policy such as:

`--restart unless-stopped`

This would allow Docker to automatically restart the application if the
container process exits unexpectedly.

A small additional deployment improvement would be to retain the previous
known-good image tag. If the new container fails the `/health` or `/version`
verification, the failed container could be removed and the previous image
started again automatically.

For this assessment, deployment is performed on the temporary GitHub-hosted
Actions runner. Therefore the deployment demonstrates the exact pull, replace,
start, and validation procedure rather than serving as a persistent production
environment.




## Part 4 — Deployment

### Deployment Approach

The deployment stage runs as a separate GitHub Actions job after the container
security and publishing job.

For a normal push to the `main` branch, the Deploy job pulls the exact
SHA-tagged image that was built, scanned by Trivy, and published to GitHub
Container Registry (GHCR).

The assessment does not require a permanent remote deployment target, so the
GitHub-hosted Actions runner is used as the deployment target. This demonstrates
the complete deployment procedure while keeping the assessment self-contained.

The container image format is:

`ghcr.io/shrogers45/orderservice:<git-sha>`

The Git commit SHA is used as the Docker image tag, providing traceability
between the source revision, GitHub Actions run, container image, and deployed
application.


### Deployment Commands

The deployment process authenticates to GHCR, pulls the selected image,
removes any existing container with the same name, starts the new container,
and validates the running application.

The equivalent Docker commands are:

```bash
# Authenticate to GitHub Container Registry.
echo "${GITHUB_TOKEN}" | docker login ghcr.io \
  -u "${GITHUB_ACTOR}" \
  --password-stdin

# Pull the exact image that passed the CI/CD security gate.
docker pull "ghcr.io/shrogers45/orderservice:${DEPLOY_TAG}"

# Stop and remove an existing container with the same name.
# docker rm -f performs both operations and || true makes the
# command safe when the container does not already exist.
docker rm -f orderservice || true

# Start the selected image in detached mode.
# APP_VERSION is set to the same value as the Docker image tag.
docker run -d \
  --name orderservice \
  -p 8080:8080 \
  -e APP_VERSION="${DEPLOY_TAG}" \
  "ghcr.io/shrogers45/orderservice:${DEPLOY_TAG}"

# Verify that the application is healthy.
curl --fail --silent --show-error \
  http://localhost:8080/health

# Verify the version running inside the container.
curl --fail --silent --show-error \
  http://localhost:8080/version
```

The `/version` response is compared with `${DEPLOY_TAG}` by the GitHub Actions
workflow. If the expected tag is not present, the command returns a non-zero
exit code and the deployment job fails.

The workflow performs the verification with:

```bash
VERSION_RESPONSE=$(curl --fail --silent --show-error \
  http://localhost:8080/version)

echo "Application response: ${VERSION_RESPONSE}"

echo "${VERSION_RESPONSE}" | grep "${DEPLOY_TAG}"
```


### Idempotent Container Replacement

An existing container must be removed before another container can be created
using the same `orderservice` name.

The assessment suggests:

```bash
docker stop orderservice 2>/dev/null
docker rm orderservice 2>/dev/null
```

The implemented workflow uses:

```bash
docker rm -f orderservice || true
```

`docker rm -f` stops and removes the existing container in one operation.

`|| true` prevents a first-time deployment from failing when an `orderservice`
container does not already exist.

This makes repeated deployments safe from Docker container-name collisions.


### Deployment Verification

After starting the container, the workflow validates both application
availability and version identity.

The health check is:

```bash
curl --fail --silent --show-error \
  http://localhost:8080/health
```

Expected response:

```json
{"status":"healthy"}
```

The version check is:

```bash
curl --fail --silent --show-error \
  http://localhost:8080/version
```

Expected response format:

```json
{"version":"<deployed-image-tag>"}
```

Because `APP_VERSION` is set to `${DEPLOY_TAG}` during `docker run`, the
`/version` endpoint provides runtime evidence that the intended container image
was deployed.


### Manual Redeployment

The workflow also supports redeploying an existing GHCR image using
`workflow_dispatch`.

Images published by the normal pipeline use the Git commit SHA as their tag.

To obtain the Git SHA for the current repository revision locally:

```bash
git rev-parse HEAD
```

The SHA can also be identified from the successful GitHub Actions workflow run
or from the container image versions published in GHCR.

The manual deployment procedure is:

1. Open the GitHub repository.
2. Select **Actions**.
3. Select **OrderService CI/CD**.
4. Select **Run workflow**.
5. Select the `main` branch.
6. Enter an existing Git SHA in the
   **Existing GHCR image tag (Git SHA) to redeploy** field.
7. Select **Run workflow**.

During `workflow_dispatch`, the CI jobs are intentionally skipped:

- Format Check
- Build and Test
- Dependency Scan
- Docker Build, Trivy Scan, and GHCR Push

Only the Deploy job executes.

The existing image is therefore pulled directly from GHCR rather than being
rebuilt or republished.

The manual redeployment test completed successfully. The Deploy job pulled the
existing image, started the container, verified `/health`, and confirmed through
`/version` that the requested image tag was running.


### Deployment Risk — Restart Policy and Rollback

The assessment deployment currently uses:

```bash
docker run -d \
  --name orderservice \
  -p 8080:8080 \
  -e APP_VERSION="${DEPLOY_TAG}" \
  "ghcr.io/shrogers45/orderservice:${DEPLOY_TAG}"
```

There is no Docker restart policy.

If the application process terminates unexpectedly on a persistent Docker host,
the container will remain stopped until something explicitly starts it again.

A small improvement would be:

```bash
docker run -d \
  --restart unless-stopped \
  --name orderservice \
  -p 8080:8080 \
  -e APP_VERSION="${DEPLOY_TAG}" \
  "ghcr.io/shrogers45/orderservice:${DEPLOY_TAG}"
```

This allows Docker to restart the container automatically after an unexpected
process failure or host restart.

However, a restart policy does not provide application rollback.

The current deployment also removes the previous container before the new
container has passed its health verification:

```bash
docker rm -f orderservice || true
```

If the new image starts but subsequently fails `/health`, the previous
known-good container has already been removed. This can result in service
downtime.

The smallest practical rollback improvement would be to retain the previous
known-good image tag before deployment. If the new container fails its health
or version verification, the failed container could be removed and the previous
known-good image started again.

For example, the recovery procedure would conceptually perform:

```bash
docker rm -f orderservice || true

docker run -d \
  --restart unless-stopped \
  --name orderservice \
  -p 8080:8080 \
  -e APP_VERSION="${PREVIOUS_TAG}" \
  "ghcr.io/shrogers45/orderservice:${PREVIOUS_TAG}"
```

For this assessment, deployment runs on an ephemeral GitHub-hosted Actions
runner, so `--restart unless-stopped` would provide little practical benefit
after the job terminates. On a persistent Docker deployment host, however,
adding a restart policy and retaining the previous known-good image for rollback
would be appropriate next improvements.