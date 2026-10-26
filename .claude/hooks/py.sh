# Interpreter launcher for the Python hooks: finds python3, python or py, then execs it.
# Sourced, not run, so it costs no extra process:  set -- <script> <args>; . "<repo>/.claude/hooks/py.sh"
# On Windows (Git Bash: OSTYPE msys or cygwin) it tries python first, because the Microsoft Store
# python3.exe alias stub exists where python.org installs ship no python3.exe.
# With no interpreter it warns on stderr and exits 1: a visible, non-blocking hook error
# (exit 2 would block every tool call). Ported from SandboxServers/Cimmeria.
case "${OSTYPE:-}" in
  msys*|cygwin*) _mc_py_order='python py python3' ;;
  *) _mc_py_order='python3 python' ;;
esac
for _mc_py in $_mc_py_order; do
  if hash "$_mc_py" 2>/dev/null; then
    exec "$_mc_py" "$@"
  fi
done
echo "meridian hook: no python3, python or py on PATH, so ${1:-a hook} did not run and its guard is off" >&2
exit 1
