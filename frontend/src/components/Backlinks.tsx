import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";

export function Backlinks({ relativePath }: { relativePath: string }) {
  const [links, setLinks] = useState<{ relativePath: string; pageName: string }[]>([]);

  useEffect(() => {
    api.getBacklinks(relativePath).then(setLinks);
  }, [relativePath]);

  if (links.length === 0) return null;

  return (
    <div className="backlinks">
      <h3>Linked from</h3>
      <ul>
        {links.map((l) => (
          <li key={l.relativePath}>
            <Link to={`/page/${l.relativePath.replace(/\.md$/i, "")}`}>{l.pageName}</Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
