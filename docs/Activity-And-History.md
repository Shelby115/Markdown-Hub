# Design Document: Version History and Activity Log

## Overview

Add two related capabilities to the markdown notes application:

1. **Version History**

   * Automatically retain recent versions of markdown pages.
   * Allow authorized users to view previous versions.
   * Provide a GitHub-style diff comparison between versions.
   * Allow authorized users to restore previous versions.
   * Make version retention configurable.

2. **Activity Log**

   * Provide administrators with an audit trail of meaningful application activity.
   * Track authentication, file/folder changes, and settings/permission changes.
   * Keep the primary activity view concise and readable.
   * Allow individual events to be expanded for additional details.
   * Make activity retention configurable.

These features should share an underlying history/audit architecture rather than being implemented as unrelated systems.

---

# Goals

* Give users a reliable way to recover from accidental edits or deletions.
* Allow users to understand how their own pages have changed over time.
* Allow administrators to audit activity across the application.
* Avoid creating excessive versions from frequent editor autosaves.
* Keep the activity log useful to a human rather than producing an overwhelming stream of low-value events.
* Keep the implementation simple and maintainable.
* Store complete markdown snapshots rather than implementing delta-based storage.
* Make retention periods configurable.
* Preserve history when files are renamed or moved.

# Non-Goals

* Long-term archival/version control comparable to Git.
* Unlimited historical versions.
* Git repository integration.
* Binary file versioning.
* Complex analytics or dashboards.
* Reconstructing files from binary/delta patches.

---

# 1. Version History

## 1.1 Version creation

The application already performs frequent editor autosaves. Every autosave should **not** necessarily become a version.

A version should represent a meaningful state change rather than every intermediate editor state.

### Required behavior

When a save/autosave occurs:

1. Load the current persisted page content.
2. Compare it with the content being saved.
3. If the content is identical, do nothing.
4. If the content differs, determine whether an appropriate version should be created.
5. The version represents the meaningful before/after state of the document.

The important behavior is that temporary edits should not create unnecessary historical versions.

### Example

Initial document:

```text
Hello world.
```

User adds:

```text
Hello world.
This is some additional text.
```

Two minutes later they remove the text again:

```text
Hello world.
```

Because the final persisted content is identical to the original content, the system should not retain a useless intermediate version merely because the editor temporarily contained different text.

Conversely:

```text
Hello world.
```

becomes:

```text
Hello world.
This is additional text.
```

and later becomes:

```text
Hello world.
This is completely different text.
```

The final state is different from the original state, so a new version should be retained.

### Important implementation detail

The versioning system should not depend solely on the editor's autosave interval.

Instead, treat version creation as a **coalescing/debouncing process**:

* Frequent saves may update the current working state.
* Historical versions should only be created when the resulting content represents a meaningful change from the previous historical state.
* The implementation should avoid creating dozens of versions during a short editing session.

The exact coalescing mechanism should be designed around the application's existing autosave implementation.

The design should favor correctness and simple behavior over excessive version granularity.

---

# 1.2 What a version contains

Each version should be a complete snapshot of the markdown document.

Do **not** implement delta/diff storage.

A version should contain enough information to independently reconstruct the document at that point in time.

At minimum:

* Version ID
* Page/document ID
* Author/user ID
* Creation timestamp
* Complete markdown content
* Relevant document metadata needed to restore the document
* Optional reason/type for the version, such as:

  * Edit
  * Restore
  * Delete
  * Rename/move-related state change

Complete snapshots are preferred because markdown documents are relatively small and this makes:

* Restoration simple
* Historical retrieval simple
* Diff generation simple
* Data integrity easier to reason about

---

# 1.3 Logical document identity

History must belong to the **logical document**, not its filename/path.

For example:

```text
Session 5.md
```

renamed to:

```text
Session 6.md
```

must retain the existing version history.

Similarly, moving a document to another folder must not break its history.

The database should therefore use a stable document/page ID independent of:

* Filename
* Path
* Folder
* Display name

---

# 1.4 Deleted documents

Deleting a document must be recoverable during the version retention period.

A deletion should:

1. Generate an appropriate history/audit event.
2. Preserve the document's historical versions.
3. Allow an authorized user to restore the document.
4. Preserve the document's historical identity when restored.

Deletion should not immediately destroy the historical snapshots.

Once the retention period expires, the historical data may be permanently removed according to the configured retention policy.

---

# 1.5 Renames and moves

Renaming or moving a document must preserve its complete version history.

For example:

```text
Campaign/Session 5.md
```

becomes:

```text
Campaign/Session 6.md
```

The version history remains associated with the same logical document.

The activity log should record the rename/move as a separate activity event.

---

# 1.6 Viewing history

Users should be able to access the history of documents they are authorized to access.

History should display information such as:

* Version timestamp
* User who created the version
* Current/previous version indicators
* Available actions

The interface should make it straightforward to:

* View an older version
* Compare versions
* Restore a version

---

# 1.7 History permissions

History access follows the same ownership concept as requested for activity auditing.

### Regular users

A user can:

* View their own document history.
* Restore their own versions.

A user should not be able to inspect the historical activity/version information belonging to another user simply because they are an administrator of a particular document unless the application's existing permission model explicitly grants that access.

### Administrators

Administrators can:

* View any user's history.
* Compare any versions.
* Restore any version.
* Inspect historical changes through the administrative activity interface.

The authorization checks must be enforced server-side, not only by hiding UI controls.

---

# 1.8 Restoring a version

Restoring an old version must **never overwrite/delete the historical record**.

For example:

```text
Version 1
Version 2
Version 3
Version 4   <-- current
```

Restoring Version 2 should produce:

```text
Version 1
Version 2
Version 3
Version 4
Version 5   <-- restored state of Version 2
```

Version 5 becomes the new current document state.

This ensures the history remains chronological and auditable.

Restoration must also generate an activity event.

---

# 1.9 GitHub-style diff

The application should provide a diff viewer modeled closely after GitHub's code comparison interface.

The comparison should be between two complete markdown versions.

The preferred presentation is:

* Side-by-side comparison
* Line numbers
* Added lines clearly identified
* Removed lines clearly identified
* Unchanged sections collapsed where appropriate
* Ability to expand surrounding context
* Clear indication of which version is older/newer

The comparison should operate on the **raw Markdown source**, not merely the rendered HTML.

This means changes such as:

```markdown
# Session 5
```

to:

```markdown
# Session 6
```

are visible directly.

The diff component should be implemented as a reusable UI component so it can be used from both:

* Document history
* Administrative activity details

---

# 1.10 Metadata

For this initial implementation, document metadata should be treated as part of the document's persisted state where necessary for accurate restoration.

Do not build a specialized metadata-diff system.

If metadata changes are represented in the document's stored representation, those changes can be handled as part of the normal version snapshot/diff.

The system should remain fundamentally **document/content based**, rather than attempting to understand every semantic meaning of Markdown.

---

# 1.11 Version retention

Version retention must be configurable.

Default:

```text
3 days
```

The retention setting should represent the maximum age of retained document versions.

The system should periodically remove versions older than the configured retention period.

The cleanup process should be safe to run repeatedly and should not affect the current document.

The retention value should be configurable without requiring source-code changes.

---

# 2. Activity Log

## 2.1 Purpose

The activity log is an administrative audit interface.

Its primary purpose is to answer:

> "What has been happening in the application?"

It should **not** be a raw stream of every low-level database operation or autosave.

Only meaningful user-visible actions should appear.

The primary activity view should therefore be concise and scannable.

---

# 2.2 Events to record

The activity system should record:

### Authentication

* Successful login
* Logout
* Failed login attempts

Failed login attempts should be grouped when there are many consecutive attempts rather than flooding the activity log with individual entries.

For example, instead of:

```text
9:01 Failed login
9:02 Failed login
9:03 Failed login
9:04 Failed login
9:05 Failed login
```

the activity view may display something equivalent to:

```text
9:01–9:05 — 5 failed login attempts
```

The detailed event should still retain enough information to investigate the individual attempts if necessary.

---

### Files

Record meaningful actions involving files/documents:

* Create
* Modify
* Delete
* Restore
* Rename
* Move

A normal autosave should not create an activity event merely because an autosave request occurred.

The event should represent the meaningful document change.

For modifications, the activity event should link to the corresponding version information so the administrator can inspect the before/after difference.

---

### Folders

Record:

* Create
* Delete
* Rename
* Move, if supported

---

### Settings and permissions

Record any action that changes:

* Application settings
* User settings where administratively relevant
* Permissions
* Roles
* Access rights
* Other security-sensitive configuration

The event should identify what changed and who made the change.

---

# 2.3 Activity event structure

An activity event should contain enough information to answer:

* Who did it?
* What did they do?
* What did they change?
* When did they do it?
* What object was affected?
* Can the administrator inspect the details?

At minimum:

* Event ID
* Timestamp
* User ID, where applicable
* Event/action type
* Object type
* Object ID
* Human-readable object name/path where appropriate
* Related version ID, where applicable
* IP address
* Additional structured details

The detailed information should be stored in a structured manner rather than relying exclusively on a pre-rendered text description.

This allows the UI to evolve later without changing historical records.

---

# 2.4 Activity display

The main activity page should be deliberately clean.

A typical entry might look like:

```text
9:12 AM   Alice modified "Session 5.md"
9:05 AM   Bob logged in
8:54 AM   Alice renamed "Session 4.md" → "Session 5.md"
8:40 AM   Bob changed permissions for "Campaign"
```

The main list should not dump every possible field into each row.

Each activity entry should be expandable/clickable to show more information.

---

# 2.5 Activity details

Selecting an event should display additional details appropriate to that event.

For a document modification:

```text
User: Alice
Action: Modified document
Document: Session 5.md
Time: 9:12 AM
IP Address: 192.168.1.100

[View Before/After]
```

The before/after action should open the GitHub-style diff viewer using the relevant versions.

For a login:

```text
User: Bob
Action: Login
Time: 9:05 AM
IP Address: 192.168.1.101
```

For a permission change:

```text
User: Alice
Action: Changed permissions
Object: Campaign
Time: 8:40 AM
IP Address: 192.168.1.100

Details:
  Bob: Viewer → Editor
```

---

# 2.6 User identification

When an activity is associated with an authenticated user, the primary activity display should show the user's username/name rather than their IP address.

The IP address should remain available in the expanded details.

For example:

```text
9:05 AM — Bob logged in
```

rather than:

```text
9:05 AM — 192.168.1.101 logged in
```

The detailed view can show:

```text
IP Address: 192.168.1.101
```

For unauthenticated events, the IP address should be the primary identifier.

---

# 2.7 Administrative access

The complete activity log is **admin-only**.

Regular users must not have access to the global activity log.

Administrators should be able to see activity generated by all users.

The backend must enforce this authorization.

---

# 2.8 Activity filtering

The activity page should support practical filtering without becoming an analytics application.

At minimum, support filtering by:

* Date/time range
* User
* Activity type/action
* Object/document

The interface should also support searching where practical.

---

# 2.9 Activity retention

Default activity retention:

```text
30 days
```

This is intentionally longer than document version retention.

Activity records older than the configured retention period should be eligible for cleanup.

The default activity page should display/load only the **most recent 14 days**.

Administrators can explicitly request older activity within the retention window.

For example:

```text
Default:
Last 14 days

Available:
Up to 30 days
```

The retention and default display window are separate settings/concepts.

---

# 2.10 Activity pagination

The activity log must not load the entire activity table into the browser.

Use server-side pagination/querying.

The API should support:

* Date filtering
* User filtering
* Event-type filtering
* Object filtering/search
* Pagination
* Sorting by timestamp

The default ordering should be newest first.

---

# 3. Shared Audit/History Architecture

Version history and activity logging should share a common architecture.

The conceptual relationship is:

```text
                    ┌─────────────────┐
                    │     Document    │
                    └────────┬────────┘
                             │
                    ┌────────▼────────┐
                    │ DocumentVersion │
                    └────────┬────────┘
                             │
                             │ related to
                             ▼
                    ┌─────────────────┐
                    │ ActivityEvent   │
                    └─────────────────┘
```

A document modification therefore produces:

1. A new document state.
2. A version snapshot when appropriate.
3. An activity event describing the modification.
4. A relationship between the activity event and the relevant version(s).

This allows an administrator to go from:

```text
Activity:
Alice modified Session 5.md
```

directly to:

```text
Before:
Version 12

After:
Version 13

[Compare]
```

---

# 4. Suggested Data Model

The exact implementation should follow the application's existing database conventions, but conceptually the system should contain entities similar to:

## DocumentVersion

```text
Id
DocumentId
UserId
CreatedAt
Content
Metadata/state required for restoration
VersionType
```

Where `DocumentId` is the stable logical document ID.

## ActivityEvent

```text
Id
CreatedAt
UserId
ActionType
ObjectType
ObjectId
ObjectName
IpAddress
RelatedVersionId
Details
```

`Details` should contain structured event-specific information rather than requiring every possible event to have its own table.

The existing document/page/folder/user models should be reused rather than duplicated.

---

# 5. Cleanup

Version and activity retention should be enforced through a background cleanup mechanism.

The cleanup process should:

### Versions

Delete document versions older than the configured version retention period.

Default:

```text
3 days
```

### Activity

Delete activity events older than the configured activity retention period.

Default:

```text
30 days
```

Cleanup should not delete:

* Current documents
* Current document state
* User accounts
* Other data unrelated to historical records

Cleanup should be safe to execute repeatedly.

---

# 6. Configuration

Add configurable settings for:

```text
VersionHistoryRetentionDays = 3
ActivityLogRetentionDays = 30
ActivityLogDefaultDays = 14
```

Use the application's existing configuration/settings architecture.

The values should not be hard-coded throughout the implementation.

If settings are exposed through an administrative settings UI, validate that:

* Values cannot be negative.
* Extremely large/unreasonable values are handled appropriately.
* Changing retention settings affects future cleanup without corrupting existing history.

---

# 7. API Requirements

The implementation should expose appropriate backend APIs for:

### Versions

* List document versions
* Retrieve a specific version
* Compare two versions
* Restore a version
* Retrieve history for a deleted document where authorized

### Activity

* Query activity events
* Retrieve activity-event details
* Filter activity
* Paginate activity

The backend must enforce authorization for every endpoint.

Do not rely on the frontend hiding unauthorized controls.

---

# 8. UI Requirements

## Document history

A document should have a discoverable **History** action.

The history interface should show:

```text
Version History

Today
  9:12 AM — Alice
  8:47 AM — Alice

Yesterday
  4:31 PM — Alice

[View] [Compare] [Restore]
```

The exact visual design should fit the existing application.

---

## Diff viewer

The diff viewer should closely resemble GitHub's comparison experience.

Provide:

* Side-by-side view
* Line numbers
* Added/removed lines
* Collapsed unchanged regions
* Context expansion
* Clear older/newer labels
* Restore action where authorized

---

## Admin activity page

Add an admin-only page such as:

```text
Activity
```

with:

* Recent activity list
* Filters
* Date range
* Search
* Pagination
* Expandable event details

The page should prioritize **signal over volume**.

---

# 9. Important Behavioral Rules

These rules should be treated as requirements:

1. **Autosaves are not automatically historical versions.**
2. **Identical before/after document states do not create unnecessary history.**
3. **History is associated with a stable document ID, not its filename/path.**
4. **Renaming and moving a document preserves its history.**
5. **Deleting a document is recoverable during the retention period.**
6. **Restoring a version creates a new version.**
7. **Regular users can access their own history.**
8. **Administrators can access and restore any user's history.**
9. **Only administrators can access the global activity log.**
10. **Meaningful file/folder/settings/permission changes generate activity events.**
11. **Activity events should not be generated for inconsequential internal operations.**
12. **Failed login attempts should be grouped when they occur in large consecutive quantities.**
13. **Authenticated activity displays the username primarily; IP address appears in details.**
14. **Activity history defaults to the most recent 14 days but can access up to the configured 30-day retention period.**
15. **Version history defaults to 3 days of retention.**
16. **Retention periods are configurable.**
17. **Historical document versions are complete snapshots, not deltas.**
18. **The diff viewer operates on raw Markdown.**
19. **All authorization must be enforced server-side.**
20. **Historical data cleanup must never remove the current document state.**

---

# 10. Implementation Guidance

Before writing code:

1. Inspect the existing document/page data model.
2. Inspect the current autosave implementation.
3. Inspect the existing authentication/Keycloak user model.
4. Inspect the existing permission/authorization system.
5. Inspect existing settings/configuration infrastructure.
6. Inspect existing background-job/hosted-service infrastructure.
7. Determine the appropriate database migrations for the new entities.
8. Identify existing UI components that can be reused.

Do not introduce a second authentication, authorization, settings, or persistence mechanism when an existing application mechanism already serves that purpose.

The implementation should integrate with the application's existing architecture.

---

# 11. Testing Requirements

Tests should cover at minimum:

### Versioning

* Identical saves do not create unnecessary versions.
* Meaningful changes create versions.
* Rapid successive edits are coalesced appropriately.
* Multiple meaningful edits produce distinct versions.
* Version retention cleanup works.
* Renaming preserves history.
* Moving preserves history.
* Deleting preserves recoverable history.
* Restoring creates a new version.
* Restored content exactly matches the selected historical version.
* Diff output correctly identifies additions/removals.
* Users cannot access unauthorized history.
* Administrators can access all history.

### Activity

* Login creates an event.
* Logout creates an event.
* Failed logins are recorded.
* Consecutive failed logins can be grouped.
* File creation/edit/delete/restore/rename/move are recorded.
* Folder operations are recorded.
* Permission changes are recorded.
* Settings changes are recorded.
* Activity events contain the correct user and timestamp.
* IP addresses are captured.
* Activity details correctly expose IP addresses.
* Document modification events link to the correct versions.
* Administrators can query activity.
* Non-administrators cannot access the global activity API.
* Activity pagination works.
* Activity filtering works.
* Activity retention cleanup works.
* The default 14-day activity view behaves correctly.

### Authorization

Explicitly test authorization at the API/backend level rather than only testing whether UI controls appear.

---

# 12. Implementation Philosophy

The goal is a **small, dependable history/audit subsystem**, not a miniature Git implementation.

Prefer:

* Complete snapshots
* Simple relational data
* Stable document IDs
* Clear event types
* Server-side authorization
* Configurable age-based retention
* Reusable diff UI
* Background cleanup
* Meaningful activity events

Avoid unnecessary complexity such as:

* Delta storage
* Full Git repositories
* Excessive autosave versions
* Large analytics dashboards
* Semantic Markdown parsing solely for versioning
* Duplicate permission systems

The resulting system should make it easy to answer three questions:

> **What changed?**

> **Who changed it?**

> **Can I see or restore what it looked like before?**
