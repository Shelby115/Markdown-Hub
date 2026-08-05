import { useEffect, useRef, useState } from "react";
import { api, SearchHit } from "../api/client";
import { renderSearchSnippet } from "./searchSnippet";

export function SearchBar({ onSelect }: { onSelect: (relativePath: string) => void }) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchHit[]>([]);
  const debounceRef = useRef<number | undefined>(undefined);

  useEffect(() => {
    window.clearTimeout(debounceRef.current);
    if (!query.trim()) {
      setResults([]);
      return;
    }
    debounceRef.current = window.setTimeout(() => {
      api.search(query).then(setResults);
    }, 200);
    return () => window.clearTimeout(debounceRef.current);
  }, [query]);

  return (
    <div className="search-bar">
      <input
        type="search"
        placeholder="Search pages and content…"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
      />
      {results.length > 0 && (
        <ul className="search-results">
          {results.map((hit) => (
            <li
              key={hit.relativePath}
              onClick={() => {
                onSelect(hit.relativePath);
                setQuery("");
                setResults([]);
              }}
            >
              <div className="search-result-title">{hit.pageName}</div>
              <div className="search-result-snippet">{renderSearchSnippet(hit.snippet)}</div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
