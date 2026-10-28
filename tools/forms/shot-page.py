#!/usr/bin/env python3
"""Screenshots of hand-written pages in Chrome (a black box) over the DevTools Protocol, with the preferred color scheme and the mouse
under control. Standard library only (a minimal WebSocket client).

Usage (on the worker): shot-page.py <out-dir> <spec>...   where spec = <page.html>[@dark][@hover=X,Y][@press=X,Y]
  @dark       emulate prefers-color-scheme: dark (Emulation.setEmulatedMedia)
  @hover=X,Y  move the mouse to (X, Y) before the shot
  @press=X,Y  move there and hold the left button down
  @js=EXPR    evaluate EXPR (no "@" in it) after the mouse moves and print the value
Focus emulation is on, so :focus-visible rings show. Environment: CHROME, FONTCONFIG_FILE, WINDOW (default 900x400), DPR (default 1).
"""
import base64, http.client, json, os, socket, struct, subprocess, sys, tempfile, time


class WS:
    def __init__(self, port, path):
        self.s = socket.create_connection(("127.0.0.1", port))
        key = base64.b64encode(os.urandom(16)).decode()
        self.s.sendall(("GET %s HTTP/1.1\r\nHost: 127.0.0.1:%d\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: %s\r\nSec-WebSocket-Version: 13\r\n\r\n" % (path, port, key)).encode())
        buf = b""
        while b"\r\n\r\n" not in buf:
            buf += self.s.recv(1)
        self.n = 0

    def send(self, method, params=None):
        self.n += 1
        data = json.dumps({"id": self.n, "method": method, "params": params or {}}).encode()
        mask = os.urandom(4)
        hdr = bytes([0x81])
        if len(data) < 126:
            hdr += bytes([0x80 | len(data)])
        elif len(data) < 65536:
            hdr += bytes([0x80 | 126]) + struct.pack(">H", len(data))
        else:
            hdr += bytes([0x80 | 127]) + struct.pack(">Q", len(data))
        self.s.sendall(hdr + mask + bytes(b ^ mask[i % 4] for i, b in enumerate(data)))
        while True:
            m = self.recv()
            if m.get("id") == self.n:
                if "error" in m:
                    raise RuntimeError("%s: %s" % (method, m["error"]))
                return m.get("result", {})

    def _exact(self, n):
        b = b""
        while len(b) < n:
            c = self.s.recv(n - len(b))
            if not c:
                raise EOFError
            b += c
        return b

    def recv(self):
        data = b""
        while True:
            h = self._exact(2)
            fin, ln = h[0] & 0x80, h[1] & 0x7F
            if ln == 126:
                ln = struct.unpack(">H", self._exact(2))[0]
            elif ln == 127:
                ln = struct.unpack(">Q", self._exact(8))[0]
            data += self._exact(ln)
            if fin:
                return json.loads(data)


def main():
    out = os.path.abspath(sys.argv[1])
    os.makedirs(out, exist_ok=True)
    w, h = (int(x) for x in os.environ.get("WINDOW", "900x400").split("x"))
    dpr = float(os.environ.get("DPR", "1"))
    prof = tempfile.mkdtemp()
    p = subprocess.Popen([os.environ["CHROME"], "--headless=new", "--no-sandbox", "--disable-gpu", "--hide-scrollbars", "--font-render-hinting=none",
                          "--disable-lcd-text", "--force-color-profile=srgb", "--remote-debugging-port=0", "--user-data-dir=" + prof, "about:blank"],
                         stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
    try:
        port = None
        for line in p.stderr:
            if "DevTools listening on ws://127.0.0.1:" in line:
                port = int(line.split("127.0.0.1:")[1].split("/")[0])
                break
        c = http.client.HTTPConnection("127.0.0.1", port)
        c.request("GET", "/json")
        page = [t for t in json.loads(c.getresponse().read()) if t["type"] == "page"][0]
        ws = WS(port, "/devtools/page/" + page["id"])
        ws.send("Emulation.setFocusEmulationEnabled", {"enabled": True})
        for spec in sys.argv[2:]:
            parts = spec.split("@")
            path = os.path.abspath(parts[0])
            name = os.path.splitext(os.path.basename(path))[0] + "".join("-" + x.replace("=", "").replace(",", "_") for x in parts[1:] if not x.startswith("js="))
            ws.send("Emulation.setDeviceMetricsOverride", {"width": w, "height": h, "deviceScaleFactor": dpr, "mobile": False})
            ws.send("Emulation.setEmulatedMedia", {"features": [{"name": "prefers-color-scheme", "value": "dark" if "dark" in parts[1:] else "light"}]})
            ws.send("Page.navigate", {"url": "file://" + path})
            time.sleep(0.6)
            for x in parts[1:]:
                if x.startswith(("hover=", "press=")):
                    mx, my = (float(v) for v in x.split("=")[1].split(","))
                    ws.send("Input.dispatchMouseEvent", {"type": "mouseMoved", "x": mx, "y": my})
                    if x.startswith("press="):
                        ws.send("Input.dispatchMouseEvent", {"type": "mousePressed", "x": mx, "y": my, "button": "left", "clickCount": 1})
                    time.sleep(0.3)
            for x in parts[1:]:
                if x.startswith("js="):
                    r = ws.send("Runtime.evaluate", {"expression": x[3:], "returnByValue": True})
                    print(name, "js:", json.dumps(r.get("result", {}).get("value")))
            shot = ws.send("Page.captureScreenshot", {"format": "png"})
            open(os.path.join(out, name + ".png"), "wb").write(base64.b64decode(shot["data"]))
            print(name, "ok")
    finally:
        p.terminate()
        p.wait()


main()
