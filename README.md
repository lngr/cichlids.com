# cichlids.com

Revival of the photo-sharing community **cichlids.com** as a modern, mobile-first app.

This file is the entry point for humans. For the full project context, conventions, and
agent instructions, read **[`AGENTS.md`](AGENTS.md)** and **[`PROJECT-CONTEXT.md`](PROJECT-CONTEXT.md)**.

## Getting started

The repository ships a devcontainer (`.devcontainer/`). Open the folder in a devcontainer
(VS Code "Reopen in Container", or your `docker compose` based tooling). On container start
the toolchain is installed and the Backlog.md web UI is launched as a background service.

## Backlog board (user stories, specs, tasks)

User stories, specs, and tasks are managed with [Backlog.md](https://github.com/MrLesk/Backlog.md).
They live as Markdown under [`backlog/`](backlog/) and are the single source of truth for what
gets built (see [`docs/adr/0015`](docs/adr/0015-user-story-and-spec-management.md)).

- **Web board:** **http://localhost:6480** — started automatically on every container start and
  published to the host loopback by the devcontainer, so you only need to open the URL in your
  host browser.
- **CLI alternatives** (inside the container):
  - `backlog board` — Kanban TUI
  - `backlog task list`
  - `backlog task create "Title" --ac "Given … When … Then …"`

Do **not** hand-edit files under `backlog/`. Use the Backlog.md CLI or, for coding agents, the
`backlog` MCP server (configured in [`.mcp.json`](.mcp.json)) so IDs, metadata, and history stay
consistent.

### If the board is not reachable on the host

The web UI binds to `0.0.0.0:6480` inside the container and is published to `127.0.0.1:6480`
on the host via `.devcontainer/docker-compose.yml`. If you changed that file, recreate the
container so the new port mapping takes effect (a plain restart is not enough). You can verify
the service from inside the container with `curl -s -o /dev/null -w '%{http_code}\n' http://localhost:6480`.
