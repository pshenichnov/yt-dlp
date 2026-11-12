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

apiKeyInput.value = sessionStorage.getItem(apiKeyStorageKey) ?? "";

form.addEventListener("submit", async (event) => {
  event.preventDefault();

  const video = videoInput.value.trim();
  const apiKey = apiKeyInput.value.trim();
  if (!video || !apiKey) {
    showError("Enter both a YouTube video ID and your local API key.");
    return;
  }

  sessionStorage.setItem(apiKeyStorageKey, apiKey);
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
  } catch (error) {
    showError(error instanceof Error ? error.message : "Analysis failed.");
  } finally {
    setBusy(false);
  }
});

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
  if (!artifactUrl) return;

  try {
    const response = await fetch(artifactUrl, {
      headers: { "X-Api-Key": apiKey }
    });
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
  submitButton.querySelector("span").textContent = isBusy ? "Analyzing…" : "Run analysis";
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function capitalize(value) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
