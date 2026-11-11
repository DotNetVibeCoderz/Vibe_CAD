// Gravicode.CAD three.js viewer — Made by Gravicode Studios, led by Kang Fadhil.
// Geometry arrives from .NET as one binary buffer; CAD space is Z-up, so the camera uses up = +Z.
import * as THREE from 'https://cdn.jsdelivr.net/npm/three@0.170.0/build/three.module.min.js';

const THEMES = {
  dark: { bg: 0x0d1117, grid: 0x2a3445, gridMajor: 0x3b4a61, edge: 0x0a0c10, wire: 0xe6edf3 },
  light: { bg: 0xf4f6fa, grid: 0xd5dbe5, gridMajor: 0xb8c2d1, edge: 0x1d232b, wire: 0x1d232b },
};

export function createViewer(host, options) {
  return new CadViewer(host, options ?? {});
}

class CadViewer {
  constructor(host, options) {
    this.host = host;
    this.theme = THEMES[options.theme] ?? THEMES.dark;
    this.showGrid = options.grid !== false;
    this.autoRotate = !!options.autoRotate;
    this.edgesVisible = true;

    this.renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true, alpha: false });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.05;
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.domElement.className = 'gcad-viewer__canvas';
    host.prepend(this.renderer.domElement);

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(this.theme.bg);
    this.camera = new THREE.PerspectiveCamera(35, 1, 0.1, 100000);
    this.camera.up.set(0, 0, 1);

    // procedural studio environment so metals have something to reflect
    this.scene.environment = studioEnvironment(this.renderer);
    this.scene.environmentIntensity = 0.9;
    // studio lighting: environment-like hemisphere + key/fill/rim
    this.scene.add(new THREE.HemisphereLight(0xdfe8ff, 0x2a2622, 0.8));
    this.key = new THREE.DirectionalLight(0xfff4e8, 2.6);
    this.key.castShadow = true;
    this.key.shadow.mapSize.set(2048, 2048);
    this.scene.add(this.key, this.key.target);
    const fill = new THREE.DirectionalLight(0xc8d8ff, 0.8); fill.position.set(-1, -0.6, 0.4);
    const rim = new THREE.DirectionalLight(0xffffff, 1.0); rim.position.set(-0.4, 1, 0.8);
    this.scene.add(fill, rim);

    this.content = new THREE.Group();
    this.scene.add(this.content);
    this.helpers = new THREE.Group();
    this.scene.add(this.helpers);

    // orbit state (spherical around target, Z-up)
    this.target = new THREE.Vector3();
    this.radius = 100; this.yaw = -0.6; this.pitch = 0.5; this.sceneRadius = 50;
    this.#bindInput();

    this.resizeObserver = new ResizeObserver(() => this.#resize());
    this.resizeObserver.observe(host);
    this.#resize();
    this.disposed = false;
    const loop = (t) => {
      if (this.disposed) return;
      if (this.autoRotate && !this.dragging) this.yaw += 0.003;
      this.#updateCamera();
      this.renderer.render(this.scene, this.camera);
      this.frame = requestAnimationFrame(loop);
    };
    this.frame = requestAnimationFrame(loop);
  }

  setScene(scene, bytes, showEdges) {
    this.#clear();
    const buf = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
    for (const b of scene.bodies) {
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.BufferAttribute(new Float32Array(buf, b.positionsOffset, b.positionsCount), 3));
      geo.setAttribute('normal', new THREE.BufferAttribute(new Float32Array(buf, b.normalsOffset, b.positionsCount), 3));
      geo.setIndex(new THREE.BufferAttribute(new Uint32Array(buf, b.indicesOffset, b.indicesCount), 1));
      const mat = new THREE.MeshStandardMaterial({
        color: new THREE.Color(b.color), metalness: b.metallic, roughness: b.roughness,
        transparent: b.opacity < 1, opacity: b.opacity,
        polygonOffset: true, polygonOffsetFactor: 1, polygonOffsetUnits: 1,
      });
      const mesh = new THREE.Mesh(geo, mat);
      mesh.name = b.name; mesh.castShadow = true; mesh.receiveShadow = true;
      this.content.add(mesh);
      if (b.edgesCount > 0) {
        const eg = new THREE.BufferGeometry();
        eg.setAttribute('position', new THREE.BufferAttribute(new Float32Array(buf, b.edgesOffset, b.edgesCount), 3));
        const lines = new THREE.LineSegments(eg, new THREE.LineBasicMaterial({ color: this.theme.edge }));
        lines.userData.isEdges = true;
        lines.visible = showEdges !== false;
        this.content.add(lines);
      }
    }
    for (const w of scene.wires) {
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(buf, w.offset, w.count), 3));
      this.content.add(new THREE.LineSegments(g, new THREE.LineBasicMaterial({ color: new THREE.Color(w.color) })));
    }
    this.edgesVisible = showEdges !== false;
    this.bounds = new THREE.Box3(new THREE.Vector3(...scene.min), new THREE.Vector3(...scene.max));
    this.#buildHelpers();
    this.fit();
  }

  #buildHelpers() {
    this.helpers.clear();
    const size = this.bounds.getSize(new THREE.Vector3());
    const center = this.bounds.getCenter(new THREE.Vector3());
    this.sceneRadius = Math.max(size.length() / 2, 1e-3);
    // ground: shadow catcher + grid in the XY plane at the model floor
    const extent = Math.max(size.x, size.y, this.sceneRadius) * 4;
    const ground = new THREE.Mesh(new THREE.PlaneGeometry(extent, extent), new THREE.ShadowMaterial({ opacity: 0.28 }));
    ground.position.set(center.x, center.y, this.bounds.min.z - this.sceneRadius * 0.002);
    ground.receiveShadow = true;
    this.helpers.add(ground);
    if (this.showGrid) {
      const step = niceStep(Math.max(size.x, size.y) / 8);
      const divisions = Math.max(4, Math.round(extent / step));
      const grid = new THREE.GridHelper(divisions * step, divisions, this.theme.gridMajor, this.theme.grid);
      grid.rotation.x = Math.PI / 2;
      grid.position.set(Math.round(center.x / step) * step, Math.round(center.y / step) * step, this.bounds.min.z - this.sceneRadius * 0.001);
      grid.material.transparent = true; grid.material.opacity = 0.65;
      this.helpers.add(grid);
    }
    // key light follows the model size
    this.key.position.set(center.x + this.sceneRadius * 2, center.y - this.sceneRadius * 1.5, center.z + this.sceneRadius * 3);
    this.key.target.position.copy(center);
    const s = this.sceneRadius * 2.2;
    Object.assign(this.key.shadow.camera, { left: -s, right: s, top: s, bottom: -s, near: 0.01, far: this.sceneRadius * 10 });
    this.key.shadow.camera.updateProjectionMatrix();
  }

  fit() {
    if (!this.bounds) return;
    this.bounds.getCenter(this.target);
    this.radius = this.sceneRadius / Math.sin(THREE.MathUtils.degToRad(this.camera.fov) / 2) * 1.12;
    this.camera.near = this.radius / 1000; this.camera.far = this.radius * 100;
    this.camera.updateProjectionMatrix();
  }

  setView(name) {
    const v = { iso: [-0.785, 0.615], front: [0, 0], top: [0, 1.5707], right: [1.5708, 0], back: [3.1416, 0], left: [-1.5708, 0] }[name] ?? [-0.785, 0.615];
    this.yaw = v[0]; this.pitch = v[1];
    this.fit();
  }

  setEdges(on) {
    this.edgesVisible = on;
    this.content.traverse(o => { if (o.userData.isEdges) o.visible = on; });
  }

  snapshot() {
    this.renderer.render(this.scene, this.camera);
    return this.renderer.domElement.toDataURL('image/png');
  }

  #updateCamera() {
    const cp = Math.cos(this.pitch), sp = Math.sin(this.pitch);
    this.camera.position.set(
      this.target.x + this.radius * Math.sin(this.yaw) * cp,
      this.target.y - this.radius * Math.cos(this.yaw) * cp,
      this.target.z + this.radius * sp);
    this.camera.lookAt(this.target);
  }

  #bindInput() {
    const el = this.renderer.domElement;
    el.style.touchAction = 'none';
    let last = null, mode = 0;
    el.addEventListener('contextmenu', e => e.preventDefault());
    el.addEventListener('pointerdown', e => {
      el.setPointerCapture(e.pointerId);
      last = [e.clientX, e.clientY];
      mode = (e.button === 2 || e.button === 1 || e.shiftKey) ? 2 : 1;
      this.dragging = true;
    });
    el.addEventListener('pointermove', e => {
      if (!last) return;
      const dx = e.clientX - last[0], dy = e.clientY - last[1];
      last = [e.clientX, e.clientY];
      if (mode === 1) {
        this.yaw -= dx * 0.008;
        this.pitch = Math.max(-1.55, Math.min(1.55, this.pitch + dy * 0.008));
      } else {
        // pan in the camera plane
        const h = el.clientHeight || 1;
        const scale = 2 * this.radius * Math.tan(THREE.MathUtils.degToRad(this.camera.fov) / 2) / h;
        const right = new THREE.Vector3().setFromMatrixColumn(this.camera.matrix, 0);
        const up = new THREE.Vector3().setFromMatrixColumn(this.camera.matrix, 1);
        this.target.addScaledVector(right, -dx * scale).addScaledVector(up, dy * scale);
      }
    });
    const end = () => { last = null; this.dragging = false; };
    el.addEventListener('pointerup', end);
    el.addEventListener('pointercancel', end);
    el.addEventListener('wheel', e => {
      e.preventDefault();
      this.radius *= Math.exp(e.deltaY * 0.0012);
      this.radius = Math.max(this.sceneRadius * 0.05, Math.min(this.sceneRadius * 50, this.radius));
    }, { passive: false });
    el.addEventListener('dblclick', () => this.fit());
  }

  #resize() {
    const w = this.host.clientWidth || 300, h = this.host.clientHeight || 200;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  #clear() {
    this.content.traverse(o => { o.geometry?.dispose?.(); o.material?.dispose?.(); });
    this.content.clear();
  }

  dispose() {
    this.disposed = true;
    cancelAnimationFrame(this.frame);
    this.resizeObserver.disconnect();
    this.#clear();
    this.renderer.dispose();
    this.renderer.domElement.remove();
  }
}

/** Soft photo-studio: gradient dome plus three large light panels, pre-filtered with PMREM. */
function studioEnvironment(renderer) {
  const env = new THREE.Scene();
  const dome = new THREE.SphereGeometry(50, 32, 16);
  const colors = [];
  const pos = dome.getAttribute('position');
  const top = new THREE.Color(0xe9eef6), horizon = new THREE.Color(0x9aa6b8), floor = new THREE.Color(0x2b2f36);
  for (let i = 0; i < pos.count; i++) {
    const t = pos.getY(i) / 50; // three's dome is Y-up; the env map is sampled in world space
    const c = t > 0 ? horizon.clone().lerp(top, t) : horizon.clone().lerp(floor, -t);
    colors.push(c.r, c.g, c.b);
  }
  dome.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
  env.add(new THREE.Mesh(dome, new THREE.MeshBasicMaterial({ side: THREE.BackSide, vertexColors: true })));
  const panel = (x, y, z, s, k) => {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(s, s * 0.6), new THREE.MeshBasicMaterial({ color: new THREE.Color(k, k, k), side: THREE.DoubleSide }));
    m.position.set(x, y, z); m.lookAt(0, 0, 0); env.add(m);
  };
  panel(20, 30, 15, 30, 4); panel(-30, 15, -10, 25, 2); panel(0, 10, -35, 30, 1.5);
  const pmrem = new THREE.PMREMGenerator(renderer);
  const tex = pmrem.fromScene(env, 0.03).texture;
  pmrem.dispose();
  return tex;
}

function niceStep(raw) {
  if (!(raw > 0)) return 10;
  const p = Math.pow(10, Math.floor(Math.log10(raw)));
  const f = raw / p;
  return (f < 1.5 ? 1 : f < 3.5 ? 2 : f < 7.5 ? 5 : 10) * p;
}
