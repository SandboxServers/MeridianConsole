using System.Text.Json.Serialization;

namespace Dhadgar.Agent.Windows.Uat.Cleanup;

/// <summary>
/// One line of <c>cleanup-manifest.jsonl</c>: a change the driver made or is about to make. Written and
/// flushed before the change; undo walks the manifest in reverse. The <c>kind</c> discriminator selects the type.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ContainerEntry), "container")]
[JsonDerivedType(typeof(ProcessEntry), "process")]
[JsonDerivedType(typeof(RegistryKeyEntry), "registryKey")]
[JsonDerivedType(typeof(RegistryValueEntry), "registryValue")]
[JsonDerivedType(typeof(CertificateEntry), "certificate")]
[JsonDerivedType(typeof(CngKeyEntry), "cngKey")]
[JsonDerivedType(typeof(CertStoreEntry), "certStore")]
[JsonDerivedType(typeof(DirectoryEntry), "directory")]
[JsonDerivedType(typeof(FileEntry), "file")]
[JsonDerivedType(typeof(EventSourceEntry), "eventSource")]
[JsonDerivedType(typeof(ServiceEntry), "service")]
internal abstract record CleanupEntry;

/// <summary>A Docker container named by the driver (<c>docker rm -f</c>).</summary>
internal sealed record ContainerEntry(string Name) : CleanupEntry;

/// <summary>A child process; undo kills its tree if the same PID and start time still run.</summary>
internal sealed record ProcessEntry(int Pid, DateTime StartTimeUtc, string Name) : CleanupEntry;

/// <summary>A registry key; undo deletes the tree when <see cref="ExistedBefore"/> is false.</summary>
internal sealed record RegistryKeyEntry(string Hive, string Path, bool ExistedBefore) : CleanupEntry;

/// <summary>A registry value; undo deletes it when <see cref="ExistedBefore"/> is false.</summary>
internal sealed record RegistryValueEntry(string Path, string Name, bool ExistedBefore) : CleanupEntry;

/// <summary>A certificate to remove from a store, by SHA-1 thumbprint.</summary>
internal sealed record CertificateEntry(string StoreLocation, string StoreName, string ThumbprintSha1) : CleanupEntry;

/// <summary>A CNG key container.</summary>
internal sealed record CngKeyEntry(string KeyName, string Provider, bool MachineKey) : CleanupEntry;

/// <summary>A system certificate store created by the driver; undo unregisters it when <see cref="ExistedBefore"/> is false.</summary>
internal sealed record CertStoreEntry(string StoreName, bool ExistedBefore) : CleanupEntry;

/// <summary>A directory; undo deletes it recursively when <see cref="ExistedBefore"/> is false.</summary>
internal sealed record DirectoryEntry(string Path, bool ExistedBefore) : CleanupEntry;

/// <summary>A file the driver created.</summary>
internal sealed record FileEntry(string Path) : CleanupEntry;

/// <summary>An event log source; undo deletes it when <see cref="ExistedBefore"/> is false.</summary>
internal sealed record EventSourceEntry(string Source, bool ExistedBefore) : CleanupEntry;

/// <summary>A Windows service; undo stops it (quiesce pass) and deletes it.</summary>
internal sealed record ServiceEntry(string Name) : CleanupEntry;
