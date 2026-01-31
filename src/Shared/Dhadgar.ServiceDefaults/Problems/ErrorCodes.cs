namespace Dhadgar.ServiceDefaults.Problems;

/// <summary>
/// Centralized error codes for consistent error handling across all services.
/// </summary>
/// <remarks>
/// Error codes follow snake_case naming convention and are organized by domain.
/// These codes are machine-readable identifiers that clients can use for conditional logic.
/// </remarks>
public static class ErrorCodes
{
    /// <summary>
    /// Error codes for the Nodes service (hardware inventory, agent enrollment, capacity management).
    /// </summary>
    public static class NodeErrors
    {
        /// <summary>The specified node was not found or the user doesn't have access to it.</summary>
        public const string NodeNotFound = "node_not_found";

        /// <summary>The node is already in maintenance mode.</summary>
        public const string AlreadyInMaintenance = "already_in_maintenance";

        /// <summary>The node is not currently in maintenance mode.</summary>
        public const string NotInMaintenance = "not_in_maintenance";

        /// <summary>The enrollment token is invalid, expired, or already used.</summary>
        public const string InvalidToken = "invalid_token";

        /// <summary>The platform must be 'linux' or 'windows'.</summary>
        public const string InvalidPlatform = "invalid_platform";

        /// <summary>The node has been decommissioned and cannot be modified.</summary>
        public const string NodeDecommissioned = "node_decommissioned";

        /// <summary>The node is already decommissioned.</summary>
        public const string AlreadyDecommissioned = "already_decommissioned";

        /// <summary>A node with this name already exists in the organization.</summary>
        public const string NameAlreadyExists = "name_already_exists";

        /// <summary>The node is not available for reservations.</summary>
        public const string NodeUnavailable = "node_unavailable";

        /// <summary>The node does not have capacity data configured.</summary>
        public const string CapacityDataMissing = "capacity_data_missing";

        /// <summary>Insufficient memory available on the node.</summary>
        public const string InsufficientMemory = "insufficient_memory";

        /// <summary>Insufficient disk space available on the node.</summary>
        public const string InsufficientDisk = "insufficient_disk";
    }

    /// <summary>
    /// Error codes for capacity reservation operations.
    /// </summary>
    public static class Reservations
    {
        /// <summary>The specified reservation was not found.</summary>
        public const string ReservationNotFound = "reservation_not_found";

        /// <summary>The reservation has expired.</summary>
        public const string ReservationExpired = "reservation_expired";

        /// <summary>The reservation has already been claimed.</summary>
        public const string ReservationClaimed = "reservation_claimed";

        /// <summary>The reservation has been released.</summary>
        public const string ReservationReleased = "reservation_released";

        /// <summary>The reservation has already been released or expired.</summary>
        public const string ReservationAlreadyReleased = "reservation_already_released";
    }

    /// <summary>
    /// Error codes for the Identity service (users, organizations, roles).
    /// </summary>
    public static class IdentityErrors
    {
        /// <summary>The specified user was not found.</summary>
        public const string UserNotFound = "user_not_found";

        /// <summary>The specified organization was not found.</summary>
        public const string OrganizationNotFound = "organization_not_found";

        /// <summary>The specified role was not found.</summary>
        public const string RoleNotFound = "role_not_found";

        /// <summary>The specified member was not found in this organization.</summary>
        public const string MemberNotFound = "member_not_found";

        /// <summary>The user is already a member of this organization.</summary>
        public const string AlreadyMember = "already_member";

        /// <summary>The specified invitation was not found or has expired.</summary>
        public const string InviteNotFound = "invite_not_found";

        /// <summary>The invitation has expired.</summary>
        public const string InviteExpired = "invite_expired";

        /// <summary>A pending invitation already exists for this user.</summary>
        public const string InvitationExists = "invitation_exists";

        /// <summary>The specified claim was not found.</summary>
        public const string ClaimNotFound = "claim_not_found";

        /// <summary>The email address is invalid.</summary>
        public const string InvalidEmail = "invalid_email";

        /// <summary>A user with this email address already exists.</summary>
        public const string EmailAlreadyExists = "email_already_exists";

        /// <summary>A role with this name already exists in the organization.</summary>
        public const string RoleAlreadyExists = "role_already_exists";

        /// <summary>The role name is invalid (empty, too long, or reserved).</summary>
        public const string InvalidRoleName = "invalid_role_name";

        /// <summary>The specified permissions are invalid or cannot be granted.</summary>
        public const string InvalidPermissions = "invalid_permissions";

        /// <summary>An organization with this slug already exists.</summary>
        public const string OrganizationSlugExists = "organization_slug_exists";

        /// <summary>The organization name is invalid (empty, too short, or too long).</summary>
        public const string InvalidOrganizationName = "invalid_organization_name";

        /// <summary>System roles cannot be updated.</summary>
        public const string CannotUpdateSystemRole = "cannot_update_system_role";

        /// <summary>System roles cannot be deleted.</summary>
        public const string CannotDeleteSystemRole = "cannot_delete_system_role";

        /// <summary>The role has active members and cannot be deleted.</summary>
        public const string RoleHasActiveMembers = "role_has_active_members";
    }

    /// <summary>
    /// Error codes for the Secrets service (Azure Key Vault integration).
    /// </summary>
    public static class Secrets
    {
        /// <summary>The specified secret was not found.</summary>
        public const string SecretNotFound = "secret_not_found";

        /// <summary>The secret name contains invalid characters.</summary>
        public const string InvalidSecretName = "invalid_secret_name";

        /// <summary>The secret value exceeds the maximum allowed size.</summary>
        public const string SecretTooLarge = "secret_too_large";

        /// <summary>You have exceeded the rate limit for this operation.</summary>
        public const string RateLimitExceeded = "rate_limit_exceeded";

        /// <summary>The secret value is required.</summary>
        public const string SecretValueRequired = "secret_value_required";

        /// <summary>Rotation failed due to an internal error.</summary>
        public const string RotationFailed = "rotation_failed";
    }

    /// <summary>
    /// Error codes for certificate operations in the Secrets service.
    /// </summary>
    public static class Certificates
    {
        /// <summary>The specified certificate was not found.</summary>
        public const string CertificateNotFound = "certificate_not_found";

        /// <summary>Certificate name and data are required.</summary>
        public const string CertificateDataRequired = "certificate_data_required";

        /// <summary>The certificate data is not valid base64.</summary>
        public const string InvalidCertificateData = "invalid_certificate_data";

        /// <summary>A certificate with this name already exists.</summary>
        public const string CertificateAlreadyExists = "certificate_already_exists";
    }

    /// <summary>
    /// Error codes for Key Vault management operations.
    /// </summary>
    public static class KeyVaults
    {
        /// <summary>The specified Key Vault was not found.</summary>
        public const string VaultNotFound = "vault_not_found";

        /// <summary>Vault name and location are required.</summary>
        public const string VaultDataRequired = "vault_data_required";

        /// <summary>A Key Vault with this name already exists.</summary>
        public const string VaultAlreadyExists = "vault_already_exists";
    }

    /// <summary>
    /// Error codes for authentication and authorization.
    /// </summary>
    public static class Auth
    {
        /// <summary>The provided credentials are invalid.</summary>
        public const string InvalidCredentials = "invalid_credentials";

        /// <summary>You do not have permission to access this resource.</summary>
        public const string AccessDenied = "access_denied";

        /// <summary>Your account has been locked due to too many failed login attempts.</summary>
        public const string AccountLocked = "account_locked";

        /// <summary>Your session has expired. Please log in again.</summary>
        public const string SessionExpired = "session_expired";

        /// <summary>The authentication token has expired.</summary>
        public const string TokenExpired = "token_expired";

        /// <summary>The OAuth provider is not supported.</summary>
        public const string InvalidOAuthProvider = "invalid_oauth_provider";
    }

    /// <summary>
    /// Common error codes applicable across all services.
    /// </summary>
    public static class CommonErrors
    {
        /// <summary>An internal server error occurred. Please try again later.</summary>
        public const string InternalError = "internal_error";

        /// <summary>A database error occurred. Please contact support if this persists.</summary>
        public const string DatabaseError = "database_error";

        /// <summary>The request contains invalid data.</summary>
        public const string ValidationFailed = "validation_failed";
    }
}
