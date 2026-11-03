using System.Text.Json;
using Dhadgar.Shared.Results;
using OpenIddict.Abstractions;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Reads and writes the organization a client_credentials client is bound to.
/// The binding lives in the OpenIddict application property <see cref="PropertyName"/>.
/// </summary>
public static class ClientOrganizationBinding
{
    /// <summary>OpenIddict application property that holds the bound organization id as a JSON string.</summary>
    public const string PropertyName = "dhadgar_organization_id";

    /// <summary>Error code for a client with no binding.</summary>
    public const string UnboundError = "[Identity.ClientUnbound]";

    /// <summary>Error code for a client whose binding is present but not a valid Guid string.</summary>
    public const string InvalidError = "[Identity.ClientBindingInvalid]";

    /// <summary>Sets (Guid) or clears (null) the binding on a descriptor.</summary>
    public static void Apply(OpenIddictApplicationDescriptor descriptor, Guid? organizationId)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (organizationId is { } id)
        {
            descriptor.Properties[PropertyName] = JsonSerializer.SerializeToElement(id.ToString("D"));
        }
        else
        {
            descriptor.Properties.Remove(PropertyName);
        }
    }

    /// <summary>
    /// Success(Guid) for a valid binding; Failure("[Identity.ClientUnbound]") when absent;
    /// Failure("[Identity.ClientBindingInvalid]") when present but not a non-empty Guid string.
    /// </summary>
    public static Result<Guid> Read(IReadOnlyDictionary<string, JsonElement> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (!properties.TryGetValue(PropertyName, out var element))
        {
            return Result<Guid>.Failure(UnboundError);
        }

        if (element.ValueKind != JsonValueKind.String ||
            element.GetString() is not { Length: > 0 } text ||
            !Guid.TryParse(text, out var organizationId) ||
            organizationId == Guid.Empty)
        {
            return Result<Guid>.Failure(InvalidError);
        }

        return Result<Guid>.Success(organizationId);
    }
}
