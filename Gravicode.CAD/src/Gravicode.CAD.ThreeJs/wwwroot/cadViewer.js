// Gravicode.CAD three.js viewer — Made by Gravicode Studios, led by Kang Fadhil.
// Geometry arrives from .NET as one binary buffer; CAD space is Z-up, so the camera uses up = +Z.
import * as THREE from 'https://cdn.jsdelivr.net/npm/three@0.170.0/build/three.module.min.js';

const THEMES = {
  dark: { bg: 0x0d1117, grid: 0x2a3445, gridMajor: 0x3b4a61, edge: 0x0a0c10, wire: 0xe6edf3 },
  light: { bg: 0xf4f6fa, grid: 0xd5dbe5, gridMajor: 0xb8c2d1, edge: 0x1d232b, wire: 0x1d232b },
};

export function createViewer(host, options, dotnet) {
  return new CadViewer(host, options ?? {}, dotnet);
}

const AXES = [
  { name: 'X', dir: new THREE.Vector3(1, 0, 0), color: 0xff5a4e },
  { name: 'Y', dir: new THREE.Vector3(0, 1, 0), color: 0x5ad17a },
  { name: 'Z', dir: new THREE.Vector3(0, 0, 1), color: 0x4f8df7 },
];
const HOVER = 0xffd84d, SELECT = 0xff8a3d;

class CadViewer {
  constructor(host, options, dotnet) {
    this.host = host;
    this.dotnet = dotnet ?? null;
    host.gcadViewer = this;   // handle for tests and host-page scripting
    this.selectable = false; this.highlighted = new Set(); this.gizmoMode = null; this.snap = 0;
    this.raycaster = new THREE.Raycaster();
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
    this.gizmo = new THREE.Group();
    this.gizmo.visible = false;
    this.scene.add(this.gizmo);

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
      this.#updateGizmo();
      this.renderer.render(this.scene, this.camera);
      this.frame = requestAnimationFrame(loop);
    };
    this.frame = requestAnimationFrame(loop);
  }

  setScene(scene, bytes, showEdges, keepView) {
    const firstLoad = !this.bounds;
    this.#clear();
    const buf = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
    scene.bodies.forEach((b, bodyIndex) => {
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
      mesh.userData.body = bodyIndex;
      this.content.add(mesh);
      if (b.edgesCount > 0) {
        const eg = new THREE.BufferGeometry();
        eg.setAttribute('position', new THREE.BufferAttribute(new Float32Array(buf, b.edgesOffset, b.edgesCount), 3));
        const lines = new THREE.LineSegments(eg, new THREE.LineBasicMaterial({ color: this.theme.edge }));
        lines.userData.isEdges = true;
        lines.userData.body = bodyIndex;
        lines.visible = showEdges !== false;
        this.content.add(lines);
      }
    });
    for (const w of scene.wires) {
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(buf, w.offset, w.count), 3));
      this.content.add(new THREE.LineSegments(g, new THREE.LineBasicMaterial({ color: new THREE.Color(w.color) })));
    }
    this.edgesVisible = showEdges !== false;
    this.bounds = new THREE.Box3(new THREE.Vector3(...scene.min), new THREE.Vector3(...scene.max));
    this.#buildHelpers();
    if (firstLoad || !keepView) this.fit();
    this.#applyHighlight();
    this.#buildGizmo();
  }

  // ---------------------------------------------------------------- selection & gizmo

  /** selectable: clicks report bodies; highlighted: body indices; gizmo: null | 'move' | 'rotate' | 'scale'; snap: move step. */
  setInteraction(opts) {
    this.selectable = !!opts.selectable;
    this.highlighted = new Set(opts.highlighted ?? []);
    this.snap = opts.snap ?? 0;
    if ((opts.gizmo ?? null) !== this.gizmoMode) { this.gizmoMode = opts.gizmo ?? null; this.#buildGizmo(); }
    this.#applyHighlight();
  }

  #applyHighlight() {
    this.content.traverse(o => {
      if (o.userData.body === undefined || !o.material) return;
      const on = this.highlighted.has(o.userData.body);
      if (o.isMesh) { o.material.emissive?.setHex(on ? SELECT : 0x000000); o.material.emissiveIntensity = on ? 0.6 : 0; }
      else if (o.userData.isEdges) o.material.color.setHex(on ? SELECT : this.theme.edge);
    });
  }

  #selectionBox() {
    const box = new THREE.Box3();
    this.content.traverse(o => { if (o.isMesh && this.highlighted.has(o.userData.body)) box.expandByObject(o); });
    return box;
  }

  #buildGizmo() {
    this.gizmo.traverse(o => { o.geometry?.dispose?.(); o.material?.dispose?.(); });
    this.gizmo.clear();
    this.handles = [];
    if (!this.gizmoMode) return;
    const up = new THREE.Vector3(0, 1, 0);
    for (const a of AXES) {
      const mat = new THREE.MeshBasicMaterial({ color: a.color, depthTest: false, transparent: true, opacity: 0.95 });
      const hitMat = new THREE.MeshBasicMaterial({ visible: false });
      const part = new THREE.Group();
      if (this.gizmoMode === 'scale') {
        const shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.016, 0.016, 0.85, 8), mat); shaft.position.y = 0.425;
        const head = new THREE.Mesh(new THREE.BoxGeometry(0.14, 0.14, 0.14), mat); head.position.y = 0.9;
        const hit = new THREE.Mesh(new THREE.CylinderGeometry(0.09, 0.09, 1.05, 8), hitMat); hit.position.y = 0.52;
        part.add(shaft, head, hit);
        part.quaternion.setFromUnitVectors(up, a.dir);
        this.handles.push({ axis: a, hit, mat });
      } else if (this.gizmoMode === 'move') {
        const shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.018, 0.018, 0.8, 8), mat); shaft.position.y = 0.4;
        const head = new THREE.Mesh(new THREE.ConeGeometry(0.07, 0.2, 16), mat); head.position.y = 0.9;
        const hit = new THREE.Mesh(new THREE.CylinderGeometry(0.09, 0.09, 1.05, 8), hitMat); hit.position.y = 0.52;
        part.add(shaft, head, hit);
        part.quaternion.setFromUnitVectors(up, a.dir);
        this.handles.push({ axis: a, hit, mat });
      } else {
        const ring = new THREE.Mesh(new THREE.TorusGeometry(0.8, 0.014, 8, 96), mat);
        const hit = new THREE.Mesh(new THREE.TorusGeometry(0.8, 0.08, 6, 48), hitMat);
        part.add(ring, hit);
        // a torus lies in its local XY plane: turn its normal (+Z) onto the axis
        part.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), a.dir);
        this.handles.push({ axis: a, hit, mat });
      }
      part.traverse(o => { o.renderOrder = 999; });
      this.gizmo.add(part);
    }
  }

  #updateGizmo() {
    if (!this.gizmoMode || this.highlighted.size === 0) { this.gizmo.visible = false; return; }
    if (!this.gizmoDrag) {
      const box = this.#selectionBox();
      if (box.isEmpty()) { this.gizmo.visible = false; return; }
      box.getCenter(this.gizmo.position);
    }
    this.gizmo.visible = true;
    // constant size on screen
    this.gizmo.scale.setScalar(this.camera.position.distanceTo(this.gizmo.position) * 0.16);
  }

  #ray(e) {
    const r = this.renderer.domElement.getBoundingClientRect();
    const ndc = new THREE.Vector2(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
    this.raycaster.setFromCamera(ndc, this.camera);
    return this.raycaster;
  }

  #handleAt(e) {
    if (!this.gizmo.visible || !this.handles?.length) return null;
    const hits = this.#ray(e).intersectObjects(this.handles.map(h => h.hit), false);
    return hits.length ? this.handles.find(h => h.hit === hits[0].object) : null;
  }

  #hover(e) {
    const h = this.#handleAt(e);
    for (const x of this.handles ?? []) x.mat.color.setHex(x === h ? HOVER : x.axis.color);
    this.renderer.domElement.style.cursor = h ? 'grab' : '';
  }

  /** Point where the pointer ray meets the drag plane of the active handle. */
  #dragPoint(e) {
    const hit = new THREE.Vector3();
    return this.#ray(e).ray.intersectPlane(this.gizmoDrag.plane, hit) ? hit : null;
  }

  #gizmoDown(e) {
    const h = this.#handleAt(e);
    if (!h) return false;
    const axis = h.axis.dir.clone(), pivot = this.gizmo.position.clone();
    let normal;
    if (this.gizmoMode === 'move' || this.gizmoMode === 'scale') {
      // plane containing the axis that faces the camera best
      const view = new THREE.Vector3().subVectors(this.camera.position, pivot).normalize();
      normal = new THREE.Vector3().crossVectors(axis, new THREE.Vector3().crossVectors(view, axis)).normalize();
      if (normal.lengthSq() < 1e-9) return false;
    } else normal = axis.clone();
    const plane = new THREE.Plane().setFromNormalAndCoplanarPoint(normal, pivot);
    this.gizmoDrag = { handle: h, axis, pivot, plane, start: null, value: this.gizmoMode === 'scale' ? 1 : 0, length: this.gizmo.scale.x * 0.9 };
    this.gizmoDrag.start = this.#dragPoint(e);
    if (!this.gizmoDrag.start) { this.gizmoDrag = null; return false; }
    // live preview: the selected objects move inside a group pivoted at the gizmo
    this.dragGroup = new THREE.Group();
    this.dragGroup.position.copy(pivot);
    this.content.add(this.dragGroup);
    this.dragGroup.updateMatrixWorld();
    const picked = this.content.children.filter(o => o !== this.dragGroup && this.highlighted.has(o.userData.body));
    for (const o of picked) this.dragGroup.attach(o);
    h.mat.color.setHex(HOVER);
    try { this.renderer.domElement.setPointerCapture(e.pointerId); } catch { /* pen / synthetic pointers */ }
    return true;
  }

  #gizmoMove(e) {
    const d = this.gizmoDrag, p = this.#dragPoint(e);
    if (!p) return;
    if (this.gizmoMode === 'scale') {
      // dragging the handle outwards by its own length doubles the size
      let f = Math.max(0.05, 1 + new THREE.Vector3().subVectors(p, d.start).dot(d.axis) / d.length);
      if (!e.altKey) f = Math.max(0.1, Math.round(f * 10) / 10);
      d.value = f;
      this.dragGroup.scale.setScalar(f);
    } else if (this.gizmoMode === 'move') {
      let t = new THREE.Vector3().subVectors(p, d.start).dot(d.axis);
      if (this.snap > 0 && !e.altKey) t = Math.round(t / this.snap) * this.snap;
      d.value = t;
      this.dragGroup.position.copy(d.pivot).addScaledVector(d.axis, t);
      this.gizmo.position.copy(this.dragGroup.position);
    } else {
      const v0 = new THREE.Vector3().subVectors(d.start, d.pivot), v1 = new THREE.Vector3().subVectors(p, d.pivot);
      let a = Math.atan2(new THREE.Vector3().crossVectors(v0, v1).dot(d.axis), v0.dot(v1));
      if (!e.altKey) { const step = Math.PI / 12; a = Math.round(a / step) * step; }
      d.value = a;
      this.dragGroup.quaternion.setFromAxisAngle(d.axis, a);
    }
  }

  #gizmoUp() {
    const d = this.gizmoDrag;
    this.gizmoDrag = null;
    d.handle.mat.color.setHex(d.handle.axis.color);
    const idle = this.gizmoMode === 'scale' ? Math.abs(d.value - 1) < 1e-9 : Math.abs(d.value) < 1e-9;
    if (idle || !this.dotnet) {
      // nothing to commit: put the objects back where they were
      this.dragGroup.position.copy(d.pivot); this.dragGroup.quaternion.identity(); this.dragGroup.scale.setScalar(1); this.dragGroup.updateMatrixWorld();
      for (const o of [...this.dragGroup.children]) this.content.attach(o);
      this.dragGroup.removeFromParent();
      this.dragGroup = null;
      return;
    }
    // the host applies the edit and sends a new scene, which replaces the preview
    const a = d.axis, p = d.pivot;
    if (this.gizmoMode === 'move') this.dotnet.invokeMethodAsync('OnGizmoFromJs', 'move', a.x * d.value, a.y * d.value, a.z * d.value, a.x, a.y, a.z, 0, p.x, p.y, p.z, 1);
    else if (this.gizmoMode === 'scale') this.dotnet.invokeMethodAsync('OnGizmoFromJs', 'scale', 0, 0, 0, a.x, a.y, a.z, 0, p.x, p.y, p.z, d.value);
    else this.dotnet.invokeMethodAsync('OnGizmoFromJs', 'rotate', 0, 0, 0, a.x, a.y, a.z, d.value, p.x, p.y, p.z, 1);
  }

  #pick(e) {
    if (!this.selectable || !this.dotnet) return;
    const meshes = [];
    this.content.traverse(o => { if (o.isMesh && o.userData.body !== undefined) meshes.push(o); });
    const hit = this.#ray(e).intersectObjects(meshes, false)[0];
    const pt = hit?.point;
    const n = hit?.face ? hit.face.normal.clone().transformDirection(hit.object.matrixWorld) : null;
    this.dotnet.invokeMethodAsync('OnPickFromJs', hit ? hit.object.userData.body : -1, pt?.x ?? 0, pt?.y ?? 0, pt?.z ?? 0, e.shiftKey || e.ctrlKey, n?.x ?? 0, n?.y ?? 0, n?.z ?? 0);
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
    let last = null, mode = 0, down = null;
    el.addEventListener('contextmenu', e => e.preventDefault());
    el.addEventListener('pointerdown', e => {
      down = e.button === 0 ? [e.clientX, e.clientY] : null;
      if (e.button === 0 && this.#gizmoDown(e)) { down = null; return; }
      try { el.setPointerCapture(e.pointerId); } catch { /* pen / synthetic pointers */ }
      last = [e.clientX, e.clientY];
      mode = (e.button === 2 || e.button === 1 || e.shiftKey) ? 2 : 1;
      this.dragging = true;
    });
    el.addEventListener('pointermove', e => {
      if (this.gizmoDrag) { this.#gizmoMove(e); return; }
      if (!last) { this.#hover(e); return; }
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
    el.addEventListener('pointerup', e => {
      if (this.gizmoDrag) { this.#gizmoUp(); return; }
      // a click (no drag) picks a body
      if (down && Math.hypot(e.clientX - down[0], e.clientY - down[1]) < 4) this.#pick(e);
      down = null;
      end();
    });
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
    this.dragGroup = null;
    this.gizmoDrag = null;
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
