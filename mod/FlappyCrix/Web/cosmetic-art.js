/* cosmetic-art.js — how a worn cosmetic is drawn
 *
 * Shared by the game and the owner console. It lived inside flappycrix.html
 * until the console needed to draw the same hats, and a second copy of these
 * shapes would have started drifting from the first the next time one was
 * nudged — which is exactly the bug that made a hat sit in one place on the
 * bird and another in the locker.
 *
 * Everything here is pure drawing: no image loading, no Firestore, no game
 * state. The caller passes in the image (or null), the fit and the wobble,
 * so the same function serves a canvas in the game, a card in the shop and a
 * preview in the console.
 *
 * COORDINATES. Each shape draws in the bird's own space: the origin is the
 * middle of the sprite and `s` is the bird's size, so the sprite runs from
 * -s/2 to +s/2 on both axes.
 *
 * MEASURED OFF THE PIXELS (800x800 source, sampled row by row):
 *
 *      y = -0.50   the sprite's top edge — and already the crown of the
 *                  character's hood. There is NO headroom above the head.
 *      -0.50..-0.24  the hood: 98-100% dark pixels, about 0.86s wide
 *      -0.22       the face starts; it is lighter from here down
 *      -0.03       the eyes
 *      +0.38       the widest row, the shoulders
 *
 * That top line is the one that matters and the one the first version of
 * this file got wrong. It assumed a hat could perch at -0.40 in clear air.
 * There is no clear air at -0.40: it is the middle of the hood, so every
 * band drew as a bar painted across the character's head. A hat rests its
 * brim at HEAD_TOP and puts its body ABOVE the sprite, in negative space.
 */
(function (root) {
    'use strict';

    /* ---- THE HEAD, MEASURED ----
       The character wears a big black hat of its own, tilted to the right.
       Every cosmetic used to be stacked on top of that hat, so each one sat
       high above the head and the black brim stuck out underneath it. Now a
       worn hat is drawn on img/bird-bare.png — the same sprite with its hat
       taken off, down to the red hood — and fits the hood itself:

           crown of the hood   y -0.368 at x +0.01
           the dome            y ≈ HEAD_TOP + 1.4 x²   (-0.28 at x ±0.25)
           hood width          0.64 where a hat band sits
           eyes                (-0.14, -0.05) and (+0.09, -0.05)
           face                centred on x -0.025, y -0.05
       (fractions of the sprite's size, origin at its centre, facing +x) */
    const HEAD_CX  = 0.01;
    const HEAD_TOP = -0.368;
    const HEAD_W   = 0.64;
    const FACE_CX  = -0.025, FACE_CY = -0.05;
    const EYE_L = -0.1375, EYE_R = 0.0875, EYE_Y = -0.05;
    const domeY  = x => HEAD_TOP + 1.4 * (x - HEAD_CX) * (x - HEAD_CX);
    const brimY  = x => domeY(x) + 0.05;             // a hat sits down onto the head

    // One outline for every hat: the near-black the sprite itself is lined in
    const INK = '#170C1C';
    function hatStroke(g, s, colour, width) {
        g.strokeStyle = colour || INK;
        g.lineWidth = Math.max(1, s * (width || 0.034));
        g.lineJoin = 'round';
        g.lineCap = 'round';
        g.stroke();
    }
    // roundRect only arrived in Chrome 99 and Safari 16.4. Calling it on
    // anything older throws, and this runs inside the draw loop, so it
    // would take out the whole frame rather than one hat.
    function hatRect(g, x, y, w, h, r) {
        if (typeof g.roundRect === 'function') { g.roundRect(x, y, w, h, r); return; }
        g.moveTo(x + r, y);
        g.arcTo(x + w, y,     x + w, y + h, r);
        g.arcTo(x + w, y + h, x,     y + h, r);
        g.arcTo(x,     y + h, x,     y,     r);
        g.arcTo(x,     y,     x + w, y,     r);
        g.closePath();
    }
    function hatBlob(g, s, fill, path, strokeColour, strokeWidth) {
        g.fillStyle = fill;
        g.beginPath();
        path();
        g.fill();
        if (strokeColour !== false) hatStroke(g, s, strokeColour, strokeWidth);
    }
    // A curve through three points, as a quadratic (the control point is
    // solved so the middle point is actually on the line).
    function through(g, s, x0, y0, xm, ym, x1, y1) {
        g.quadraticCurveTo(xm * s * 2 - (x0 + x1) * s / 2, ym * s * 2 - (y0 + y1) * s / 2, x1 * s, y1 * s);
    }
    // A band that hugs the dome between two x positions, `h` deep
    function domeBand(g, s, x0, x1, h, drop) {
        const d = drop || 0, xm = (x0 + x1) / 2;
        g.moveTo(x0 * s, (brimY(x0) + d) * s);
        through(g, s, x0, brimY(x0) + d, xm, brimY(xm) + d, x1, brimY(x1) + d);
        g.lineTo(x1 * s, (brimY(x1) + d - h) * s);
        through(g, s, x1, brimY(x1) + d - h, xm, brimY(xm) + d - h, x0, brimY(x0) + d - h);
        g.closePath();
    }

    /* Gradients cost an allocation each, and a hat is redrawn every frame for
       every player on screen, so the handful in use are kept by shape+size. */
    const _grads = new Map();
    function shade(g, s, key, x0, y0, x1, y1, stops) {
        const id = key + '@' + Math.round(s);
        let gr = _grads.get(id);
        if (!gr) {
            gr = g.createLinearGradient(x0 * s, y0 * s, x1 * s, y1 * s);
            stops.forEach(st => gr.addColorStop(st[0], st[1]));
            _grads.set(id, gr);
            if (_grads.size > 120) _grads.clear();   // sizes change; don't hoard
        }
        return gr;
    }

    /* Rotate a dangling part about the point it hangs from. The pivot is
       where the part meets the hat, so an ear bends at its root and a cap's
       tip swings from the brim, which is how they do it. */
    function swingAbout(g, px, py, ang, draw) {
        if (!ang) { draw(); return; }
        g.save();
        g.translate(px, py);
        g.rotate(ang);
        g.translate(-px, -py);
        draw();
        g.restore();
    }
    function dot(g, s, x, y, r, fill) {
        g.fillStyle = fill; g.beginPath(); g.arc(x * s, y * s, r * s, 0, Math.PI * 2); g.fill();
    }

    /* Each shape takes (context, size, swing). `swing` is radians and is 0
       when the caller has no physics to offer — every shape must look right
       at 0, because the shop cards and the catalogue draw them still.
       One look for all of them: a flat base colour, a shadow side, a single
       highlight, and the sprite's own dark outline. They are drawn to read
       at 30px first and at 150px second. */
    const HAT_SHAPES = {
        // A gold crown sitting on the dome, five points and three stones
        crown(g, s) {
            const xL = -0.29, xR = 0.31, band = 0.12;
            const tips = [[-0.27, -0.60], [-0.13, -0.68], [0.01, -0.72], [0.15, -0.68], [0.29, -0.60]];
            const gold = shade(g, s, 'crown', -0.3, -0.7, 0.32, -0.3,
                               [[0, '#FFF1A8'], [0.35, '#FFD23A'], [0.75, '#F2A900'], [1, '#B87400']]);
            hatBlob(g, s, gold, () => {
                g.moveTo(xL * s, brimY(xL) * s);
                through(g, s, xL, brimY(xL), HEAD_CX, brimY(HEAD_CX), xR, brimY(xR));
                g.lineTo(xR * s, (brimY(xR) - band) * s);
                // the points, right to left, dipping to the band between them
                for (let i = tips.length - 1; i >= 0; i--) {
                    const [tx, ty] = tips[i];
                    g.lineTo(tx * s, ty * s);
                    if (i > 0) {
                        const vx = (tx + tips[i - 1][0]) / 2;
                        g.lineTo(vx * s, (brimY(vx) - band - 0.035) * s);
                    }
                }
                g.lineTo(xL * s, (brimY(xL) - band) * s);
                g.closePath();
            });
            // the band's lower rim, a shade darker
            hatBlob(g, s, '#D99200', () => domeBand(g, s, xL + 0.01, xR - 0.01, 0.04), false);
            tips.forEach(([tx, ty]) => { dot(g, s, tx, ty, 0.035, '#FFE680'); });
            g.beginPath(); tips.forEach(([tx, ty]) => { g.moveTo((tx + 0.035) * s, ty * s); g.arc(tx * s, ty * s, 0.035 * s, 0, Math.PI * 2); });
            hatStroke(g, s, INK, 0.022);
            // stones
            const st = [[-0.15, '#2F7BFF'], [HEAD_CX, '#FF2E4D'], [0.17, '#2F7BFF']];
            st.forEach(([x, c]) => {
                const y = brimY(x) - band * 0.55;
                g.fillStyle = c; g.beginPath(); g.ellipse(x * s, y * s, 0.045 * s, 0.038 * s, 0, 0, Math.PI * 2); g.fill();
                hatStroke(g, s, INK, 0.018);
                dot(g, s, x - 0.015, y - 0.012, 0.012, 'rgba(255,255,255,.85)');
            });
        },

        // The CRIXGAMING VR cap: a black six-panel crown, the bill forward
        cap(g, s) {
            const xL = -0.31, xR = 0.31, top = -0.64;
            // bill first, so the crown's edge sits over its root
            hatBlob(g, s, shade(g, s, 'capbill', 0.1, -0.3, 0.6, -0.2, [[0, '#2A2A33'], [1, '#131318']]), () => {
                g.moveTo(0.12 * s, (brimY(0.12) + 0.005) * s);
                g.quadraticCurveTo(0.46 * s, (brimY(0.3) - 0.07) * s, 0.62 * s, (brimY(0.3) + 0.03) * s);
                g.quadraticCurveTo(0.5 * s, (brimY(0.3) + 0.075) * s, 0.26 * s, (brimY(0.26) + 0.045) * s);
                g.closePath();
            });
            hatBlob(g, s, shade(g, s, 'cap', -0.3, -0.64, 0.3, -0.3, [[0, '#4A4A57'], [0.45, '#26262F'], [1, '#121217']]), () => {
                g.moveTo(xL * s, brimY(xL) * s);
                g.bezierCurveTo(xL * s, (top + 0.02) * s, (xR - 0.04) * s, (top - 0.02) * s, xR * s, brimY(xR) * s);
                through(g, s, xR, brimY(xR), HEAD_CX, brimY(HEAD_CX), xL, brimY(xL));
                g.closePath();
            });
            // panel seams and the button
            g.beginPath();
            g.moveTo(0.02 * s, (top + 0.035) * s); g.quadraticCurveTo(0.1 * s, -0.48 * s, 0.13 * s, (brimY(0.13) - 0.01) * s);
            g.moveTo(0.02 * s, (top + 0.035) * s); g.quadraticCurveTo(-0.12 * s, -0.48 * s, -0.16 * s, (brimY(-0.16) - 0.01) * s);
            hatStroke(g, s, 'rgba(255,255,255,.14)', 0.016);
            dot(g, s, 0.02, top + 0.035, 0.03, '#2E2E38');
            // the red logo on the front panel
            g.fillStyle = '#FF4655';
            g.beginPath(); hatRect(g, 0.05 * s, -0.5 * s, 0.17 * s, 0.065 * s, 0.015 * s); g.fill();
            g.fillStyle = '#FFFFFF'; g.fillRect(0.075 * s, -0.475 * s, 0.12 * s, 0.014 * s);
        },

        // The CRIXGAMING VR bucket hat: soft round crown, brim turned down all round
        bucket(g, s) {
            const by = brimY(HEAD_CX) + 0.03, top = -0.62;
            // the brim: a wide, shallow bowl under the crown
            hatBlob(g, s, shade(g, s, 'bucketbrim', 0, by - 0.1, 0, by + 0.1, [[0, '#3A3A46'], [1, '#141419']]), () => {
                g.moveTo(-0.47 * s, (by + 0.02) * s);
                g.quadraticCurveTo(HEAD_CX * s, (by - 0.13) * s, 0.49 * s, (by + 0.02) * s);
                g.quadraticCurveTo(0.44 * s, (by + 0.11) * s, HEAD_CX * s, (by + 0.1) * s);
                g.quadraticCurveTo(-0.42 * s, (by + 0.11) * s, -0.47 * s, (by + 0.02) * s);
                g.closePath();
            });
            // the crown: tapered, with a soft rounded top
            hatBlob(g, s, shade(g, s, 'bucket', -0.3, top, 0.3, by, [[0, '#55556A'], [0.45, '#2A2A34'], [1, '#131318']]), () => {
                g.moveTo(-0.3 * s, (by - 0.02) * s);
                g.quadraticCurveTo(-0.29 * s, (top + 0.12) * s, -0.2 * s, (top + 0.02) * s);
                g.quadraticCurveTo(HEAD_CX * s, (top - 0.045) * s, 0.22 * s, (top + 0.02) * s);
                g.quadraticCurveTo(0.31 * s, (top + 0.12) * s, 0.32 * s, (by - 0.02) * s);
                g.quadraticCurveTo(HEAD_CX * s, (by - 0.09) * s, -0.3 * s, (by - 0.02) * s);
                g.closePath();
            });
            // a darker band round the foot of the crown
            hatBlob(g, s, '#101014', () => {
                g.moveTo(-0.3 * s, (by - 0.02) * s);
                g.quadraticCurveTo(HEAD_CX * s, (by - 0.09) * s, 0.32 * s, (by - 0.02) * s);
                g.lineTo(0.315 * s, (by - 0.075) * s);
                g.quadraticCurveTo(HEAD_CX * s, (by - 0.145) * s, -0.295 * s, (by - 0.075) * s);
                g.closePath();
            }, false);
            // stitching on the brim, and the red tag on the front
            g.beginPath();
            g.moveTo(-0.4 * s, (by + 0.045) * s);
            g.quadraticCurveTo(HEAD_CX * s, (by + 0.1) * s, 0.42 * s, (by + 0.045) * s);
            g.setLineDash([0.03 * s, 0.025 * s]);
            hatStroke(g, s, 'rgba(255,255,255,.25)', 0.012);
            g.setLineDash([]);
            g.fillStyle = '#FF4655';
            g.beginPath(); hatRect(g, 0.05 * s, (by - 0.27) * s, 0.16 * s, 0.07 * s, 0.015 * s); g.fill();
            g.fillStyle = '#FFFFFF'; g.fillRect(0.072 * s, (by - 0.243) * s, 0.115 * s, 0.015 * s);
        },

        // Santa's hat: a full red body over the whole crown that tips back
        // (the bird faces +x) and droops to a big pompom behind the head,
        // over a thick roll of fur. Only the drooping part swings, from the
        // fold, so the body stays put on the head.
        santa(g, s, w) {
            const xL = -0.35, xR = 0.37, fur = 0.13;
            const topL = brimY(xL) - fur + 0.035, topR = brimY(xR) - fur + 0.035;
            const F = [-0.08, -0.79];            // the fold, where it starts to droop
            const red = shade(g, s, 'santa', 0.35, -0.3, -0.35, -0.8,
                              [[0, '#B8121C'], [0.35, '#E3232E'], [0.75, '#FF4E58'], [1, '#D11A25']]);
            // the drooping end, behind the body so the fold overlaps it
            swingAbout(g, F[0] * s, F[1] * s, w * 1.1, () => {
                hatBlob(g, s, shade(g, s, 'santaflop', -0.2, -0.8, -0.6, -0.45,
                                    [[0, '#E3232E'], [1, '#A8101A']]), () => {
                    g.moveTo((F[0] + 0.05) * s, (F[1] + 0.005) * s);
                    g.bezierCurveTo(-0.3 * s, -0.86 * s, -0.56 * s, -0.74 * s, -0.63 * s, -0.5 * s);
                    g.quadraticCurveTo(-0.5 * s, -0.56 * s, -0.36 * s, -0.62 * s);
                    g.quadraticCurveTo(-0.24 * s, -0.67 * s, (F[0] - 0.07) * s, (F[1] + 0.09) * s);
                    g.closePath();
                });
                // pompom: a fluffy ball, lit from above
                const px = -0.64, py = -0.47, pr = 0.1;
                hatBlob(g, s, shade(g, s, 'santapom', px, py - pr, px, py + pr, [[0, '#FFFFFF'], [1, '#CFD8E3']]), () =>
                    g.arc(px * s, py * s, pr * s, 0, Math.PI * 2));
                dot(g, s, px - 0.035, py - 0.035, 0.035, 'rgba(255,255,255,.95)');
                dot(g, s, px + 0.03, py + 0.04, 0.022, 'rgba(160,175,195,.55)');
            });
            // the body: wide at the head, rising to the fold
            hatBlob(g, s, red, () => {
                g.moveTo(xR * s, topR * s);
                g.bezierCurveTo(0.34 * s, -0.56 * s, 0.14 * s, -0.8 * s, F[0] * s, F[1] * s);
                g.quadraticCurveTo(-0.27 * s, -0.72 * s, xL * s, topL * s);
                through(g, s, xL, topL, HEAD_CX, brimY(HEAD_CX) - fur + 0.035, xR, topR);
                g.closePath();
            });
            // a soft highlight down the body, and the crease at the fold
            hatBlob(g, s, 'rgba(255,255,255,.18)', () => {
                g.moveTo(0.2 * s, (topR - 0.03) * s);
                g.quadraticCurveTo(0.18 * s, -0.66 * s, 0.02 * s, -0.75 * s);
                g.quadraticCurveTo(0.1 * s, -0.62 * s, 0.1 * s, (topR - 0.03) * s);
                g.closePath();
            }, false);
            g.beginPath();
            g.moveTo((F[0] - 0.02) * s, (F[1] + 0.03) * s);
            g.quadraticCurveTo(-0.16 * s, -0.7 * s, -0.2 * s, -0.63 * s);
            hatStroke(g, s, 'rgba(90,0,8,.55)', 0.022);
            // the fur roll: thick, following the head, with a puffy edge
            const furFill = shade(g, s, 'santafur', 0, -0.5, 0, -0.2, [[0, '#FFFFFF'], [0.6, '#F1F4F8'], [1, '#C9D3DF']]);
            hatBlob(g, s, furFill, () => domeBand(g, s, xL - 0.02, xR + 0.02, fur, 0.035));
            g.save();
            g.globalAlpha = 0.55;
            for (let i = 0; i <= 8; i++) {
                const x = xL + (xR - xL) * i / 8, y = brimY(x) + 0.035 - fur * 0.45;
                dot(g, s, x, y + (i % 2 ? 0.02 : -0.015), 0.02, i % 2 ? '#D6DEE8' : '#FFFFFF');
            }
            g.restore();
        },

        // Antlers on a thin band, with a pair of soft ears. The ears swing.
        reindeer(g, s, w) {
            const antler = side => {
                const x0 = side < 0 ? -0.13 : 0.16, d = side;
                g.beginPath();
                g.moveTo(x0 * s, (domeY(x0) + 0.01) * s);
                g.quadraticCurveTo((x0 + 0.02 * d) * s, -0.58 * s, (x0 + 0.12 * d) * s, -0.8 * s);
                g.moveTo((x0 + 0.03 * d) * s, -0.6 * s);
                g.quadraticCurveTo((x0 + 0.12 * d) * s, -0.62 * s, (x0 + 0.2 * d) * s, -0.7 * s);
                g.moveTo((x0 + 0.01 * d) * s, -0.5 * s);
                g.quadraticCurveTo((x0 - 0.06 * d) * s, -0.56 * s, (x0 - 0.08 * d) * s, -0.66 * s);
                hatStroke(g, s, INK, 0.09);
                hatStroke(g, s, '#8A5A33', 0.058);
            };
            antler(-1); antler(1);
            const ear = (x, dir) => swingAbout(g, x * s, brimY(x) * s, w * 0.8 * dir, () => {
                g.save(); g.translate((x + 0.07 * dir) * s, (brimY(x) - 0.02) * s); g.rotate(dir * 0.9);
                hatBlob(g, s, '#A0693D', () => g.ellipse(0, 0, 0.1 * s, 0.05 * s, 0, 0, Math.PI * 2));
                hatBlob(g, s, '#F2A5B5', () => g.ellipse(0.01 * s * dir, 0, 0.055 * s, 0.022 * s, 0, 0, Math.PI * 2), false);
                g.restore();
            });
            ear(-0.29, -1); ear(0.3, 1);
            hatBlob(g, s, '#6B4526', () => domeBand(g, s, -0.3, 0.32, 0.045, -0.005));
        },

        // A tall purple hat with a bent tip, an orange band and a buckle
        witch(g, s, w) {
            const by = brimY(HEAD_CX) + 0.005;
            swingAbout(g, HEAD_CX * s, (by - 0.12) * s, w * 0.9, () => {
                hatBlob(g, s, shade(g, s, 'witch', -0.25, -0.9, 0.25, -0.4, [[0, '#8C55D9'], [0.5, '#5B2E9E'], [1, '#34175E']]), () => {
                    g.moveTo(-0.24 * s, (by - 0.02) * s);
                    g.quadraticCurveTo(-0.12 * s, -0.62 * s, -0.08 * s, -0.84 * s);
                    g.quadraticCurveTo(-0.14 * s, -0.95 * s, -0.3 * s, -0.94 * s);     // the tip, bent back
                    g.quadraticCurveTo(-0.06 * s, -1.0 * s, 0.05 * s, -0.84 * s);
                    g.quadraticCurveTo(0.14 * s, -0.62 * s, 0.26 * s, (by - 0.02) * s);
                    g.closePath();
                });
            });
            // band and buckle, just above the brim
            hatBlob(g, s, '#FF8A1F', () => {
                g.moveTo(-0.215 * s, (by - 0.06) * s); g.lineTo(0.235 * s, (by - 0.06) * s);
                g.lineTo(0.205 * s, (by - 0.14) * s); g.lineTo(-0.19 * s, (by - 0.14) * s); g.closePath();
            });
            g.beginPath(); hatRect(g, -0.035 * s, (by - 0.145) * s, 0.09 * s, 0.09 * s, 0.01 * s);
            g.fillStyle = '#FFD84A'; g.fill(); hatStroke(g, s, INK, 0.02);
            g.fillStyle = '#FF8A1F'; g.fillRect(-0.01 * s, (by - 0.12) * s, 0.04 * s, 0.04 * s);
            // brim, in front of the cone's foot
            hatBlob(g, s, shade(g, s, 'witchbrim', 0, by - 0.06, 0, by + 0.06, [[0, '#5E2FA3'], [1, '#2B1250']]), () =>
                g.ellipse(HEAD_CX * s, (by + 0.01) * s, 0.47 * s, 0.065 * s, -0.05, 0, Math.PI * 2));
        },

        // A skull over the face: sockets over the eyes, teeth over the mouth
        skull(g, s) {
            const cx = FACE_CX + 0.01;
            hatBlob(g, s, shade(g, s, 'skull', 0, -0.34, 0, 0.24, [[0, '#FFFDF6'], [0.7, '#EDE6D6'], [1, '#CFC5B0']]), () => {
                g.moveTo((cx - 0.29) * s, 0.0 * s);
                g.bezierCurveTo((cx - 0.31) * s, -0.37 * s, (cx + 0.31) * s, -0.37 * s, (cx + 0.29) * s, 0.0 * s);
                g.quadraticCurveTo((cx + 0.28) * s, 0.09 * s, (cx + 0.18) * s, 0.11 * s);
                g.lineTo((cx + 0.16) * s, 0.22 * s);
                g.lineTo((cx - 0.16) * s, 0.22 * s);
                g.lineTo((cx - 0.18) * s, 0.11 * s);
                g.quadraticCurveTo((cx - 0.28) * s, 0.09 * s, (cx - 0.29) * s, 0.0 * s);
                g.closePath();
            });
            // sockets, over the sprite's own eyes
            [EYE_L, EYE_R].forEach(ex => {
                hatBlob(g, s, '#1C1022', () => g.ellipse(ex * s, EYE_Y * s, 0.088 * s, 0.08 * s, 0, 0, Math.PI * 2), false);
                dot(g, s, ex + 0.012, EYE_Y + 0.006, 0.026, '#B05CFF');
            });
            // nose
            hatBlob(g, s, '#1C1022', () => {
                g.moveTo((cx - 0.012) * s, 0.04 * s); g.lineTo((cx - 0.05) * s, 0.095 * s); g.lineTo((cx + 0.025) * s, 0.095 * s); g.closePath();
            }, false);
            // teeth
            g.beginPath();
            g.moveTo((cx - 0.15) * s, 0.165 * s); g.lineTo((cx + 0.15) * s, 0.165 * s);
            for (let i = -2; i <= 2; i++) { g.moveTo((cx + i * 0.058) * s, 0.13 * s); g.lineTo((cx + i * 0.058) * s, 0.215 * s); }
            hatStroke(g, s, INK, 0.018);
        },

        // A carved pumpkin in place of the head, lit from inside
        pumpkin(g, s) {
            const cx = FACE_CX + 0.015, cy = -0.07, rx = 0.38, ry = 0.34;
            // stem
            hatBlob(g, s, '#4E7A2A', () => {
                g.moveTo((cx - 0.03) * s, (cy - ry + 0.03) * s);
                g.quadraticCurveTo((cx - 0.02) * s, (cy - ry - 0.09) * s, (cx + 0.06) * s, (cy - ry - 0.12) * s);
                g.lineTo((cx + 0.075) * s, (cy - ry - 0.08) * s);
                g.quadraticCurveTo((cx + 0.03) * s, (cy - ry - 0.05) * s, (cx + 0.04) * s, (cy - ry + 0.03) * s);
                g.closePath();
            });
            const body = shade(g, s, 'pumpkin', -0.3, -0.4, 0.35, 0.25, [[0, '#FFB14A'], [0.45, '#FF8A1C'], [1, '#C4520A']]);
            hatBlob(g, s, body, () => g.ellipse(cx * s, cy * s, rx * s, ry * s, 0, 0, Math.PI * 2));
            // ribs
            g.beginPath();
            [-0.2, 0.2].forEach(o => {
                g.moveTo((cx + o * 0.35) * s, (cy - ry + 0.02) * s);
                g.quadraticCurveTo((cx + o * 1.3) * s, cy * s, (cx + o * 0.35) * s, (cy + ry - 0.02) * s);
            });
            hatStroke(g, s, 'rgba(150,60,0,.55)', 0.02);
            // carved face, glowing
            const glow = '#FFD84D';
            [[EYE_L, -1], [EYE_R, 1]].forEach(([ex]) => hatBlob(g, s, glow, () => {
                g.moveTo((ex - 0.07) * s, (EYE_Y + 0.04) * s); g.lineTo(ex * s, (EYE_Y - 0.06) * s);
                g.lineTo((ex + 0.07) * s, (EYE_Y + 0.04) * s); g.closePath();
            }, INK, 0.018));
            hatBlob(g, s, glow, () => {
                g.moveTo((cx - 0.02) * s, 0.0 * s); g.lineTo((cx - 0.05) * s, 0.05 * s); g.lineTo((cx + 0.01) * s, 0.05 * s); g.closePath();
            }, INK, 0.015);
            hatBlob(g, s, glow, () => {
                const y = 0.11, x0 = cx - 0.2, x1 = cx + 0.2;
                g.moveTo(x0 * s, (y - 0.02) * s);
                g.quadraticCurveTo(cx * s, (y + 0.06) * s, x1 * s, (y - 0.02) * s);
                g.quadraticCurveTo(cx * s, (y + 0.16) * s, x0 * s, (y - 0.02) * s);
                g.closePath();
            }, INK, 0.018);
            g.fillStyle = '#FF8A1C';
            g.fillRect((cx - 0.08) * s, 0.12 * s, 0.05 * s, 0.035 * s);
            g.fillRect((cx + 0.04) * s, 0.12 * s, 0.05 * s, 0.035 * s);
        },

        // A gold party cone with a big 1 on the front and a pompom on top,
        // tipped a little to one side. The whole cone sways from its base.
        party(g, s, w) {
            const xL = -0.21, xR = 0.24, base = 0.03;
            const yL = brimY(xL) + base, yR = brimY(xR) + base;
            const tipX = HEAD_CX + 0.08, tipY = -0.97;
            const cone = () => {
                g.moveTo(xL * s, yL * s);
                g.lineTo(tipX * s, tipY * s);
                g.lineTo(xR * s, yR * s);
                through(g, s, xR, yR, HEAD_CX, brimY(HEAD_CX) + base + 0.02, xL, yL);
                g.closePath();
            };
            swingAbout(g, HEAD_CX * s, (brimY(HEAD_CX) + base) * s, w * 0.8, () => {
                hatBlob(g, s, shade(g, s, 'party', xL, -0.6, xR, -0.6,
                        [[0, '#8A5A00'], [0.22, '#D99A00'], [0.5, '#FFE27A'], [0.78, '#F2B705'], [1, '#9C6A00']]), cone);
                // darker gold stripes wrapped round the cone
                g.save();
                g.beginPath(); cone(); g.clip();
                g.strokeStyle = 'rgba(140,85,0,.55)';
                g.lineWidth = Math.max(1, s * 0.05);
                for (let i = 0; i < 4; i++) {
                    const y = -0.86 + i * 0.17;
                    g.beginPath();
                    g.moveTo(-0.4 * s, (y + 0.1) * s);
                    g.lineTo(0.4 * s, (y - 0.06) * s);
                    g.stroke();
                }
                g.restore();
                g.beginPath(); cone(); hatStroke(g, s, INK, 0.026);
                // the 1, on a white badge so it reads at 30px
                const bx = HEAD_CX + 0.035, by = -0.46, br = 0.12;
                hatBlob(g, s, '#FFFDF3', () => g.arc(bx * s, by * s, br * s, 0, Math.PI * 2), INK, 0.02);
                g.fillStyle = '#B07400';
                g.font = `900 ${Math.max(1, s * 0.2)}px Arial, Helvetica, sans-serif`;
                g.textAlign = 'center';
                g.textBaseline = 'middle';
                g.fillText('1', bx * s, (by + 0.012) * s);
                // pompom: gold tinsel, lit from above
                const pr = 0.085;
                hatBlob(g, s, shade(g, s, 'partypom', tipX, tipY - pr, tipX, tipY + pr,
                        [[0, '#FFF6C9'], [1, '#E8A400']]), () =>
                    g.arc(tipX * s, tipY * s, pr * s, 0, Math.PI * 2), INK, 0.02);
                dot(g, s, tipX - 0.03, tipY - 0.03, 0.028, 'rgba(255,255,255,.9)');
            });
        },

        // Two tall ears on a band, splayed a little. They swing.
        bunny(g, s, w) {
            const ear = (x, tilt) => swingAbout(g, x * s, domeY(x) * s, w * 1.2 + tilt, () => {
                const b = domeY(x) + 0.02;
                hatBlob(g, s, shade(g, s, 'bunny', -0.1, -1, 0.1, -0.3, [[0, '#FFFFFF'], [1, '#DCD6CF']]), () =>
                    g.ellipse(x * s, (b - 0.3) * s, 0.085 * s, 0.31 * s, 0, 0, Math.PI * 2));
                hatBlob(g, s, '#FFAFC6', () =>
                    g.ellipse(x * s, (b - 0.3) * s, 0.04 * s, 0.22 * s, 0, 0, Math.PI * 2), false);
            });
            ear(-0.11, -0.16); ear(0.14, 0.16);
            hatBlob(g, s, '#F4F1EC', () => domeBand(g, s, -0.31, 0.33, 0.05, -0.005));
        }
    };
    // A sprite centred on (cx, cy) inside a box, keeping its aspect ratio.
    function drawSprite(context, img, cx, cy, boxSize) {
        if (!img || !img.complete || !img.naturalWidth) return false;
        const ratio = img.naturalWidth / img.naturalHeight;
        let w = boxSize, h = boxSize;
        if (ratio > 1) h = boxSize / ratio;   // wider than tall
        else if (ratio < 1) w = boxSize * ratio;
        context.drawImage(img, cx - w / 2, cy - h / 2, w, h);
        return true;
    }

    // An image hat (artwork dropped into /img) rests its bottom edge on the
    // hood's brim line, as wide as the head plus a little overhang — the same
    // place the drawn shapes use, so swapping the artwork in does not move it.
    // `imgFit` is the item's own: how wide the picture is across the head
    // (w, of the bird's size) and how far below the brim line its bottom
    // edge sits (y), because a flat crown, a cap photographed from the front
    // and a bucket hat with a wide brim each meet the head differently. The
    // files are trimmed to their outline, so these numbers are the artwork.
    function drawHatImage(context, img, size, swing, imgFit) {
        if (!img || !img.complete || !img.naturalWidth) return;
        const f = imgFit || {};
        const ratio = img.naturalWidth / img.naturalHeight;
        const w = size * (f.w || HEAD_W + 0.16);
        const h = w / ratio;
        const cx = (HEAD_CX + (f.x || 0)) * size;
        const base = (brimY(HEAD_CX) + (f.y != null ? f.y : 0.04)) * size;
        swingAbout(context, cx, base, (swing || 0) * 1.1, () => {
            context.drawImage(img, cx - w / 2, base - h, w, h);
        });
    }

    const FIT_DEFAULT = { x: 0, y: 0, scale: 1, rot: 0 };

    /* ---- WOBBLE ----
     * A damped spring, one per worn item. The driver is the bird's vertical
     * velocity: fall and the dangly parts stream upward, flap and they whip
     * the other way, then they settle. It is integrated in ticks so it is the
     * same motion at any frame rate, and it is purely cosmetic — nothing here
     * is ever read back by the game or sent over the network.
     */
    const WOB_K = 0.16,      // stiffness
          WOB_D = 0.70,      // damping per tick: overshoots ~18% and is done
                             // in about a quarter-second, which reads as a
                             // hat with some life in it rather than a wobble
                             // that rings on for half a second after landing
          WOB_MAX = 0.52,    // hard stop, so nothing can ever look broken
          WOB_DRIVE = 0.030, // radians per unit of fall speed
          WOB_TARGET = 0.42;

    function makeWobble() { return { a: 0, v: 0 }; }
    function stepWobble(wob, velocity, ticks) {
        if (!wob) return 0;
        // Whole ticks only. The game runs a fixed 60Hz step and passes 1; a
        // preview that has been backgrounded passes more, and is capped so it
        // cannot catch up with one enormous lurch.
        const n = Math.max(0, Math.min(3, Math.round(ticks == null ? 1 : ticks)));
        // Where the part wants to hang, given how fast the bird is moving
        const target = Math.max(-WOB_TARGET, Math.min(WOB_TARGET, (velocity || 0) * WOB_DRIVE));
        for (let i = 0; i < n; i++) {
            wob.v += (target - wob.a) * WOB_K;
            wob.v *= WOB_D;
            wob.a += wob.v;
        }
        if (wob.a > WOB_MAX)  { wob.a = WOB_MAX;  wob.v = 0; }
        if (wob.a < -WOB_MAX) { wob.a = -WOB_MAX; wob.v = 0; }
        return wob.a;
    }

    /* The one entry point for a worn head item.
       `img` is a loaded image or null — artwork wins whenever there is any,
       and the drawn shape stands in until the file exists.
       `fit` offsets are fractions of `size`, not pixels, so a fit made against
       a big preview is the same fit on a 30px bird.
       `swing` is radians from stepWobble, or nothing at all for a still pose. */
    function drawHatOn(context, item, size, img, fit, swing) {
        if (!item) return;
        const f = Object.assign({}, FIT_DEFAULT, fit || {});
        const s = size * (f.scale || 1);
        const w = (item.rigid || !swing) ? 0 : swing;
        context.save();
        context.translate(f.x * size, f.y * size);
        if (f.rot) context.rotate(f.rot * Math.PI / 180);
        if (img) drawHatImage(context, img, s, w, item.imgFit);
        else if (item.draw && HAT_SHAPES[item.draw]) HAT_SHAPES[item.draw](context, s, w);
        context.restore();
    }

    /* ---- TRAILS ----
       A trail is a ribbon laid along the points behind the bird, oldest
       first. It used to be drawn as one short see-through round-capped
       stroke per point, each in the next colour of the list: every joint
       overlapped the last and doubled up into a bead, and the colours
       changed every couple of pixels, so a rainbow came out as speckle and
       every trail looked like a caterpillar.

       Now the ribbon is filled as quads between the points' edges, so
       neighbouring pieces meet exactly and nothing overlaps, and each trail
       has a style of its own:
         bands   the colours side by side across the ribbon (rainbow, pastel)
         fire    a hot core in a flame, with sparks that rise off it (ember)
         frost   an icy glow that glints (frost)
         toxic   a green glow that bubbles (toxic)
         void    a dark core in a purple halo, with motes that circle (void)
         ghost   a wisp that thins and drifts (ghost)
         tinsel  a gold ribbon scattered with glitter (tinsel)
       A particle belongs to the point it was born with (flat.seq counts the
       points ever pushed), so sparks ride along with the trail instead of
       flickering from place to place as the oldest point drops off. */
    /* Each layer is filled in a handful of pieces, each a step fainter
       toward the tail. Per point it was ~90 fills a frame for a rainbow,
       which cost a slow TV about six frames a second; five pieces a layer
       is ~30 and the steps are too small to see on a ribbon that narrows. */
    const TRAIL_PIECES = 5;
    function trailFrame(flat) {
        const n = flat.length / 2, P = [];
        for (let i = 0; i < n; i++) {
            const a = Math.max(0, i - 1), b = Math.min(n - 1, i + 1);
            let tx = flat[b * 2] - flat[a * 2], ty = flat[b * 2 + 1] - flat[a * 2 + 1];
            const l = Math.hypot(tx, ty) || 1;
            tx /= l; ty /= l;
            P.push({ x: flat[i * 2], y: flat[i * 2 + 1], nx: -ty, ny: tx, t: n > 1 ? i / (n - 1) : 1 });
        }
        return P;
    }
    // One strip of the ribbon between two offsets from the centre line.
    // Neighbouring pieces share their end points, so they meet exactly.
    function trailStrip(g, P, off0, off1, colour, alpha) {
        g.fillStyle = colour;
        const step = Math.max(1, Math.ceil((P.length - 1) / TRAIL_PIECES));
        for (let s = 0; s < P.length - 1; s += step) {
            const e = Math.min(s + step, P.length - 1);
            const a = alpha((P[s].t + P[e].t) / 2);
            if (a <= 0.01) continue;
            g.globalAlpha = Math.min(1, a);
            g.beginPath();
            for (let i = s; i <= e; i++) { const q = P[i], o = off0(q.t); g.lineTo(q.x + q.nx * o, q.y + q.ny * o); }
            for (let i = e; i >= s; i--) { const q = P[i], o = off1(q.t); g.lineTo(q.x + q.nx * o, q.y + q.ny * o); }
            g.closePath();
            g.fill();
        }
    }
    function trailGlow(g, P, hw, layers) {
        for (const L of layers) trailStrip(g, P, t => -hw(t) * L.w, t => hw(t) * L.w, L.c, t => L.a * Math.pow(t, L.p || 1));
    }
    // A cheap stable pseudo-random number per particle
    function rnd(k) { const x = Math.sin(k * 127.1 + 311.7) * 43758.5453; return x - Math.floor(x); }

    function drawTrail(context, flat, colours, size, style) {
        if (!flat || flat.length < 4 || !colours || !colours.length) return;
        const P = trailFrame(flat), n = P.length;
        const seq = flat.seq || n, now = (typeof performance !== 'undefined' ? performance.now() : 0);
        const fx = style || (colours.length >= 4 ? 'bands' : 'fire');
        const c = i => colours[Math.min(i, colours.length - 1)];
        const id = i => seq - (n - 1 - i);                    // which point this was, ever

        const g = context;
        g.save();

        if (fx === 'bands') {
            // Full width by the bird, narrowing and fading toward the tail
            const K = colours.length;
            const W = t => size * (0.34 + 0.44 * Math.sqrt(t));
            for (let k = 0; k < K; k++) {
                trailStrip(g, P,
                    t => -W(t) / 2 + W(t) * k / K - 0.3,
                    t => -W(t) / 2 + W(t) * (k + 1) / K + 0.3,
                    colours[k], t => 0.1 + 0.85 * Math.pow(t, 1.4));
            }
        } else {
            const hw = t => size * (0.06 + 0.3 * Math.pow(t, 0.8));   // half-width
            if (fx === 'fire') {
                trailGlow(g, P, hw, [{ c: c(2), w: 1.25, a: .32, p: 1.2 }, { c: c(1), w: .8, a: .8, p: 1.3 },
                                     { c: c(0), w: .38, a: .95, p: 1.6 }]);
                for (let i = 0; i < n; i++) {
                    const k = id(i); if (k % 3) continue;
                    const q = P[i], age = n - 1 - i;
                    g.globalAlpha = Math.max(0, q.t * 1.1 - .1);
                    g.fillStyle = rnd(k) > .5 ? c(0) : c(1);
                    g.beginPath();
                    g.arc(q.x + (rnd(k + 1) - .5) * size * .45 - age * .15,
                          q.y - age * .5 - rnd(k + 2) * size * .25, size * (.035 + .04 * rnd(k + 3)), 0, 7);
                    g.fill();
                }
            } else if (fx === 'frost') {
                trailGlow(g, P, hw, [{ c: c(2), w: 1.3, a: .3 }, { c: c(1), w: .75, a: .7, p: 1.2 },
                                     { c: c(0), w: .32, a: .95, p: 1.5 }]);
                g.strokeStyle = '#FFFFFF'; g.lineWidth = Math.max(1, size * .04);
                for (let i = 0; i < n; i++) {
                    const k = id(i); if (k % 4) continue;
                    const q = P[i], tw = .5 + .5 * Math.sin(now * .012 + k);
                    const r = size * (.07 + .07 * rnd(k)) * (.5 + tw * .5);
                    const x = q.x + (rnd(k + 1) - .5) * size * .6, y = q.y + (rnd(k + 2) - .5) * size * .6;
                    g.globalAlpha = q.t * tw;
                    g.beginPath(); g.moveTo(x - r, y); g.lineTo(x + r, y); g.moveTo(x, y - r); g.lineTo(x, y + r); g.stroke();
                }
            } else if (fx === 'toxic') {
                trailGlow(g, P, hw, [{ c: c(2), w: 1.3, a: .4 }, { c: c(1), w: .8, a: .85, p: 1.2 },
                                     { c: c(0), w: .35, a: .9, p: 1.6 }]);
                g.strokeStyle = c(0); g.lineWidth = Math.max(1, size * .035);
                for (let i = 0; i < n; i++) {
                    const k = id(i); if (k % 4) continue;
                    const q = P[i], age = n - 1 - i;
                    g.globalAlpha = q.t * .9;
                    g.beginPath();
                    g.arc(q.x + (rnd(k) - .5) * size * .5, q.y - age * .32 - size * .1,
                          size * (.04 + .06 * (1 - q.t) + .03 * rnd(k + 1)), 0, 7);
                    g.stroke();
                }
            } else if (fx === 'void') {
                trailGlow(g, P, hw, [{ c: c(1), w: 1.45, a: .45 }, { c: c(2), w: .95, a: .95, p: .9 },
                                     { c: c(0), w: .2, a: .55, p: 1.6 }]);
                for (let i = 0; i < n; i++) {
                    const k = id(i); if (k % 3) continue;
                    const q = P[i], ang = now * .004 + k, rr = size * (.28 + .22 * (1 - q.t));
                    g.globalAlpha = q.t * .9;
                    g.fillStyle = rnd(k) > .5 ? c(0) : c(1);
                    g.beginPath(); g.arc(q.x + Math.cos(ang) * rr, q.y + Math.sin(ang) * rr * .6, size * .04, 0, 7); g.fill();
                }
            } else if (fx === 'ghost') {
                // Puffs that grow and drift up as they age, round a thin bright spine
                for (let i = 0; i < n; i++) {
                    const k = id(i); if (k % 2) continue;
                    const q = P[i], age = n - 1 - i;
                    g.globalAlpha = .16 * q.t + .04;
                    g.fillStyle = rnd(k) > .6 ? c(1) : c(0);
                    g.beginPath();
                    g.arc(q.x - age * .2, q.y - age * .3 + (rnd(k) - .5) * size * .2,
                          size * (.14 + .3 * (1 - q.t)), 0, 7);
                    g.fill();
                }
                trailGlow(g, P, hw, [{ c: c(2), w: .7, a: .35, p: 1.5 }, { c: c(0), w: .3, a: .85, p: 2 }]);
            } else if (fx === 'tinsel') {
                trailGlow(g, P, hw, [{ c: c(1), w: 1.1, a: .8, p: 1.2 }, { c: c(0), w: .45, a: .95, p: 1.4 }]);
                for (let i = 0; i < n; i++) {
                    const k = id(i); if (k % 2) continue;
                    const q = P[i], tw = .45 + .55 * Math.abs(Math.sin(now * .01 + k * 1.7));
                    g.globalAlpha = q.t * tw;
                    g.fillStyle = [c(1), c(2), c(3), '#FFFFFF'][k % 4];
                    const x = q.x + (rnd(k) - .5) * size * .7, y = q.y + (rnd(k + 1) - .5) * size * .7;
                    const r = size * (.03 + .03 * rnd(k + 2));
                    g.fillRect(x - r, y - r, r * 2, r * 2);
                }
            }
        }
        g.restore();
    }

    root.CrixArt = {
        HEAD_TOP: HEAD_TOP,
        HEAD_W: HEAD_W,
        FACE_CY: FACE_CY,
        HEAD_CX: HEAD_CX,
        HAT_SHAPES: HAT_SHAPES,
        FIT_DEFAULT: FIT_DEFAULT,
        drawSprite: drawSprite,
        drawHatImage: drawHatImage,
        drawHatOn: drawHatOn,
        drawTrail: drawTrail,
        makeWobble: makeWobble,
        stepWobble: stepWobble
    };
})(typeof window !== 'undefined' ? window : globalThis);
