#!/bin/sh
set -e
PUID=${PUID:-913}
usermod -u "$PUID" "freshonion" >/dev/null
PGID=${PGID:-913}
groupmod -g "$PGID" "freshonion" >/dev/null
APP_PATH=${APP_PATH:-/app}

BASEPATH=/freshonion
# create folders
if [ ! -d "${BASEPATH}" ]; then \
    mkdir -p "${BASEPATH}"
    chown -R "$PUID:$PGID" "${BASEPATH}"
fi

# check permissions
if [ ! "$(stat -c %u "${BASEPATH}")" = "$PUID" ]; then
	echo "Change in ownership detected, please be patient while we chown existing files ..."
	chown "$PUID:$PGID" -R "${BASEPATH}"
fi

renice "+${NICE_ADJUSTEMENT:-1}" $$ >/dev/null 2>&1 || :
exec ionice -c "${IONICE_CLASS:-3}" -n "${IONICE_CLASSDATA:-7}" -t su-exec "$PUID:$PGID" "dotnet" "${APP_PATH}/FreshOnion.dll" $@