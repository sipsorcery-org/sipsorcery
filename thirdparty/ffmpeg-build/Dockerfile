# Use Ubuntu 24.04 as the base image
FROM ubuntu:24.04

# Set environment variables to non-interactive mode to avoid prompts during installation
ENV DEBIAN_FRONTEND=noninteractive

# Install dependencies for FFmpeg build
RUN apt-get update && apt-get install -y \
    autoconf \
    automake \
    build-essential \
    curl \
    git \
    libass-dev \
    libfdk-aac-dev \
    libmp3lame-dev \
    libtheora-dev \
    libtool \
    libvpx-dev \
    libsdl2-dev \
    libx264-dev \
    libx265-dev \
    pkg-config \
    texinfo \
    wget \
    yasm \
    zlib1g-dev \
    libopus-dev \
    libfreetype6-dev \
    libvorbis-dev \
    libxvidcore-dev \
    libavutil-dev \
    libssl-dev \
    libavdevice-dev \
    && rm -rf /var/lib/apt/lists/*

# Set the working directory for the build
WORKDIR /ffmpeg

# Download FFmpeg source code for version 7.0 "Dijkstra"
RUN git clone --branch n7.0 https://git.ffmpeg.org/ffmpeg.git .

# Configure the build with required options, disable all device options as none available in a docker container.
RUN ./configure \
    --prefix=/usr/local \
    --enable-gpl \
    --enable-nonfree \
    --enable-libass \
    --enable-libfdk-aac \
    --enable-libmp3lame \
    --enable-libtheora \
    --enable-libvpx \
    --enable-libx264 \
    --enable-libx265 \
    --enable-openssl \
    --enable-libopus \
    --enable-libvorbis \
    --enable-libfreetype \
    --enable-libxvid \
    --enable-avdevice \
    --enable-shared \
    --disable-debug \
    --disable-doc \
    --disable-doc \
    --disable-vaapi \
    --disable-vdpau \
    --disable-ffplay \
    --disable-indevs \
    --disable-outdevs
	
# Build FFmpeg
RUN make -j$(nproc)

# Install ffmpeg programs to /usr/local and shared libraries ot /usr/local/lib
RUN make install

# Clean up build dependencies to reduce image size
RUN apt-get purge -y --auto-remove \
    autoconf \
    automake \
    build-essential \
    curl \
    git \
    libtool \
    texinfo \
    wget \
    yasm

# Clean up the downloaded source code
RUN rm -rf /ffmpeg

# Set the entrypoint to ffmpeg
ENTRYPOINT ["ffmpeg"]

# Verify the installation
CMD ["-version"]
