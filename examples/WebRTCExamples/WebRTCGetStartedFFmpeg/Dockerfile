# Stage 1: Build FFmpeg Image
FROM sipsorcery/ffmpegbuild:7.0 AS ffmpeg

# Stage 2: Base Image - Install FFmpeg dependencies (This will be cached)
FROM ubuntu:24.04 AS base

ENV DEBIAN_FRONTEND=noninteractive

# Install the required libraries for FFmpeg in the final image (as root)
RUN apt-get update && apt-get install -y \
    libdrm2 \
    libsdl2-2.0-0 \
    libsndio7.0 \
    libxvidcore4 \
    libxv1 \
    libass9 \
    libvpx-dev \
    libsdl2-dev \
    libx264-dev \
    libx265-dev \
    libopus-dev \
    libfreetype6-dev \
    libvorbis-dev \
    libxvidcore-dev \
    libavutil-dev \
    libssl-dev \
    libavdevice-dev \
    libfdk-aac-dev \
    aspnetcore-runtime-8.0 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
EXPOSE 8081

# Stage 3: Build .NET Application (Only rebuilds if source code changes)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Add local NuGet source (replace with your actual package version)
#RUN mkdir -p /local-nuget
#COPY ./local-nuget/*.nupkg /local-nuget/
#RUN dotnet nuget add source /local-nuget --name local

COPY [".", "."]

# Publish the application
FROM build AS publish
RUN dotnet publish "./WebRTCFFmpegGetStarted.csproj" -c $BUILD_CONFIGURATION -o /app/publish

# Stage 4: Final Image to Run the App
FROM base AS final

WORKDIR /app

# Copy the published app from the build image
COPY --from=publish /app/publish .

# Copy FFmpeg binaries and libraries from the FFmpeg build image
COPY --from=ffmpeg /usr/local/bin/ffmpeg /usr/local/bin/
COPY --from=ffmpeg /usr/local/bin/ffprobe /usr/local/bin/
COPY --from=ffmpeg /usr/local/lib/libavcodec.so.61.3.100 /usr/local/lib/
COPY --from=ffmpeg /usr/local/lib/libavdevice.so.61.1.100 /usr/local/lib/
COPY --from=ffmpeg /usr/local/lib/libavfilter.so.10.1.100 /usr/local/lib/
COPY --from=ffmpeg /usr/local/lib/libavformat.so.61.1.100 /usr/local/lib/
COPY --from=ffmpeg /usr/local/lib/libavutil.so.59.8.100 /usr/local/lib/
COPY --from=ffmpeg /usr/local/lib/libpostproc.so.58.1.100 /usr/local/lib/
COPY --from=ffmpeg /usr/local/lib/libswresample.so.5.1.100 /usr/local/lib/
COPY --from=ffmpeg /usr/local/lib/libswscale.so.8.1.100 /usr/local/lib/

# Update library links
RUN ldconfig

# Ensure FFmpeg is available in the PATH for your app
ENV PATH="/usr/local/bin:${PATH}"

# Set entrypoint to run the .NET application
ENTRYPOINT ["dotnet", "WebRTCFFmpegGetStarted.dll"]
