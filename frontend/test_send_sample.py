#!/usr/bin/env python3
"""
Send one of the real OptiPMD JSONs from lims_json/ to the running Tauri
backend over TCP localhost:9980. Mimics what the equipment does.

Usage:
    python3 test_send_sample.py [path_to_json]

Default: picks the first JSON from ../lims_json/.
"""
import socket, sys, glob, os

HOST = "127.0.0.1"
PORT = 9980

def main():
    if len(sys.argv) > 1:
        path = sys.argv[1]
    else:
        candidates = sorted(glob.glob(os.path.join(os.path.dirname(__file__), "..", "lims_json", "*.json")))
        if not candidates:
            print("No JSON files found in ../lims_json/")
            sys.exit(1)
        path = candidates[0]

    print(f"Sending: {path}")
    with open(path, "rb") as f:
        data = f.read()

    sock = socket.create_connection((HOST, PORT), timeout=10)
    sock.sendall(data)
    sock.sendall(b"\x00")  # null terminator (the equipment uses this)
    response = b""
    sock.settimeout(5)
    try:
        while True:
            chunk = sock.recv(4096)
            if not chunk: break
            response += chunk
    except socket.timeout:
        pass
    sock.close()
    print(f"Server response ({len(response)}B): {response.decode('utf-8', errors='replace')}")

if __name__ == "__main__":
    main()
