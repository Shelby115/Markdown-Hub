import { useEffect, useState } from "react";

/** Tracks a max-width media query reactively, so components can switch layout (e.g. collapsing
 * toolbar buttons into an overflow menu) as the viewport crosses the breakpoint - not just at
 * mount, since rotating a phone or resizing a window should update it live. */
export function useIsMobile(breakpoint = 768): boolean {
  const query = `(max-width: ${breakpoint}px)`;
  const [isMobile, setIsMobile] = useState(() => window.matchMedia(query).matches);

  useEffect(() => {
    const mql = window.matchMedia(query);
    const onChange = () => setIsMobile(mql.matches);
    onChange();
    mql.addEventListener("change", onChange);
    return () => mql.removeEventListener("change", onChange);
  }, [query]);

  return isMobile;
}
