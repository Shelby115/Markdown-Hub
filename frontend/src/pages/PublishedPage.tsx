import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";

/** Public, unauthenticated view of a published page. Deliberately uses plain fetch (no
 * Authorization header, no sign-in) - anyone with the link can view this, by design. */
export function PublishedPage() {
  const { slug } = useParams();
  const [data, setData] = useState<{ pageName: string; html: string } | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    setData(null);
    setError(false);
    fetch(`/api/publish/view/${encodeURIComponent(slug ?? "")}`)
      .then((res) => {
        if (!res.ok) throw new Error("not found");
        return res.json();
      })
      .then(setData)
      .catch(() => setError(true));
  }, [slug]);

  if (error) {
    return (
      <div className="splash">
        <h1>Not found</h1>
        <p>This page isn't published, or the link is no longer valid.</p>
      </div>
    );
  }
  if (!data) return <div className="muted published-page-loading">Loading…</div>;

  return (
    <div className="published-page">
      <header className="published-page-header">
        <span className="brand">Markdown Hub</span>
      </header>
      <main className="published-page-content">
        <h1>{data.pageName}</h1>
        <div className="rendered-page" dangerouslySetInnerHTML={{ __html: data.html }} />
      </main>
    </div>
  );
}
