# Technical Assessment Worklog

## Containerize, Scan, and Ship a .NET Service via GitHub Actions

## Overview

This worklog documents the implementation of the technical assessment, including commands executed, design decisions, validation results, failures encountered, and remediation steps.

The solution demonstrates:

- ASP.NET Core Web API development using .NET 8

- Application health and version endpoints

- NuGet dependency vulnerability scanning

- Docker containerization using a multi-stage build

- Non-root container execution

- Container image vulnerability scanning with Trivy

- CI/CD implementation using GitHub Actions

- GitHub Container Registry (GHCR) publishing

- Automated deployment from `main`

- Manual redeployment of an existing image

- Post-deployment health and version verification

---

## Environment

Development environment:

- Operating System: Windows

- .NET SDK: 8.0.303

- Git: 2.55.0.windows.5

- Docker: 27.2.0

- IDE: Visual Studio Code

- Application: ASP.NET Core Web API

- Target Framework: .NET 8

- CI/CD: GitHub Actions

- Container Registry: GitHub Container Registry (GHCR)

- Image Scanner: Trivy 0.74.0

Environment validation commands:

```cmd

dotnet --version

git --version

docker --version

docker info

```

Docker Desktop was configured to use the Linux/WSL2 container engine.

One environment observation was:

```text

WARNING: daemon is not using the default seccomp profile

```

This warning did not prevent the assessment implementation or container execution.

---

# Part 1 — Application Development

## Project Creation

Created an ASP.NET Core Web API targeting .NET 8.

The application is intentionally small, but it is a real ASP.NET Core service rather than a bare console "Hello World" application.

The service exposes two endpoints:

- `GET /health`

- `GET /version`

The application was built locally with:

```cmd

dotnet build

```

Result:

```text

Build succeeded.

0 Warning(s)

0 Error(s)

```

## Health Endpoint

The `/health` endpoint provides a lightweight application health response.

Implementation:

```csharp

app.MapGet("/health", () =>

{

    return Results.Ok(new { status = "healthy" });

});

```

Local verification:

```cmd

dotnet run

```

The application listened locally on:

```text

http://localhost:5111

```

Health verification:

```cmd

curl http://localhost:5111/health

```

Expected and observed response:

```json

{"status":"healthy"}

```

## Version Endpoint

The `/version` endpoint reads the `APP_VERSION` environment variable. If the variable is not defined, the application returns `dev`.

Implementation:

```csharp

app.MapGet("/version", () =>

{

    var version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";

    return Results.Ok(new { version });

});

```

Verification without `APP_VERSION`:

```cmd

curl http://localhost:5111/version

```

Response:

```json

{"version":"dev"}

```

The application was then run with a version value supplied through the environment:

```cmd

set APP_VERSION=1.0.0-test

dotnet run

```

Verification:

```cmd

curl http://localhost:5111/version

```

Response:

```json

{"version":"1.0.0-test"}

```

This endpoint later provides runtime confirmation that the expected container image tag was deployed.

---

# Part 2 — Dependency and Container Security

## Baseline Dependency Scan

Before intentionally adding a vulnerable dependency, I ran:

```cmd

dotnet list package --vulnerable --include-transitive

```

Result:

```text

The following sources were used:

   https://api.nuget.org/v3/index.json

The given project `OrderService` has no vulnerable packages given the current sources.

```

The original package references were:

```text

Project 'OrderService' has the following package references

   [net8.0]:

   Top-level Package                   Requested   Resolved

   > Microsoft.AspNetCore.OpenApi      8.0.7       8.0.7

   > Swashbuckle.AspNetCore            6.4.0       6.4.0

```

## Deliberately Introduced Vulnerable Dependency

To demonstrate that the dependency scanning process could detect a real application vulnerability, I deliberately added the older `Newtonsoft.Json` 9.0.1 package.

Command:

```cmd

dotnet add package Newtonsoft.Json --version 9.0.1

```

Package listing:

```cmd

dotnet list package

```

Result:

```text

Project 'OrderService' has the following package references

   [net8.0]:

   Top-level Package                   Requested   Resolved

   > Microsoft.AspNetCore.OpenApi      8.0.7       8.0.7

   > Newtonsoft.Json                   9.0.1       9.0.1

   > Swashbuckle.AspNetCore            6.4.0       6.4.0

```

The vulnerability scan was rerun:

```cmd

dotnet list package --vulnerable --include-transitive

```

It detected:

```text

Project `OrderService` has the following vulnerable packages

   [net8.0]:

   Top-level Package      Requested   Resolved   Severity   Advisory URL

   > Newtonsoft.Json      9.0.1       9.0.1      High       https://github.com/advisories/GHSA-5crp-9r3c-p9vr

```

This proved that the dependency scan could identify the deliberately introduced HIGH-severity vulnerability.

## Dockerfile Implementation

A multi-stage Dockerfile was created.

The first stage uses the .NET SDK image to restore and publish the application in Release mode.

The final stage uses the smaller ASP.NET runtime image and runs the application as a non-root user.

Dockerfile:

```dockerfile

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY OrderService.csproj ./

RUN dotnet restore

COPY . .

RUN dotnet publish \

    -c Release \

    -o /app/publish \

    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app

RUN apt-get update \

    && apt-get install -y --no-install-recommends curl \

    && rm -rf /var/lib/apt/lists/* \

    && adduser --disabled-password --gecos "" appuser

COPY --from=build /app/publish .

RUN chown -R appuser:appuser /app

USER appuser

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \

    CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "OrderService.dll"]

```

The build is framework-dependent because the application is published without a self-contained runtime and executes on the ASP.NET runtime image.

A `.dockerignore` file was also created:

```text

bin/

obj/

.git/

.github/

.vscode/

*.md

```

## Docker Engine Issue

The first Docker build attempt failed because the Docker Desktop Linux engine was not available.

Command:

```cmd

docker build -t orderservice:vulnerable .

```

Error:

```text

error during connect:

open //./pipe/dockerDesktopLinuxEngine:

The system cannot find the file specified.

```

I verified Docker after starting/configuring Docker Desktop:

```cmd

docker info

```

The environment then reported:

```text

Server Version: 27.2.0

Operating System: Docker Desktop

OSType: linux

Architecture: x86_64

Kernel: WSL2

```

The image build was then retried:

```cmd

docker build -t orderservice:vulnerable .

```

## Container Health Check Issue

The initial container health check used `wget`, but the ASP.NET runtime image did not contain that utility.

The application itself was reachable, but Docker marked the container unhealthy.

Inspection showed:

```text

/bin/sh: 1: wget: not found

```

I changed the runtime image configuration to install `curl` and changed the Docker `HEALTHCHECK` to call the `/health` endpoint with `curl`.

After rebuilding and restarting the container, Docker reported:

```text

orderservice:vulnerable

Up 2 minutes (healthy)

```

Installing `curl` slightly increases the runtime image/package surface, but it provides a simple executable health probe for this assessment.

## Vulnerable Container Image Scan

Trivy was installed and verified with:

```cmd

trivy --version

```

The deliberately vulnerable image was scanned before remediation:

```cmd

trivy image orderservice:vulnerable

```

Trivy scanned both operating-system packages and application dependencies.

The application-level result included:

```text

app/OrderService.deps.json (dotnet-core)

Total: 1

HIGH: 1

CRITICAL: 0

Library:            Newtonsoft.Json

Vulnerability:      CVE-2024-21907

Severity:           HIGH

Installed Version:  9.0.1

Fixed Version:      13.0.1

```

This demonstrated that the image scanner could identify the vulnerable .NET dependency inside the built container.

## Dependency Remediation

The vulnerable package was upgraded from 9.0.1 to 13.0.1:

```cmd

dotnet add package Newtonsoft.Json --version 13.0.1

```

The dependency scan was repeated:

```cmd

dotnet list package --vulnerable --include-transitive

```

The remediated project no longer reported the deliberately introduced vulnerable package.

The fixed image was rebuilt:

```cmd

docker build -t orderservice:fixed .

```

It was then rescanned:

```cmd

trivy image orderservice:fixed

```

## Final Trivy Security Gate

I ran the same style of HIGH/CRITICAL vulnerability gate used by the CI/CD pipeline:

```cmd

trivy image --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 orderservice:fixed

echo %ERRORLEVEL%

```

The filtered scan returned no blocking HIGH or CRITICAL findings and:

```text

0

```

The `--exit-code 1` option is important because a qualifying vulnerability causes the command to return a non-zero exit status, allowing the CI/CD pipeline to stop before publishing or deployment.

`--ignore-unfixed` prevents currently unfixable vulnerabilities from blocking the deployment. The security implications of this choice are discussed in Part 5.

## Fixed Container Runtime Verification

The remediated image was started with `APP_VERSION` supplied to the container:

```cmd

docker run -d --name orderservice-fixed -p 8080:8080 -e APP_VERSION=fixed orderservice:fixed

```

Verification:

```cmd

curl http://localhost:8080/health

curl http://localhost:8080/version

docker exec orderservice-fixed whoami

```

Results:

```json

{"status":"healthy"}

```

```json

{"version":"fixed"}

```

Non-root verification:

```text

appuser

```

Running as a non-root user reduces the privileges available to an attacker if the application process is compromised.

To support the non-root runtime, the Dockerfile:

1. Creates `appuser`.

2. Copies the published application into `/app`.

3. Changes ownership of `/app` to `appuser`.

4. Uses `USER appuser` before starting the application.

---

# Part 3 — GitHub Actions CI/CD Pipeline

## Pipeline Design

A single self-contained GitHub Actions workflow was created at:

```text

.github/workflows/ci.yml

```

The workflow supports:

- Pushes to `main`

- Pull requests targeting `main`

- Manual execution using `workflow_dispatch`

The normal CI/CD sequence is:

```text

Format Check

    |

    v

Build and Test

    |

    v

Dependency Scan

    |

    v

Docker Build + Trivy Gate + GHCR Push

    |

    v

Deploy

```

Explicit `needs:` dependencies are used so downstream stages do not execute when a required upstream gate fails.

Publishing and automatic deployment occur only for a direct push to `main`.

## Format Gate

The first job validates source formatting with:

```cmd

dotnet format --verify-no-changes --no-restore

```

An `.editorconfig` was added to establish consistent source formatting:

```ini

root = true

[*.cs]

indent_style = space

indent_size = 4

charset = utf-8

end_of_line = lf

insert_final_newline = true

dotnet_sort_system_directives_first = true

dotnet_separate_import_directive_groups = false

csharp_new_line_before_open_brace = all

csharp_indent_case_contents = true

csharp_indent_switch_labels = true

```

### Formatting Failure and Remediation

The first GitHub Actions execution failed during the Format Check job.

GitHub Actions reported:

```text

Program.cs(14,11): error FINALNEWLINE:

Fix final newline. Insert '\n'.

Process completed with exit code 2.

```

After correcting the final newline and converting `Program.cs` to LF line endings, I verified the formatting locally:

```cmd

dotnet format --verify-no-changes --no-restore

echo %ERRORLEVEL%

```

The command returned:

```text

0

```

The CI formatting gate then passed.

## GitHub Workflow Authentication Issue

The initial attempt to push `.github/workflows/ci.yml` was rejected because the cached Personal Access Token did not have permission to create or modify GitHub Actions workflows.

GitHub returned:

```text

refusing to allow a Personal Access Token to create or update workflow

`.github/workflows/ci.yml` without `workflow` scope

```

The cached credential was removed:

```cmd

cmdkey /delete:git:https://github.com

```

I then authenticated using the appropriate GitHub browser authentication flow and successfully pushed the workflow.

This was a useful reminder to avoid unnecessarily broad long-lived credentials and to use the platform's supported authentication mechanisms.

## Build and Test Stage

A separate Build and Test job was added with an explicit dependency on the formatting gate:

```yaml

needs: format

```

The job restores dependencies, builds the application in Release configuration, and runs a dedicated xUnit integration test project:

```text

tests/OrderService.Tests

```

The test project uses `Microsoft.AspNetCore.Mvc.Testing` and `WebApplicationFactory<Program>` to start the actual ASP.NET Core application in memory and exercise its HTTP endpoints.

Two integration tests were added:

- `GET /health` verifies HTTP 200 and confirms the response reports `status = healthy`.

- `GET /version` verifies HTTP 200 and confirms the default version is `dev` when `APP_VERSION` is not defined.

To make the application entry point accessible to the integration test host, the following declaration was added to the end of `Program.cs`:

```csharp

public partial class Program

{

}

```

Because the application project is located at the repository root, test source files are excluded from the main application's compilation:

```xml

<ItemGroup>

  <Compile Remove="tests/**/*.cs" />

</ItemGroup>

```

Local test execution:

```cmd

dotnet restore tests\OrderService.Tests\OrderService.Tests.csproj

dotnet test tests\OrderService.Tests\OrderService.Tests.csproj

```

Observed result:

```text

Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2

```

The GitHub Actions Build and Test job explicitly runs the integration test project in Release configuration:

```yaml

- name: Run integration tests

  run: dotnet test tests/OrderService.Tests/OrderService.Tests.csproj --configuration Release --no-restore

```

This prevents downstream security, publishing, and deployment work when the application build or automated endpoint tests fail.

### Integration Test CI Restore Failure and Resolution

After the dedicated `OrderService.Tests` integration test project was added, the first GitHub Actions run of the enhanced Build and Test job failed.

The workflow attempted to execute:

```bash

dotnet test tests/OrderService.Tests/OrderService.Tests.csproj --configuration Release --no-restore

```

GitHub Actions returned:

```text

error NETSDK1004: Assets file

'/home/runner/work/OrderService-Technical-Assessment/OrderService-Technical-Assessment/tests/OrderService.Tests/obj/project.assets.json'

not found. Run a NuGet package restore to generate this file.

Process completed with exit code 1.

```

#### Root Cause

The Build and Test job originally ran:

```bash

dotnet restore

```

from the repository root. After introducing the separate integration test project, that restore covered the main `OrderService.csproj` but did not generate the test project's `obj/project.assets.json`.

The subsequent test command intentionally used `--no-restore`, so the test project could not resolve its NuGet dependency graph and the job failed with `NETSDK1004`.

This was a CI restore-order/configuration issue rather than a failure of either integration test. Both tests had already passed locally.

#### Resolution

The Build and Test restore step was changed to explicitly restore the integration test project:

```yaml

- name: Restore application and test dependencies

  run: dotnet restore tests/OrderService.Tests/OrderService.Tests.csproj

```

`OrderService.Tests.csproj` contains a project reference to the main `OrderService.csproj`, so restoring the test project restores the dependency graph required by both projects.

The corrected Build and Test sequence is:

```text

Restore application and test dependencies

        |

        v

Build application

        |

        v

Run integration tests

```

The relevant workflow steps are:

```yaml

- name: Restore application and test dependencies

  run: dotnet restore tests/OrderService.Tests/OrderService.Tests.csproj

- name: Build application

  run: dotnet build --configuration Release --no-restore

- name: Run integration tests

  run: dotnet test tests/OrderService.Tests/OrderService.Tests.csproj --configuration Release --no-restore

```

This failure reinforced an important CI/CD behavior: using `--no-restore` avoids redundant package restoration, but every project that will be built or tested must first participate in a restore operation. A successful local test does not guarantee that a fresh GitHub-hosted runner has the same generated NuGet assets, so the CI workflow must explicitly create them.



### Second CI Restore Failure — Fix Applied to Wrong Job

After identifying the missing test-project restore as the cause of the first integration-test CI failure, an initial remediation was committed. However, the subsequent GitHub Actions run failed with the same `NETSDK1004` error.

The repeated failure showed that the intended correction had not actually been applied to the Build and Test job.

#### Root Cause

Inspection of `.github/workflows/ci.yml` showed that the explicit test-project restore had been added to the Format Check job:

```yaml

- name: Restore application and test dependencies

  run: dotnet restore tests/OrderService.Tests/OrderService.Tests.csproj

```

However, the Build and Test job still contained:

```yaml

- name: Restore dependencies

  run: dotnet restore

```

followed by:

```yaml

- name: Run integration tests

  run: dotnet test tests/OrderService.Tests/OrderService.Tests.csproj --configuration Release --no-restore

```

Because GitHub-hosted jobs execute on separate fresh runners, restoring the test project during the Format Check job did not make the generated NuGet assets available to the Build and Test job.

The Build and Test runner therefore still lacked:

```text

tests/OrderService.Tests/obj/project.assets.json

```

when `dotnet test --no-restore` executed.

#### Resolution

The Format Check job was returned to its normal application restore:

```yaml

- name: Restore dependencies

  run: dotnet restore

```

The Build and Test job was then explicitly corrected to restore the integration test project:

```yaml

- name: Restore application and test dependencies

  run: dotnet restore tests/OrderService.Tests/OrderService.Tests.csproj

```

The final Build and Test sequence became:

```yaml

- name: Restore application and test dependencies

  run: dotnet restore tests/OrderService.Tests/OrderService.Tests.csproj

- name: Build application

  run: dotnet build --configuration Release --no-restore

- name: Run integration tests

  run: dotnet test tests/OrderService.Tests/OrderService.Tests.csproj --configuration Release --no-restore

```

Before committing the correction, the workflow was inspected with:

```cmd

findstr /n /c:"Restore application and test dependencies" .github\workflows\ci.yml

```

The result confirmed that the explicit test-project restore appeared only in the Build and Test job:

```text

221:      - name: Restore application and test dependencies

```

A `git diff` was also reviewed to verify that the change removed the test-project restore from Format Check and added it to Build and Test.

The corrected workflow was committed as:

```text

5646033 Fix test project restore in build job

```

#### Lesson Learned

This failure demonstrated an important property of GitHub Actions: each job runs on an isolated runner. Files generated during a restore in one job are not automatically available to another job.

It also reinforced the importance of inspecting the actual workflow diff before pushing a CI/CD correction. The first diagnosis was correct, but the change had been applied to the wrong job. Verifying the exact YAML location before the second commit ensured the correction was applied to the Build and Test job.

#### Final CI Validation

After correcting the restore step and pushing commit `5646033`, GitHub Actions run #20 completed successfully.

The complete pipeline passed:

```text

Format Check                         PASSED

Build and Test                       PASSED

Dependency Scan                      PASSED

Docker Build / Trivy / GHCR Push     PASSED

Deploy                               PASSED

```

The Build and Test job successfully restored the integration test project and executed:

```bash

dotnet test tests/OrderService.Tests/OrderService.Tests.csproj --configuration Release --no-restore

```

GitHub Actions reported:

```text

Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2

```

This confirmed that the restore correction resolved the `NETSDK1004` failure and that both integration tests execute successfully on a clean GitHub-hosted runner.

The successful run also confirmed that the downstream dependency scan, container build, Trivy security gate, GHCR publishing, and deployment stages continued to operate correctly after the integration tests were introduced.

## Dependency Vulnerability Scan

A dedicated dependency scanning job runs:

```cmd

dotnet list package --vulnerable --include-transitive

```

This stage is treated as a reporting step for dependency visibility.

The blocking image-security decision is made later by the Trivy container scan.

## Docker Build and Trivy Security Gate

The container-security job:

1. Builds the Docker image.

2. Runs the Trivy HIGH/CRITICAL gate.

3. Authenticates to GHCR when appropriate.

4. Pushes the already-scanned image on `main`.

Images are tagged using the Git commit SHA.

Local image pattern:

```text

orderservice:<git-sha>

```

Published image pattern:

```text

ghcr.io/shrogers45/orderservice:<git-sha>

```

The Trivy gate uses:

```text

--severity HIGH,CRITICAL --ignore-unfixed --exit-code 1

```

If the security gate fails, downstream publishing/deployment is prevented.

## GHCR Publishing

After the image passes the Trivy gate, the `main`-branch workflow authenticates to GitHub Container Registry and publishes the SHA-tagged image.

Image format:

```text

ghcr.io/shrogers45/orderservice:${{ github.sha }}

```

Using the immutable Git SHA as the image tag provides traceability between:

- Source revision

- GitHub Actions run

- Container image

- Deployed application version

The workflow uses the GitHub-provided `GITHUB_TOKEN` rather than embedding a registry password in source control.

## NuGet Caching

NuGet package caching was added to the Format Check, Build and Test, and Dependency Scan jobs using `actions/cache@v4`.

Cache path:

```text

~/.nuget/packages

```

Cache key:

```text

${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}

```

A restore key is also used:

```text

${{ runner.os }}-nuget-

```

Caching is implemented as a step within each .NET job rather than as a separate job because GitHub-hosted jobs run on separate ephemeral runners and do not automatically share local filesystems.

The cache reduces repeated NuGet downloads across workflow runs while invalidating appropriately when project package references change.

## Pipeline Validation

After the CI/CD stages were implemented, the normal push-to-main workflow completed successfully.

Validated stages included:

```text

Format Check                              PASSED

Build and Test                            PASSED

Dependency Scan                           PASSED

Docker Build / Trivy / GHCR Publishing    PASSED

Deploy                                    PASSED

```

This demonstrates the complete gated path from source validation through deployment.

## Manual Redeploy Validation

The workflow also supports `workflow_dispatch` so a previously built, scanned, and published image can be redeployed without rebuilding it.

During manual redeployment:

```text

Format Check                              SKIPPED

Build and Test                            SKIPPED

Dependency Scan                           SKIPPED

Docker Build / Trivy / GHCR Publishing    SKIPPED

Deploy                                    PASSED

```

The Deploy job reads the supplied existing GHCR image tag, pulls that image, starts the container, and verifies both `/health` and `/version`.

The normal push-to-main pipeline was retested after adding the manual deployment path and continued to pass.

## Pipeline Diagram

A separate pipeline diagram was added to the repository and referenced from `PIPELINE.md`.

The diagram shows:

- Normal push/PR triggers

- Manual `workflow_dispatch`

- Jobs

- `needs:` dependencies

- Blocking gates

- Report-only dependency scanning

- GHCR publishing

- Main-only deployment

- Manual redeploy-only behavior

---

# Part 4 — Deployment

## Deployment Approach

The deployment stage is implemented as a separate GitHub Actions job after the container security and publishing stage.

For a normal push to `main`, the Deploy job pulls the exact SHA-tagged image that was built, scanned by Trivy, and published to GHCR.

The assessment does not require a permanent remote deployment target, so the GitHub-hosted Actions runner is used as the deployment target. This demonstrates the complete deployment procedure while keeping the assessment self-contained.

Container image format:

```text

ghcr.io/shrogers45/orderservice:<git-sha>

```

The Git SHA provides traceability between the source revision and the running application.

## Deployment Commands

The equivalent deployment commands are:

```bash
# Authenticate to GitHub Container Registry.
echo "${GITHUB_TOKEN}" | docker login ghcr.io \
  -u "${GITHUB_ACTOR}" \
  --password-stdin

# Pull the exact image that passed the CI/CD security gate.
docker pull "ghcr.io/shrogers45/orderservice:${DEPLOY_TAG}"

# Stop and remove an existing container with the same name.
docker rm -f orderservice || true

# Start the selected image.
docker run -d \
  --name orderservice \
  -p 8080:8080 \
  -e APP_VERSION="${DEPLOY_TAG}" \
  "ghcr.io/shrogers45/orderservice:${DEPLOY_TAG}"

# Verify application health.
curl --fail --silent --show-error \
  http://localhost:8080/health

# Verify the running application version.
curl --fail --silent --show-error \
  http://localhost:8080/version
```

The workflow also verifies that `/version` contains the expected deployment tag:

```bash

VERSION_RESPONSE=$(curl --fail --silent --show-error \
  http://localhost:8080/version)

echo "Application response: ${VERSION_RESPONSE}"

echo "${VERSION_RESPONSE}" | grep "${DEPLOY_TAG}"

```

A mismatch returns a non-zero status and fails the deployment job.

## Idempotent Container Replacement

Docker does not allow a new container to use a name that is already assigned to another container.

The assessment suggests:

```bash

docker stop orderservice 2>/dev/null

docker rm orderservice 2>/dev/null

```

The implemented workflow uses:

```bash

docker rm -f orderservice || true

```

`docker rm -f` combines the stop and remove operations.

`|| true` allows a first-time deployment to continue when an `orderservice` container does not already exist.

This makes repeated deployments safe from container-name collisions.

## Deployment Verification

Health verification:

```bash

curl --fail --silent --show-error \

  http://localhost:8080/health

```

Expected response:

```json

{"status":"healthy"}

```

Version verification:

```bash

curl --fail --silent --show-error \

  http://localhost:8080/version

```

Expected format:

```json

{"version":"<deployed-image-tag>"}

```

Because `APP_VERSION` is set to `${DEPLOY_TAG}` during `docker run`, the `/version` endpoint confirms that the intended image tag is actually running.

## Manual Redeployment

Images published by the normal pipeline use the Git commit SHA as the image tag.

To identify the current Git SHA locally:

```cmd

git rev-parse HEAD

```

The image tag can also be identified from a successful GitHub Actions run or the published GHCR package versions.

Manual redeployment procedure:

1. Open the GitHub repository.

2. Select **Actions**.

3. Select **OrderService CI/CD**.

4. Select **Run workflow**.

5. Select the `main` branch.

6. Enter an existing Git SHA in **Existing GHCR image tag (Git SHA) to redeploy**.

7. Select **Run workflow**.

During `workflow_dispatch`, Jobs 1–4 are intentionally skipped and only Deploy executes.

The existing image is pulled directly from GHCR rather than rebuilt or republished.

The manual redeployment was tested successfully. The Deploy job pulled the selected image, started the container, verified `/health`, and confirmed through `/version` that the requested image tag was running.

## Deployment Risk and Mitigation

The assessment deployment currently starts the container with:

```bash

docker run -d \

  --name orderservice \

  -p 8080:8080 \

  -e APP_VERSION="${DEPLOY_TAG}" \

  "ghcr.io/shrogers45/orderservice:${DEPLOY_TAG}"

```

There is no Docker restart policy.

On a persistent Docker host, if the application process terminates unexpectedly, the container remains stopped until something explicitly restarts it.

The smallest improvement is to add a restart policy:

```bash

docker run -d \

  --restart unless-stopped \

  --name orderservice \

  -p 8080:8080 \

  -e APP_VERSION="${DEPLOY_TAG}" \

  "ghcr.io/shrogers45/orderservice:${DEPLOY_TAG}"

```

This improves recovery from process or host restarts.

However, a restart policy is not a rollback mechanism.

The current deployment removes the previous container before the new container has passed health and version verification:

```bash

docker rm -f orderservice || true

```

If the new image fails `/health`, the previous known-good container is already gone, which can result in downtime.

A small rollback improvement would be to retain the previous known-good image tag. If the new image fails `/health` or `/version`, the failed container could be removed and the previous image restarted.

Conceptually:

```bash

docker rm -f orderservice || true

docker run -d \

  --restart unless-stopped \

  --name orderservice \

  -p 8080:8080 \

  -e APP_VERSION="${PREVIOUS_TAG}" \

  "ghcr.io/shrogers45/orderservice:${PREVIOUS_TAG}"

```

For this assessment, deployment occurs on an ephemeral GitHub-hosted Actions runner, so a restart policy provides little practical value after the job terminates. On a persistent Docker host, restart behavior and rollback to a known-good image would be appropriate next improvements.

---

# Part 5 — Written Questions

## 1. Could Format Check, Test, and Security Scan Run in Parallel?

Yes. Formatting, testing, and some security analysis are largely independent and could run concurrently. The main benefit would be shorter overall CI execution time because GitHub Actions could perform several validations at the same time.

For this assessment, I intentionally used a sequential pipeline with explicit `needs:` dependencies:

```text

Format Check -> Build and Test -> Dependency Scan -> Container Security

```

The advantage is clear fail-fast behavior. If formatting or compilation fails, the pipeline does not spend additional runner time building and scanning a container that cannot be released.

The trade-off is pipeline duration. In a larger production pipeline, I would consider running independent checks such as formatting, testing, and dependency analysis in parallel and then have the image build/publish stage depend on all required gates. This would improve speed while still preventing release when a required check fails.

## 2. Where Do Secrets Live, and What Is the Blast Radius if One Leaks?

Registry credentials are not stored directly in the repository or workflow YAML. Authentication to GHCR uses the GitHub-provided `${{ secrets.GITHUB_TOKEN }}`, which is supplied to the registry login action during the workflow run.

The workflow grants the permissions required for repository/package operations, including:

```yaml

contents: read

packages: write

```

If a credential were accidentally exposed in a log, the blast radius would depend on that credential's permissions. A token with package write access could potentially be used to modify or publish packages within the scope authorized to that token.

I would reduce this risk by continuing to use short-lived GitHub Actions credentials instead of long-lived secrets where possible, never printing credentials to logs, and applying least privilege. A further improvement would be to scope `packages: write` only to the job that actually publishes the container rather than granting it workflow-wide.

## 3. What Is the Gap Created by `--ignore-unfixed`?

The Trivy gate blocks HIGH and CRITICAL vulnerabilities with available fixes while ignoring findings that currently have no upstream fix.

This creates a time-based security gap. An image may pass today because a HIGH-severity base-image CVE has no fix. If a fix becomes available next month but no source change causes the pipeline to run again, the previously published image is not automatically rescanned.

I would close this gap by adding a scheduled GitHub Actions security workflow that periodically rescans the published production image, for example daily or weekly. The scheduled scan could alert or fail when a previously unfixable HIGH or CRITICAL vulnerability becomes fixable.

I would also regularly rebuild the application against updated base images so operating-system and runtime security fixes are incorporated even when application source code has not changed.

## 4. What Is the Next Step for Three Replicas Behind a Load Balancer?

A single `docker run` command is appropriate for this assessment, but it is not an appropriate mechanism for managing multiple application replicas.

For three replicas behind a load balancer, the next step is to introduce a deployment/orchestration layer that can define the desired replica count, perform health checks, provide service discovery, distribute traffic, and replace unhealthy instances.

For a small/simple environment, Docker Compose together with a reverse proxy could describe the services and load-balancing layer. For a production environment requiring scaling, rolling deployments, self-healing, and stronger availability guarantees, I would use Kubernetes or an appropriate managed container orchestration service.

I would explicitly not try to solve replica count, load balancing, service discovery, failover, rolling deployment, or health-based replacement by changing the Dockerfile. The Dockerfile defines how one application image is built and how one container runs. Deployment topology and orchestration belong in the deployment/platform layer.

---

# Final Validation Summary

The completed implementation demonstrates:

- .NET 8 ASP.NET Core service

- `/health` endpoint

- `/version` endpoint using `APP_VERSION`

- Baseline NuGet vulnerability scanning

- Deliberately introduced HIGH-severity dependency

- Detection of the vulnerable dependency

- Dependency remediation

- Multi-stage Docker build

- Framework-dependent Release publish

- Non-root runtime execution

- Docker health check

- Trivy image scanning

- HIGH/CRITICAL CI security gate

- GitHub Actions formatting gate

- Build and test stage with two passing xUnit integration tests validated locally and in GitHub Actions

- Dependency scan/report

- Explicit `needs:` job dependencies

- NuGet package caching

- GHCR image publishing on `main`

- SHA-based image tagging

- Main-only automatic deployment

- Idempotent container replacement

- `/health` deployment validation

- `/version` image-tag verification

- Manual redeployment using `workflow_dispatch`

- Pipeline diagram

- Deployment risk and mitigation analysis

- CI/CD architecture and security trade-off discussion

The implementation intentionally favors traceability, explicit gating, least-privilege container execution, reproducible image identification, and clear documentation of failures and engineering decisions.
