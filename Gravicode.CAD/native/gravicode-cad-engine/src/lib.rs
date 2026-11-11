//! Gravicode.CAD native engine.
//!
//! Hot-path mesh kernels used by the .NET SDK through `Gravicode.CAD.Native`.
//! All functions take flat `f64` position arrays (x,y,z per vertex) and `i32` triangle
//! index arrays, never allocate memory that crosses the boundary, and return a status code:
//! `0` = ok, negative = error (see `GC_ERR_*`).
//!
//! Made by Gravicode Studios, led by Kang Fadhil.

use std::collections::HashMap;
use std::ffi::c_char;
use std::slice;
use std::thread;

pub const GC_OK: i32 = 0;
pub const GC_ERR_NULL: i32 = -1;
pub const GC_ERR_RANGE: i32 = -2;
pub const GC_ERR_CAPACITY: i32 = -3;

static VERSION: &[u8] = b"gravicode-cad-engine 1.0.0 (Rust)\0";

#[inline]
fn sub(a: [f64; 3], b: [f64; 3]) -> [f64; 3] { [a[0] - b[0], a[1] - b[1], a[2] - b[2]] }
#[inline]
fn cross(a: [f64; 3], b: [f64; 3]) -> [f64; 3] {
    [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]
}
#[inline]
fn dot(a: [f64; 3], b: [f64; 3]) -> f64 { a[0] * b[0] + a[1] * b[1] + a[2] * b[2] }
#[inline]
fn len(a: [f64; 3]) -> f64 { dot(a, a).sqrt() }
#[inline]
fn vert(p: &[f64], i: usize) -> [f64; 3] { [p[i * 3], p[i * 3 + 1], p[i * 3 + 2]] }

/// Validates inputs and returns safe slices.
unsafe fn mesh_slices<'a>(pos: *const f64, nverts: usize, idx: *const i32, nidx: usize) -> Result<(&'a [f64], &'a [i32]), i32> {
    if (pos.is_null() && nverts > 0) || (idx.is_null() && nidx > 0) { return Err(GC_ERR_NULL); }
    let p = if nverts == 0 { &[][..] } else { slice::from_raw_parts(pos, nverts * 3) };
    let i = if nidx == 0 { &[][..] } else { slice::from_raw_parts(idx, nidx) };
    if i.iter().any(|&v| v < 0 || v as usize >= nverts) { return Err(GC_ERR_RANGE); }
    Ok((p, i))
}

/// Version string (static, NUL terminated).
#[no_mangle]
pub extern "C" fn gc_engine_version() -> *const c_char { VERSION.as_ptr() as *const c_char }

/// Number of worker threads the engine will use.
#[no_mangle]
pub extern "C" fn gc_engine_threads() -> i32 {
    thread::available_parallelism().map(|n| n.get() as i32).unwrap_or(1)
}

/// Mass properties: out[0]=signed volume, out[1]=area, out[2..5]=centroid.
/// Large meshes are processed in parallel chunks.
#[no_mangle]
pub unsafe extern "C" fn gc_mesh_properties(pos: *const f64, nverts: usize, idx: *const i32, nidx: usize, out: *mut f64) -> i32 {
    if out.is_null() { return GC_ERR_NULL; }
    let (p, ix) = match mesh_slices(pos, nverts, idx, nidx) { Ok(v) => v, Err(e) => return e };
    let ntri = ix.len() / 3;

    let work = |range: std::ops::Range<usize>| -> [f64; 5] {
        let mut acc = [0.0f64; 5];
        for t in range {
            let a = vert(p, ix[t * 3] as usize);
            let b = vert(p, ix[t * 3 + 1] as usize);
            let c = vert(p, ix[t * 3 + 2] as usize);
            let v = dot(a, cross(b, c)) / 6.0;
            acc[0] += v;
            acc[1] += len(cross(sub(b, a), sub(c, a))) * 0.5;
            acc[2] += v * (a[0] + b[0] + c[0]);
            acc[3] += v * (a[1] + b[1] + c[1]);
            acc[4] += v * (a[2] + b[2] + c[2]);
        }
        acc
    };

    let total = if ntri < 200_000 {
        work(0..ntri)
    } else {
        let threads = gc_engine_threads().max(1) as usize;
        let chunk = (ntri + threads - 1) / threads;
        thread::scope(|s| {
            let handles: Vec<_> = (0..threads)
                .map(|k| { let r = (k * chunk).min(ntri)..((k + 1) * chunk).min(ntri); s.spawn(move || work(r)) })
                .collect();
            handles.into_iter().map(|h| h.join().unwrap()).fold([0.0; 5], |mut a, b| { for i in 0..5 { a[i] += b[i]; } a })
        })
    };

    let o = slice::from_raw_parts_mut(out, 5);
    o[0] = total[0];
    o[1] = total[1];
    if total[0].abs() > 1e-300 {
        o[2] = total[2] / (4.0 * total[0]);
        o[3] = total[3] / (4.0 * total[0]);
        o[4] = total[4] / (4.0 * total[0]);
    } else { o[2] = 0.0; o[3] = 0.0; o[4] = 0.0; }
    GC_OK
}

/// Area-weighted smooth vertex normals; `out_normals` holds nverts*3 doubles.
#[no_mangle]
pub unsafe extern "C" fn gc_mesh_vertex_normals(pos: *const f64, nverts: usize, idx: *const i32, nidx: usize, out_normals: *mut f64) -> i32 {
    if out_normals.is_null() && nverts > 0 { return GC_ERR_NULL; }
    let (p, ix) = match mesh_slices(pos, nverts, idx, nidx) { Ok(v) => v, Err(e) => return e };
    let n = slice::from_raw_parts_mut(out_normals, nverts * 3);
    n.fill(0.0);
    for t in 0..ix.len() / 3 {
        let (ia, ib, ic) = (ix[t * 3] as usize, ix[t * 3 + 1] as usize, ix[t * 3 + 2] as usize);
        let a = vert(p, ia);
        let f = cross(sub(vert(p, ib), a), sub(vert(p, ic), a));
        for &v in &[ia, ib, ic] { n[v * 3] += f[0]; n[v * 3 + 1] += f[1]; n[v * 3 + 2] += f[2]; }
    }
    for v in 0..nverts {
        let l = len([n[v * 3], n[v * 3 + 1], n[v * 3 + 2]]);
        if l > 0.0 { n[v * 3] /= l; n[v * 3 + 1] /= l; n[v * 3 + 2] /= l; }
    }
    GC_OK
}

/// Grid-snapping vertex weld. Writes, for each input vertex, the index of its representative
/// (first vertex seen in the same tolerance cell) into `out_remap`, and the unique count.
#[no_mangle]
pub unsafe extern "C" fn gc_mesh_weld(pos: *const f64, nverts: usize, tolerance: f64, out_remap: *mut i32, out_unique: *mut usize) -> i32 {
    if out_remap.is_null() || out_unique.is_null() { return GC_ERR_NULL; }
    let (p, _) = match mesh_slices(pos, nverts, std::ptr::null(), 0) { Ok(v) => v, Err(e) => return e };
    let inv = 1.0 / tolerance.max(1e-12);
    let remap = slice::from_raw_parts_mut(out_remap, nverts);
    let mut map: HashMap<(i64, i64, i64), i32> = HashMap::with_capacity(nverts);
    let mut unique = 0usize;
    for i in 0..nverts {
        let k = ((p[i * 3] * inv).round() as i64, (p[i * 3 + 1] * inv).round() as i64, (p[i * 3 + 2] * inv).round() as i64);
        let id = *map.entry(k).or_insert_with(|| { let id = unique as i32; unique += 1; id });
        remap[i] = id;
    }
    *out_unique = unique;
    GC_OK
}

/// Intersects the mesh with z = `z`. Writes segments as 6 doubles (ax,ay,az,bx,by,bz).
/// `capacity` is the number of segments the output buffer can hold (ntri is always enough).
#[no_mangle]
pub unsafe extern "C" fn gc_mesh_slice_z(pos: *const f64, nverts: usize, idx: *const i32, nidx: usize, z: f64,
                                         out_segments: *mut f64, capacity: usize, out_count: *mut usize) -> i32 {
    if out_count.is_null() || (out_segments.is_null() && capacity > 0) { return GC_ERR_NULL; }
    let (p, ix) = match mesh_slices(pos, nverts, idx, nidx) { Ok(v) => v, Err(e) => return e };
    let out = if capacity == 0 { &mut [][..] } else { slice::from_raw_parts_mut(out_segments, capacity * 6) };
    let mut count = 0usize;
    for t in 0..ix.len() / 3 {
        let tri = [vert(p, ix[t * 3] as usize), vert(p, ix[t * 3 + 1] as usize), vert(p, ix[t * 3 + 2] as usize)];
        let mut pts = [[0.0f64; 3]; 3];
        let mut n = 0;
        for e in 0..3 {
            let (mut a, mut b) = (tri[e], tri[(e + 1) % 3]);
            if (a[2] >= z) != (b[2] >= z) && n < 3 {
                // canonical order: neighbouring triangles produce bit-identical points on shared edges
                if a[2] > b[2] { std::mem::swap(&mut a, &mut b); }
                let s = (z - a[2]) / (b[2] - a[2]);
                pts[n] = [a[0] + (b[0] - a[0]) * s, a[1] + (b[1] - a[1]) * s, z];
                n += 1;
            }
        }
        if n >= 2 {
            let d = sub(pts[1], pts[0]);
            if dot(d, d) <= 1e-24 { continue; }
            if count >= capacity { return GC_ERR_CAPACITY; }
            out[count * 6..count * 6 + 3].copy_from_slice(&pts[0]);
            out[count * 6 + 3..count * 6 + 6].copy_from_slice(&pts[1]);
            count += 1;
        }
    }
    *out_count = count;
    GC_OK
}

#[cfg(test)]
mod tests {
    use super::*;

    fn cube() -> (Vec<f64>, Vec<i32>) {
        let p = vec![0., 0., 0., 1., 0., 0., 1., 1., 0., 0., 1., 0., 0., 0., 1., 1., 0., 1., 1., 1., 1., 0., 1., 1.];
        let q = [0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7];
        let mut i = vec![];
        for f in q.chunks(4) { i.extend_from_slice(&[f[0], f[1], f[2], f[0], f[2], f[3]]); }
        (p, i)
    }

    #[test]
    fn unit_cube_properties() {
        let (p, i) = cube();
        let mut out = [0.0; 5];
        assert_eq!(unsafe { gc_mesh_properties(p.as_ptr(), 8, i.as_ptr(), i.len(), out.as_mut_ptr()) }, GC_OK);
        assert!((out[0] - 1.0).abs() < 1e-12);
        assert!((out[1] - 6.0).abs() < 1e-12);
        assert!((out[2] - 0.5).abs() < 1e-12);
    }

    #[test]
    fn slice_cube() {
        let (p, i) = cube();
        let mut out = vec![0.0; 12 * 6];
        let mut n = 0usize;
        assert_eq!(unsafe { gc_mesh_slice_z(p.as_ptr(), 8, i.as_ptr(), i.len(), 0.5, out.as_mut_ptr(), 12, &mut n) }, GC_OK);
        assert_eq!(n, 8);
    }

    #[test]
    fn rejects_bad_index() {
        let p = [0.0; 9];
        let i = [0, 1, 7];
        let mut out = [0.0; 5];
        assert_eq!(unsafe { gc_mesh_properties(p.as_ptr(), 3, i.as_ptr(), 3, out.as_mut_ptr()) }, GC_ERR_RANGE);
    }
}
