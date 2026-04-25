# Conceptual Overview

NinePSharp brings the "Everything is a File" philosophy of Plan 9 to modern .NET services and local resources.

## Why 9P?

The 9P protocol is unique in its simplicity and transparency. By representing resources as files, we eliminate the need for custom SDKs and complex orchestration layers. If you can `ls`, `cat`, and `echo`, you can inspect and control a service surface exposed through 9P.

## Architecture

The system is split into several logical layers:

1.  **Transport Layer:** Handles TCP and TLS connections.
2.  **Parser Layer (F#):** Converts raw bytes into strongly-typed 9P messages.
3.  **Dispatcher Layer:** Routes messages to the correct FID (File ID) handler.
4.  **Backend Layer:** Individual handlers that translate 9P messages into storage or service-specific operations.
