# WebRTC Lightning Payment Video Stream Demo

This demo project showcases a real-time WebRTC video stream whose source is controlled by Bitcoin Lightning payments. Viewers can trigger changes in the video stream by making micropayments over the Lightning Network.

## Overview

This project integrates two cutting-edge technologies:
- **WebRTC Video Streaming:** Enabling low-latency, real-time video communications.
- **Bitcoin Lightning Payments:** Allowing instant, low-cost micropayments that trigger changes in the video source.

When a viewer makes a payment, the system processes and verifies the Lightning payment. Once confirmed, the video source is updated accordingly—demonstrating a pay-per-view or dynamic content control model.

## Features

- **Real-Time Video Streaming:** Built using WebRTC for low-latency video delivery.
- **Micropayment Control:** Use Bitcoin Lightning payments to trigger video content updates.
- **Dynamic Integration:** Seamless integration between payment events and WebRTC streaming.
- **Demo-Ready:** Easy-to-run demo for testing and showcasing the technology.

## Prerequisites

Before running the demo, ensure you have:
- **Docker** and **Docker Compose** installed on your system.
- A modern web browser with WebRTC support (Chrome, Firefox, etc.).

## Running the Demo

### 1. Clone the Repository

Clone the repository and navigate to the project directory:

```bash
git clone git@github.com:sipsorcery-org/sipsorcery.git
cd sipsorcery/examples/WebRTCLightningExamples/WebRTCLightningGetStarted
```

### 2. Set Up the Docker Environment

This demo uses a `docker-compose` file to set up a Bitcoin Signet node, an LND Lightning node, and the RTL (Ride the Lightning) web interface.

1. **Start the Docker Containers**

    Run the following command to start the Bitcoin Signet node, LND, and RTL:

    ```bash
    docker-compose up -d
    ```

    This will spin up three services:

    - bitcoind: A Bitcoin Signet node.
    - lnd: A Lightning Network node connected to the Bitcoin Signet network.
    - rtl: A web interface for managing your LND node.

2. **Wait for Initialization**

    The LND node will take a few minutes to sync with the Bitcoin Signet network. You can monitor the progress by checking the logs:

    ```bash
    docker logs -f lnd
    ```

3. **Access RTL (Optional)**

    Once the LND node is synced, you can access the RTL web interface at [http://localhost:3000](http://localhost:3000) to manage your Lightning node.

### 3. Configure the Demo Application

1. **Update appSettings.json:**

    Ensure the LND connection settings in your appSettings.Development.json file match the credentials and ports used in the docker-compose setup. For example:

    ```json
    {
      "LndSettings": {
        "Url": "localhost:10009",
        "MacaroonPath": "../lnd-data/data/chain/bitcoin/signet/admin.macaroon",
        "CertificatePath": "../lnd-data/tls.cert"
      }
    }
    ```

    If you're using the default `docker-compose` setup, the paths and ports should already be preconfigured.

2. **Run the Demo Application**

    Start the demo application using the following command:

    ```bash
    dotnet run
    ```

### 4. Make Payments

1. **Scan the QR Code**

    The video stream will display a payment request QR code when it transitions to certain states. Use a mobile Lightning Network client (e.g. Zeus) to scan the QR code and make a payment.

2. **Trigger Video Updates**

    Once the payment is detected, the video source will update dynamically, demonstrating the pay-per-view or dynamic content control model.

### 5. Stopping the Demo

To stop the Docker containers, run:

```bash
docker-compose down
```

This will gracefully shut down the Bitcoin Signet node, LND, and RTL services.

### Docker Compose Configuration

The `docker-compose.yml` file provided sets up the following services:

- **Bitcoin Signet Node (bitcoind):**

  - Runs a Bitcoin Signet node for testing purposes.
  - Exposes RPC and ZMQ ports for communication with LND.

- **Lightning Network Node (lnd):**

  - Connects to the Bitcoin Signet node.
  - Exposes gRPC, REST, and peer-to-peer ports for Lightning Network operations.

- **RTL (Ride the Lightning) Interface (rtl):**

   - Provides a web-based interface for managing the LND node.
   - Accessible at http://localhost:3000 default password `sipsorcery`.

For advanced configuration, refer to the comments in the `docker-compose.yml` file.

### Troubleshooting

- **LND Node Not Syncing:** Ensure the Bitcoin Signet node is fully synced before using LND. Check the logs with:

```bash
docker logs -f bitcoind
```

 - **Connection Issues:** Verify that the `appSettings.json`, or `appSettings.Development.json`, file has the correct LND connection details.

 - **Payment Not Triggering Video Updates:** Ensure the payment is confirmed and the demo application is correctly processing payment events.

## Docker Image

### Build

`C:\dev\sipsorcery\examples\WebRTCLightningExamples> docker build -t webrtclightningdemo --progress=plain -f Dockerfile .

### Run

```
set LND_URL="use real value"
set LND_MACAROON_HEX="use real value"
set LND_CERTIFICATE_BASE64="use real value"
```

`docker run --rm -it -p 8080:8080 -e ASPNETCORE_URLS="http://0.0.0.0:8080" -e Lnd__Url="%LND_URL%" -e Lnd__MacaroonHex="%LND_MACAROON_HEX%" -e Lnd__CertificateBase64="%LND_CERTIFICATE_BASE64%" webrtclightningdemo`

### From DokcerHub

`docker run --rm -it -p 8080:8080 -e ASPNETCORE_URLS="http://0.0.0.0:8080" -e Lnd__Url="%LND_URL%" -e Lnd__MacaroonHex="%LND_MACAROON_HEX%" -e Lnd__CertificateBase64="%LND_CERTIFICATE_BASE64%" -e WAIT_FOR_ICE_GATHERING_TO_SEND_OFFER="True" -e STUN_URL="stun:stun.cloudflare.com" sipsorcery/webrtclightningdemo`

### Troubleshooting

`docker run --rm -it -p 8080:8080 -e ASPNETCORE_URLS="http://0.0.0.0:8080" -e Lnd__Url="%LND_URL%" -e Lnd__MacaroonHex="%LND_MACAROON_HEX%" -e Lnd__CertificateBase64="%LND_CERTIFICATE_BASE64%" --entrypoint "/bin/bash" webrtclightningdemo`

If there is a missing font exception check the `/usr/share/fonts/truetype/msttcorefonts` directory.