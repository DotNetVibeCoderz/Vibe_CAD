// Gravicode CAD Studio — blueprint drafting canvas. Made by Gravicode Studios, led by Kang Fadhil.
// Geometry arrives from .NET as display items in world coordinates (mm, Y up). Every cursor move asks .NET
// for the snapped point and the rubber-band preview of the active command.

const C = {
  paper: '#0F2747', paperEdge: '#0B1E38', grid: 'rgba(214,231,255,0.055)', gridMajor: 'rgba(214,231,255,0.13)',
  axisX: 'rgba(255,120,110,0.55)', axisY: 'rgba(120,230,160,0.55)', ink: '#EAF2FF', select: '#FF8A3D',
  preview: '#FFB46B', snap: '#FFD84D', ruler: '#0A1A30', rulerText: 'rgba(214,231,255,0.62)',
  window: 'rgba(79,141,247,0.16)', windowEdge: '#4F8DF7', crossing: 'rgba(72,199,142,0.14)', crossingEdge: '#48C78E',
};
const RULER = 22;

export function createCanvas(host, dotnet) { return new BlueprintCanvas(host, dotnet); }

class BlueprintCanvas {
  constructor(host, dotnet) {
    this.host = host; this.dotnet = dotnet;
    this.canvas = document.createElement('canvas');
    this.canvas.className = 'bp-canvas';
    this.canvas.tabIndex = 0;
    host.appendChild(this.canvas);
    this.ctx = this.canvas.getContext('2d');
    this.items = []; this.preview = []; this.snap = null;
    this.cx = 0; this.cy = 0; this.scale = 2; // px per world unit
    this.mouse = null; this.drag = null; this.pending = false; this.queued = null;
    this.gridSize = 10; this.showGrid = true;
    this.#bind();
    this.ro = new ResizeObserver(() => this.#resize()); this.ro.observe(host);
    this.#resize();
  }

  setScene(items) { this.items = items || []; this.draw(); }
  setPreview(preview, snap) { this.preview = preview || []; this.snap = snap; this.draw(); }
  setGrid(show, size) { this.showGrid = show; this.gridSize = size || 10; this.draw(); }

  zoomExtents(minX, minY, maxX, maxY) {
    this.fit = [minX, minY, maxX, maxY];   // refit on resize until the user pans or zooms
    if (!(maxX > minX) && !(maxY > minY)) { this.cx = 0; this.cy = 0; this.scale = 2; this.draw(); return; }
    const w = this.w - RULER, h = this.h - RULER;
    const sx = w / Math.max(maxX - minX, 1e-6), sy = h / Math.max(maxY - minY, 1e-6);
    this.scale = Math.min(sx, sy) * 0.86;
    this.cx = (minX + maxX) / 2; this.cy = (minY + maxY) / 2;
    this.draw();
  }

  // ---------------------------------------------------------------- coordinates
  toWorld(px, py) { return [this.cx + (px - RULER - (this.w - RULER) / 2) / this.scale, this.cy - (py - RULER - (this.h - RULER) / 2) / this.scale]; }
  toScreen(x, y) { return [RULER + (this.w - RULER) / 2 + (x - this.cx) * this.scale, RULER + (this.h - RULER) / 2 - (y - this.cy) * this.scale]; }

  #resize() {
    const dpr = window.devicePixelRatio || 1;
    this.w = this.host.clientWidth; this.h = this.host.clientHeight;
    this.canvas.width = Math.max(1, this.w * dpr); this.canvas.height = Math.max(1, this.h * dpr);
    this.canvas.style.width = this.w + 'px'; this.canvas.style.height = this.h + 'px';
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    if (this.fit) { const f = this.fit; this.zoomExtents(f[0], f[1], f[2], f[3]); return; }
    this.draw();
  }

  // ---------------------------------------------------------------- input
  #bind() {
    const el = this.canvas;
    const pos = e => { const r = el.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };
    el.addEventListener('contextmenu', e => e.preventDefault());
    el.addEventListener('pointerdown', e => {
      el.focus();
      const [px, py] = pos(e);
      if (e.button === 1 || (e.button === 0 && this.space)) { this.drag = { pan: true, px, py, cx: this.cx, cy: this.cy }; el.setPointerCapture(e.pointerId); e.preventDefault(); return; }
      if (e.button === 2) { this.dotnet.invokeMethodAsync('OnKey', 'Enter'); return; }
      if (e.button === 0) { this.drag = { pan: false, px, py, moved: false }; el.setPointerCapture(e.pointerId); }
    });
    el.addEventListener('pointermove', e => {
      const [px, py] = pos(e);
      this.mouse = [px, py];
      if (this.drag?.pan) {
        this.fit = null;
        this.cx = this.drag.cx - (px - this.drag.px) / this.scale;
        this.cy = this.drag.cy + (py - this.drag.py) / this.scale;
        this.draw(); return;
      }
      if (this.drag && Math.hypot(px - this.drag.px, py - this.drag.py) > 5) this.drag.moved = true;
      this.#requestSnap(px, py);
      this.draw();
    });
    el.addEventListener('pointerup', e => {
      const [px, py] = pos(e);
      const d = this.drag; this.drag = null;
      if (!d || d.pan) return;
      const tol = 7 / this.scale;
      if (d.moved) {
        const [x0, y0] = this.toWorld(d.px, d.py), [x1, y1] = this.toWorld(px, py);
        this.dotnet.invokeMethodAsync('OnWindow', x0, y0, x1, y1, e.shiftKey);
      } else {
        const p = this.snap ? [this.snap.x, this.snap.y] : this.toWorld(px, py);
        this.dotnet.invokeMethodAsync('OnClick', p[0], p[1], tol, e.shiftKey);
      }
      this.draw();
    });
    el.addEventListener('pointerleave', () => { this.mouse = null; this.snap = null; this.draw(); });
    el.addEventListener('wheel', e => {
      e.preventDefault();
      const [px, py] = pos(e);
      const [wx, wy] = this.toWorld(px, py);
      this.scale = Math.min(1e5, Math.max(1e-5, this.scale * Math.exp(-e.deltaY * 0.0015)));
      const [nx, ny] = this.toWorld(px, py);
      this.fit = null;
      this.cx += wx - nx; this.cy += wy - ny;
      this.#requestSnap(px, py);
      this.draw();
    }, { passive: false });
    el.addEventListener('dblclick', e => { if (e.button === 1) this.dotnet.invokeMethodAsync('OnKey', 'ZoomExtents'); });

    this.keydown = e => {
      const t = e.target;
      const typing = t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable);
      if (e.key === ' ' && !typing) { this.space = true; }
      if (typing && t.id !== 'cmd-input') return;
      const k = e.key;
      if (k === 'Escape') { this.dotnet.invokeMethodAsync('OnKey', 'Escape'); e.preventDefault(); return; }
      if (typing) return; // command input handles its own Enter
      if (k === 'Enter' || (k === ' ' && !this.drag)) { this.dotnet.invokeMethodAsync('OnKey', 'Enter'); e.preventDefault(); return; }
      if (k === 'Delete') { this.dotnet.invokeMethodAsync('OnKey', 'Delete'); e.preventDefault(); return; }
      if (['F3', 'F7', 'F8', 'F9'].includes(k)) { this.dotnet.invokeMethodAsync('OnKey', k); e.preventDefault(); return; }
      if ((e.ctrlKey || e.metaKey) && ['z', 'y', 'a', 's', 'o'].includes(k.toLowerCase())) { this.dotnet.invokeMethodAsync('OnKey', 'Ctrl+' + k.toUpperCase()); e.preventDefault(); return; }
      if (k.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
        // AutoCAD habit: start typing anywhere and it goes to the command line
        const input = document.getElementById('cmd-input');
        if (input) { input.focus(); }
      }
    };
    this.keyup = e => { if (e.key === ' ') this.space = false; };
    document.addEventListener('keydown', this.keydown);
    document.addEventListener('keyup', this.keyup);
  }

  async #requestSnap(px, py) {
    const [x, y] = this.toWorld(px, py);
    if (this.pending) { this.queued = [px, py]; return; }
    this.pending = true;
    try {
      const r = await this.dotnet.invokeMethodAsync('OnMove', x, y, 10 / this.scale);
      if (r) { this.snap = r.snap; this.preview = r.preview || []; this.coords = r.snap; }
    } catch { /* circuit busy or closed */ }
    this.pending = false;
    if (this.queued) { const q = this.queued; this.queued = null; this.#requestSnap(q[0], q[1]); }
    this.draw();
  }

  // ---------------------------------------------------------------- drawing
  draw() {
    if (this.raf) return;
    this.raf = requestAnimationFrame(() => { this.raf = 0; this.#paint(); });
  }

  #paint() {
    const g = this.ctx, W = this.w, H = this.h;
    // blueprint paper with a slight vignette
    const grad = g.createRadialGradient(W * 0.55, H * 0.45, 0, W * 0.55, H * 0.45, Math.max(W, H) * 0.8);
    grad.addColorStop(0, C.paper); grad.addColorStop(1, C.paperEdge);
    g.fillStyle = grad; g.fillRect(0, 0, W, H);
    g.save();
    g.beginPath(); g.rect(RULER, RULER, W - RULER, H - RULER); g.clip();
    if (this.showGrid) this.#grid();
    this.#axes();
    for (const it of this.items) if (it.solid) this.#item(it, false);
    for (const it of this.items) if (!it.solid) this.#item(it, false);
    for (const it of this.items) if (it.selected) this.#item(it, true);
    g.setLineDash([6, 4]);
    for (const it of this.preview) this.#item({ ...it, color: C.preview }, false, true);
    g.setLineDash([]);
    this.#windowRect();
    this.#snapMarker();
    this.#crosshair();
    this.#ucs();
    g.restore();
    this.#rulers();
  }

  #gridStep() {
    let step = this.gridSize;
    while (step * this.scale < 8) step *= 5;
    while (step * this.scale > 80) step /= 5;
    return step;
  }

  #grid() {
    const g = this.ctx, step = this.#gridStep();
    const [x0, y1] = this.toWorld(RULER, RULER), [x1, y0] = this.toWorld(this.w, this.h);
    const lines = (major) => {
      g.beginPath();
      const s = major ? step * 5 : step;
      for (let x = Math.floor(x0 / s) * s; x <= x1; x += s) { const [px] = this.toScreen(x, 0); g.moveTo(Math.round(px) + 0.5, RULER); g.lineTo(Math.round(px) + 0.5, this.h); }
      for (let y = Math.floor(y0 / s) * s; y <= y1; y += s) { const [, py] = this.toScreen(0, y); g.moveTo(RULER, Math.round(py) + 0.5); g.lineTo(this.w, Math.round(py) + 0.5); }
      g.strokeStyle = major ? C.gridMajor : C.grid; g.lineWidth = 1; g.stroke();
    };
    lines(false); lines(true);
  }

  #axes() {
    const g = this.ctx, [ox, oy] = this.toScreen(0, 0);
    g.lineWidth = 1;
    g.strokeStyle = C.axisX; g.beginPath(); g.moveTo(RULER, oy); g.lineTo(this.w, oy); g.stroke();
    g.strokeStyle = C.axisY; g.beginPath(); g.moveTo(ox, RULER); g.lineTo(ox, this.h); g.stroke();
  }

  #item(it, highlight, preview = false) {
    const g = this.ctx, p = it.points;
    const color = highlight ? C.select : (it.color || C.ink);
    g.strokeStyle = color; g.fillStyle = color;
    g.lineWidth = highlight ? 2.2 : it.solid ? 1.1 : 1.35;
    g.globalAlpha = it.solid && !highlight ? 0.85 : 1;
    if (highlight) { g.setLineDash([7, 4]); g.shadowColor = 'rgba(255,138,61,0.55)'; g.shadowBlur = 6; }
    switch (it.kind) {
      case 'poly': {
        g.beginPath();
        for (let i = 0; i < p.length; i += 2) { const [x, y] = this.toScreen(p[i], p[i + 1]); i === 0 ? g.moveTo(x, y) : g.lineTo(x, y); }
        if (it.closed) g.closePath();
        g.stroke(); break;
      }
      case 'segs': {
        g.beginPath();
        for (let i = 0; i < p.length; i += 4) { const [ax, ay] = this.toScreen(p[i], p[i + 1]), [bx, by] = this.toScreen(p[i + 2], p[i + 3]); g.moveTo(ax, ay); g.lineTo(bx, by); }
        g.stroke(); break;
      }
      case 'circle': {
        const [x, y] = this.toScreen(p[0], p[1]);
        g.beginPath(); g.arc(x, y, Math.max(0.5, p[2] * this.scale), 0, Math.PI * 2); g.stroke(); break;
      }
      case 'point': {
        const [x, y] = this.toScreen(p[0], p[1]);
        g.beginPath(); g.moveTo(x - 4, y); g.lineTo(x + 4, y); g.moveTo(x, y - 4); g.lineTo(x, y + 4); g.stroke(); break;
      }
      case 'text': {
        const size = it.height * this.scale;
        if (size >= 3) {
          const [x, y] = this.toScreen(p[0], p[1]);
          g.save(); g.translate(x, y);
          if (p.length > 2) { let a = -p[2]; if (a < -Math.PI / 2 || a > Math.PI / 2) a += Math.PI; g.rotate(a); }
          g.font = `500 ${size}px "Barlow Condensed", "IBM Plex Sans", sans-serif`;
          g.textBaseline = 'alphabetic'; g.fillText(it.text || '', 0, 0);
          g.restore();
        }
        break;
      }
    }
    g.globalAlpha = 1;
    if (highlight) { g.setLineDash(preview ? [6, 4] : []); g.shadowBlur = 0; }
  }

  #windowRect() {
    const d = this.drag;
    if (!d || d.pan || !d.moved || !this.mouse) return;
    const g = this.ctx, [mx, my] = this.mouse;
    const crossing = mx < d.px;
    g.fillStyle = crossing ? C.crossing : C.window;
    g.strokeStyle = crossing ? C.crossingEdge : C.windowEdge;
    g.setLineDash(crossing ? [5, 4] : []);
    g.fillRect(d.px, d.py, mx - d.px, my - d.py);
    g.strokeRect(d.px + 0.5, d.py + 0.5, mx - d.px, my - d.py);
    g.setLineDash([]);
  }

  #snapMarker() {
    const s = this.snap;
    if (!s || !s.kind || s.kind === 'None') return;
    const g = this.ctx, [x, y] = this.toScreen(s.x, s.y), r = 6;
    g.strokeStyle = C.snap; g.lineWidth = 2;
    g.beginPath();
    switch (s.kind) {
      case 'Endpoint': g.rect(x - r, y - r, 2 * r, 2 * r); break;
      case 'Midpoint': g.moveTo(x, y - r - 1); g.lineTo(x + r + 1, y + r); g.lineTo(x - r - 1, y + r); g.closePath(); break;
      case 'Center': g.arc(x, y, r, 0, Math.PI * 2); break;
      case 'Quadrant': g.moveTo(x, y - r - 1); g.lineTo(x + r + 1, y); g.lineTo(x, y + r + 1); g.lineTo(x - r - 1, y); g.closePath(); break;
      case 'Intersection': g.moveTo(x - r, y - r); g.lineTo(x + r, y + r); g.moveTo(x + r, y - r); g.lineTo(x - r, y + r); break;
      default: g.moveTo(x - 3, y); g.lineTo(x + 3, y); g.moveTo(x, y - 3); g.lineTo(x, y + 3);
    }
    g.stroke();
    if (s.kind !== 'Grid') {
      g.font = '600 10px "JetBrains Mono", monospace'; g.fillStyle = C.snap;
      g.fillText(s.kind.toUpperCase(), x + 10, y - 10);
    }
  }

  #crosshair() {
    if (!this.mouse || this.drag?.pan) return;
    const g = this.ctx;
    const [x, y] = this.snap && this.snap.kind !== 'None' ? this.toScreen(this.snap.x, this.snap.y) : this.mouse;
    g.strokeStyle = 'rgba(234,242,255,0.85)'; g.lineWidth = 1;
    g.beginPath();
    g.moveTo(x - 46, y + 0.5); g.lineTo(x - 5, y + 0.5); g.moveTo(x + 5, y + 0.5); g.lineTo(x + 46, y + 0.5);
    g.moveTo(x + 0.5, y - 46); g.lineTo(x + 0.5, y - 5); g.moveTo(x + 0.5, y + 5); g.lineTo(x + 0.5, y + 46);
    g.stroke();
    g.strokeRect(x - 4.5, y - 4.5, 9, 9);
  }

  #ucs() {
    const g = this.ctx, x = RULER + 26, y = this.h - 26;
    g.lineWidth = 1.6; g.font = '700 10px "JetBrains Mono", monospace';
    g.strokeStyle = C.axisX; g.fillStyle = C.axisX;
    g.beginPath(); g.moveTo(x, y); g.lineTo(x + 34, y); g.lineTo(x + 28, y - 4); g.moveTo(x + 34, y); g.lineTo(x + 28, y + 4); g.stroke(); g.fillText('X', x + 38, y + 4);
    g.strokeStyle = C.axisY; g.fillStyle = C.axisY;
    g.beginPath(); g.moveTo(x, y); g.lineTo(x, y - 34); g.lineTo(x - 4, y - 28); g.moveTo(x, y - 34); g.lineTo(x + 4, y - 28); g.stroke(); g.fillText('Y', x - 3, y - 40);
    g.strokeStyle = 'rgba(234,242,255,0.6)'; g.strokeRect(x - 3, y - 3, 6, 6);
  }

  #rulers() {
    const g = this.ctx, W = this.w, H = this.h, step = this.#gridStep() * 5;
    g.fillStyle = C.ruler; g.fillRect(0, 0, W, RULER); g.fillRect(0, 0, RULER, H);
    g.strokeStyle = 'rgba(214,231,255,0.25)'; g.fillStyle = C.rulerText; g.lineWidth = 1;
    g.font = '500 9px "JetBrains Mono", monospace';
    const [x0, y1] = this.toWorld(RULER, RULER), [x1, y0] = this.toWorld(W, H);
    const fmt = v => Math.abs(v) >= 1e4 ? (v / 1000).toFixed(0) + 'k' : (+v.toFixed(3)).toString();
    g.beginPath();
    for (let x = Math.floor(x0 / step) * step; x <= x1; x += step) {
      const [px] = this.toScreen(x, 0); if (px < RULER) continue;
      g.moveTo(px + 0.5, RULER - 8); g.lineTo(px + 0.5, RULER); g.fillText(fmt(x), px + 3, 10);
      for (let k = 1; k < 5; k++) { const sx = px + k * step * this.scale / 5; g.moveTo(sx + 0.5, RULER - 4); g.lineTo(sx + 0.5, RULER); }
    }
    for (let y = Math.floor(y0 / step) * step; y <= y1; y += step) {
      const [, py] = this.toScreen(0, y); if (py < RULER) continue;
      g.moveTo(RULER - 8, py + 0.5); g.lineTo(RULER, py + 0.5);
      g.save(); g.translate(10, py - 3); g.rotate(-Math.PI / 2); g.fillText(fmt(y), 0, 0); g.restore();
      for (let k = 1; k < 5; k++) { const sy = py - k * step * this.scale / 5; g.moveTo(RULER - 4, sy + 0.5); g.lineTo(RULER, sy + 0.5); }
    }
    g.stroke();
    if (this.mouse) {
      g.fillStyle = C.select;
      g.fillRect(this.mouse[0] - 0.5, 0, 1.5, RULER); g.fillRect(0, this.mouse[1] - 0.5, RULER, 1.5);
    }
    g.fillStyle = '#081426'; g.fillRect(0, 0, RULER, RULER);
  }

  dispose() {
    this.ro.disconnect();
    document.removeEventListener('keydown', this.keydown);
    document.removeEventListener('keyup', this.keyup);
    this.canvas.remove();
  }
}

/** Saves bytes as a download. */
export function download(name, bytes, mime) {
  const blob = new Blob([bytes], { type: mime || 'application/octet-stream' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob); a.download = name;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 4000);
}

export function focusCommand() { document.getElementById('cmd-input')?.focus(); }
export function scrollHistory(el) { if (el) el.scrollTop = el.scrollHeight; }
