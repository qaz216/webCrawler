#!/bin/sh
# Runs the .NET test suite inside a Linux SDK container (like CI). Useful on machines where local
# policy (e.g. Windows Smart App Control) blocks freshly built test assemblies.
# Testcontainers reaches the host's Docker through the mounted socket.
#
# Usage (from the repo root):
#   docker run --rm -v "$PWD:/src:ro" -v /var/run/docker.sock:/var/run/docker.sock \
#     -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
#     mcr.microsoft.com/dotnet/sdk:8.0 sh /src/scripts/test-in-docker.sh
set -e
mkdir -p /work
cd /src
cp -r global.json Directory.Build.props WebCrawler.sln src tests /work/
cd /work
find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
dotnet test WebCrawler.sln --nologo "$@"
