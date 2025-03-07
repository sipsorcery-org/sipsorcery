# WebRTC Lightning Payment Video Stream Demo

This demo project showcases a real-time WebRTC video stream whose source is controlled by Bitcoin Lightning payments. In this demo, viewers can trigger changes in the video stream by making micropayments over the Lightning Network.

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

- A Bitcoin Lightning node or test environment (e.g., [LND](https://github.com/lightningnetwork/lnd))
- A modern web browser with WebRTC support (Chrome, Firefox, etc.)

## Running the Demo

1. **Clone the Repository:**

   ```bash
   git clone git@github.com:sipsorcery-org/sipsorcery.git
   cd sipsorcery/examples/WebRTCLightningExamples/WebRTCLightningGetStarted

2. **Lnd Lightning Node:**

An [LND](https://github.com/lightningnetwork/lnd) is required. The connection settings need to be set in your `appSettings.Development.json` file.

3. **Run the Demo**

`dotnet run`

4. **Make Payments**

The video stream will display a payment request QR code when it transitions to certain states. A payment can be made with a mobile Lightning Network client by scanning the QRCode.

The [Zeus](https://zeusln.com/) Bitcoin Lightning client is recommended.
