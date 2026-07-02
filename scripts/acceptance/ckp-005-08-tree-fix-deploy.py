"""CKP-005-08: Deploy tree display/checkbox fix per DEPLOYMENT-STAGING-RULES.md §10."""
from __future__ import annotations

import asyncio
import json
import os
import sys
from datetime import datetime

sys.path.insert(0, r"D:\MechPilot\sw-remote-mcp-master\frontend")

from mcp.client.session import ClientSession
from mcp.client.streamable_http import streamablehttp_client

MCP_URL = "http://10.254.60.31:19090/mcp"
Z_DEPLOY = r"Z:\MechPilot\Cockpit\deploy"
Z_ARCHIVE_BASE = r"Z:\MechPilot\archive"
TARGET_ROOT = r"D:\SWAgentAddin"

DEPLOY_FILES = [
    (r"SwAgentAddin.dll", r"SwAgentAddin.dll"),
    (r"SwAgentAddin.pdb", r"SwAgentAddin.pdb"),
    (r"frontend\property-workbench\app.js", r"frontend\property-workbench\app.js"),
    (r"frontend\property-workbench\styles.css", r"frontend\property-workbench\styles.css"),
]

PROTECTED = [
    r"config\config.json",
    r"config\rules.local.json",
]

SW_EXIT_POLL_SEC = 2
SW_EXIT_TIMEOUT_SEC = 60
SW_POST_EXIT_GRACE_SEC = 8


async def call_tool(session, name, arguments):
    result = await session.call_tool(name, arguments)
    texts = [c.text for c in result.content if hasattr(c, "text") and c.text]
    raw = "\n".join(texts)
    try:
        return json.loads(raw)
    except (json.JSONDecodeError, TypeError):
        return {"success": False, "error": raw or "no parseable result"}


def _unwrap(data: dict) -> dict:
    inner = data.get("data")
    return inner if isinstance(inner, dict) else data


def _collect_restore_paths(status: dict) -> list[str]:
    """Collect unique file paths to reopen after deploy, active doc last."""
    open_docs = status.get("open_documents") or []
    active_path = (status.get("active_doc_path") or "").strip()
    seen: set[str] = set()
    inactive: list[str] = []
    active: list[str] = []

    for doc in open_docs:
        if not isinstance(doc, dict):
            continue
        path = (doc.get("file_path") or "").strip()
        if not path or path in seen:
            continue
        seen.add(path)
        if doc.get("is_active") or (
            active_path and path.lower() == active_path.lower()
        ):
            active.append(path)
        else:
            inactive.append(path)

    if active_path and active_path not in seen:
        active.append(active_path)

    return inactive + active


async def _wait_sw_fully_stopped(session, report: dict) -> bool:
    """Wait until SW COM is gone and allow process teardown before restart."""
    elapsed = 0
    while elapsed < SW_EXIT_TIMEOUT_SEC:
        status = _unwrap(await call_tool(session, "sw_get_status", {}))
        report["lifecycle"].append(
            {"step": f"sw_get_status_wait_exit_{elapsed}", "result": status}
        )
        if not status.get("running"):
            print(f"[WAIT-EXIT] running=false after {elapsed}s, grace {SW_POST_EXIT_GRACE_SEC}s")
            await asyncio.sleep(SW_POST_EXIT_GRACE_SEC)
            recheck = _unwrap(await call_tool(session, "sw_get_status", {}))
            report["lifecycle"].append({"step": "sw_get_status_post_grace", "result": recheck})
            if not recheck.get("running"):
                return True
        await asyncio.sleep(SW_EXIT_POLL_SEC)
        elapsed += SW_EXIT_POLL_SEC

    final = _unwrap(await call_tool(session, "sw_get_status", {}))
    report["lifecycle"].append({"step": "sw_get_status_wait_exit_final", "result": final})
    return not final.get("running")


async def main() -> bool:
    ts = datetime.now().strftime("%Y%m%d-%H%M%S")
    archive_dir = fr"{Z_ARCHIVE_BASE}\{ts}\Cockpit-ckp-005-08"
    report = {"archive_dir": archive_dir, "lifecycle": [], "backups": [], "deploys": [], "protected": [], "errors": []}

    async with streamablehttp_client(MCP_URL) as (read, write, _):
        async with ClientSession(read, write) as session:
            await session.initialize()
            print(f"[CONNECTED] {MCP_URL}")

            pre = _unwrap(await call_tool(session, "sw_get_status", {}))
            restore_paths = _collect_restore_paths(pre)
            report["restore_paths"] = restore_paths
            report["lifecycle"].append({"step": "sw_get_status_pre", "result": pre})
            print(
                f"[PRE] sw_get_status: running={pre.get('running')} "
                f"active={pre.get('active_doc')} docs={len(restore_paths)}"
            )

            fc = _unwrap(await call_tool(session, "sw_force_close", {}))
            report["lifecycle"].append({"step": "sw_force_close", "result": fc})
            print(f"[LIFECYCLE] sw_force_close: {fc}")

            stopped = await _wait_sw_fully_stopped(session, report)
            if not stopped:
                for retry in range(2):
                    fc2 = _unwrap(await call_tool(session, "sw_force_close", {}))
                    report["lifecycle"].append({"step": f"sw_force_close_retry_{retry}", "result": fc2})
                    print(f"[LIFECYCLE retry {retry+1}] sw_force_close: {fc2}")
                    stopped = await _wait_sw_fully_stopped(session, report)
                    if stopped:
                        break

            if not stopped:
                report["errors"].append("SolidWorks still running after sw_force_close")
                print("ERROR: SW still running — aborting DLL deploy")
                print(json.dumps(report, ensure_ascii=False, indent=2))
                return False

            await call_tool(session, "sw_file_ops", {"operation": "mkdir", "path": archive_dir})

            for prot in PROTECTED:
                target_path = fr"{TARGET_ROOT}\{prot}"
                exists = _unwrap(
                    await call_tool(session, "sw_file_ops", {"operation": "exists", "path": target_path})
                )
                report["protected"].append({"file": prot, "exists": exists.get("exists"), "touched": False})
                print(f"[PROTECTED] {prot}: exists={exists.get('exists')}")

            for src_rel, tgt_rel in DEPLOY_FILES:
                src_path = os.path.join(Z_DEPLOY, src_rel)
                target_path = fr"{TARGET_ROOT}\{tgt_rel}"
                archive_path = fr"{archive_dir}\{tgt_rel.replace('/', chr(92))}"

                archive_subdir = os.path.dirname(archive_path)
                if archive_subdir:
                    await call_tool(session, "sw_file_ops", {"operation": "mkdir", "path": archive_subdir})

                exists = _unwrap(
                    await call_tool(session, "sw_file_ops", {"operation": "exists", "path": target_path})
                )
                if exists.get("exists"):
                    backup = await call_tool(
                        session,
                        "sw_file_ops",
                        {"operation": "copy", "path": target_path, "target_path": archive_path, "overwrite": True},
                    )
                    ok = _unwrap(backup).get("success", backup.get("success"))
                    print(f"[BACKUP] {tgt_rel}: {ok}")
                    report["backups"].append({"file": tgt_rel, "ok": ok})

                target_subdir = os.path.dirname(target_path)
                if target_subdir:
                    await call_tool(session, "sw_file_ops", {"operation": "mkdir", "path": target_subdir})

                deploy = await call_tool(
                    session,
                    "sw_file_ops",
                    {"operation": "copy", "path": src_path, "target_path": target_path, "overwrite": True},
                )
                ok = _unwrap(deploy).get("success", deploy.get("success"))
                stat = _unwrap(await call_tool(session, "sw_file_ops", {"operation": "stat", "path": target_path}))
                size = (stat.get("item") or stat).get("size", "N/A")
                print(f"[DEPLOY] {tgt_rel}: ok={ok} size={size}")
                report["deploys"].append({"file": tgt_rel, "ok": ok, "size": size})
                if not ok:
                    report["errors"].append(f"deploy failed: {tgt_rel}")

            start = _unwrap(await call_tool(session, "sw_start", {}))
            report["lifecycle"].append({"step": "sw_start", "result": start})
            print(f"[LIFECYCLE] sw_start: {start}")

            report["document_restore"] = []
            for path in restore_paths:
                opened = _unwrap(await call_tool(session, "sw_open_document", {"file_path": path}))
                ok = opened.get("success", opened.get("opened", True))
                print(f"[RESTORE] {path}: ok={ok}")
                report["document_restore"].append({"path": path, "result": opened, "ok": ok})

            post_start = _unwrap(await call_tool(session, "sw_get_status", {}))
            report["lifecycle"].append({"step": "sw_get_status_post_start", "result": post_start})
            print(f"[POST-START] sw_get_status: running={post_start.get('running')}")

    print("\n=== CKP-005-08 DEPLOY SUMMARY ===")
    failed = [d for d in report["deploys"] if not d["ok"]]
    if failed:
        print(f"FAILED: {len(failed)} files")
    else:
        print("All deploy files copied successfully.")
    print(f"Archive: {archive_dir}")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return not failed and not report["errors"]


if __name__ == "__main__":
    raise SystemExit(0 if asyncio.run(main()) else 1)
