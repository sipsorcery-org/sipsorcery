// Page B's decoded frames, read off the page's main thread for their frame numbers. Reading a
// frame's barcode strip means reading it back from the GPU, which waits for the GPU; on the main
// thread that held up the test card's timer, so page A sent fewer frames and page B fell behind.
//
// Most of a read is waiting (at 4K one takes longer than a frame lasts), so several are kept in
// flight. If reads still fall behind (a slower GPU, say), the reader takes every frame as it comes
// and reads the ones it has room for: the rest are reported as not read rather than dropped
// unseen, where they'd have counted as missing.
//
// Takes { card } (whether frames carry the barcode) first, then { frame } for each frame page B
// decodes, and posts, in order, one { n (frame number, -1 if unreadable, null if not read), rtp
// (the frame's RTP timestamp, which the page matches with the <video>'s for latency), skipped (no
// room to read it), error } for every frame.

import { readFrame } from "./card.js";

const IN_FLIGHT = 4;

let card = false;
let reading = 0;
const results = [];   // in frame order: results, or promises of them
let posting = null;

async function post() {
  while (results.length) postMessage(await results.shift());
  posting = null;
}

onmessage = ({ data }) => {
  if (!data.frame) {
    card = data.card;
    return;
  }
  const frame = data.frame;
  const rtp = frame.metadata?.().rtpTimestamp;
  if (card && reading < IN_FLIGHT) {
    reading++;
    results.push(read(frame, rtp).finally(() => reading--));
  } else {
    frame.close();
    results.push({ n: null, rtp, skipped: card });
  }
  posting ??= post();
};

async function read(frame, rtp) {
  try {
    return { n: await readFrame(frame), rtp };
  } catch (err) {
    return { n: null, rtp, error: err.message };
  } finally {
    frame.close();
  }
}
