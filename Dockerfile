# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
# The build runs on the builder's own platform: the published app is portable (no native host), so the arm64 image needs no emulation here.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG Channel=local
ARG BuildNumber=0
ARG BuildDate=
WORKDIR /src

# Restore first (better layer caching).
COPY ["global.json", "Directory.Build.props", "./"]
COPY ["src/MatNote/MatNote.csproj", "src/MatNote/"]
RUN dotnet restore "src/MatNote/MatNote.csproj"

COPY src src
# Fall back to today's date when BuildDate is not supplied, so the version never ends in "-".
RUN BUILD_DATE="${BuildDate:-$(date -u +%Y%m%d)}" && \
    dotnet publish "src/MatNote/MatNote.csproj" \
    -c Release -o /app/publish --no-restore \
    -p:UseAppHost=false \
    -p:Channel=$Channel -p:BuildNumber=$BuildNumber -p:BuildDate=$BUILD_DATE

# ---------------------------------------------------------------------------
# Runtime stage (ASP.NET Core base image)
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# postgresql-client-17: pg_dump / pg_restore for the backups. The base image is Ubuntu, whose own package is version 16, and pg_dump refuses a server that is newer than itself:
# the client comes from the PostgreSQL project's repository (PGDG), like the server image (postgres:17). fontconfig + fonts: the PDF export draws text.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && install -d /usr/share/postgresql-common/pgdg \
    && curl -fsSL -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc https://www.postgresql.org/media/keys/ACCC4CF8.asc \
    && . /etc/os-release \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client-17 libfontconfig1 fonts-dejavu-core fonts-liberation fonts-droid-fallback \
    && apt-get purge -y --auto-remove curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

ENV ASPNETCORE_URLS=http://+:2301 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
    DOTNET_TieredPGO=1 \
    MATNOTE_DATA=/data

EXPOSE 2301

COPY --from=build /app/publish .

# One volume for everything the app keeps in files: the JSON configuration, the keys that protect sessions and secrets, temporary files.
# The database is the other container. The unprivileged user of the image may write to /data.
RUN mkdir -p /data /backups && chown $APP_UID:$APP_UID /data /backups
VOLUME /data
USER $APP_UID

# The app checks itself, so the image needs no extra tools.
HEALTHCHECK --interval=30s --timeout=8s --start-period=30s --retries=3 \
    CMD ["dotnet", "MatNote.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "MatNote.dll"]
