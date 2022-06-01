FROM mcr.microsoft.com/dotnet/runtime:6.0-alpine AS base
WORKDIR /app

FROM base AS final
ARG TARGETPLATFORM
ARG maintainer="maxisoft"
ARG uid=914 # the User id used to run program / create new output files
ARG gid=914
ARG APP_PATH=/app
ARG username=freshonion
ARG groupname=freshonion
LABEL maintainer="${maintainer}" name="FreshOnion" url="https://github.com/${maintainer}/FreshOnion" vcs-url="https://github.com/${maintainer}/FreshOnion" org.opencontainers.image.source="https://github.com/${maintainer}/FreshOnion"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_RUNNING_IN_CONTAINER=true PUID=$uid PGID=$gid IS_DOCKER=1 NICE_ADJUSTEMENT=5 IONICE_CLASS=3 IONICE_CLASSDATA=7 APP_PATH=${APP_PATH} FRESHONION_TOR__PATH=/usr/bin/tor
RUN \
    apk add --no-cache su-exec shadow icu-libs tor && \
    addgroup --system --gid $gid $groupname && \
    adduser --system --uid $uid --ingroup $groupname --shell /bin/sh $username && \
    mkdir -p "$APP_PATH" && \
    chown -R $uid:$gid "$APP_PATH"
VOLUME [ "/freshonion" ]
WORKDIR "/freshonion"
COPY publish/$TARGETPLATFORM ${APP_PATH}
ADD --chown=$uid:$gid ./docker/start_freshonion.sh ${APP_PATH}/start_freshonion.sh
EXPOSE 9050 9051 9052
ENTRYPOINT [ "/bin/ash", "/app/start_freshonion.sh" ]
