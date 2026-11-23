FROM ubuntu:22.04
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates libatomic1 libgcc-s1 libstdc++6 \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --uid 10001 gameserver
WORKDIR /app
COPY --chown=gameserver:gameserver . /app/
RUN chmod +x /app/KLTNServer.x86_64
USER gameserver
EXPOSE 7777/tcp
ENTRYPOINT ["/app/KLTNServer.x86_64", "-batchmode", "-nographics", "-logFile", "-"]
