// Self-contained VP9 decode + draw for the shared-session (fan-out) co-viewer. Kept separate from
// RemoteDisplay's module because the co-viewer is read-only: no pointer/keyboard handlers, no .NET
// input callbacks — just decode the producer's frames onto a canvas.

const states = new Map();

function getState(canvasId) {
  let state = states.get(canvasId);
  if (!state) {
    state = { decoder: null, ctx: null, canvas: null, needsKeyFrame: true };
    states.set(canvasId, state);
  }
  return state;
}

export function initializeViewer(canvasId) {
  const canvas = document.getElementById(canvasId);
  if (!canvas) {
    return false;
  }
  const state = getState(canvasId);
  state.canvas = canvas;
  state.ctx = canvas.getContext("2d");
  return true;
}

/**
 * Checks whether this browser can actually display a co-view session before we try to connect, so a
 * locked-down or outdated browser shows a clear message instead of a silent black canvas. Returns an
 * empty string when everything required is present, otherwise a human-readable reason.
 * @returns {Promise<string>}
 */
export async function getUnsupportedReason() {
  if (typeof WebSocket === "undefined") {
    return "This browser has WebSocket disabled, which live co-view requires.";
  }

  if (!window.isSecureContext) {
    return "Live co-view needs a secure (HTTPS) connection so this browser can decode the video. Open this site over HTTPS and try again.";
  }

  if (typeof VideoDecoder === "undefined" || typeof EncodedVideoChunk === "undefined") {
    return "This browser doesn't support the WebCodecs video API that live co-view needs. Use a current version of Chrome or Edge.";
  }

  try {
    const support = await VideoDecoder.isConfigSupported({ codec: "vp09.00.51.08" });
    if (!support || support.supported !== true) {
      return "This browser can't decode the VP9 video that live co-view uses. Use a current version of Chrome or Edge.";
    }
  } catch (e) {
    return "This browser can't decode the VP9 video that live co-view uses. Use a current version of Chrome or Edge.";
  }

  return "";
}

/**
 * Decodes one VP9 frame onto the canvas.
 * @returns {boolean} False if the decoder is waiting for a key frame (the caller should expect the
 * server to replay one on join).
 */
export function drawVideoPacket(canvasId, packet, timestamp, isKeyFrame) {
  const state = getState(canvasId);
  if (!state.ctx) {
    return true;
  }

  if (!state.decoder || state.decoder.state === "closed") {
    state.decoder = createDecoder(state);
    state.needsKeyFrame = true;
  }

  if (state.needsKeyFrame) {
    if (!isKeyFrame) {
      return false;
    }
    state.needsKeyFrame = false;
  }

  state.decoder.decode(new EncodedVideoChunk({
    type: isKeyFrame ? "key" : "delta",
    timestamp: timestamp * 1000,
    data: packet,
  }));

  return true;
}

export function disposeViewer(canvasId) {
  const state = states.get(canvasId);
  if (state?.decoder && state.decoder.state !== "closed") {
    try { state.decoder.close(); } catch (e) { /* already closed */ }
  }
  states.delete(canvasId);
}

function createDecoder(state) {
  const decoder = new VideoDecoder({
    output: (frame) => {
      const canvas = state.canvas;
      // Size the canvas to the stream so the picture isn't stretched; CSS fits it to the viewport.
      if (canvas.width !== frame.displayWidth || canvas.height !== frame.displayHeight) {
        canvas.width = frame.displayWidth;
        canvas.height = frame.displayHeight;
      }
      state.ctx.drawImage(frame, 0, 0, canvas.width, canvas.height);
      frame.close();
    },
    error: (error) => {
      // The next packet rebuilds the decoder, which then waits for a key frame.
      console.warn("Co-viewer VP9 decoder failed.", error);
      state.decoder = null;
    },
  });

  decoder.configure({ codec: "vp09.00.51.08", optimizeForLatency: true });
  return decoder;
}
