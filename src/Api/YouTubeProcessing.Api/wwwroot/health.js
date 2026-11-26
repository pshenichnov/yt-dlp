(() => {
  'use strict';
  const components = [
    ['api','API'], ['postgres','PostgreSQL'], ['rabbitMq','RabbitMQ'],
    ['blobStore','Blob storage'], ['orchestrator','Orchestrator'],
    ['extractor','Extractor'], ['analyzer','Analyzer']
  ];
  const validStates = new Set(['healthy','unhealthy','disabled']);
  const grid = document.querySelector('#components');
  const overall = document.querySelector('#overall-status');
  const overallDot = document.querySelector('#overall-dot');
  const checkedAt = document.querySelector('#checked-at');
  const notice = document.querySelector('#notice');
  const refreshButton = document.querySelector('#refresh');
  const autoRefresh = document.querySelector('#auto-refresh');
  let controller;
  let retainedReport = false;

  const normalizedState = value => typeof value === 'string' && validStates.has(value.toLowerCase()) ? value.toLowerCase() : 'unknown';
  const finiteNumber = value => typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : null;
  const safeObject = value => value && typeof value === 'object' && !Array.isArray(value) ? value : {};
  const formatCheckedAt = value => {
    const date = typeof value === 'string' ? new Date(value) : new Date(NaN);
    return Number.isNaN(date.getTime()) ? 'Check time unavailable' : `Last checked ${date.toLocaleString(undefined, {
      year:'numeric', month:'short', day:'numeric', hour:'numeric', minute:'2-digit', second:'2-digit', timeZoneName:'short'
    })}`;
  };

  function setOverall(state, label) {
    overall.textContent = label;
    overallDot.className = `status-dot ${state}`;
  }

  function render(report) {
    const data = safeObject(report);
    const serviceData = safeObject(data.services);
    const state = normalizedState(data.status);
    setOverall(state, state === 'unknown' ? 'Unknown' : state[0].toUpperCase() + state.slice(1));
    checkedAt.textContent = formatCheckedAt(data.checkedAt);
    grid.replaceChildren();
    for (const [key, name] of components) {
      const item = safeObject(serviceData[key]);
      const itemState = normalizedState(item.status);
      const card = document.createElement('article');
      card.className = 'component';
      const heading = document.createElement('div'); heading.className = 'component-heading';
      const title = document.createElement('h3'); title.textContent = name;
      const badge = document.createElement('span'); badge.className = `badge ${itemState}`; badge.textContent = itemState;
      heading.append(title, badge); card.append(heading);
      const description = document.createElement('p');
      description.textContent = typeof item.description === 'string' && item.description.trim() ? item.description : 'No component detail reported.';
      card.append(description);
      const details = document.createElement('ul'); details.className = 'details';
      const duration = finiteNumber(item.durationMilliseconds);
      const durationRow = document.createElement('li'); durationRow.textContent = duration === null ? 'Probe duration unavailable' : `Probe duration: ${duration.toLocaleString()} ms`; details.append(durationRow);
      const queues = Array.isArray(item.queues)
        ? item.queues.map(queue => [null, queue])
        : Object.entries(safeObject(item.queues));
      if (queues.length) {
        for (const [queueKey, rawQueue] of queues) {
          const queue = safeObject(rawQueue); const row = document.createElement('li');
          const queueName = typeof queue.name === 'string' && queue.name.trim()
            ? queue.name
            : typeof queueKey === 'string' && queueKey.trim() ? queueKey : 'Unknown queue';
          const consumers = finiteNumber(queue.consumers);
          row.textContent = `${queueName}: ${normalizedState(queue.status)}${consumers === null ? '' : ` · ${consumers} consumer${consumers === 1 ? '' : 's'}`}`;
          details.append(row);
        }
      }
      card.append(details); grid.append(card);
    }
    retainedReport = true;
  }

  function showUnavailable(message) {
    notice.textContent = retainedReport ? `${message} Showing the last successful report; it may be stale.` : message;
    notice.hidden = false;
    if (!retainedReport) { setOverall('unknown', 'Unavailable'); checkedAt.textContent = 'No health report is available.'; }
  }

  async function refresh() {
    if (controller) return;
    controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 10000);
    refreshButton.disabled = true; refreshButton.textContent = 'Refreshing…';
    try {
      const response = await fetch('/health', { cache:'no-store', headers:{ Accept:'application/json' }, signal:controller.signal });
      if (response.status !== 200 && response.status !== 503) throw new Error(`Health endpoint returned HTTP ${response.status}.`);
      const report = await response.json();
      if (!report || typeof report !== 'object' || Array.isArray(report)) throw new Error('Health endpoint returned an invalid report.');
      render(report); notice.hidden = true; notice.textContent = '';
    } catch (error) {
      const message = error && error.name === 'AbortError' ? 'The health request timed out.' : 'The latest health report could not be loaded.';
      showUnavailable(message);
    } finally {
      clearTimeout(timeout); controller = null; refreshButton.disabled = false; refreshButton.textContent = 'Refresh now';
    }
  }

  refreshButton.addEventListener('click', refresh);
  setInterval(() => { if (autoRefresh.checked && !document.hidden) refresh(); }, 30000);
  document.addEventListener('visibilitychange', () => { if (!document.hidden && autoRefresh.checked) refresh(); });
  refresh();
})();
