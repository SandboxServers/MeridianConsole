"""Tests for the Claude Code hooks in .claude/hooks/.

Run from the repo root:  python -m unittest discover -s .claude/hooks/tests -v
"""

import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HOOKS = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HOOKS))

import orchestrator_guard  # noqa: E402
import resume_card  # noqa: E402
import shell_guard  # noqa: E402


def run_hook(script, payload, env=None):
    proc = subprocess.run(
        [sys.executable, str(HOOKS / script)],
        input=json.dumps(payload),
        capture_output=True,
        text=True,
        env={**os.environ, **(env or {})},
        timeout=30,
    )
    return proc.returncode, proc.stdout, proc.stderr


class ShellGuardTests(unittest.TestCase):
    def assert_refused(self, command):
        self.assertIsNotNone(shell_guard.verdict(command), command)

    def assert_allowed(self, command):
        self.assertIsNone(shell_guard.verdict(command), command)

    def test_plain_reads_are_refused(self):
        for command in (
            "cat src/Dhadgar.Nodes/Program.cs",
            "grep -rn Enroll src",
            "sed -n 1,40p CLAUDE.md",
            "Get-Content README.md | Select-Object -First 20",
            "cd src && cat Program.cs",
            "head -50 file.cs | grep foo",
        ):
            self.assert_refused(command)

    def test_mixed_or_writing_commands_are_allowed(self):
        for command in (
            "git log --oneline | grep seam",
            "dotnet test tests/Dhadgar.Nodes.Tests",
            "cat a.txt > b.txt",
            "tail -f logs/app.log",
            "sed -i 's/a/b/' file.txt",
            'git commit -m "cat the file"',
            "gh pr checks 12 --watch",
        ):
            self.assert_allowed(command)

    def test_wrapped_reads_are_refused(self):
        for command in (
            'powershell -Command "Get-Content x.cs"',
            "bash -c 'cat x.cs | head -5'",
            'pwsh -c "gc x.cs"',
        ):
            self.assert_refused(command)

    def test_wrapped_non_reads_are_allowed(self):
        for command in (
            'pwsh -Command "dotnet build"',
            'bash -c "git log | grep x"',
            'powershell -Command "Get-Content a.txt > b.txt"',
        ):
            self.assert_allowed(command)

    def test_bypass_comment(self):
        self.assert_allowed("cat binary.dat # guard-ok")
        self.assert_allowed("git worktree prune # guard-ok")

    def test_worktree_prune_is_refused(self):
        for command in (
            "git worktree prune",
            "git -C C:/repo worktree prune --verbose",
            'bash -c "git worktree prune"',
            "git fetch && git worktree prune",
        ):
            self.assert_refused(command)

    def test_other_worktree_commands_are_allowed(self):
        for command in (
            "git worktree list",
            "git worktree add .claude/worktrees/sr-01 -b seam/sr-01 origin/main",
            "git worktree remove .claude/worktrees/sr-01",
            'git commit -m "do not run git worktree prune"',
        ):
            self.assert_allowed(command)

    def test_hook_protocol(self):
        code, _, err = run_hook("shell_guard.py", {"tool_input": {"command": "cat x.cs"}})
        self.assertEqual(code, 2)
        self.assertIn("Read tool", err)
        code, _, _ = run_hook("shell_guard.py", {"tool_input": {"command": "dotnet build"}})
        self.assertEqual(code, 0)

    def test_bad_payload_is_allowed(self):
        proc = subprocess.run(
            [sys.executable, str(HOOKS / "shell_guard.py")],
            input="not json",
            capture_output=True,
            text=True,
            timeout=30,
        )
        self.assertEqual(proc.returncode, 0)


class OrchestratorGuardTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        (self.root / "docs" / "analysis" / "camp").mkdir(parents=True)
        (self.root / "src").mkdir()
        self.big = self.root / "src" / "Big.cs"
        self.big.write_text("x\n" * 500)

    def tearDown(self):
        self.tmp.cleanup()

    def verdict(self, tool, path, **extra):
        payload = {"tool_name": tool, "tool_input": {"file_path": str(path), **extra}}
        return orchestrator_guard.verdict(payload, self.root)

    def test_ledger_writes_allowed(self):
        self.assertIsNone(self.verdict("Write", self.root / "docs/analysis/camp/RESUME.md"))
        self.assertIsNone(self.verdict("Edit", self.root / ".claude/state/active-campaign"))

    def test_code_writes_refused(self):
        self.assertIsNotNone(self.verdict("Write", self.root / "src/New.cs"))
        self.assertIsNotNone(self.verdict("Edit", self.root / "CLAUDE.md"))

    def test_large_reads_refused_unless_limited(self):
        self.assertIsNotNone(self.verdict("Read", self.big))
        self.assertIsNone(self.verdict("Read", self.big, limit=100))

    def test_subagents_are_exempt(self):
        payload = {"tool_name": "Write", "agent_id": "abc", "tool_input": {"file_path": str(self.root / "src/New.cs")}}
        self.assertIsNone(orchestrator_guard.verdict(payload, self.root))

    def test_empty_agent_id_is_not_a_subagent(self):
        for agent_id in ("", None, 0):
            payload = {"tool_name": "Write", "agent_id": agent_id, "tool_input": {"file_path": str(self.root / "src/New.cs")}}
            self.assertIsNotNone(orchestrator_guard.verdict(payload, self.root), repr(agent_id))

    def test_traversal_out_of_ledger_refused(self):
        self.assertIsNotNone(self.verdict("Write", "docs/analysis/../../src/New.cs"))
        self.assertIsNotNone(self.verdict("Write", self.root / "docs/analysis/camp/../../../src/New.cs"))

    def test_symlink_out_of_ledger_refused(self):
        link = self.root / "docs" / "analysis" / "escape"
        try:
            os.symlink(self.root / "src", link, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("cannot create symlinks here")
        self.assertIsNotNone(self.verdict("Write", link / "New.cs"))

    def test_worktree_ledger_allowed_and_code_refused(self):
        wt = self.root / ".claude" / "worktrees" / "camp-wt"
        (wt / "docs" / "analysis").mkdir(parents=True)
        (wt / "src").mkdir()
        self.assertIsNone(self.verdict("Write", wt / "docs/analysis/x.md"))
        self.assertIsNotNone(self.verdict("Write", wt / "src/x.cs"))
        # Same from inside the worktree with relative paths.
        payload = {"tool_name": "Write", "cwd": str(wt), "tool_input": {"file_path": "docs/analysis/x.md"}}
        self.assertIsNone(orchestrator_guard.verdict(payload, self.root))
        payload["tool_input"]["file_path"] = "src/x.cs"
        self.assertIsNotNone(orchestrator_guard.verdict(payload, self.root))

    def test_hook_protocol(self):
        payload = {"tool_name": "Write", "tool_input": {"file_path": str(self.root / "src/New.cs")}}
        code, _, err = run_hook("orchestrator_guard.py", payload, env={"CLAUDE_PROJECT_DIR": str(self.root)})
        self.assertEqual(code, 2)
        self.assertIn(orchestrator_guard.WRITE_REASON, err)
        payload["tool_input"]["file_path"] = str(self.root / "docs/analysis/camp/RESUME.md")
        code, _, _ = run_hook("orchestrator_guard.py", payload, env={"CLAUDE_PROJECT_DIR": str(self.root)})
        self.assertEqual(code, 0)


class ResumeCardTests(unittest.TestCase):
    def test_card_from_active_campaign(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / ".claude" / "state").mkdir(parents=True)
            (root / ".claude" / "state" / "active-campaign").write_text("seam-reconciliation\n")
            resume = root / "docs" / "analysis" / "seam-reconciliation" / "RESUME.md"
            resume.parent.mkdir(parents=True)
            resume.write_text("\n".join(f"line {i}" for i in range(60)))
            card = resume_card.build_card(root)
            self.assertIn("seam-reconciliation", card)
            self.assertIn("line 39", card)
            self.assertNotIn("line 40", card)

    def test_no_pointer_no_card(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.assertIsNone(resume_card.build_card(Path(tmp)))

    def test_path_names_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / ".claude" / "state").mkdir(parents=True)
            (root / ".claude" / "state" / "active-campaign").write_text("../etc\n")
            self.assertIsNone(resume_card.build_card(root))

    def write_pointer(self, root, name):
        (root / ".claude" / "state").mkdir(parents=True, exist_ok=True)
        (root / ".claude" / "state" / "active-campaign").write_text(name + "\n")

    def test_unsafe_names_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "docs" / "analysis").mkdir(parents=True)
            for name in ("..", "a/../../x", "C:foo", "D:\\x", "..hidden", ".camp", "-x"):
                self.write_pointer(root, name)
                self.assertIsNone(resume_card.build_card(root), name)

    def test_symlinked_resume_outside_base_rejected(self):
        with tempfile.TemporaryDirectory() as tmp, tempfile.TemporaryDirectory() as outside:
            root = Path(tmp)
            secret = Path(outside) / "secret.md"
            secret.write_text("outside the repo")
            camp = root / "docs" / "analysis" / "camp"
            camp.mkdir(parents=True)
            try:
                os.symlink(secret, camp / "RESUME.md")
            except (OSError, NotImplementedError):
                self.skipTest("cannot create symlinks here")
            self.write_pointer(root, "camp")
            self.assertIsNone(resume_card.build_card(root))


if __name__ == "__main__":
    unittest.main()
