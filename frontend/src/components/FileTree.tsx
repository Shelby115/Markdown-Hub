import { createContext, useContext, useEffect, useRef, useState } from "react";
import { api, TemplateInfo, TreeNode } from "../api/client";
import { TemplateVariablesModal } from "./TemplateVariablesModal";

const DRAG_MIME = "text/x-hub-path";

interface FileTreeContextValue {
  activePath: string | null;
  onOpen: (relativePath: string) => void;
  defaultFolderPath: string | null;
  toggleDefaultFolder: (relativePath: string) => void;
  menuOpenPath: string | null;
  toggleMenu: (relativePath: string) => void;
  renamingPath: string | null;
  startRename: (relativePath: string) => void;
  commitRename: (oldPath: string, newBaseName: string) => void;
  cancelRename: () => void;
  deleteFile: (relativePath: string) => void;
  renamingFolderPath: string | null;
  startRenameFolder: (relativePath: string) => void;
  commitRenameFolder: (oldPath: string, newBaseName: string) => void;
  cancelRenameFolder: () => void;
  deleteFolder: (relativePath: string) => void;
  creatingInFolder: string | null;
  createFocusTemplate: boolean;
  templates: TemplateInfo[];
  startCreate: (folderPath: string, focusTemplate?: boolean) => void;
  commitCreate: (folderPath: string, name: string, templateRelativePath?: string) => void;
  cancelCreate: () => void;
  creatingFolderIn: string | null;
  startCreateFolder: (folderPath: string) => void;
  commitCreateFolder: (folderPath: string, name: string) => void;
  cancelCreateFolder: () => void;
  folderMenuOpenPath: string | null;
  toggleFolderMenu: (relativePath: string) => void;
  moveFile: (oldPath: string, newFolderPath: string) => void;
  setDraggingActive: (active: boolean) => void;
}

const FileTreeContext = createContext<FileTreeContextValue | null>(null);

function baseName(relativePath: string): string {
  const name = relativePath.split("/").pop() ?? relativePath;
  return name.replace(/\.md$/i, "");
}

/** Unique {{Variable}} names, in order of first appearance. */
function extractTemplateVariables(content: string): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  const re = /\{\{([^}]+)\}\}/g;
  let match: RegExpExecArray | null;
  while ((match = re.exec(content))) {
    const name = match[1].trim();
    if (!seen.has(name)) {
      seen.add(name);
      result.push(name);
    }
  }
  return result;
}

function substituteTemplateVariables(content: string, values: Record<string, string>): string {
  return content.replace(/\{\{([^}]+)\}\}/g, (_, name) => values[name.trim()] ?? "");
}

interface PendingTemplateCreate {
  folderPath: string;
  name: string;
  content: string;
  variables: string[];
}

export function FileTree({
  onOpen,
  activePath,
  onActiveDeleted,
  defaultFolderPath,
  onDefaultFolderChanged,
}: {
  onOpen: (relativePath: string) => void;
  activePath: string | null;
  onActiveDeleted: () => void;
  defaultFolderPath: string | null;
  onDefaultFolderChanged: (folderPath: string | null) => void;
}) {
  const [nodes, setNodes] = useState<TreeNode[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [menuOpenPath, setMenuOpenPath] = useState<string | null>(null);
  const [renamingPath, setRenamingPath] = useState<string | null>(null);
  const [renamingFolderPath, setRenamingFolderPath] = useState<string | null>(null);
  const [creatingInFolder, setCreatingInFolder] = useState<string | null>(null);
  const [createFocusTemplate, setCreateFocusTemplate] = useState(false);
  const [creatingFolderIn, setCreatingFolderIn] = useState<string | null>(null);
  const [folderMenuOpenPath, setFolderMenuOpenPath] = useState<string | null>(null);
  const [templates, setTemplates] = useState<TemplateInfo[]>([]);
  const [pendingTemplateCreate, setPendingTemplateCreate] = useState<PendingTemplateCreate | null>(null);
  const [rootDragOver, setRootDragOver] = useState(false);
  // True for the whole duration of any file drag (not just while hovering a specific drop
  // target) - used to enlarge/highlight every folder row's drop area for the entire drag, so
  // you don't need to hit a thin row precisely on the first try.
  const [draggingActive, setDraggingActive] = useState(false);

  const load = () => {
    setLoading(true);
    api
      .getTree()
      .then(setNodes)
      .finally(() => setLoading(false));
  };

  useEffect(load, []);
  useEffect(() => {
    api.getTemplates().then(setTemplates).catch(() => {});
  }, []);

  const createPage = async (folderPath: string, name: string, content: string) => {
    const trimmed = name.trim();
    const newPath = (folderPath ? `${folderPath}/${trimmed}` : trimmed) + ".md";
    try {
      await api.savePage(newPath, content);
      setError(null);
      load();
      onOpen(newPath);
    } catch {
      setError(`Couldn't create "${trimmed}" - a file with that name may already exist.`);
    }
  };

  const createFolder = async (folderPath: string, name: string) => {
    const trimmed = name.trim();
    if (!trimmed) return;
    const newPath = folderPath ? `${folderPath}/${trimmed}` : trimmed;
    try {
      await api.createFolder(newPath);
      setError(null);
      load();
    } catch {
      setError(`Couldn't create folder "${trimmed}" - a file or folder with that name may already exist.`);
    }
  };

  const context: FileTreeContextValue = {
    activePath,
    onOpen,
    defaultFolderPath,
    toggleDefaultFolder: async (relativePath) => {
      const next = defaultFolderPath === relativePath ? null : relativePath;
      onDefaultFolderChanged(next);
      try {
        await api.setDefaultFolder(next);
      } catch {
        setError("Couldn't save your home folder preference.");
        onDefaultFolderChanged(defaultFolderPath); // revert the optimistic update
      }
    },
    menuOpenPath,
    toggleMenu: (relativePath) => setMenuOpenPath((cur) => (cur === relativePath ? null : relativePath)),
    renamingPath,
    startRename: (relativePath) => {
      setMenuOpenPath(null);
      setRenamingPath(relativePath);
    },
    cancelRename: () => setRenamingPath(null),
    commitRename: async (oldPath, newBaseName) => {
      setRenamingPath(null);
      const trimmed = newBaseName.trim();
      if (!trimmed || trimmed === baseName(oldPath)) return;
      const folder = oldPath.split("/").slice(0, -1).join("/");
      const newPath = (folder ? `${folder}/${trimmed}` : trimmed) + ".md";
      try {
        await api.renamePage(oldPath, newPath);
        setError(null);
        load();
        if (activePath === oldPath) onOpen(newPath);
      } catch {
        setError(`Couldn't rename "${baseName(oldPath)}" - the name may already be in use.`);
      }
    },
    renamingFolderPath,
    startRenameFolder: (relativePath) => {
      setFolderMenuOpenPath(null);
      setRenamingFolderPath(relativePath);
    },
    cancelRenameFolder: () => setRenamingFolderPath(null),
    commitRenameFolder: async (oldPath, newBaseName) => {
      setRenamingFolderPath(null);
      const trimmed = newBaseName.trim();
      const currentName = oldPath.split("/").pop() ?? oldPath;
      if (!trimmed || trimmed === currentName) return;
      const parent = oldPath.split("/").slice(0, -1).join("/");
      const newPath = parent ? `${parent}/${trimmed}` : trimmed;
      try {
        await api.renameFolder(oldPath, newPath);
        setError(null);
        load();
        // If the currently-open page lived inside the renamed folder, follow it to its new path.
        if (activePath?.startsWith(oldPath + "/")) {
          onOpen(newPath + activePath.slice(oldPath.length));
        }
      } catch {
        setError(`Couldn't rename "${currentName}" - the name may already be in use.`);
      }
    },
    deleteFolder: async (relativePath) => {
      setFolderMenuOpenPath(null);
      const folderLabel = relativePath.split("/").pop() ?? relativePath;
      if (
        !window.confirm(
          `Delete "${folderLabel}" and everything inside it? All files and subfolders in this folder will be deleted. This can't be undone.`
        )
      )
        return;
      try {
        await api.deleteFolder(relativePath);
        setError(null);
        load();
        if (activePath === relativePath || activePath?.startsWith(relativePath + "/")) onActiveDeleted();
      } catch {
        setError(`Couldn't delete "${folderLabel}".`);
      }
    },
    deleteFile: async (relativePath) => {
      setMenuOpenPath(null);
      if (!window.confirm(`Delete "${baseName(relativePath)}"? This can't be undone.`)) return;
      try {
        await api.deletePage(relativePath);
        setError(null);
        load();
        if (activePath === relativePath) onActiveDeleted();
      } catch {
        setError(`Couldn't delete "${baseName(relativePath)}".`);
      }
    },
    creatingInFolder,
    createFocusTemplate,
    templates,
    startCreate: (folderPath, focusTemplate) => {
      setCreatingInFolder(folderPath);
      setCreateFocusTemplate(!!focusTemplate);
    },
    cancelCreate: () => setCreatingInFolder(null),
    creatingFolderIn,
    startCreateFolder: (folderPath) => setCreatingFolderIn(folderPath),
    cancelCreateFolder: () => setCreatingFolderIn(null),
    commitCreateFolder: async (folderPath, name) => {
      setCreatingFolderIn(null);
      await createFolder(folderPath, name);
    },
    folderMenuOpenPath,
    toggleFolderMenu: (relativePath) => setFolderMenuOpenPath((cur) => (cur === relativePath ? null : relativePath)),
    commitCreate: async (folderPath, name, templateRelativePath) => {
      setCreatingInFolder(null);
      const trimmed = name.trim();
      if (!trimmed) return;

      if (!templateRelativePath) {
        await createPage(folderPath, trimmed, "");
        return;
      }
      try {
        const template = await api.getPage(templateRelativePath);
        const variables = extractTemplateVariables(template.content);
        if (variables.length === 0) {
          await createPage(folderPath, trimmed, template.content);
        } else {
          setPendingTemplateCreate({ folderPath, name: trimmed, content: template.content, variables });
        }
      } catch {
        setError(`Couldn't load template "${baseName(templateRelativePath)}".`);
      }
    },
    moveFile: async (oldPath, newFolderPath) => {
      const fileName = oldPath.split("/").pop();
      if (!fileName) return;
      const newPath = newFolderPath ? `${newFolderPath}/${fileName}` : fileName;
      if (newPath === oldPath) return;
      try {
        await api.renamePage(oldPath, newPath);
        setError(null);
        load();
        if (activePath === oldPath) onOpen(newPath);
      } catch {
        setError(`Couldn't move "${baseName(oldPath)}" - a file with that name may already exist there.`);
      }
    },
    setDraggingActive,
  };

  // Folders that need to be force-expanded to reveal activePath, e.g. for
  // "Medonia/Session 1.md" that's the single-element set {"Medonia"}. Also expands the user's
  // chosen default/home folder (and its ancestors) so it's already open when landing on "/".
  const activeAncestors = new Set<string>();
  if (activePath) {
    const parts = activePath.split("/").slice(0, -1);
    let acc = "";
    for (const part of parts) {
      acc = acc ? `${acc}/${part}` : part;
      activeAncestors.add(acc);
    }
  }
  if (defaultFolderPath) {
    const parts = defaultFolderPath.split("/");
    let acc = "";
    for (const part of parts) {
      acc = acc ? `${acc}/${part}` : part;
      activeAncestors.add(acc);
    }
  }

  return (
    <FileTreeContext.Provider value={context}>
      <div
        className={`file-tree ${rootDragOver ? "drag-over" : ""} ${draggingActive ? "dragging-active" : ""}`}
        onDragOver={(e) => {
          if (e.dataTransfer.types.includes(DRAG_MIME)) {
            e.preventDefault();
            e.dataTransfer.dropEffect = "move";
          }
        }}
        onDragEnter={(e) => {
          if (e.dataTransfer.types.includes(DRAG_MIME)) setRootDragOver(true);
        }}
        onDragLeave={(e) => {
          if (!e.currentTarget.contains(e.relatedTarget as Node)) setRootDragOver(false);
        }}
        onDrop={(e) => {
          e.preventDefault();
          setRootDragOver(false);
          const path = e.dataTransfer.getData(DRAG_MIME);
          if (path) context.moveFile(path, "");
        }}
      >
        <div className="file-tree-header">
          <span>Files</span>
          <span className="file-tree-header-actions">
            <button className="icon-button tree-add-button" title="New file at hub root" onClick={() => context.startCreate("")}>
              +
            </button>
            <button
              className="icon-button tree-add-button"
              title="More actions"
              onClick={() => context.toggleFolderMenu("")}
            >
              ⋮
            </button>
            {folderMenuOpenPath === "" && (
              <FolderMenu
                isRoot
                relativePath=""
                folderLabel="/"
                onClose={() => context.toggleFolderMenu("")}
                onNewFolder={() => context.startCreateFolder("")}
                onNewFromTemplate={() => context.startCreate("", true)}
              />
            )}
            <button className="icon-button" title="Refresh - pick up changes made outside the app" onClick={load}>
              ↻
            </button>
          </span>
        </div>
        {error && <div className="banner banner-error tree-error">{error}</div>}
        {creatingInFolder === "" && <CreateFileInput folderPath="" focusTemplate={createFocusTemplate} />}
        {creatingFolderIn === "" && <CreateFolderInput folderPath="" />}
        {loading ? <div className="muted">Loading…</div> : <TreeLevel nodes={nodes} activeAncestors={activeAncestors} />}
      </div>
      {pendingTemplateCreate && (
        <TemplateVariablesModal
          variables={pendingTemplateCreate.variables}
          onCancel={() => setPendingTemplateCreate(null)}
          onSubmit={(values) => {
            const filled = substituteTemplateVariables(pendingTemplateCreate.content, values);
            setPendingTemplateCreate(null);
            void createPage(pendingTemplateCreate.folderPath, pendingTemplateCreate.name, filled);
          }}
        />
      )}
    </FileTreeContext.Provider>
  );
}

function CreateFileInput({ folderPath, focusTemplate }: { folderPath: string; focusTemplate?: boolean }) {
  const ctx = useContext(FileTreeContext)!;
  const [name, setName] = useState("");
  const [templateChoice, setTemplateChoice] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  const selectRef = useRef<HTMLSelectElement>(null);
  const wrapRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (focusTemplate && ctx.templates.length > 0) selectRef.current?.focus();
    else inputRef.current?.focus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const commit = () => ctx.commitCreate(folderPath, name, templateChoice || undefined);

  return (
    <div
      className="tree-create-form"
      ref={wrapRef}
      onBlur={(e) => {
        // Only commit once focus actually leaves this whole form (e.g. clicking the
        // template <select> shouldn't prematurely submit or cancel the name field).
        if (!wrapRef.current?.contains(e.relatedTarget as Node)) commit();
      }}
    >
      <input
        ref={inputRef}
        className="tree-inline-input"
        placeholder="New page name…"
        value={name}
        onChange={(e) => setName(e.target.value)}
        onClick={(e) => e.stopPropagation()}
        onKeyDown={(e) => {
          if (e.key === "Enter") commit();
          if (e.key === "Escape") ctx.cancelCreate();
        }}
      />
      {ctx.templates.length > 0 && (
        <select
          ref={selectRef}
          className="tree-template-select"
          value={templateChoice}
          onClick={(e) => e.stopPropagation()}
          onChange={(e) => setTemplateChoice(e.target.value)}
        >
          <option value="">(blank page)</option>
          {ctx.templates.map((t) => (
            <option key={t.relativePath} value={t.relativePath}>
              {t.pageName}
            </option>
          ))}
        </select>
      )}
    </div>
  );
}

function CreateFolderInput({ folderPath }: { folderPath: string }) {
  const ctx = useContext(FileTreeContext)!;
  const [name, setName] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const commit = () => ctx.commitCreateFolder(folderPath, name);

  return (
    <div className="tree-create-form">
      <input
        ref={inputRef}
        className="tree-inline-input"
        placeholder="New folder name…"
        value={name}
        onChange={(e) => setName(e.target.value)}
        onClick={(e) => e.stopPropagation()}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === "Enter") commit();
          if (e.key === "Escape") ctx.cancelCreateFolder();
        }}
      />
    </div>
  );
}

/** Shared "..." dropdown for both the root header and per-folder rows - set/clear as home
 * folder and create-from-template are lower-frequency actions tucked in here to keep the row
 * itself down to a single "+" quick action. */
function FolderMenu({
  relativePath,
  folderLabel,
  isRoot,
  onClose,
  onNewFolder,
  onNewFromTemplate,
  onToggleHome,
  onRename,
  onDelete,
}: {
  relativePath: string;
  folderLabel: string;
  isRoot?: boolean;
  onClose: () => void;
  onNewFolder: () => void;
  onNewFromTemplate: () => void;
  onToggleHome?: () => void;
  onRename?: () => void;
  onDelete?: () => void;
}) {
  const ctx = useContext(FileTreeContext)!;
  const menuRef = useRef<HTMLDivElement>(null);
  const isDefaultFolder = relativePath === ctx.defaultFolderPath;

  useEffect(() => {
    const onDocClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) onClose();
    };
    document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="tree-menu" ref={menuRef} onClick={(e) => e.stopPropagation()}>
      {!isRoot && (
        <button
          onClick={() => {
            onClose();
            onRename?.();
          }}
        >
          Rename
        </button>
      )}
      {!isRoot && (
        <button
          onClick={() => {
            onClose();
            onToggleHome?.();
          }}
        >
          {isDefaultFolder ? "Unset as home folder" : `Set "${folderLabel}" as home`}
        </button>
      )}
      <button
        onClick={() => {
          onClose();
          onNewFolder();
        }}
      >
        New folder
      </button>
      {ctx.templates.length > 0 && (
        <button
          onClick={() => {
            onClose();
            onNewFromTemplate();
          }}
        >
          New page from template
        </button>
      )}
      {!isRoot && (
        <button
          className="tree-menu-danger"
          onClick={() => {
            onClose();
            onDelete?.();
          }}
        >
          Delete folder
        </button>
      )}
    </div>
  );
}

function TreeLevel({ nodes, activeAncestors }: { nodes: TreeNode[]; activeAncestors: Set<string> }) {
  return (
    <ul className="tree-level">
      {nodes.map((node) => (
        <TreeItem key={node.relativePath} node={node} activeAncestors={activeAncestors} />
      ))}
    </ul>
  );
}

function TreeItem({ node, activeAncestors }: { node: TreeNode; activeAncestors: Set<string> }) {
  const ctx = useContext(FileTreeContext)!;
  const shouldBeExpanded = activeAncestors.has(node.relativePath);
  const [expanded, setExpanded] = useState(shouldBeExpanded);
  const [dragOver, setDragOver] = useState(false);
  const itemRef = useRef<HTMLDivElement>(null);
  const isActive = node.relativePath === ctx.activePath;
  const isRenaming = node.relativePath === ctx.renamingPath;
  const isRenamingFolder = node.relativePath === ctx.renamingFolderPath;
  const isDefaultFolder = node.relativePath === ctx.defaultFolderPath;

  // Only ever force-expand (never force-collapse) so the user's own toggling still works
  // once the currently open file is revealed.
  useEffect(() => {
    if (shouldBeExpanded) setExpanded(true);
  }, [shouldBeExpanded]);

  useEffect(() => {
    if (isActive) itemRef.current?.scrollIntoView({ block: "nearest" });
  }, [isActive]);

  if (node.isFolder) {
    if (isRenamingFolder) {
      return (
        <li>
          <RenameInput
            initialValue={node.name}
            onCommit={(newBaseName) => ctx.commitRenameFolder(node.relativePath, newBaseName)}
            onCancel={ctx.cancelRenameFolder}
          />
        </li>
      );
    }
    return (
      <li>
        <div
          ref={itemRef}
          className={`tree-folder ${dragOver ? "drag-over" : ""}`}
          onClick={() => setExpanded((v) => !v)}
          onDragOver={(e) => {
            if (e.dataTransfer.types.includes(DRAG_MIME)) {
              e.preventDefault();
              e.dataTransfer.dropEffect = "move";
            }
          }}
          onDragEnter={(e) => {
            if (e.dataTransfer.types.includes(DRAG_MIME)) setDragOver(true);
          }}
          onDragLeave={(e) => {
            // dragenter/dragleave fire for every child element (the chevron, name, + button),
            // not just the row's own boundary - without this containment check the highlight
            // flickers on/off as the mouse crosses those children instead of staying steady.
            if (!itemRef.current?.contains(e.relatedTarget as Node)) setDragOver(false);
          }}
          onDrop={(e) => {
            e.preventDefault();
            e.stopPropagation();
            setDragOver(false);
            const path = e.dataTransfer.getData(DRAG_MIME);
            if (path) ctx.moveFile(path, node.relativePath);
          }}
        >
          <span className={`chevron ${expanded ? "open" : ""}`}>▸</span>
          <span className="tree-folder-name">{node.name || "/"}</span>
          {isDefaultFolder && (
            <span className="tree-home-badge" title="Your home folder">
              ⌂
            </span>
          )}
          {dragOver ? (
            <span className="tree-drop-hint">Drop here</span>
          ) : (
            <>
              <button
                className="tree-add-button"
                title={`New file in ${node.name || "/"}`}
                onClick={(e) => {
                  e.stopPropagation();
                  setExpanded(true);
                  ctx.startCreate(node.relativePath);
                }}
              >
                +
              </button>
              <button
                className="tree-add-button"
                title="More actions"
                onClick={(e) => {
                  e.stopPropagation();
                  ctx.toggleFolderMenu(node.relativePath);
                }}
              >
                ⋮
              </button>
              {ctx.folderMenuOpenPath === node.relativePath && (
                <FolderMenu
                  relativePath={node.relativePath}
                  folderLabel={node.name || "/"}
                  onClose={() => ctx.toggleFolderMenu(node.relativePath)}
                  onRename={() => ctx.startRenameFolder(node.relativePath)}
                  onToggleHome={() => ctx.toggleDefaultFolder(node.relativePath)}
                  onNewFolder={() => {
                    setExpanded(true);
                    ctx.startCreateFolder(node.relativePath);
                  }}
                  onNewFromTemplate={() => {
                    setExpanded(true);
                    ctx.startCreate(node.relativePath, true);
                  }}
                  onDelete={() => ctx.deleteFolder(node.relativePath)}
                />
              )}
            </>
          )}
        </div>
        {expanded && (
          <>
            {ctx.creatingInFolder === node.relativePath && (
              <div className="tree-level" style={{ paddingLeft: "0.75rem" }}>
                <CreateFileInput folderPath={node.relativePath} focusTemplate={ctx.createFocusTemplate} />
              </div>
            )}
            {ctx.creatingFolderIn === node.relativePath && (
              <div className="tree-level" style={{ paddingLeft: "0.75rem" }}>
                <CreateFolderInput folderPath={node.relativePath} />
              </div>
            )}
            {node.children && <TreeLevel nodes={node.children} activeAncestors={activeAncestors} />}
          </>
        )}
      </li>
    );
  }

  if (isRenaming) {
    return (
      <li>
        <RenameInput
          initialValue={baseName(node.relativePath)}
          onCommit={(newBaseName) => ctx.commitRename(node.relativePath, newBaseName)}
          onCancel={ctx.cancelRename}
        />
      </li>
    );
  }

  return (
    <li>
      <div
        ref={itemRef}
        className={`tree-file ${isActive ? "active" : ""}`}
        draggable
        onDragStart={(e) => {
          e.dataTransfer.setData(DRAG_MIME, node.relativePath);
          e.dataTransfer.effectAllowed = "move";
          // Deferred: a React state update applied synchronously inside dragstart can trigger
          // a re-render before the browser finishes initializing the native drag operation,
          // which silently cancels the drag before it even begins in some browsers.
          setTimeout(() => ctx.setDraggingActive(true), 0);
        }}
        onDragEnd={() => ctx.setDraggingActive(false)}
        onClick={() => ctx.onOpen(node.relativePath)}
      >
        <span className="tree-file-name">{node.name}</span>
        <button
          className="tree-menu-trigger"
          title="File options"
          onClick={(e) => {
            e.stopPropagation();
            ctx.toggleMenu(node.relativePath);
          }}
        >
          ⋮
        </button>
        {ctx.menuOpenPath === node.relativePath && <FileMenu relativePath={node.relativePath} />}
      </div>
    </li>
  );
}

function RenameInput({
  initialValue,
  onCommit,
  onCancel,
}: {
  initialValue: string;
  onCommit: (newBaseName: string) => void;
  onCancel: () => void;
}) {
  const [value, setValue] = useState(initialValue);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    inputRef.current?.focus();
    inputRef.current?.select();
  }, []);

  const commit = () => onCommit(value);

  return (
    <input
      ref={inputRef}
      className="tree-inline-input"
      value={value}
      onChange={(e) => setValue(e.target.value)}
      onClick={(e) => e.stopPropagation()}
      onKeyDown={(e) => {
        if (e.key === "Enter") commit();
        if (e.key === "Escape") onCancel();
      }}
      onBlur={commit}
    />
  );
}

function FileMenu({ relativePath }: { relativePath: string }) {
  const ctx = useContext(FileTreeContext)!;
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const onDocClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        ctx.toggleMenu(relativePath);
      }
    };
    document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="tree-menu" ref={menuRef} onClick={(e) => e.stopPropagation()}>
      <button onClick={() => ctx.startRename(relativePath)}>Rename</button>
      <button className="tree-menu-danger" onClick={() => ctx.deleteFile(relativePath)}>
        Delete
      </button>
    </div>
  );
}
