/** Small pulsing-dots indicator for anywhere the app is waiting on an AI response, so it reads
 * as actively working rather than frozen. */
export function ThinkingIndicator({ label = "Thinking" }: { label?: string }) {
  return (
    <span className="thinking-indicator">
      {label}
      <span className="thinking-dots">
        <span></span>
        <span></span>
        <span></span>
      </span>
    </span>
  );
}
