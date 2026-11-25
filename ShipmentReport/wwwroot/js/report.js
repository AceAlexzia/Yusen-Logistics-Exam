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

    const palette = ['#1d4e89', '#f28f3b', '#2a9d8f', '#e76f51', '#8e7dbe', '#e9c46a', '#577590', '#90be6d'];
    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
    Chart.defaults.maintainAspectRatio = false;
    // White background so exported PNGs aren't transparent.
    Chart.register({
        id: 'whiteBg',
        beforeDraw: c => { const x = c.ctx; x.save(); x.fillStyle = '#fff'; x.fillRect(0, 0, c.width, c.height); x.restore(); },
    });

    // Clicking a bar/slice reloads the page with that value as a filter.
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

    new Chart(canvas('daily'), {
        type: 'bar',
        data: {
            labels: s.daily.labels,
            datasets: s.daily.series.map((x, i) => ({ label: x.label, data: x.values, backgroundColor: palette[i % palette.length] })),
        },
        options: {
            interaction: { mode: 'index', intersect: false },
            scales: { x: { stacked: true }, y: { stacked: true, beginAtZero: true, ticks: { precision: 0 }, title: { display: true, text: 'Containers' } } },
            plugins: {
                tooltip: { callbacks: { footer: items => 'Total: ' + items.reduce((a, b) => a + b.parsed.y, 0) } },
                // Legend click filters by that size (click again to clear) instead of hiding the series.
                legend: {
                    onClick: (e, item) => item.text !== 'Unknown' && go({ Size: filter.size === item.text ? null : item.text }),
                    onHover: e => { e.native.target.style.cursor = 'pointer'; },
                    onLeave: e => { e.native.target.style.cursor = 'default'; },
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

    const containersAndCbm = (key, data, filterKey) => new Chart(canvas(key), {
        type: 'bar',
        data: {
            labels: data.labels,
            datasets: [
                { label: 'Containers', data: data.series[0].values, backgroundColor: palette[0], yAxisID: 'y' },
                { label: 'CBM', data: data.series[1].values, backgroundColor: palette[1], yAxisID: 'y1' },
            ],
        },
        options: {
            scales: {
                y: { beginAtZero: true, ticks: { precision: 0 }, title: { display: true, text: 'Containers' } },
                y1: { beginAtZero: true, position: 'right', grid: { drawOnChartArea: false }, title: { display: true, text: 'CBM' } },
            },
            onHover: pointer,
            onClick: onPick(label => label !== 'Unknown' && go({ [filterKey]: label })),
        },
    });
    containersAndCbm('origin', s.byOrigin, 'Origin');
    containersAndCbm('carrier', s.byCarrier, 'Carrier');

    new Chart(canvas('lead'), {
        type: 'bar',
        data: {
            labels: s.leadTime.labels,
            datasets: s.leadTime.series.map((x, i) => ({ label: x.label, data: x.values, backgroundColor: palette[i + 2] })),
        },
        options: {
            indexAxis: 'y',
            scales: { x: { beginAtZero: true, title: { display: true, text: 'Days after ETA' } } },
            plugins: {
                tooltip: {
                    callbacks: {
                        label: i => `${i.dataset.label}: ${i.parsed.x} days`,
                        footer: items => `${s.leadTime.counts[items[0].dataIndex]} invoice(s)`,
                    },
                },
            },
            onHover: pointer,
            onClick: onPick(label => label !== 'Unknown' && go({ Origin: label })),
        },
    });

    const doughnut = (key, data, filterKey, colors) => new Chart(canvas(key), {
        type: 'doughnut',
        data: { labels: data.labels, datasets: [{ label: 'Invoices', data: data.series[0].values, backgroundColor: colors }] },
        options: {
            plugins: {
                legend: { position: 'right' },
                tooltip: {
                    callbacks: {
                        label: i => {
                            const total = i.dataset.data.reduce((a, b) => a + b, 0);
                            return ` ${i.label}: ${i.parsed} (${total ? Math.round(i.parsed * 100 / total) : 0}%)`;
                        },
                    },
                },
            },
            onHover: pointer,
            onClick: onPick(label => go({ [filterKey]: label })),
        },
    });
    doughnut('doc', s.docStatus, 'Doc', ['#e76f51', '#2a9d8f']);
    doughnut('decl', s.declaration, 'Decl', palette);

    // Export: send each chart as a PNG along with the filters.
    document.getElementById('export')?.addEventListener('submit', ev => {
        const form = ev.target;
        form.querySelectorAll('input[data-png]').forEach(i => i.remove());
        document.querySelectorAll('.chart-card').forEach(card => {
            const c = card.querySelector('canvas');
            const input = document.createElement('input');
            input.type = 'hidden';
            input.dataset.png = '1';
            input.name = `charts[${card.querySelector('h2').firstChild.textContent.trim()}]`;
            input.value = c.toDataURL('image/png');
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
