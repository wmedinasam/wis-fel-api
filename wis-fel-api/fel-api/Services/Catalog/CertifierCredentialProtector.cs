using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace fel_api.Services.Catalog;

public sealed class CertifierCredentialProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("wis-fel.certifier-credentials.v1");

    public byte[] Protect(IReadOnlyDictionary<string, string> credentials) =>
        _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(credentials));

    public IReadOnlyDictionary<string, string> Unprotect(byte[] payload) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(_protector.Unprotect(payload)) ?? [];
}
