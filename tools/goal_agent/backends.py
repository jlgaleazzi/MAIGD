"""Backends: anything with `complete(system, prompt) -> str`.

    AnthropicBackend       Claude via the Messages API           (ANTHROPIC_API_KEY)
    OpenAICompatBackend    any /v1/chat/completions server, e.g. the project's local Ollama
                           (`ollama create qwen3.6-crew -f tools/Modelfile`)
    ScaffoldBackend        offline: writes compilable *placeholder* classes (marked as stubs,
                           so the agent never counts them as done). For wiring tests only.

Standard library only: no SDK to install.
"""
import json
import os
import re
import urllib.error
import urllib.request
from typing import Optional

DEFAULT_ANTHROPIC_MODEL = "claude-sonnet-5-5"
DEFAULT_LOCAL_MODEL = "qwen3.6-crew"
DEFAULT_LOCAL_URL = "http://localhost:11434/v1"


class BackendError(RuntimeError):
    pass


def _post(url: str, headers: dict, payload: dict, timeout: int) -> dict:
    req = urllib.request.Request(url, json.dumps(payload).encode("utf-8"),
                                 {"content-type": "application/json", **headers})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", "replace")[:500]
        raise BackendError(f"HTTP {e.code} from {url}: {detail}")
    except (urllib.error.URLError, TimeoutError) as e:
        raise BackendError(f"cannot reach {url}: {e}")


class AnthropicBackend:
    name = "anthropic"

    def __init__(self, model: str = DEFAULT_ANTHROPIC_MODEL, api_key: Optional[str] = None,
                 max_tokens: int = 16000, timeout: int = 600, base_url: str = "https://api.anthropic.com"):
        self.model = model
        self.api_key = api_key or os.environ.get("ANTHROPIC_API_KEY")
        self.max_tokens, self.timeout = max_tokens, timeout
        self.base_url = base_url.rstrip("/")
        if not self.api_key:
            raise BackendError("ANTHROPIC_API_KEY is not set")

    def complete(self, system: str, prompt: str) -> str:
        data = _post(f"{self.base_url}/v1/messages",
                     {"x-api-key": self.api_key, "anthropic-version": "2023-06-01"},
                     {"model": self.model, "max_tokens": self.max_tokens, "system": system,
                      "messages": [{"role": "user", "content": prompt}]}, self.timeout)
        text = "".join(b.get("text", "") for b in data.get("content", []) if b.get("type") == "text")
        if not text:
            raise BackendError(f"empty reply (stop_reason={data.get('stop_reason')})")
        return text


class OpenAICompatBackend:
    name = "openai"

    def __init__(self, model: str = DEFAULT_LOCAL_MODEL, base_url: str = DEFAULT_LOCAL_URL,
                 api_key: Optional[str] = None, max_tokens: int = 16000, timeout: int = 1800):
        self.model, self.base_url = model, base_url.rstrip("/")
        self.api_key = api_key or os.environ.get("OPENAI_API_KEY", "")
        self.max_tokens, self.timeout = max_tokens, timeout

    def complete(self, system: str, prompt: str) -> str:
        headers = {"authorization": f"Bearer {self.api_key}"} if self.api_key else {}
        data = _post(f"{self.base_url}/chat/completions", headers,
                     {"model": self.model, "max_tokens": self.max_tokens,
                      "messages": [{"role": "system", "content": system},
                                   {"role": "user", "content": prompt}]}, self.timeout)
        try:
            return data["choices"][0]["message"]["content"]
        except (KeyError, IndexError):
            raise BackendError(f"unexpected reply shape: {str(data)[:300]}")


class ScaffoldBackend:
    """Offline placeholder writer. Reads the task header out of the prompt, so it needs no model.

    It only knows how to create missing types listed under "Create:" in the prompt. The files
    carry GOAL-AGENT-SCAFFOLD, which the scanner reads as a stub: the gap stays open.
    """
    name = "scaffold"

    def complete(self, system: str, prompt: str) -> str:
        m = re.search(r"^Create: (.+)$", prompt, re.M)
        if not m:
            return "NOTE: scaffold backend can only create files listed under 'Create:'"
        blocks = []
        for rel in (p.strip() for p in m.group(1).split(",")):
            if not rel.endswith(".cs"):
                continue
            name = os.path.splitext(os.path.basename(rel))[0]
            area = rel.split("/")[1] if rel.count("/") >= 2 else "Core"
            blocks.append(
                f"```csharp\n// FILE: {rel}\nusing UnityEngine;\n\nnamespace Biplanes.{area}\n{{\n"
                f"    // GOAL-AGENT-SCAFFOLD: placeholder, replace with the real implementation.\n"
                f"    public class {name} : MonoBehaviour\n    {{\n    }}\n}}\n```")
        return "\n\n".join(blocks) or "NOTE: nothing to scaffold"


def make_backend(kind: str, model: Optional[str] = None, base_url: Optional[str] = None):
    if kind == "anthropic":
        return AnthropicBackend(model or DEFAULT_ANTHROPIC_MODEL)
    if kind == "openai":
        return OpenAICompatBackend(model or DEFAULT_LOCAL_MODEL, base_url or DEFAULT_LOCAL_URL)
    if kind == "scaffold":
        return ScaffoldBackend()
    raise BackendError(f"unknown backend {kind!r}")
