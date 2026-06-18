using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameTranslator.Application.Credentials;

namespace GameTranslator.Infrastructure.Credentials;

public sealed class WindowsCredentialStorage : ICredentialStorage
{
    private const int ErrorNotFound = 1168;
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int MaxCredentialBlobSize = 5 * 512;
    private const string TargetPrefix = "GameTranslator/Translator";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task SaveAsync(
        TranslatorCredentialRecord credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = StoredCredentialPayload.FromRecord(credential);
        var blob = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        if (blob.Length > MaxCredentialBlobSize)
        {
            throw new CredentialStorageException(
                $"Translator credentials for provider '{credential.Provider}' exceed the Windows Credential Manager blob limit.");
        }

        var blobPointer = Marshal.AllocCoTaskMem(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);

            var nativeCredential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = CreateTargetName(credential.Provider),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredentialPersistLocalMachine,
                UserName = credential.Provider,
            };

            if (!CredWrite(ref nativeCredential, 0))
            {
                ThrowLastWin32Error(
                    $"Windows Credential Manager could not save translator credentials for provider '{credential.Provider}'.");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(blobPointer);
        }

        return Task.CompletedTask;
    }

    public Task<TranslatorCredentialRecord?> ReadAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedProvider = TranslatorCredentialService.NormalizeProvider(provider);
        var targetName = CreateTargetName(normalizedProvider);

        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode == ErrorNotFound)
            {
                return Task.FromResult<TranslatorCredentialRecord?>(null);
            }

            throw CreateWin32Exception(
                errorCode,
                $"Windows Credential Manager could not read translator credentials for provider '{normalizedProvider}'.");
        }

        try
        {
            var nativeCredential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            var blob = new byte[nativeCredential.CredentialBlobSize];
            Marshal.Copy(nativeCredential.CredentialBlob, blob, 0, blob.Length);

            var payload = JsonSerializer.Deserialize<StoredCredentialPayload>(blob, JsonOptions);
            if (payload is null)
            {
                throw new CredentialStorageException(
                    $"Translator credentials for provider '{normalizedProvider}' could not be parsed.");
            }

            return Task.FromResult<TranslatorCredentialRecord?>(
                payload.ToRecord(normalizedProvider));
        }
        catch (JsonException exception)
        {
            throw new CredentialStorageException(
                $"Translator credentials for provider '{normalizedProvider}' could not be parsed.",
                exception);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Task DeleteAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedProvider = TranslatorCredentialService.NormalizeProvider(provider);
        var targetName = CreateTargetName(normalizedProvider);

        if (!CredDelete(targetName, CredentialTypeGeneric, 0))
        {
            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode == ErrorNotFound)
            {
                return Task.CompletedTask;
            }

            throw CreateWin32Exception(
                errorCode,
                $"Windows Credential Manager could not delete translator credentials for provider '{normalizedProvider}'.");
        }

        return Task.CompletedTask;
    }

    private static string CreateTargetName(string provider)
    {
        return $"{TargetPrefix}/{TranslatorCredentialService.NormalizeProvider(provider)}";
    }

    private static void ThrowLastWin32Error(string message)
    {
        throw CreateWin32Exception(Marshal.GetLastWin32Error(), message);
    }

    private static CredentialStorageException CreateWin32Exception(int errorCode, string message)
    {
        return new CredentialStorageException(
            $"{message} Win32 error {errorCode}: {new Win32Exception(errorCode).Message}");
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string targetName,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(
        string targetName,
        uint type,
        uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    private sealed class StoredCredentialPayload
    {
        public string? Provider { get; init; }

        public string? AccessToken { get; init; }

        public string? ProjectId { get; init; }

        public string? Location { get; init; }

        public string? Endpoint { get; init; }

        public static StoredCredentialPayload FromRecord(TranslatorCredentialRecord record)
        {
            return new StoredCredentialPayload
            {
                Provider = record.Provider,
                AccessToken = record.AccessToken,
                ProjectId = record.ProjectId,
                Location = record.Location,
                Endpoint = record.Endpoint.ToString(),
            };
        }

        public TranslatorCredentialRecord ToRecord(string expectedProvider)
        {
            var provider = string.IsNullOrWhiteSpace(Provider)
                ? expectedProvider
                : TranslatorCredentialService.NormalizeProvider(Provider);
            var endpointText = string.IsNullOrWhiteSpace(Endpoint)
                ? TranslatorCredentialService.GetDefaultEndpoint(provider)
                : Endpoint.Trim();

            if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint))
            {
                throw new CredentialStorageException(
                    $"Translator credentials for provider '{expectedProvider}' contain an invalid endpoint.");
            }

            return new TranslatorCredentialRecord(
                provider,
                AccessToken ?? string.Empty,
                ProjectId ?? string.Empty,
                string.IsNullOrWhiteSpace(Location) ? "global" : Location,
                endpoint);
        }
    }
}
