import { useEffect, useState } from "react";

const RADIUS = 9;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

/**
 * Counts down to the generator's next pass. The server sends the real seconds remaining (it's the
 * only thing that knows when the loop actually wakes up); this ticks locally in between so the
 * dial moves every second rather than jumping on each poll, and resyncs whenever a fresh value
 * arrives. Shows nothing while paused or outside the window - there's no pass to wait for - and
 * a working state while an entry is mid-generation.
 */
export function NextCheckDial({
  seconds,
  intervalSeconds,
  running,
  working,
}: {
  seconds: number | null;
  intervalSeconds: number;
  running: boolean;
  working: boolean;
}) {
  // Held as a wall-clock deadline rather than a decrementing counter: subtracting 1 per tick
  // accumulates the drift of every late timer callback, which showed up as the display jumping
  // backwards by a few seconds each time a fresh server value arrived.
  const [deadline, setDeadline] = useState(() => Date.now() + (seconds ?? 0) * 1000);
  const [now, setNow] = useState(Date.now());

  useEffect(() => {
    if (seconds !== null) {
      setDeadline(Date.now() + seconds * 1000);
      setNow(Date.now());
    }
  }, [seconds]);

  useEffect(() => {
    if (!running || working || seconds === null) return;
    const timer = setInterval(() => setNow(Date.now()), 250);
    return () => clearInterval(timer);
  }, [running, working, seconds]);

  const remaining = Math.max(0, Math.round((deadline - now) / 1000));

  if (working) {
    return (
      <span className="ai-pools-dial ai-pools-dial-working" title="Writing an entry now.">
        <span className="ai-pools-dial-spinner" aria-hidden="true" />
      </span>
    );
  }

  if (!running || seconds === null) {
    return null;
  }

  const fraction = intervalSeconds > 0 ? Math.min(1, remaining / intervalSeconds) : 0;

  return (
    <span
      className="ai-pools-dial"
      title={`Next check for a pool to top up in ${remaining}s.`}
      role="timer"
      aria-label={`Next check in ${remaining} seconds`}
    >
      <svg viewBox="0 0 24 24" aria-hidden="true">
        <circle cx="12" cy="12" r={RADIUS} className="ai-pools-dial-track" />
        <circle
          cx="12"
          cy="12"
          r={RADIUS}
          className="ai-pools-dial-hand"
          strokeDasharray={CIRCUMFERENCE}
          strokeDashoffset={CIRCUMFERENCE * (1 - fraction)}
        />
      </svg>
      <span className="ai-pools-dial-count">{remaining}s</span>
    </span>
  );
}
