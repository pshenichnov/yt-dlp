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

backToChannelsButton.addEventListener("click", showChannelList);

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
    await loadChannels({ preserveErrors: true });
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
    ? `${formatCount(items.length, "channel")} · ${formatCount(totalVideos, "processed video")}`
    : "No processed videos yet. Process a video below to start your library.";

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

async function loadChannelVideos(channel) {
  const apiKey = apiKeyInput.value.trim();
  if (!apiKey) return;

  libraryError.hidden = true;
  libraryLoading.textContent = `Loading ${channel.name || "channel"}…`;
  setLibraryBusy(true);

  try {
    const separator = channel.videosUrl.includes("?") ? "&" : "?";
    const videos = await fetchJson(`${channel.videosUrl}${separator}limit=200`, apiKey);
    renderChannelVideos(channel, videos, apiKey);
  } catch (error) {
    showLibraryError(error instanceof Error ? error.message : "The channel videos could not be loaded.");
  } finally {
    libraryLoading.textContent = "Loading channels…";
    setLibraryBusy(false);
  }
}

function renderChannelVideos(channel, videos, apiKey) {
  releaseLibraryThumbnails();
  videoList.replaceChildren();
  channelList.hidden = true;
  channelVideos.hidden = false;
  selectedChannelName.textContent = channel.name || "Unknown channel";
  selectedChannelCount.textContent = `${formatCount(videos.length, "video")} · newest processed first`;

  for (const item of videos) {
    const card = document.createElement("article");
    card.className = "library-video-card";
    card.innerHTML = `
      <div class="library-thumbnail"><span>Loading thumbnail…</span></div>
      <div>
        <div class="library-video-meta">
          <span>Processed <time data-kind="processed"></time></span>
          <span data-kind="published"></span>
        </div>
        <h3 class="library-video-title"></h3>
        <p class="library-video-summary"></p>
        <div class="library-video-actions">
          <a data-kind="youtube" target="_blank" rel="noreferrer">Watch on YouTube ↗</a>
        </div>
      </div>`;

    card.querySelector(".library-video-title").textContent = item.video.title;
    card.querySelector(".library-video-summary").textContent = item.summary || "No summary was saved.";

    const processedTime = card.querySelector('[data-kind="processed"]');
    processedTime.dateTime = item.lastProcessedAt;
    processedTime.textContent = formatDateTime(item.lastProcessedAt);

    const published = card.querySelector('[data-kind="published"]');
    if (item.video.publishedAt) {
      const publishedTime = document.createElement("time");
      publishedTime.dateTime = item.video.publishedAt;
      publishedTime.textContent = `Published ${formatShortDate(item.video.publishedAt)}`;
      published.append(publishedTime);
    } else {
      published.remove();
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

    videoList.append(card);
    loadLibraryThumbnail(card.querySelector(".library-thumbnail"), item, apiKey);
  }

  channelVideos.scrollIntoView({ behavior: "smooth", block: "start" });
}

function showChannelList() {
  releaseLibraryThumbnails();
  channelVideos.hidden = true;
  channelList.hidden = false;
  libraryError.hidden = true;
  librarySummary.textContent = channels.length
    ? `${formatCount(channels.length, "channel")} · ${formatCount(channels.reduce((sum, channel) => sum + channel.videoCount, 0), "processed video")}`
    : "No processed videos yet. Process a video below to start your library.";
}

async function loadLibraryThumbnail(container, item, apiKey) {
  const artifactUrl = getArtifactUrl(item.thumbnail);
  if (!artifactUrl) {
    container.textContent = "Thumbnail unavailable";
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

function formatCount(count, noun) {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function capitalize(value) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
