// Draws the report charts from the JSON the server embeds, and wires click-to-filter, export and table sorting.
(() => {
    // Drag & drop on the empty-state upload box.
    const drop = document.getElementById('dropzone');
    if (drop) {
        const input = drop.querySelector('input[type=file]');
        ['dragenter', 'dragover'].forEach(e => drop.addEventListener(e, ev => { ev.preventDefault(); drop.classList.add('over'); }));
        ['dragleave', 'drop'].forEach(e => drop.addEventListener(e, ev => { ev.preventDefault(); drop.classList.remove('over'); }));
        drop.addEventListener('drop', ev => {
            if (!ev.dataTransfer.files.length) return;
            input.files = ev.dataTransfer.files;
            drop.submit();
        });
    }

    const dataEl = document.getElementById('report-data');
    if (!dataEl || !window.Chart) return;
    const { summary: s, filter } = JSON.parse(dataEl.textContent);

    // Neutral series first, then the accent; neighbours differ in lightness as well as hue.
    const css = getComputedStyle(document.documentElement);
    const v = n => css.getPropertyValue(n).trim();
    const INK = v('--ink'), MUTED = v('--muted'), ACCENT = v('--accent'), GREY = '#a19e97', PALE = '#dad8d3';
    const palette = [INK, ACCENT, GREY, PALE, '#8fb0e3', MUTED];
    const GRID = 'rgba(28, 27, 25, .08)';

    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
    Chart.defaults.font.size = 13;
    Chart.defaults.color = MUTED;
    Chart.defaults.borderColor = GRID;
    Chart.defaults.maintainAspectRatio = false;
    Chart.defaults.animation.duration = 400;
    Object.assign(Chart.defaults.plugins.legend.labels, { boxWidth: 12, boxHeight: 12, color: MUTED, font: { size: 14 } });
    Chart.defaults.plugins.legend.align = 'start';
    Object.assign(Chart.defaults.plugins.tooltip, {
        backgroundColor: INK, titleColor: '#fff', bodyColor: '#fff', footerColor: '#fff',
        padding: 12, cornerRadius: 6, boxPadding: 4, titleFont: { weight: '500' }, footerFont: { weight: '400' },
    });
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) Chart.defaults.animation = false;
    // White background so exported PNGs aren't transparent.
    Chart.register({
        id: 'whiteBg',
        beforeDraw: c => { const x = c.ctx; x.save(); x.fillStyle = '#fff'; x.fillRect(0, 0, c.width, c.height); x.restore(); },
    });

    // Clicking a bar reloads the page with that value as a filter.
    const go = params => {
        const url = new URL(location.href);
        for (const [k, v] of Object.entries(params)) v == null ? url.searchParams.delete(k) : url.searchParams.set(k, v);
        location.href = url.toString();
    };
    const onPick = handler => (evt, els, chart) => {
        if (!els.length) return;
        const { index, datasetIndex } = els[0];
        handler(chart.data.labels[index], chart.data.datasets[datasetIndex].label);
    };
    const pointer = (evt, els) => { evt.native.target.style.cursor = els.length ? 'pointer' : 'default'; };
    const canvas = key => document.querySelector(`canvas[data-chart="${key}"]`);
    const fmt = n => n.toLocaleString('en-US', { maximumFractionDigits: 1 });
    const noAxis = { grid: { display: false }, border: { display: false }, ticks: { display: false } };
    const nameTicks = { grid: { display: false }, border: { display: false }, ticks: { color: INK, font: { size: 14, weight: '500' } } };
    const legendPointer = {
        onHover: e => { e.native.target.style.cursor = 'pointer'; },
        onLeave: e => { e.native.target.style.cursor = 'default'; },
    };

    new Chart(canvas('daily'), {
        type: 'bar',
        data: {
            labels: s.daily.labels,
            datasets: s.daily.series.map((x, i) => ({
                label: x.label, data: x.values, backgroundColor: palette[i % palette.length],
                borderColor: '#fff', borderWidth: { top: 1 }, borderSkipped: 'bottom', maxBarThickness: 96,
            })),
        },
        options: {
            interaction: { mode: 'index', intersect: false },
            scales: {
                x: { stacked: true, grid: { display: false }, border: { color: INK }, ticks: { color: v('--ink-2'), font: { size: 14 } } },
                y: { stacked: true, beginAtZero: true, border: { display: false }, ticks: { precision: 0, padding: 8 } },
            },
            plugins: {
                tooltip: { callbacks: { footer: items => 'Total: ' + items.reduce((a, b) => a + b.parsed.y, 0) } },
                // Legend click filters by that size (click again to clear) instead of hiding the series.
                legend: {
                    position: 'top',
                    onClick: (e, item) => item.text !== 'Unknown' && go({ Size: filter.size === item.text ? null : item.text }),
                    ...legendPointer,
                },
            },
            onHover: pointer,
            // A bar click filters to its day/week/month and keeps every size, so all sizes stay clickable.
            onClick: (evt, els) => {
                if (!els.length) return;
                const [from, to] = s.daily.ranges[els[0].index];
                go({ From: from, To: to });
            },
        },
    });

    // Writes "containers · CBM" after each bar, so both numbers read without a second axis.
    const endLabels = cbm => ({
        id: 'endLabels',
        afterDatasetsDraw(chart) {
            const { ctx } = chart, family = Chart.defaults.font.family;
            ctx.save();
            ctx.textBaseline = 'middle';
            chart.getDatasetMeta(0).data.forEach((bar, i) => {
                const n = fmt(chart.data.datasets[0].data[i]);
                ctx.font = `500 14px ${family}`;
                ctx.fillStyle = INK;
                ctx.fillText(n, bar.x + 8, bar.y);
                const w = ctx.measureText(n).width;
                ctx.font = `400 13px ${family}`;
                ctx.fillStyle = MUTED;
                ctx.fillText(`· ${fmt(cbm[i])} CBM`, bar.x + 14 + w, bar.y);
            });
            ctx.restore();
        },
    });

    const containersAndCbm = (key, data, filterKey) => new Chart(canvas(key), {
        type: 'bar',
        data: {
            labels: data.labels,
            datasets: [{ label: 'Containers', data: data.series[0].values, backgroundColor: INK, borderRadius: 4, barThickness: 10 }],
        },
        plugins: [endLabels(data.series[1].values)],
        options: {
            indexAxis: 'y',
            layout: { padding: { right: 150 } },
            scales: { x: { ...noAxis, beginAtZero: true }, y: nameTicks },
            plugins: {
                legend: { display: false },
                tooltip: { callbacks: { label: i => ` ${i.parsed.x} containers · ${fmt(data.series[1].values[i.dataIndex])} CBM` } },
            },
            onHover: pointer,
            onClick: onPick(label => label !== 'Unknown' && go({ [filterKey]: label })),
        },
    });
    containersAndCbm('origin', s.byOrigin, 'Origin');
    containersAndCbm('carrier', s.byCarrier, 'Carrier');
    containersAndCbm('release', s.byReleasePort, 'Release');

    new Chart(canvas('lead'), {
        type: 'bar',
        data: {
            labels: s.leadTime.labels,
            datasets: s.leadTime.series.map((x, i) => ({
                label: x.label, data: x.values, backgroundColor: [GREY, ACCENT][i] ?? palette[i],
                borderRadius: 4, barThickness: 10,
            })),
        },
        options: {
            indexAxis: 'y',
            scales: {
                x: { beginAtZero: true, border: { display: false }, title: { display: true, text: 'Days after ETA', color: MUTED } },
                y: nameTicks,
            },
            plugins: {
                legend: { position: 'top' },
                tooltip: {
                    callbacks: {
                        label: i => ` ${i.dataset.label}: ${i.parsed.x} days`,
                        footer: items => `${s.leadTime.counts[items[0].dataIndex]} invoice(s)`,
                    },
                },
            },
            onHover: pointer,
            onClick: onPick(label => label !== 'Unknown' && go({ Origin: label })),
        },
    });

    // Status: one split bar, a dataset per category, with a legend of buttons (and counts) underneath.
    const split = (key, data, filterKey, colors) => {
        const values = data.series[0].values;
        const total = values.reduce((a, b) => a + b, 0);
        const legend = document.createElement('div');
        legend.className = 'split-legend';
        data.labels.forEach((label, i) => {
            const b = document.createElement('button');
            b.type = 'button';
            b.className = 'split-item';
            b.title = `Filter to ${label}`;
            const sw = document.createElement('span');
            sw.className = 'swatch';
            sw.style.background = colors[i % colors.length];
            const n = document.createElement('strong');
            n.textContent = fmt(values[i]);
            b.append(sw, label, n);
            b.addEventListener('click', () => go({ [filterKey]: label }));
            legend.append(b);
        });
        canvas(key).parentElement.after(legend);
        return new Chart(canvas(key), {
            type: 'bar',
            data: {
                labels: ['Invoices'],
                datasets: data.labels.map((label, i) => ({
                    label, data: [values[i]], backgroundColor: colors[i % colors.length],
                    borderColor: '#fff', borderWidth: { left: 2 }, borderSkipped: false, barThickness: 14,
                })),
            },
            options: {
                indexAxis: 'y',
                scales: { x: { ...noAxis, stacked: true, max: total || 1 }, y: { ...noAxis, stacked: true } },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            title: () => '',
                            label: i => ` ${i.dataset.label}: ${i.parsed.x} (${total ? Math.round(i.parsed.x * 100 / total) : 0}%)`,
                        },
                    },
                },
                onHover: pointer,
                onClick: onPick((_, dataset) => go({ [filterKey]: dataset })),
            },
        });
    };
    split('doc', s.docStatus, 'Doc', [ACCENT, PALE]);
    split('decl', s.declaration, 'Decl', [INK, GREY, PALE, ACCENT, '#8fb0e3']);
    // Late is the one status to notice, so it gets the warm colour.
    const delivColor = { 'On time': ACCENT, 'Late': '#c2410c', 'Not delivered': PALE, 'No plan': GREY };
    split('deliv', s.delivery, 'Deliv', s.delivery.labels.map(l => delivColor[l] ?? GREY));

    // The status charts' legend is HTML, so for export draw the bar plus one labelled row per category into one image.
    const splitPng = c => {
        const sets = Chart.getChart(c).data.datasets;
        const total = sets.reduce((a, d) => a + d.data[0], 0);
        const W = Math.max(c.clientWidth, 280), barH = c.clientHeight, rowH = 28, k = 2;
        const out = document.createElement('canvas');
        out.width = W * k;
        out.height = (barH + 12 + sets.length * rowH) * k;
        const x = out.getContext('2d');
        x.scale(k, k);
        x.fillStyle = '#fff';
        x.fillRect(0, 0, W, out.height / k);
        x.drawImage(c, 0, 0, W, barH); // the bar is plain rectangles, so stretching it is safe
        x.textBaseline = 'middle';
        sets.forEach((d, i) => {
            const y = barH + 12 + i * rowH + rowH / 2;
            x.fillStyle = d.backgroundColor;
            x.fillRect(0, y - 6, 12, 12);
            x.fillStyle = INK;
            x.font = `400 15px ${Chart.defaults.font.family}`;
            x.textAlign = 'left';
            x.fillText(d.label, 22, y);
            x.font = `500 15px ${Chart.defaults.font.family}`;
            x.textAlign = 'right';
            const pct = total ? Math.round(d.data[0] * 100 / total) : 0;
            x.fillText(`${fmt(d.data[0])}  (${pct}%)`, W, y);
        });
        return out.toDataURL('image/png');
    };

    // Export: send each chart as a PNG along with the filters.
    document.getElementById('export')?.addEventListener('submit', ev => {
        const form = ev.target;
        form.querySelectorAll('input[data-png]').forEach(i => i.remove());
        document.querySelectorAll('.chart-card').forEach(card => {
            const c = card.querySelector('canvas');
            const input = document.createElement('input');
            input.type = 'hidden';
            input.dataset.png = '1';
            input.name = `charts[${card.dataset.title}]`;
            input.value = card.querySelector('.split-legend') ? splitPng(c) : c.toDataURL('image/png');
            form.appendChild(input);
        });
    });

    // Sortable table: click a header; click again to reverse.
    const table = document.getElementById('rows');
    table?.querySelectorAll('th').forEach((th, col) => th.addEventListener('click', () => {
        const asc = th.dataset.sort !== 'asc';
        table.querySelectorAll('th').forEach(h => delete h.dataset.sort);
        th.dataset.sort = asc ? 'asc' : 'desc';
        const body = table.tBodies[0];
        const val = tr => tr.children[col].textContent.trim();
        const rows = [...body.rows].sort((a, b) => {
            const x = val(a), y = val(b), isNum = v => /^-?\d+(\.\d+)?$/.test(v);
            const cmp = isNum(x) && isNum(y) ? x - y : x.localeCompare(y, undefined, { numeric: true });
            return asc ? cmp : -cmp;
        });
        body.append(...rows);
    }));
})();
