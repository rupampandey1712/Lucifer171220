# ADR-007: Tool-based AI agents over the Application layer

- **Status:** Accepted (implemented)
- **Decision:** Use Microsoft Agent Framework on `Microsoft.Extensions.AI.IChatClient` (Google Gemini by default through the official `Google.GenAI` SDK; Foundry Local, Ollama, Azure OpenAI or OpenAI by configuration; an offline rule engine as the fallback). Agents act only via registered tools that wrap Application commands and queries and run under the calling user's identity. Write and Financial tools return pending actions that need a UI confirmation, and no tool can take payment. Input and output guards, token budgets, and audit apply to every call.
- **Alternatives:** Agents with direct DB or SQL access (rejected: bypasses authz and business rules); a hard-coded provider SDK (rejected: lock-in).
- **Consequences:** The assistant can never do anything the user couldn't do through the UI, and every action is auditable. Quality varies with the local model, which an evaluation suite tracks.
