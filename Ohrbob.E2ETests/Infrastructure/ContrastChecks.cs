using NUnit.Framework;

namespace Ohrbob.E2ETests.Infrastructure;

/// <summary>
/// Dark-mode readability checks shared by the theme and page-restyle tests.
/// The script walks every visible text/control node and composites translucent
/// colours over their ancestors before comparing luminance, so a subtle
/// white-on-dark surface is not mistaken for a light one.
/// </summary>
public static class ContrastChecks
{
    public const string Script = @"() => {
        const parse = (value) => {
            const m = value.match(/[\d.]+/g);
            if (!m) return { r: 0, g: 0, b: 0, a: 0 };
            // color-mix()/color() results can serialise as color(srgb 0.63 0.62 0.97);
            // those channels are 0..1, unlike the 0..255 rgb()/rgba() form.
            const scale = /^\s*color\(/.test(value) ? 255 : 1;
            return { r: +m[0] * scale, g: +m[1] * scale, b: +m[2] * scale, a: m.length > 3 ? +m[3] : 1 };
        };
        const luma = (c) => (0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b) / 255;
        const effectiveBg = (el) => {
            const chain = [];
            let node = el;
            while (node && node.nodeType === 1) { chain.push(node); node = node.parentElement; }
            let acc = { r: 255, g: 255, b: 255 };
            for (let i = chain.length - 1; i >= 0; i--) {
                const c = parse(getComputedStyle(chain[i]).backgroundColor);
                if (c.a <= 0) continue;
                acc = { r: c.r * c.a + acc.r * (1 - c.a), g: c.g * c.a + acc.g * (1 - c.a), b: c.b * c.a + acc.b * (1 - c.a) };
            }
            return acc;
        };
        const offenders = [];
        document.querySelectorAll('body *').forEach((el) => {
            if (el instanceof SVGElement) return;
            // Decorative watermarks (the ghost page kicker) are intentionally low contrast.
            if (el.closest('[aria-hidden=""true""]')) return;
            const direct = Array.from(el.childNodes).some((n) => n.nodeType === 3 && n.textContent.trim().length > 0);
            const isControl = el.matches('button, a, .btn, [role=""button""], [role=""slider""]');
            if (!direct && !isControl) return;
            const cs = getComputedStyle(el);
            if (cs.display === 'none' || cs.visibility === 'hidden' || parseFloat(cs.opacity) === 0) return;
            const rect = el.getBoundingClientRect();
            if (rect.width === 0 || rect.height === 0) return;

            const bg = effectiveBg(el);
            const fgColor = parse(cs.color);
            const fg = {
                r: fgColor.r * fgColor.a + bg.r * (1 - fgColor.a),
                g: fgColor.g * fgColor.a + bg.g * (1 - fgColor.a),
                b: fgColor.b * fgColor.a + bg.b * (1 - fgColor.a)
            };
            const bl = luma(bg);
            const fl = luma(fg);
            if ((bl < 0.4 && fl < 0.45) || (bl > 0.6 && fl > 0.55)) {
                const cls = typeof el.className === 'string' ? el.className : '';
                offenders.push(el.tagName + '.' + cls + ' (fg=' + fl.toFixed(2) + ', bg=' + bl.toFixed(2) + ')');
            }
        });
        return offenders.slice(0, 25);
    }";

    public static void AssertNoIssues(string[] offenders, string label) =>
        Assert.That(offenders, Is.Empty,
            $"Unreadable text/controls in dark mode ({label}):\n  {string.Join("\n  ", offenders)}");
}
