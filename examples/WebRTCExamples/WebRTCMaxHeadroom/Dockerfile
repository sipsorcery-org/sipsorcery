# syntax=docker/dockerfile:1.7

ARG DOTNET_RUNTIME_VERSION=8.0
ARG DOTNET_SDK_VERSION=10.0

# FFmpeg.AutoGen 8.1 binds to libavcodec.so.62 (FFmpeg 8). Debian Bookworm's
# ffmpeg package is 5.x, so use a matching, checksum-verified shared build.
FROM debian:bookworm-slim AS ffmpeg
ARG FFMPEG_URL=https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-06-30-13-34/ffmpeg-n8.1.2-21-gce3c09c101-linux64-lgpl-shared-8.1.tar.xz
ARG FFMPEG_SHA256=bc60572d89533c7987e7cfbb654eba4d35122a77885c6938483dd7ef8219cde9
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl xz-utils \
    && rm -rf /var/lib/apt/lists/* \
    && curl --fail --location --retry 3 "$FFMPEG_URL" --output /tmp/ffmpeg.tar.xz \
    && echo "$FFMPEG_SHA256  /tmp/ffmpeg.tar.xz" | sha256sum --check --strict \
    && mkdir /opt/ffmpeg \
    && tar --extract --xz --file /tmp/ffmpeg.tar.xz --strip-components=1 --directory /opt/ffmpeg \
    && test -f /opt/ffmpeg/lib/libavcodec.so.62 \
    && rm /tmp/ffmpeg.tar.xz

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
WORKDIR /src
COPY . .
RUN dotnet restore examples/WebRTCExamples/WebRTCMaxHeadroom/WebRTCMaxHeadroom.csproj \
        --runtime linux-x64 \
    && dotnet publish examples/WebRTCExamples/WebRTCMaxHeadroom/WebRTCMaxHeadroom.csproj \
        --configuration Release \
        --runtime linux-x64 \
        --self-contained false \
        --no-restore \
        --output /app/publish \
        /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION}-bookworm-slim AS runtime

# SkiaSharp, sherpa-onnx, onnxruntime and llama.cpp use these native libraries.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        libfontconfig1 \
        libfreetype6 \
        libglib2.0-0 \
        libgomp1 \
        libjpeg62-turbo \
        libpng16-16 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=ffmpeg /opt/ffmpeg /opt/ffmpeg
COPY --from=build --chown=app:app /app/publish /app

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_EnableDiagnostics=0 \
    PATH=/opt/ffmpeg/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin \
    LD_LIBRARY_PATH=/opt/ffmpeg/lib:/app \
    SHERPA_MODEL_DIR=/models/sherpa-tts/vits-piper-en_US-ryan-high \
    SHERPA_STT_DIR=/models/sherpa-stt/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8 \
    SHERPA_STT_PROVIDER=cpu \
    LLM_GGUF=/models/llm/Llama-3.2-3B-Instruct-Q4_K_M.gguf \
    LLM_GPU_LAYERS=0 \
    WAV2LIP_ONNX=/models/wav2lip/wav2lip-onnx/checkpoints/wav2lip_gan.onnx \
    NEURAL_PERSONA=/models/wav2lip/persona.jpg \
    NEURAL_MATTE=/models/wav2lip/persona_alpha.png

WORKDIR /app
EXPOSE 8080
VOLUME ["/models"]
HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=3 \
    CMD curl --fail --silent http://127.0.0.1:8080/healthz > /dev/null || exit 1

USER app
ENTRYPOINT ["dotnet", "WebRTCMaxHeadroom.dll"]
