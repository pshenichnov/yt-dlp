const form = document.querySelector("#analysis-form");
const videoInput = document.querySelector("#video");
const apiKeyInput = document.querySelector("#api-key");
const submitButton = document.querySelector("#submit-button");
const errorPanel = document.querySelector("#error-panel");
const statusPanel = document.querySelector("#status-panel");
const statusMessage = document.querySelector("#status-message");
const statusPercent = document.querySelector("#status-percent");
const progressBar = document.querySelector("#progress-bar");
const resultPanel = document.querySelector("#result-panel");
const thumbnail = document.querySelector("#thumbnail");
const thumbnailPlaceholder = document.querySelector("#thumbnail-placeholder");
const providerBadge = document.querySelector("#provider-badge");
const channelName = document.querySelector("#channel-name");
const videoTitle = document.querySelector("#video-title");
const summary = document.querySelector("#summary");
const youtubeLink = document.querySelector("#youtube-link");

const loadLibraryButton = document.querySelector("#load-library-button");
const librarySummary = document.querySelector("#library-summary");
const libraryError = document.querySelector("#library-error");
const libraryLoading = document.querySelector("#library-loading");
const channelList = document.querySelector("#channel-list");
const channelVideos = document.querySelector("#channel-videos");
const backToChannelsButton = document.querySelector("#back-to-channels");
const selectedChannelName = document.querySelector("#selected-channel-name");
const selectedChannelCount = document.querySelector("#selected-channel-count");
const videoList = document.querySelector("#video-list");
const channelForm = document.querySelector("#channel-form");
const channelIdInput = document.querySelector("#channel-id");
const addChannelButton = document.querySelector("#add-channel-button");
const loadMoreVideosButton = document.querySelector("#load-more-videos");
const processedOnlyInput = document.querySelector("#processed-only");

const apiKeyStorageKey = "youtube-processing-api-key";
const stageLabels = {
  queued: "Waiting for an available worker…",
  extractingMetadata: "Reading video metadata…",
  extractingTranscript: "Extracting and normalizing captions…",
  extractingThumbnail: "Fetching the video thumbnail…",
  analyzingTranscript: "Codex is summarizing the transcript…",
  completed: "Analysis complete."
};

let thumbnailObjectUrl;
let libraryThumbnailUrls = [];
let channels = [];
let selectedChannel;
let currentVideoPage;
let nextVideoOffset = 0;

processedOnlyInput.checked =
  new URLSearchParams(window.location.search).get("processed-only") === "true";

apiKeyInput.value = sessionStorage.getItem(apiKeyStorageKey) ?? "";
if (apiKeyInput.value) {
  loadChannels();
}

loadLibraryButton.addEventListener("click", loadChannels);
apiKeyInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    event.preventDefault();
    loadChannels();
  }
});

backToChannelsButton.addEventListener("click", () => showChannelList(true));
loadMoreVideosButton.addEventListener("click", loadMoreChannelVideos);
processedOnlyInput.addEventListener("change", async () => {
  if (!selectedChannel) return;
  setChannelUrl(selectedChannel);
  await loadChannelVideos(selectedChannel, 0, false, false);
});
window.addEventListener("popstate", async () => {
  processedOnlyInput.checked =
    new URLSearchParams(window.location.search).get("processed-only") === "true";
  const reference = getRequestedChannelReference();
  if (!reference) {
    showChannelList(false);
    return;
  }

  const channel = findChannel(reference);
  if (channel) await loadChannelVideos(channel, 0, false, false);
});

channelForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const channelId = channelIdInput.value.trim();
  const apiKey = apiKeyInput.value.trim();
  if (!channelId || !apiKey) {
    showLibraryError("Enter a YouTube channel ID and your local API key.");
    return;
  }

  saveApiKey(apiKey);
  libraryError.hidden = true;
  addChannelButton.disabled = true;
  libraryLoading.textContent = "Loading the newest channel videos…";
  libraryLoading.hidden = false;
  try {
    let channel = await fetchJson("/api/v1/channels", apiKey, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ channelId })
    });
    channel = await waitForChannelPage(channel, apiKey);
    channelIdInput.value = "";
    await loadChannels({ preserveErrors: true, openRequestedChannel: false });
    await loadChannelVideos(channel);
  } catch (error) {
    showLibraryError(error instanceof Error ? error.message : "The channel could not be added.");
  } finally {
    addChannelButton.disabled = false;
    libraryLoading.textContent = "Loading channels…";
    libraryLoading.hidden = true;
  }
});

form.addEventListener("submit", async (event) => {
  event.preventDefault();

  const video = videoInput.value.trim();
  const apiKey = apiKeyInput.value.trim();
  if (!video || !apiKey) {
    showError("Enter a YouTube video ID and your local API key.");
    return;
  }

  saveApiKey(apiKey);
  resetOutput();
  setBusy(true);
  updateStatus("queued", 0);

  try {
    const created = await fetchJson("/api/v1/video-analyses", apiKey, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": crypto.randomUUID()
      },
      body: JSON.stringify({
        video,
        preferredLanguage: "en",
        scenarios: ["summary"]
      })
    });

    await pollJob(created, apiKey);
    await loadChannels({ preserveErrors: true, openRequestedChannel: false });
  } catch (error) {
    showError(error instanceof Error ? error.message : "Analysis failed.");
  } finally {
    setBusy(false);
  }
});

async function loadChannels(options = {}) {
  const apiKey = apiKeyInput.value.trim();
  if (!apiKey) {
    showLibraryError("Enter your local API key to load the library.");
    return;
  }

  saveApiKey(apiKey);
  setLibraryBusy(true);
  if (!options.preserveErrors) libraryError.hidden = true;

  try {
    channels = await fetchJson("/api/v1/channels?limit=200", apiKey);
    renderChannels(channels);
    if (options.openRequestedChannel !== false) {
      await openRequestedChannel();
    }
  } catch (error) {
    showLibraryError(error instanceof Error ? error.message : "The library could not be loaded.");
  } finally {
    setLibraryBusy(false);
  }
}

function renderChannels(items) {
  releaseLibraryThumbnails();
  channelList.replaceChildren();
  channelVideos.hidden = true;
  channelList.hidden = false;

  const totalVideos = items.reduce((total, channel) => total + channel.videoCount, 0);
  librarySummary.textContent = items.length
    ? `${formatCount(items.length, "channel")} · ${formatCount(totalVideos, "video")}`
    : "No channels yet. Add one by its YouTube channel ID.";

  for (const channel of items) {
    const card = document.createElement("button");
    card.type = "button";
    card.className = "channel-card";
    card.innerHTML = `
      <span class="channel-card-name"></span>
      <span class="channel-card-footer">
        <span class="channel-card-count"></span>
        <span>Updated <time></time> ↗</span>
      </span>`;
    card.querySelector(".channel-card-name").textContent = channel.name || "Unknown channel";
    card.querySelector(".channel-card-count").textContent = formatCount(channel.videoCount, "video");
    const time = card.querySelector("time");
    time.dateTime = channel.updatedAt;
    time.textContent = formatShortDate(channel.updatedAt);
    card.addEventListener("click", () => loadChannelVideos(channel));
    channelList.append(card);
  }
}

async function loadChannelVideos(
  channel,
  offset = 0,
  append = false,
  updateUrl = !append) {
  const apiKey = apiKeyInput.value.trim();
  if (!apiKey) return;

  selectedChannel = channel;
  if (updateUrl && offset === 0) setChannelUrl(channel);
  libraryError.hidden = true;
  libraryLoading.textContent = `Loading ${channel.name || "channel"}…`;
  setLibraryBusy(true);

  try {
    const separator = channel.videosUrl.includes("?") ? "&" : "?";
    const page = await fetchJson(
      `${channel.videosUrl}${separator}limit=12&offset=${offset}` +
        `&processedOnly=${processedOnlyInput.checked}`,
      apiKey);
    renderChannelVideos(channel, page, apiKey, append);
  } catch (error) {
    showLibraryError(error instanceof Error ? error.message : "The channel videos could not be loaded.");
  } finally {
    libraryLoading.textContent = "Loading channels…";
    setLibraryBusy(false);
  }
}

function renderChannelVideos(channel, page, apiKey, append) {
  if (!append) {
    releaseLibraryThumbnails();
    videoList.replaceChildren();
  }
  channelList.hidden = true;
  channelVideos.hidden = false;
  selectedChannelName.textContent = channel.name || "Unknown channel";
  currentVideoPage = page;
  nextVideoOffset = page.nextOffset ?? 0;
  const renderedVideoIds = new Set(
    [...videoList.querySelectorAll("[data-video-id]")]
      .map((card) => card.dataset.videoId));

  for (const item of page.items) {
    if (renderedVideoIds.has(item.video.videoId)) continue;

    const card = document.createElement("article");
    card.className = "library-video-card";
    card.dataset.videoId = item.video.videoId;
    renderedVideoIds.add(item.video.videoId);
    card.innerHTML = `
      <div class="library-thumbnail"><span>Loading thumbnail…</span></div>
      <div>
        <div class="library-video-meta">
          <span data-kind="processed"></span>
          <span data-kind="published"></span>
          <span data-kind="duration"></span>
        </div>
        <div class="video-state-list"></div>
        <h3 class="library-video-title"></h3>
        <div class="content-copy">
          <p class="library-video-summary"></p>
          <button class="content-more" type="button" aria-expanded="false" hidden>More</button>
        </div>
        <div class="library-video-actions">
          <a data-kind="youtube" target="_blank" rel="noreferrer">Watch on YouTube ↗</a>
        </div>
      </div>`;

    card.querySelector(".library-video-title").textContent = item.video.title;
    const content = card.querySelector(".library-video-summary");
    const descriptionText = item.description || "No description is available.";
    const summaryText = item.summary?.trim();
    content.textContent = descriptionText;
    configureExpandableContent(content);

    const states = card.querySelector(".video-state-list");
    if (item.latestAnalysisId) {
      addVideoState(states, "Processed", true);
      addVideoState(states, "Transcript extracted", Boolean(item.transcript));
      addVideoState(states, "Summary generated", Boolean(summaryText));
    } else {
      addVideoState(states, "Unprocessed", false);
    }

    if (summaryText) {
      addContentSwitcher(content, descriptionText, summaryText);
    }

    const processed = card.querySelector('[data-kind="processed"]');
    if (item.lastProcessedAt) {
      const processedTime = document.createElement("time");
      processedTime.dateTime = item.lastProcessedAt;
      processedTime.textContent = `Processed ${formatDateTime(item.lastProcessedAt)}`;
      processed.append(processedTime);
    } else {
      processed.textContent = "Metadata only";
    }

    const published = card.querySelector('[data-kind="published"]');
    const publishedAt = item.video.publishedAt ?? item.video.uploadDate;
    if (publishedAt) {
      const publishedTime = document.createElement("time");
      publishedTime.dateTime = publishedAt;
      publishedTime.textContent = `Published ${formatShortDate(publishedAt)}`;
      published.append(publishedTime);
    } else {
      published.remove();
    }

    const duration = card.querySelector('[data-kind="duration"]');
    if (Number.isFinite(item.video.durationSeconds)) {
      duration.textContent = `Length ${formatDuration(item.video.durationSeconds)}`;
    } else {
      duration.remove();
    }

    card.querySelector('[data-kind="youtube"]').href = item.video.url;
    const transcriptUrl = getArtifactUrl(item.transcript);
    if (transcriptUrl) {
      const transcriptButton = document.createElement("button");
      transcriptButton.type = "button";
      transcriptButton.className = "artifact-button";
      transcriptButton.textContent = "Open transcript ↗";
      transcriptButton.addEventListener("click", () => openArtifact(transcriptUrl, apiKey));
      card.querySelector(".library-video-actions").append(transcriptButton);
    }
    if (!item.latestAnalysisId) {
      const processButton = document.createElement("button");
      processButton.type = "button";
      processButton.className = "process-video-button";
      processButton.textContent = "Process & summarize";
      const processStatus = document.createElement("span");
      processStatus.className = "process-video-status";
      processButton.addEventListener("click", () =>
        processCatalogVideo(item, card, processButton, processStatus, apiKey));
      card.querySelector(".library-video-actions").append(processButton, processStatus);
    }

    videoList.append(card);
    refreshExpandableContent(content);
    loadLibraryThumbnail(card.querySelector(".library-thumbnail"), item, apiKey);
  }

  const shownCount = videoList.childElementCount;
  selectedChannelCount.textContent = page.processedOnly
    ? `${shownCount} shown · ${page.storedVideoCount} processed of ` +
      `${formatCount(page.totalStoredVideoCount, "video")} loaded in DB`
    : `${shownCount} shown · ${formatCount(page.storedVideoCount, "video")} loaded in DB · newest first`;
  loadMoreVideosButton.hidden = !page.hasMore;
  loadMoreVideosButton.disabled = false;
  loadMoreVideosButton.textContent = page.hasStoredNextPage
    ? "Show older loaded videos"
    : "Fetch older videos from YouTube";

  if (!append) channelVideos.scrollIntoView({ behavior: "smooth", block: "start" });
}

async function loadMoreChannelVideos() {
  if (!selectedChannel || !currentVideoPage) return;
  const apiKey = apiKeyInput.value.trim();
  loadMoreVideosButton.disabled = true;
  libraryError.hidden = true;
  try {
    if (currentVideoPage.hasStoredNextPage) {
      await loadChannelVideos(selectedChannel, nextVideoOffset, true);
      return;
    }

    if (currentVideoPage.hasRemoteNextPage) {
      let channel = await fetchJson(
        `/api/v1/channels/${encodeURIComponent(selectedChannel.channelId)}/videos/pages`,
        apiKey,
        { method: "POST" });
      channel = await waitForChannelPage(channel, apiKey);
      selectedChannel = channel;
      await loadChannelVideos(channel, nextVideoOffset, true);
      return;
    }

    loadMoreVideosButton.hidden = true;
  } catch (error) {
    loadMoreVideosButton.disabled = false;
    showLibraryError(error instanceof Error ? error.message : "Older videos could not be loaded.");
  }
}

async function waitForChannelPage(channel, apiKey) {
  const deadline = Date.now() + 2 * 60 * 1000;
  let current = channel;
  while (current.syncStatus === "queued" && Date.now() < deadline) {
    await delay(750);
    current = await fetchJson(
      current.statusUrl || `/api/v1/channels/${encodeURIComponent(current.channelId)}`,
      apiKey);
  }
  if (current.syncStatus === "failed") {
    throw new Error(current.lastSyncError || "YouTube channel metadata could not be loaded.");
  }
  if (current.syncStatus === "queued") {
    throw new Error("The channel page is still loading. Try again shortly.");
  }
  return current;
}

function showChannelList(updateUrl = false) {
  releaseLibraryThumbnails();
  selectedChannel = undefined;
  currentVideoPage = undefined;
  channelVideos.hidden = true;
  loadMoreVideosButton.hidden = true;
  channelList.hidden = false;
  libraryError.hidden = true;
  librarySummary.textContent = channels.length
    ? `${formatCount(channels.length, "channel")} · ${formatCount(channels.reduce((sum, channel) => sum + channel.videoCount, 0), "video")}`
    : "No channels yet. Add one by its YouTube channel ID.";
  if (updateUrl) history.pushState({}, "", window.location.pathname);
}

async function openRequestedChannel() {
  const reference = getRequestedChannelReference();
  if (!reference) return;

  const channel = findChannel(reference);
  if (!channel) {
    showLibraryError(`Channel '${reference}' is not in the library.`);
    return;
  }

  await loadChannelVideos(channel, 0, false, false);
}

function findChannel(reference) {
  const normalized = normalizeChannelReference(reference);
  return channels.find((channel) =>
    normalizeChannelReference(channel.channelId) === normalized ||
    normalizeChannelReference(channel.handle) === normalized ||
    normalizeChannelReference(channel.name) === normalized);
}

function getRequestedChannelReference() {
  return new URLSearchParams(window.location.search).get("channel-id")?.trim();
}

function normalizeChannelReference(value) {
  return (value || "").trim().replace(/^@/, "").toLocaleLowerCase();
}

function setChannelUrl(channel) {
  const url = new URL(window.location.href);
  url.searchParams.set("channel-id", channel.handle || channel.channelId);
  if (processedOnlyInput.checked) {
    url.searchParams.set("processed-only", "true");
  } else {
    url.searchParams.delete("processed-only");
  }
  history.pushState({}, "", `${url.pathname}${url.search}`);
}

function addVideoState(container, label, complete) {
  const state = document.createElement("span");
  state.className = `video-state${complete ? " complete" : ""}`;
  state.textContent = label;
  container.append(state);
}

function createContentButton(label, selected) {
  const button = document.createElement("button");
  button.type = "button";
  button.textContent = label;
  button.setAttribute("aria-pressed", String(selected));
  return button;
}

function addContentSwitcher(content, descriptionText, summaryText) {
  const contentCopy = content.closest(".content-copy");
  contentCopy.parentElement.querySelector(".content-switcher")?.remove();
  const switcher = document.createElement("div");
  switcher.className = "content-switcher";
  const descriptionButton = createContentButton("Description", true);
  const summaryButton = createContentButton("Generated summary", false);
  descriptionButton.addEventListener("click", () => {
    content.textContent = descriptionText;
    refreshExpandableContent(content, true);
    descriptionButton.setAttribute("aria-pressed", "true");
    summaryButton.setAttribute("aria-pressed", "false");
  });
  summaryButton.addEventListener("click", () => {
    content.textContent = summaryText;
    refreshExpandableContent(content, true);
    descriptionButton.setAttribute("aria-pressed", "false");
    summaryButton.setAttribute("aria-pressed", "true");
  });
  switcher.append(descriptionButton, summaryButton);
  contentCopy.before(switcher);
}

function configureExpandableContent(content) {
  const contentCopy = content.closest(".content-copy");
  const moreButton = contentCopy.querySelector(".content-more");
  moreButton.addEventListener("click", () => {
    const expanded = contentCopy.classList.toggle("expanded");
    moreButton.textContent = expanded ? "Less" : "More";
    moreButton.setAttribute("aria-expanded", String(expanded));
  });
}

function refreshExpandableContent(content, collapse = false) {
  const contentCopy = content.closest(".content-copy");
  const moreButton = contentCopy.querySelector(".content-more");
  if (collapse) {
    contentCopy.classList.remove("expanded");
    moreButton.textContent = "More";
    moreButton.setAttribute("aria-expanded", "false");
  }
  requestAnimationFrame(() => {
    const isOverflowing = content.scrollHeight > content.clientHeight + 1;
    moreButton.hidden = !isOverflowing && !contentCopy.classList.contains("expanded");
  });
}

async function processCatalogVideo(item, card, button, status, apiKey) {
  button.disabled = true;
  status.textContent = "Queued…";
  libraryError.hidden = true;
  try {
    const created = await fetchJson("/api/v1/video-analyses", apiKey, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": crypto.randomUUID()
      },
      body: JSON.stringify({
        video: item.video.videoId,
        preferredLanguage: "en",
        scenarios: ["summary"]
      })
    });
    const result = await pollCatalogJob(created, apiKey, (job) => {
      status.textContent = `${stageLabels[job.stage] ?? "Processing…"} ${job.progress ?? 0}%`;
    });

    item.latestAnalysisId = result.analysisId;
    item.summary = result.description;
    item.transcript = result.transcript;
    item.lastProcessedAt = new Date().toISOString();

    const states = card.querySelector(".video-state-list");
    states.replaceChildren();
    addVideoState(states, "Processed", true);
    addVideoState(states, "Transcript extracted", Boolean(result.transcript));
    addVideoState(states, "Summary generated", Boolean(result.description?.trim()));

    const processed = card.querySelector('[data-kind="processed"]');
    const processedTime = document.createElement("time");
    processedTime.dateTime = item.lastProcessedAt;
    processedTime.textContent = `Processed ${formatDateTime(item.lastProcessedAt)}`;
    processed.replaceChildren(processedTime);

    const summaryText = result.description?.trim();
    if (summaryText) {
      addContentSwitcher(
        card.querySelector(".library-video-summary"),
        item.description || "No description is available.",
        summaryText);
    }

    const transcriptUrl = getArtifactUrl(result.transcript);
    if (transcriptUrl) {
      const transcriptButton = document.createElement("button");
      transcriptButton.type = "button";
      transcriptButton.className = "artifact-button";
      transcriptButton.textContent = "Open transcript ↗";
      transcriptButton.addEventListener("click", () => openArtifact(transcriptUrl, apiKey));
      button.before(transcriptButton);
    }

    status.textContent = "Processing complete";
    button.remove();
  } catch (error) {
    button.disabled = false;
    status.textContent = "";
    showLibraryError(error instanceof Error ? error.message : "Video processing failed.");
  }
}

async function pollCatalogJob(created, apiKey, onProgress) {
  const deadline = Date.now() + 15 * 60 * 1000;
  while (Date.now() < deadline) {
    const job = await fetchJson(created.statusUrl, apiKey);
    onProgress(job);
    if (job.status === "completed") {
      return fetchJson(created.resultUrl, apiKey);
    }
    if (job.status === "failed") {
      throw new Error(job.error?.message ?? "The video analysis failed.");
    }
    await delay(1500);
  }
  throw new Error("Analysis is still running after 15 minutes. Check the service logs.");
}

async function loadLibraryThumbnail(container, item, apiKey) {
  const artifactUrl = getArtifactUrl(item.thumbnail);
  if (!artifactUrl && !item.thumbnailUrl) {
    container.textContent = "Thumbnail unavailable";
    return;
  }

  if (!artifactUrl) {
    const image = document.createElement("img");
    image.src = item.thumbnailUrl;
    image.alt = `Thumbnail for ${item.video.title}`;
    image.referrerPolicy = "no-referrer";
    image.addEventListener("error", () => {
      container.textContent = "Thumbnail unavailable";
    }, { once: true });
    container.replaceChildren(image);
    return;
  }

  try {
    const response = await fetch(artifactUrl, { headers: { "X-Api-Key": apiKey } });
    if (!response.ok) throw new Error();

    const objectUrl = URL.createObjectURL(await response.blob());
    libraryThumbnailUrls.push(objectUrl);
    const image = document.createElement("img");
    image.src = objectUrl;
    image.alt = `Thumbnail for ${item.video.title}`;
    container.replaceChildren(image);
  } catch {
    container.textContent = "Thumbnail unavailable";
  }
}

async function openArtifact(artifactUrl, apiKey) {
  const artifactWindow = window.open("", "_blank");
  try {
    if (!artifactWindow) throw new Error("Allow pop-ups to open the transcript.");
    artifactWindow.document.title = "Loading transcript…";
    artifactWindow.document.body.textContent = "Loading transcript…";

    const response = await fetch(artifactUrl, { headers: { "X-Api-Key": apiKey } });
    if (!response.ok) throw new Error("Transcript download failed.");

    const objectUrl = URL.createObjectURL(await response.blob());
    artifactWindow.location.replace(objectUrl);
    setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
  } catch (error) {
    artifactWindow?.close();
    showLibraryError(error instanceof Error ? error.message : "The transcript could not be opened.");
  }
}

async function pollJob(created, apiKey) {
  const deadline = Date.now() + 15 * 60 * 1000;

  while (Date.now() < deadline) {
    const job = await fetchJson(created.statusUrl, apiKey);
    updateStatus(job.stage, job.progress ?? 0);

    if (job.status === "completed") {
      const result = await fetchJson(created.resultUrl, apiKey);
      await renderResult(result, apiKey);
      return;
    }

    if (job.status === "failed") {
      throw new Error(job.error?.message ?? "The video analysis failed.");
    }

    await delay(1500);
  }

  throw new Error("Analysis is still running after 15 minutes. Check the service logs.");
}

async function renderResult(result, apiKey) {
  statusPanel.hidden = true;
  videoTitle.textContent = result.video.title;
  channelName.textContent = result.video.channelName ?? "Unknown channel";
  summary.textContent = result.description || "No summary was returned.";
  providerBadge.textContent = `${capitalize(result.analysis?.provider ?? "unknown")} summary`;
  youtubeLink.href = result.video.url;

  await loadThumbnail(result.thumbnail?.artifactUrl, apiKey, result.video.title);
  resultPanel.hidden = false;
  resultPanel.scrollIntoView({ behavior: "smooth", block: "nearest" });
}

async function loadThumbnail(artifactUrl, apiKey, title) {
  if (thumbnailObjectUrl) {
    URL.revokeObjectURL(thumbnailObjectUrl);
    thumbnailObjectUrl = undefined;
  }

  thumbnail.hidden = true;
  thumbnailPlaceholder.hidden = false;
  thumbnailPlaceholder.textContent = "Thumbnail unavailable";
  if (!artifactUrl) return;

  try {
    const response = await fetch(artifactUrl, { headers: { "X-Api-Key": apiKey } });
    if (!response.ok) throw new Error("Thumbnail download failed.");

    thumbnailObjectUrl = URL.createObjectURL(await response.blob());
    thumbnail.src = thumbnailObjectUrl;
    thumbnail.alt = `Thumbnail for ${title}`;
    thumbnail.hidden = false;
    thumbnailPlaceholder.hidden = true;
  } catch {
    thumbnailPlaceholder.textContent = "Thumbnail unavailable";
  }
}

async function fetchJson(url, apiKey, options = {}) {
  const headers = new Headers(options.headers ?? {});
  headers.set("X-Api-Key", apiKey);

  const response = await fetch(url, { ...options, headers });
  if (response.ok) return response.json();

  let message = `Request failed with status ${response.status}.`;
  try {
    const problem = await response.json();
    message = problem.title ?? problem.detail ?? message;
  } catch {
    // Keep the status-based message when the response is not JSON.
  }
  throw new Error(message);
}

function setLibraryBusy(isBusy) {
  loadLibraryButton.disabled = isBusy;
  libraryLoading.hidden = !isBusy;
}

function showLibraryError(message) {
  libraryError.textContent = message;
  libraryError.hidden = false;
}

function updateStatus(stage, progress) {
  const normalizedProgress = Math.max(0, Math.min(100, Number(progress) || 0));
  statusPanel.hidden = false;
  statusMessage.textContent = stageLabels[stage] ?? "Processing video…";
  statusPercent.textContent = `${normalizedProgress}%`;
  progressBar.style.width = `${normalizedProgress}%`;
}

function resetOutput() {
  errorPanel.hidden = true;
  resultPanel.hidden = true;
  statusPanel.hidden = false;
  progressBar.style.width = "0%";
}

function showError(message) {
  statusPanel.hidden = true;
  errorPanel.textContent = message;
  errorPanel.hidden = false;
}

function setBusy(isBusy) {
  submitButton.disabled = isBusy;
  videoInput.disabled = isBusy;
  apiKeyInput.disabled = isBusy;
  loadLibraryButton.disabled = isBusy;
  submitButton.querySelector("span").textContent = isBusy ? "Analyzing…" : "Run analysis";
}

function saveApiKey(apiKey) {
  sessionStorage.setItem(apiKeyStorageKey, apiKey);
}

function getArtifactUrl(artifact) {
  return artifact?.downloadUrl ?? artifact?.artifactUrl;
}

function releaseLibraryThumbnails() {
  for (const objectUrl of libraryThumbnailUrls) URL.revokeObjectURL(objectUrl);
  libraryThumbnailUrls = [];
}

function formatDateTime(value) {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(value));
}

function formatShortDate(value) {
  return new Intl.DateTimeFormat(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric"
  }).format(new Date(value));
}

function formatDuration(value) {
  const totalSeconds = Math.max(0, Math.round(Number(value) || 0));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`
    : `${minutes}:${String(seconds).padStart(2, "0")}`;
}

function formatCount(count, noun) {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function capitalize(value) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
