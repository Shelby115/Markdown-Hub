import { useState } from "react";
import { Link } from "react-router-dom";
import { PoolsCards } from "../components/pools/PoolsCards";
import { PoolsConsole } from "../components/pools/PoolsConsole";
import { PoolsShelf } from "../components/pools/PoolsShelf";
import { PoolsSplit } from "../components/pools/PoolsSplit";
import { useGenerationPools } from "../components/pools/useGenerationPools";
import "../styles/pool-lab.css";

/**
 * TEMPORARY. Four takes on the same generation-pool screen, all wired to the real API through
 * their own copy of the shared hook, so each can be clicked around independently. Once one is
 * picked this page and the three losers go away and the winner becomes the real page.
 */
const VARIANTS = [
  {
    id: "console",
    name: "A · Control room",
    pitch: "Everything on one screen. Dense rows, segmented meters, editor as a drawer. Fastest to scan, least friendly.",
  },
  {
    id: "cards",
    name: "B · Cards",
    pitch: "A tile per pool with a progress ring, editing in a modal. Reads at a glance; costs vertical space.",
  },
  {
    id: "split",
    name: "C · Two-pane",
    pitch: "List rail on the left, the selected pool on the right. Nothing jumps around; you see one pool at a time.",
  },
  {
    id: "shelf",
    name: "D · Shelf",
    pitch: "Status banner up top, pools as full-width rows that expand in place. No modals, no second pane.",
  },
] as const;

type VariantId = (typeof VARIANTS)[number]["id"];

export function PoolLab() {
  const [active, setActive] = useState<VariantId>("console");

  // One controller per variant: they poll and hold edit state independently, so clicking around
  // in one doesn't move another out from under you.
  const consoleController = useGenerationPools();
  const cardsController = useGenerationPools();
  const splitController = useGenerationPools();
  const shelfController = useGenerationPools();

  const current = VARIANTS.find((v) => v.id === active)!;

  return (
    <div className="admin-page pool-lab">
      <div className="pool-lab-head">
        <h1>Pool design lab</h1>
        <Link to="/admin">← Admin</Link>
      </div>
      <p className="muted pool-lab-blurb">
        Temporary. Four layouts for the same screen, all live against the real API - editing, pausing, generating, and
        forgetting all really happen. Pick one and the other three get deleted.
      </p>

      <div className="pool-lab-tabs" role="tablist">
        {VARIANTS.map((variant) => (
          <button
            key={variant.id}
            role="tab"
            aria-selected={active === variant.id}
            className={`pool-lab-tab${active === variant.id ? " pool-lab-tab-active" : ""}`}
            onClick={() => setActive(variant.id)}
          >
            {variant.name}
          </button>
        ))}
      </div>

      <p className="muted pool-lab-pitch">{current.pitch}</p>

      <div className="pool-lab-stage">
        {active === "console" && <PoolsConsole c={consoleController} />}
        {active === "cards" && <PoolsCards c={cardsController} />}
        {active === "split" && <PoolsSplit c={splitController} />}
        {active === "shelf" && <PoolsShelf c={shelfController} />}
      </div>
    </div>
  );
}
