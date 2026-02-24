# Conceptual Overview

NinePSharp is designed to bring the "Everything is a File" philosophy of Plan 9 to modern, distributed, and decentralized infrastructures.

## Why 9P?

The 9P protocol is unique in its simplicity and transparency. By representing services as files, we eliminate the need for custom SDKs and complex orchestration layers. If you can `ls`, `cat`, and `echo`, you can manage a global compute grid or a blockchain node.

## Architecture

The system is split into several logical layers:

1.  **Transport Layer:** Handles TCP/TLS and Noise Protocol connections.
2.  **Parser Layer (F#):** Converts raw bytes into strongly-typed 9P messages.
3.  **Dispatcher Layer:** Routes messages to the correct FID (File ID) handler.
4.  **Backend Layer:** Individual plugins that translate 9P messages into service-specific calls (e.g., S3 API, Ethereum RPC).
