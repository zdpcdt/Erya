namespace Erya.Core.Models;

/// <summary>
/// A RIME input schema (输入方案), e.g. 朙月拼音 (luna_pinyin) or 雾凇拼音 (rime_ice).
/// </summary>
public sealed record RimeSchema(
    string Id,
    string Name,
    string? Author = null,
    string? Description = null);
