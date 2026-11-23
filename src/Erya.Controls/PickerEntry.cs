namespace Erya.Controls;

/// <summary>
/// 选择面板里的一条候选：<see cref="Id"/> 是业务键（应用场景里即 exe 名 / Bundle ID），
/// <see cref="Title"/> 是可选的展示友好名，<see cref="IsAdded"/> 标记「已存在、不可再选」。
/// </summary>
public sealed record PickerEntry(string Id, string? Title = null, bool IsAdded = false)
{
    /// <summary>有友好名时行首显示 Title，标识退为小字第二行（macOS 形态）；否则单行 Id。</summary>
    public bool HasTitle => Title is { Length: > 0 };
}
