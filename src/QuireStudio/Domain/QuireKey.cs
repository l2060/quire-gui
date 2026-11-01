namespace QuireStudio.Domain;

/// <summary>
/// 本机全局命名密钥，对应 <c>quire-cli keys list</c> 的单条 <c>key</c> 记录。
/// 密钥与项目无关，所有项目都可以通过 YAML 的 <c>secret-key</c> 引用同一名称。
/// </summary>
public sealed record QuireKey
{
    public required string Name { get; init; }

    /// <summary>客户端密钥的指纹，用于人工比对。</summary>
    public required string Fingerprint { get; init; }

    /// <summary>128 字符的 Base64 客户端密钥，可直接传给 <c>new AssetKey(string)</c>。</summary>
    public required string ClientKey { get; init; }

    /// <summary>用于列表展示的短指纹。</summary>
    public string ShortFingerprint => Fingerprint.Length <= 16 ? Fingerprint : Fingerprint[..16];

    /// <summary>遮蔽后的客户端密钥，避免在无需查看时泄露到屏幕上。</summary>
    public string MaskedClientKey => ClientKey.Length <= 12
        ? new string('•', ClientKey.Length)
        : $"{ClientKey[..6]}{new string('•', 10)}{ClientKey[^6..]}";
}
