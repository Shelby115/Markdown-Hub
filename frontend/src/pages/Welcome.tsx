export function Welcome({ username }: { username?: string }) {
  return (
    <div className="welcome">
      <h1>{username ? `Welcome back, ${username}.` : "Welcome."}</h1>
      <p>Pick a page from the sidebar, or search to jump straight to one.</p>
    </div>
  );
}
