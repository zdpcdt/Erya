using System;

namespace Erya.Localization;

/// <summary>
/// 同步读取 Lingua 可观察串的当前值。行为主体语义保证订阅即得当前值，
/// 因此页面 VM 在构造时（换语言后页面已重建）可以同步取到正确语言的文案。
/// </summary>
internal static class Loc
{
    public static string Get(IObservable<string?> source)
    {
        var capture = new ValueCapture();
        using var subscription = source.Subscribe(capture);
        return capture.Value ?? string.Empty;
    }

    /// <summary>带格式参数的 <see cref="Get"/>（资源里用 {0} 占位）。</summary>
    public static string Format(IObservable<string?> source, params object?[] args)
        => string.Format(Get(source), args);

    private sealed class ValueCapture : IObserver<string?>
    {
        public string? Value;
        public void OnNext(string? value) => Value = value;
        public void OnError(Exception _) { }
        public void OnCompleted() { }
    }
}
