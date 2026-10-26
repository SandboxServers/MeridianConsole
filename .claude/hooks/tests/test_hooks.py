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


if __name__ == "__main__":
    unittest.main()
