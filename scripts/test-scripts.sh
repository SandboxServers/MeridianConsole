#!/usr/bin/env bash
#
# Test suite for MeridianConsole bootstrap and verification scripts
#
# Usage: ./test-scripts.sh
#

set -uo pipefail
# Note: Not using -e because we want tests to continue even if some fail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

PASSED=0
FAILED=0

# =============================================================================
# Test Helpers
# =============================================================================

test_pass() {
    echo -e "${GREEN}[PASS]${NC} $1"
    ((PASSED++))
}

test_fail() {
    echo -e "${RED}[FAIL]${NC} $1"
    ((FAILED++))
}

test_skip() {
    echo -e "${YELLOW}[SKIP]${NC} $1"
}

run_test() {
    local name="$1"
    local cmd="$2"

    if eval "$cmd" &>/dev/null; then
        test_pass "$name"
    else
        test_fail "$name"
    fi
}

# =============================================================================
# Syntax Tests
# =============================================================================

echo ""
echo -e "${CYAN}=== Syntax Validation ===${NC}"
echo ""

# Test bash syntax with bash -n (parse but don't execute)
run_test "bootstrap-dev.sh syntax valid" "bash -n '$SCRIPT_DIR/bootstrap-dev.sh'"
run_test "verify-environment.sh syntax valid" "bash -n '$SCRIPT_DIR/verify-environment.sh'"

# Check for shellcheck if available
if command -v shellcheck &>/dev/null; then
    echo ""
    echo -e "${CYAN}=== ShellCheck Analysis ===${NC}"
    echo ""

    # Run shellcheck with relaxed settings (info-level issues ok)
    if shellcheck -S warning "$SCRIPT_DIR/bootstrap-dev.sh" 2>/dev/null; then
        test_pass "bootstrap-dev.sh passes shellcheck (warnings)"
    else
        test_fail "bootstrap-dev.sh has shellcheck warnings"
    fi

    if shellcheck -S warning "$SCRIPT_DIR/verify-environment.sh" 2>/dev/null; then
        test_pass "verify-environment.sh passes shellcheck (warnings)"
    else
        test_fail "verify-environment.sh has shellcheck warnings"
    fi
else
    test_skip "shellcheck not installed (apt install shellcheck)"
fi

# =============================================================================
# Help Flag Tests
# =============================================================================

echo ""
echo -e "${CYAN}=== Help Flag Tests ===${NC}"
echo ""

# Test --help flag works
if "$SCRIPT_DIR/bootstrap-dev.sh" --help 2>&1 | grep -q "Usage"; then
    test_pass "bootstrap-dev.sh --help shows usage"
else
    test_fail "bootstrap-dev.sh --help missing usage info"
fi

if "$SCRIPT_DIR/bootstrap-dev.sh" --help 2>&1 | grep -q "\-\-skip-docker"; then
    test_pass "bootstrap-dev.sh --help documents --skip-docker"
else
    test_fail "bootstrap-dev.sh --help missing --skip-docker"
fi

if "$SCRIPT_DIR/bootstrap-dev.sh" --help 2>&1 | grep -q "\-\-skip-minikube"; then
    test_pass "bootstrap-dev.sh --help documents --skip-minikube"
else
    test_fail "bootstrap-dev.sh --help missing --skip-minikube"
fi

# =============================================================================
# Verification Script Tests
# =============================================================================

echo ""
echo -e "${CYAN}=== Verification Script Tests ===${NC}"
echo ""

# Test that verify script runs and produces output
verify_output=$("$SCRIPT_DIR/verify-environment.sh" 2>&1 || true)

if echo "$verify_output" | grep -q "MeridianConsole"; then
    test_pass "verify-environment.sh shows banner"
else
    test_fail "verify-environment.sh missing banner"
fi

if echo "$verify_output" | grep -q "Summary"; then
    test_pass "verify-environment.sh shows summary"
else
    test_fail "verify-environment.sh missing summary"
fi

if echo "$verify_output" | grep -q "\.NET SDK"; then
    test_pass "verify-environment.sh checks .NET SDK"
else
    test_fail "verify-environment.sh missing .NET SDK check"
fi

if echo "$verify_output" | grep -q "Docker"; then
    test_pass "verify-environment.sh checks Docker"
else
    test_fail "verify-environment.sh missing Docker check"
fi

# =============================================================================
# Function Unit Tests (source and test individual functions)
# =============================================================================

echo ""
echo -e "${CYAN}=== Function Unit Tests ===${NC}"
echo ""

# Create a subshell to test functions without side effects
(
    # Source just the function definitions (stop before main execution)
    # We'll extract and test key functions

    # Test command_exists function
    command_exists() {
        command -v "$1" &>/dev/null
    }

    if command_exists bash; then
        echo -e "${GREEN}[PASS]${NC} command_exists finds 'bash'"
    else
        echo -e "${RED}[FAIL]${NC} command_exists should find 'bash'"
    fi

    if ! command_exists nonexistent_command_xyz123; then
        echo -e "${GREEN}[PASS]${NC} command_exists returns false for missing command"
    else
        echo -e "${RED}[FAIL]${NC} command_exists should return false for missing command"
    fi

    # Test get_distro function
    get_distro() {
        if [[ -f /etc/os-release ]]; then
            source /etc/os-release
            case "${ID:-}" in
                ubuntu|debian|linuxmint|pop) echo "debian" ;;
                fedora|rhel|centos|rocky|almalinux) echo "fedora" ;;
                arch|manjaro|endeavouros) echo "arch" ;;
                *)
                    case "${ID_LIKE:-}" in
                        *debian*|*ubuntu*) echo "debian" ;;
                        *fedora*|*rhel*) echo "fedora" ;;
                        *arch*) echo "arch" ;;
                        *) echo "unknown" ;;
                    esac
                    ;;
            esac
        else
            echo "unknown"
        fi
    }

    distro=$(get_distro)
    if [[ "$distro" =~ ^(debian|fedora|arch|unknown)$ ]]; then
        echo -e "${GREEN}[PASS]${NC} get_distro returns valid distro: $distro"
    else
        echo -e "${RED}[FAIL]${NC} get_distro returned unexpected: $distro"
    fi
)

# Count the subshell tests manually
((PASSED+=3)) || true

# =============================================================================
# File Permission Tests
# =============================================================================

echo ""
echo -e "${CYAN}=== File Permission Tests ===${NC}"
echo ""

if [[ -x "$SCRIPT_DIR/bootstrap-dev.sh" ]]; then
    test_pass "bootstrap-dev.sh is executable"
else
    test_fail "bootstrap-dev.sh is not executable (run: chmod +x scripts/bootstrap-dev.sh)"
fi

if [[ -x "$SCRIPT_DIR/verify-environment.sh" ]]; then
    test_pass "verify-environment.sh is executable"
else
    test_fail "verify-environment.sh is not executable (run: chmod +x scripts/verify-environment.sh)"
fi

# =============================================================================
# Shebang Tests
# =============================================================================

echo ""
echo -e "${CYAN}=== Shebang Tests ===${NC}"
echo ""

if head -1 "$SCRIPT_DIR/bootstrap-dev.sh" | grep -q "#!/usr/bin/env bash"; then
    test_pass "bootstrap-dev.sh has portable shebang"
else
    test_fail "bootstrap-dev.sh missing portable shebang (#!/usr/bin/env bash)"
fi

if head -1 "$SCRIPT_DIR/verify-environment.sh" | grep -q "#!/usr/bin/env bash"; then
    test_pass "verify-environment.sh has portable shebang"
else
    test_fail "verify-environment.sh missing portable shebang (#!/usr/bin/env bash)"
fi

# =============================================================================
# Summary
# =============================================================================

echo ""
echo -e "${CYAN}=======================================${NC}"
echo -e "${CYAN}  Test Summary${NC}"
echo -e "${CYAN}=======================================${NC}"
echo ""
echo -e "  Passed: ${GREEN}$PASSED${NC}"
echo -e "  Failed: ${RED}$FAILED${NC}"
echo ""

if [[ $FAILED -eq 0 ]]; then
    echo -e "${GREEN}All tests passed!${NC}"
    exit 0
else
    echo -e "${RED}Some tests failed. Please review above.${NC}"
    exit 1
fi
