// Library at a glance: one horizontal bar chart per <canvas data-chart>. Chart.js (MIT), served from wwwroot/lib.
(function () {
    "use strict";
    if (!window.Chart) return;

    function css(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); }
    function shorten(s, n) { return s.length > n ? s.slice(0, n - 1) + "…" : s; }

    var charts = [];

    function colors() {
        return { bar: css("--obrl-accent"), hover: css("--obrl-accent-hover"), text: css("--obrl-muted"), grid: css("--obrl-border"), surface: css("--obrl-surface"), ink: css("--obrl-text") };
    }

    function build(canvas) {
        var data = JSON.parse(canvas.getAttribute("data-chart"));
        var c = colors();
        // Long names are shortened to fit about a third of the card; the tooltip shows them in full.
        var labelChars = Math.max(12, Math.min(28, Math.round(canvas.parentNode.clientWidth / 22)));
        var chart = new Chart(canvas, {
            type: "bar",
            data: {
                labels: data.items.map(function (i) { return i.label; }),
                datasets: [{
                    data: data.items.map(function (i) { return i.value; }),
                    backgroundColor: c.bar,
                    hoverBackgroundColor: c.hover,
                    borderRadius: 4,
                    borderSkipped: "start",
                    barThickness: 16,
                }],
            },
            options: {
                indexAxis: "y",
                maintainAspectRatio: false,
                animation: { duration: 400 },
                layout: { padding: { right: 8 } },
                scales: {
                    x: {
                        beginAtZero: true,
                        max: data.max || undefined,
                        grid: { color: c.grid, drawTicks: false },
                        border: { display: false },
                        ticks: { color: c.text, precision: data.unit === "stars" ? undefined : 0, padding: 6, font: { size: 11 } },
                    },
                    y: {
                        grid: { display: false },
                        border: { display: false },
                        ticks: {
                            color: c.ink,
                            font: { size: 12 },
                            callback: function (v) { return shorten(this.getLabelForValue(v), labelChars); },
                        },
                    },
                },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        backgroundColor: c.ink,
                        titleColor: c.surface,
                        bodyColor: c.surface,
                        displayColors: false,
                        callbacks: {
                            label: function (ctx) {
                                var item = data.items[ctx.dataIndex];
                                return item.note ? [item.text, item.note] : item.text;
                            },
                        },
                    },
                },
                onHover: function (e, els) {
                    var item = els.length ? data.items[els[0].index] : null;
                    e.native.target.style.cursor = item && item.url ? "pointer" : "default";
                },
                onClick: function (e, els) {
                    var item = els.length ? data.items[els[0].index] : null;
                    if (item && item.url) window.location.href = item.url;
                },
            },
        });
        charts.push(chart);
    }

    // Draw charts when their panel is open (a closed <details> has no size to draw into).
    function drawAll() {
        document.querySelectorAll("canvas[data-chart]").forEach(function (canvas) {
            if (canvas.dataset.drawn || !canvas.offsetParent) return;
            canvas.dataset.drawn = "1";
            build(canvas);
        });
    }
    document.querySelectorAll("details.overview").forEach(function (d) { d.addEventListener("toggle", drawAll); });
    drawAll();

    // Follow theme changes (light, sepia, dark).
    new MutationObserver(function () {
        var c = colors();
        charts.forEach(function (ch) {
            var ds = ch.data.datasets[0];
            ds.backgroundColor = c.bar;
            ds.hoverBackgroundColor = c.hover;
            ch.options.scales.x.grid.color = c.grid;
            ch.options.scales.x.ticks.color = c.text;
            ch.options.scales.y.ticks.color = c.ink;
            ch.options.plugins.tooltip.backgroundColor = c.ink;
            ch.options.plugins.tooltip.titleColor = c.surface;
            ch.options.plugins.tooltip.bodyColor = c.surface;
            ch.update("none");
        });
    }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-obrl-theme"] });
})();
