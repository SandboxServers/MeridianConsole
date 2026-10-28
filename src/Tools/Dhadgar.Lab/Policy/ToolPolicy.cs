namespace Dhadgar.Lab.Policy;

/// <summary>
/// The only table of tool gates (C3, C4). One entry per tool name in C4. An unmapped name is refused as <c>[Lab.Unclassified]</c>.
/// </summary>
public static class ToolPolicy
{
    private static readonly Dictionary<string, LabGate> GateTable = new(StringComparer.Ordinal)
    {
        ["lab_status"] = LabGate.Open,
        ["lab_doctor"] = LabGate.Open,
        ["lab_lease_acquire"] = LabGate.Open,
        ["lab_lease_renew"] = LabGate.Open,
        ["lab_lease_release"] = LabGate.Open,
        ["lab_lease_status"] = LabGate.Open,
        ["logs_tail"] = LabGate.Open,
        ["stack_status"] = LabGate.Open,
        ["db_query"] = LabGate.Open,
        ["certstore_list"] = LabGate.Open,
        ["registry_read"] = LabGate.Open,
        ["state_file_read"] = LabGate.Open,
        ["uat_list_specs"] = LabGate.Open,
        ["uat_plan"] = LabGate.Open,
        ["uat_report"] = LabGate.Open,
        ["bundle_read"] = LabGate.Open,
        ["fault_list_targets"] = LabGate.Open,
        ["faults_active"] = LabGate.Open,
        ["invariants_check"] = LabGate.Open,
        ["results_query"] = LabGate.Open,
        ["triage_show"] = LabGate.Open,
        ["job_status"] = LabGate.Open,
        ["pool_list"] = LabGate.Open,
        ["ui_flow_list"] = LabGate.Open,
        ["lab_build"] = LabGate.Leased,
        ["stack_up"] = LabGate.Leased,
        ["stack_down"] = LabGate.Leased,
        ["stack_reset_data"] = LabGate.Leased,
        ["svc_start"] = LabGate.Leased,
        ["svc_stop"] = LabGate.Leased,
        ["svc_restart"] = LabGate.Leased,
        ["svc_config_set"] = LabGate.Leased,
        ["http_request"] = LabGate.Leased,
        ["nodes_token_mint"] = LabGate.Leased,
        ["nodes_enroll_probe"] = LabGate.Leased,
        ["nodes_cert_time_shift"] = LabGate.Leased,
        ["nodes_decommission"] = LabGate.Leased,
        ["fault_toxic_add"] = LabGate.Leased,
        ["fault_proxy_disable"] = LabGate.Leased,
        ["fault_partition"] = LabGate.Leased,
        ["fault_heal"] = LabGate.Leased,
        ["fault_reset"] = LabGate.Leased,
        ["fault_kill"] = LabGate.Leased,
        ["fault_pause"] = LabGate.Leased,
        ["fault_disk_fill"] = LabGate.Leased,
        ["fault_corrupt_file"] = LabGate.Leased,
        ["fault_corrupt_registry"] = LabGate.Leased,
        ["fault_db"] = LabGate.Leased,
        ["results_flake_check"] = LabGate.OwnLease,
        ["results_known_issue_set"] = LabGate.Leased,
        ["triage_draft_issue"] = LabGate.Leased,
        ["triage_mark"] = LabGate.Leased,
        ["hunt_model_run"] = LabGate.OwnLease,
        ["fuzz_run"] = LabGate.OwnLease,
        ["chaos_run"] = LabGate.OwnLease,
        ["soak_run"] = LabGate.OwnLease,
        ["fleet_run"] = LabGate.OwnLease,
        ["job_cancel"] = LabGate.Leased,
        ["pool_acquire"] = LabGate.Leased,
        ["pool_release"] = LabGate.Leased,
        ["member_reset"] = LabGate.Leased,
        ["agent_install"] = LabGate.Leased,
        ["agent_uninstall"] = LabGate.Leased,
        ["agent_start"] = LabGate.Leased,
        ["agent_stop"] = LabGate.Leased,
        ["agent_kill"] = LabGate.Leased,
        ["agent_token_write"] = LabGate.Leased,
        ["agent_config_set"] = LabGate.Leased,
        ["agent_msi_install"] = LabGate.Leased,
        ["agent_msi_uninstall"] = LabGate.Leased,
        ["cli_run"] = LabGate.Leased,
        ["ui_flow_run"] = LabGate.Leased,
        ["uat_run"] = LabGate.OwnLease,
        ["uat_attest"] = LabGate.Leased,
        ["identity_user_token"] = LabGate.Leased,
        ["events_read"] = LabGate.Open,
        ["history_run"] = LabGate.OwnLease,
        ["twin_run"] = LabGate.OwnLease,
        ["mutation_run"] = LabGate.OwnLease,
        ["coverage_report"] = LabGate.Open,
        ["envelope_run"] = LabGate.OwnLease,
        ["peer_run"] = LabGate.OwnLease,
        ["version_run"] = LabGate.OwnLease,
        ["guided_ab_run"] = LabGate.OwnLease,
    };

    /// <summary>
    /// Every classified tool name and its gate. No entry carries <see cref="LabGate.Machine"/> (D-ML43).
    /// </summary>
    public static IReadOnlyDictionary<string, LabGate> Gates => GateTable;

    /// <summary>
    /// Names in the table that no registered tool implements yet. Grouped by the packet that registers them. A packet removes only its own names and keeps its `// Lnn` line, so parallel packets never edit adjacent lines.
    /// </summary>
    public static IReadOnlySet<string> PlannedTools { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // L13
        // L15
        // L16
        // L19
        // L20 (registered)
        // L21
        // L25
        // L26
        "triage_show",
        "results_known_issue_set",
        "triage_draft_issue",
        "triage_mark",
        // L27
        "job_status",
        "job_cancel",
        "results_flake_check",
        "hunt_model_run",
        // L28
        "fuzz_run",
        // L29
        "chaos_run",
        "soak_run",
        // L30
        "pool_list",
        "pool_acquire",
        "pool_release",
        "member_reset",
        // L32
        "agent_install",
        "agent_uninstall",
        "agent_start",
        "agent_stop",
        "agent_token_write",
        "agent_config_set",
        // L33
        "fault_corrupt_registry",
        // L35
        "ui_flow_list",
        "ui_flow_run",
        // L37
        "cli_run",
        // L42
        "fleet_run",
        // L43
        "agent_msi_install",
        "agent_msi_uninstall",
        // L48
        // L51 (registered)
        // L52 (registered)
        // L53
        "twin_run",
        // L54
        "mutation_run",
        // L56
        "identity_user_token",
        // L57
        "envelope_run",
        // L58
        "nodes_decommission",
        "peer_run",
        // L59
        "agent_kill",
        // L60
        "version_run",
        // L62
        "guided_ab_run",
    };

    /// <summary>
    /// The gate for a tool name, or null when the name is not classified (the dispatcher refuses it).
    /// </summary>
    public static LabGate? GateFor(string toolName)
    {
        ArgumentNullException.ThrowIfNull(toolName);

        return GateTable.TryGetValue(toolName, out LabGate gate) ? gate : null;
    }
}
